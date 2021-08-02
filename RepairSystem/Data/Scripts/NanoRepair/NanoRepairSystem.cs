using Sandbox.Common;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Definitions;
using Sandbox.Engine;
using Sandbox.Engine.Utils;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Character;
using Sandbox.Game.Entities.Cube;
using Sandbox.Game.Entities.Debris;
using Sandbox.Game.Lights;
using Sandbox.Game.Multiplayer;
using Sandbox.Game.World;
using Sandbox.Game.GameSystems;
using Sandbox.Game.Weapons;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces;
using SpaceEngineers.Game.ModAPI;
using VRage;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.Game.ModAPI.Interfaces;
using VRage.ModAPI;
using VRage.Utils;
using VRage.ObjectBuilders;
using VRageMath;
using VRage.Voxels;
using VRage.Collections;

using System;
//using VRage;
//using Sandbox.Definitions;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Xml.Serialization;
using ProtoBuf;

using System.Collections.Concurrent;
using ParallelTasks;

namespace NanoRepairSystem
{
	[MyEntityComponentDescriptor(typeof(MyObjectBuilder_Assembler), false, new string[] { "NanoSystem" })]
	public class NanoRepair : MyGameLogicComponent
	{
		private const float WELD_MULTIPLIER = 1f;
		private const int TICKS_PER_UPDATE = 1;
		private const int MAX_TRANSFERS_PER_TICK = 10;
		
		private long gridID;
		private float m_speed;
		private IMyCubeGrid m_grid;
		private MyObjectBuilder_EntityBase ObjectBuilder;
		
		private ConcurrentDictionary<string,int> neededComponents;
		private InventorySystem m_inventory;
		
		private List<IMySlimBlock> projectors;
		private List<IMySlimBlock> p_blocks;
		
		private bool initted = false;
		
		private int tick = 0;
		
		public override void Init(MyObjectBuilder_EntityBase objectBuilder)
		{
			base.Init(objectBuilder);
			NeedsUpdate |= MyEntityUpdateEnum.EACH_FRAME | MyEntityUpdateEnum.EACH_100TH_FRAME;
		}
		
		public override MyObjectBuilder_EntityBase GetObjectBuilder(bool copy = false)
		{
			return copy ? ObjectBuilder.Clone() as MyObjectBuilder_EntityBase : ObjectBuilder;
		}
		
		public override void UpdateBeforeSimulation()
		{
			//Set weld speed if it hasn't already been set.
			if (!initted && NanoRepairRegistry.init)
			{
				m_speed = WELD_MULTIPLIER*MyAPIGateway.Session.WelderSpeedMultiplier;
				
				m_grid = (Entity as IMyCubeBlock).CubeGrid;
				gridID = m_grid.EntityId;
				
				m_inventory = new InventorySystem((Entity as MyEntity).GetInventory());
				
				projectors = new List<IMySlimBlock>();
				p_blocks = new List<IMySlimBlock>();
				
				if (!NanoRepairRegistry.damagedBlocks.ContainsKey(gridID))
				{
					var c = new List<IMySlimBlock>();
					m_grid.GetBlocks(c, x =>
						{
							var zz = x.FatBlock?.HasInventory;
							if (zz.HasValue)
								return zz.Value;
							else
								return false;
						});	
					var q = new ConcurrentQueue<IMyInventory>();
					foreach (var block in c)
						q.Enqueue(block.FatBlock.GetInventory());
					m_inventory.updateConnectedInventories(q);
					
					m_grid.GetBlocks(projectors, x => x.FatBlock is IMyProjector && x.CubeGrid == m_grid);
					if (projectors.Count > 0)
					{
						var proj = projectors[0];
				
						var p_grid = (proj.FatBlock as IMyProjector).ProjectedGrid;
						
						if (p_grid != null)
							p_grid.GetBlocks(p_blocks);
					}
					
					List<IMySlimBlock> dblocks = new List<IMySlimBlock>();
					m_grid.GetBlocks(dblocks, x=>
						{
							return !x.IsFullIntegrity || x.HasDeformation;
						});
					NanoRepairRegistry.damagedBlocks[gridID] = new HashSet<IMySlimBlock>(dblocks);
				}
				
				neededComponents = new ConcurrentDictionary<string,int>();
				
				initted = true;
				return;
			}
			
			//Only try this if repair system is on.
			if ((MyAPIGateway.Multiplayer.IsServer || !MyAPIGateway.Multiplayer.MultiplayerActive) && (Entity as IMyFunctionalBlock).Enabled && initted && NanoRepairRegistry.init)
			{
				//Repair blocks if needed
				int length = 0;
				
				if (NanoRepairRegistry.damagedBlocks[gridID].Count > 0)
				{	
					HashSet<IMySlimBlock> repaired = new HashSet<IMySlimBlock>();
					float repairSpeed = m_speed/NanoRepairRegistry.damagedBlocks[gridID].Count;
					
					if (MyAPIGateway.Session.CreativeMode)
					{
						foreach (var block in NanoRepairRegistry.damagedBlocks[gridID])
						{
							try
							{
								block.IncreaseMountLevel(repairSpeed,0,m_inventory.m_inventory,0.1f*m_speed,true);
							}
							catch
							{
								m_grid.RazeBlock(block.Position);
							}
							
							if (block.IsFullIntegrity && !block.HasDeformation)
								repaired.Add(block);
						}
					}
					else
					{
						getNeededComponents();
						for (int i = 0; i < neededComponents.Count; i++)
						{
							m_inventory.addPullRequests(neededComponents);
							PutItems();
						}
						
						foreach (var block in NanoRepairRegistry.damagedBlocks[gridID])
						{
							try
							{
								if (block.CanContinueBuild(m_inventory.m_inventory))
								{
									block.MoveItemsToConstructionStockpile(m_inventory.m_inventory);
									block.IncreaseMountLevel(repairSpeed,0,m_inventory.m_inventory,0.1f*m_speed,true);
								}
							}
							catch
							{
								m_grid.RazeBlock(block.Position);
							}
							
							if (block.IsFullIntegrity && !block.HasDeformation)
								repaired.Add(block);
						}
					}
					
					foreach (var block in repaired)
						NanoRepairRegistry.damagedBlocks[gridID].Remove(block);
				}
				else
				{
					constructBlock();
				}
				
				//Run callbacks
				if (!m_inventory.updatingInventories)
				{
					var c = new List<IMySlimBlock>();
					m_grid.GetBlocks(c, x =>
							{
								var zz = x.FatBlock?.HasInventory;
								if (zz.HasValue)
									return zz.Value;
								else
									return false;
							});
							
					var q = new ConcurrentQueue<IMyInventory>();
					foreach (var block in c)
						q.Enqueue(block.FatBlock.GetInventory());
						
					for (int i = 0; i < q.Count; i++)
						m_inventory.updateConnectedInventories(q);
				}
				else
				{
					var q = new ConcurrentQueue<IMyInventory>();
					m_inventory.updateConnectedInventories(q);
				}
				
				//Check grid ID changes and update as necessary
				if (m_grid.EntityId != gridID)
				{
					var blocks = NanoRepairRegistry.damagedBlocks[gridID];
					NanoRepairRegistry.damagedBlocks.Remove(gridID);
					NanoRepairRegistry.damagedBlocks.Remove(gridID);
					
					gridID = m_grid.EntityId;
					NanoRepairRegistry.damagedBlocks.Add(gridID, blocks);
				}
			}
		}
		
		//Update cargo list and projector grids.
		public override void UpdateBeforeSimulation100()
		{
			if (tick == TICKS_PER_UPDATE)
			{
				projectors.Clear();
				m_grid.GetBlocks(projectors, x => x.FatBlock is IMyProjector && x.CubeGrid == m_grid && (x.FatBlock as IMyFunctionalBlock).Enabled);
				
				if (projectors.Count > 0)
				{
					var proj = projectors[0];
					
					var p_grid = (proj.FatBlock as IMyProjector).ProjectedGrid;
					
					if (p_grid == null)
						return;
					
					p_blocks.Clear();
					p_grid.GetBlocks(p_blocks);
				}
				
				tick = 0;
			}
			else
				tick++;
		}
		
		private void PutItems()
		{
			for (int i = 0; i < MAX_TRANSFERS_PER_TICK; i++)
			{
				m_inventory.transfer();
			}
		}
		
		private void constructBlock()
		{
			if (p_blocks.Count > 0)
			{
				//In case projector gets destroyed etc.
				try
				{
					var proj = projectors[0];
					
					if ((proj.FatBlock as IMyFunctionalBlock).Enabled && (proj.FatBlock as IMyProjector).IsProjecting)
					{
						foreach(var block in p_blocks)
						{	
							if ((proj.FatBlock as IMyProjector).CanBuild(block, false) != BuildCheckResult.OK)
								continue;
							
							if (MyAPIGateway.Session.CreativeMode)
							{
								(proj.FatBlock as IMyProjector).Build(block, 0, (Entity as IMyCubeBlock).OwnerId, false);
								return;
							}
							else
							{
								var itemID = ((MyCubeBlockDefinition) block.BlockDefinition).Components[0].Definition.Id;
								var c_q = new ConcurrentDictionary<string,int>();
								c_q.AddOrUpdate(itemID.SubtypeName,1,(k,v)=>1);
								
								m_inventory.addPullRequests(c_q);
								PutItems();
								
								if (m_inventory.m_inventory.GetItemAmount(itemID) >= 1)
								{
									m_inventory.m_inventory.RemoveItemsOfType((MyFixedPoint) 1, itemID);
									(proj.FatBlock as IMyProjector).Build(block, 0, (Entity as IMyCubeBlock).OwnerId, false);
									return;
								}
							}
						}
					}
				}
				catch
				{
					p_blocks.Clear();
				}
			}
		}
		
		private void getNeededComponents()
		{
			neededComponents.Clear();
			
			MyAPIGateway.Parallel.ForEach(NanoRepairRegistry.damagedBlocks[gridID], block =>
				{
					Dictionary<string,int> missing = new Dictionary<string,int>();
					block.GetMissingComponents(missing);
					
					foreach (KeyValuePair<string,int> component in missing)
						neededComponents.AddOrUpdate(component.Key,component.Value,(k,v) => v + component.Value);
				});
		}
	}
	
	public class InventorySystem
	{
		public IMyInventory m_inventory;
		private MyConcurrentHashSet<IMyInventory> connectedInv;
		public ConcurrentQueue<itemTransfer> transferQueue;
		
		public bool updatingInventories = false;
		public ConveyorWork.ConveyorWorkData cdat;
		
		public bool calculatingTransfers = false;
		public ItemWork.ItemWorkData idat;
		
		public struct itemTransfer
		{
			public IMyInventory src;
			public MyFixedPoint amount;
			public string compName;
			
			public itemTransfer(string n, IMyInventory inv, MyFixedPoint a)
			{
				src = inv;
				compName = n;
				amount = a;
			}
		}
		
		public InventorySystem(IMyInventory sink)
		{
			m_inventory = sink;
			connectedInv = new MyConcurrentHashSet<IMyInventory>();
			transferQueue = new ConcurrentQueue<itemTransfer>();
		}
		
		public void updateConnectedInventories(ConcurrentQueue<IMyInventory> c)
		{
			if (!updatingInventories)
				cdat = new ConveyorWork.ConveyorWorkData(m_inventory,c);
			MyAPIGateway.Parallel.Start(ConveyorWork.DoWork, ConveyorCallback, cdat);
		}
		
		public void transfer()
		{
			itemTransfer t;
			if (!transferQueue.TryDequeue(out t))
				return;
			
			
			try
			{
				var itemID = new MyDefinitionId(typeof(MyObjectBuilder_Component),t.compName);
				MyObjectBuilder_Component comp = new MyObjectBuilder_Component()
					{
						SubtypeName = itemID.SubtypeName
					};
				
				(t.src as MyInventory).RemoveItemsOfType(t.amount, itemID);
				(m_inventory as MyInventory).AddItems(t.amount,comp);
			}
			catch
			{
				var msg = "Transfer failed: " + t.compName;
				MyAPIGateway.Utilities.ShowNotification(msg, 5000, MyFontEnum.Red);
			}
		}
		
		public void addPullRequests(ConcurrentDictionary<string,int> needed)
		{
			if (!calculatingTransfers)
				idat = new ItemWork.ItemWorkData(m_inventory, connectedInv, needed);
			MyAPIGateway.Parallel.Start(ItemWork.DoWork, ItemCallback, idat);
		}
		
		private void ConveyorCallback(WorkData dat)
		{
			var d = dat as ConveyorWork.ConveyorWorkData;
			if (d == null)
				return;
			
			if (d.blocks.Count > 0)
			{
				updatingInventories = true;
				return;
			}
			
			connectedInv = d.cargo;
			updatingInventories = false;
		}
		
		private void ItemCallback(WorkData dat)
		{
			var d = dat as ItemWork.ItemWorkData;
			if (d == null)
				return;
			
			if (d.needed.Count > 0)
			{
				calculatingTransfers = true;
				return;
			}
			
			transferQueue = d.transfers;
			calculatingTransfers = false;
		}
		
		public static class ConveyorWork
		{
			public static void DoWork(WorkData dat)
			{
				var d = dat as ConveyorWorkData;
				if (d == null)
					return;
				
				IMyInventory block;
				if (!d.blocks.TryDequeue(out block))
					return;
				
				IMyInventory cinv = d.c;
				
				if (cinv == null || block == null)
					return;
				
				
				if (cinv.IsConnectedTo(block))
					d.cargo.Add(block);
			}
			
			public class ConveyorWorkData : WorkData
			{
				public IMyInventory c;
				public ConcurrentQueue<IMyInventory> blocks;
				public MyConcurrentHashSet<IMyInventory> cargo;
				
				public ConveyorWorkData(IMyInventory repairInv, ConcurrentQueue<IMyInventory> b)
				{
					c = repairInv;
					blocks = b;
					cargo = new MyConcurrentHashSet<IMyInventory>();
				}
			}
		}
		
		public static class ItemWork
		{
			public static void DoWork(WorkData dat)
			{
				var d = dat as ItemWorkData;
				if (d == null)
					return;
				
				KeyValuePair<string,int> comp;
				if (!d.needed.TryDequeue(out comp))
					return;
				
				var itemID = new MyDefinitionId(typeof(MyObjectBuilder_Component),comp.Key);
				var amt = d.c.GetItemAmount(itemID);
				var amtNeeded = comp.Value - (int) amt;
				
				if (amtNeeded > 0)
				{
					foreach (var inventory in d.cargos)
					{
						if (amtNeeded <= 0)
							break;
						
						amt = inventory.GetItemAmount(itemID);
						if (amt == 0)
							continue;
						
						if (amt >= amtNeeded)
						{
							//transfer amtNeeded from inventory
							d.transfers.Enqueue(new itemTransfer(comp.Key,inventory,(MyFixedPoint) amtNeeded));
							amtNeeded = 0;
						}
						else
						{
							//transfer amt from inventory
							d.transfers.Enqueue(new itemTransfer(comp.Key,inventory,amt));
							amtNeeded -= (int) amt;
						}
					}
				}
			}
			
			public class ItemWorkData : WorkData
			{
				public IMyInventory c;
				public MyConcurrentHashSet<IMyInventory> cargos;
				public ConcurrentQueue<KeyValuePair<string,int>> needed;
				
				public ConcurrentQueue<itemTransfer> transfers;
				
				public ItemWorkData(IMyInventory repairInv, MyConcurrentHashSet<IMyInventory> connectedInv, ConcurrentDictionary<string,int> missingComps)
				{
					c = repairInv;
					cargos = connectedInv;
					
					needed = new ConcurrentQueue<KeyValuePair<string,int>>();
					foreach (KeyValuePair<string,int> comp in missingComps)
						needed.Enqueue(comp);
					
					transfers = new ConcurrentQueue<itemTransfer>();
				}
			}
		}
	}
}
	