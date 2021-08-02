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

using System;
//using VRage;
//using Sandbox.Definitions;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Xml.Serialization;
using ProtoBuf;

namespace WeldingTurret
{
	[MyEntityComponentDescriptor(typeof(MyObjectBuilder_LargeGatlingTurret), new string[] { "RepairTurret" })]
	public class RepairBeam : MyGameLogicComponent
	{
		private IMyCubeBlock m_turret;
		private IMyCubeBlock m_storage;
		private IMyInventory m_inventory;
		private Vector3D m_target = Vector3D.Zero;
		private MyObjectBuilder_EntityBase ObjectBuilder;

		private float m_maxrange = 200;
		private float GRIND_MULTIPLIER = 0.1f;
		private float m_speed;
		private bool initted = false;
		private bool isCreative = false;
		private bool tryBuild = false;
		
		public override void Init(MyObjectBuilder_EntityBase objectBuilder)
		{
			base.Init(objectBuilder);
			m_turret = Entity as IMyCubeBlock;
			NeedsUpdate |= MyEntityUpdateEnum.EACH_FRAME | MyEntityUpdateEnum.EACH_100TH_FRAME;
			ObjectBuilder = objectBuilder;
			if (!HookCargo())
			{
				var msg = "Repair turret must be placed on cargo container";
				MyAPIGateway.Utilities.ShowNotification(msg, 5000, MyFontEnum.Red);
				throw new ArgumentException(msg);
			}
		}

		public override MyObjectBuilder_EntityBase GetObjectBuilder(bool copy = false)
		{
			return copy ? ObjectBuilder.Clone() as MyObjectBuilder_EntityBase : ObjectBuilder;
		}

	    public override void Close()
		{
			Log.Close();
		}

		public override void UpdateBeforeSimulation()
		{
			//Set weld speed if it hasn't already been set.
			if (!initted)
			{
				m_speed = GRIND_MULTIPLIER*MyAPIGateway.Session.WelderSpeedMultiplier;
				isCreative = MyAPIGateway.Session.CreativeMode;
				initted = true;
			}
			
			//If the gun is enabled and we're the server
			if ((MyAPIGateway.Multiplayer.IsServer || !MyAPIGateway.Multiplayer.MultiplayerActive) && (Entity as IMyFunctionalBlock).Enabled)
			{
				//If we're currently firing.
				if ((Entity as IMyGunObject<MyGunBase>).IsShooting)
				{
					//Target information
					IHitInfo target;
					Vector3D forwardVector = (m_turret as MyEntity).Subparts["GatlingTurretBase1"].Subparts["GatlingTurretBase2"].Subparts["GatlingBarrel"].WorldMatrix.Forward;
					m_target = (m_turret as IMyCubeBlock).PositionComp.GetPosition() + forwardVector * m_maxrange;
					MyAPIGateway.Physics.CastRay(m_turret.PositionComp.GetPosition(),m_target,out target);

					//Do stuff only if we actually hit something.
					if (target != null)
					{
						IMyEntity e = target.HitEntity;
						tryBuild = false;
						int length = 0;
						if (e is IMyCubeGrid && (target.Position - (m_turret as IMyCubeBlock).PositionComp.GetPosition()).Length() < m_maxrange)
						{
							List<IMySlimBlock> projectors = new List<IMySlimBlock>();
							
							//Find blocks that are damaged.
							(e as IMyCubeGrid).GetBlocks(projectors, x => !x.IsFullIntegrity || x.HasDeformation);
							if (projectors.Count > 0)
							{
								foreach (var block in projectors)
								{
									var missing = new Dictionary<string, int>();
									
									//If the inventory is filled up with necessary components.
									bool inventoryLimited = true;
									
									if (MyAPIGateway.Session.CreativeMode)
									{
										inventoryLimited = false;
										block.IncreaseMountLevel(m_speed, 0, m_inventory, 0.1f*m_speed, true);
									}
									
									//Build block if possible.
									block.GetMissingComponents(missing);
									length = missing.Count;
									bool succeeded = true;
									
									while (inventoryLimited)
									{
										//Put the appropriate items in the welder inventory.
										foreach (KeyValuePair<string, int> component in missing)
										{
											var itemID = new MyDefinitionId(typeof(MyObjectBuilder_Component), component.Key);
											MyFixedPoint amt = (MyFixedPoint) component.Value;
											succeeded = succeeded && PutItems(amt, itemID);
										}
										
										//Make the block great again.
										block.MoveItemsToConstructionStockpile(m_inventory);
										block.IncreaseMountLevel(m_speed, 0, m_inventory, 0.1f*m_speed, true);
										
										//Check inventory status and item inventory.
										inventoryLimited = !succeeded && ((m_inventory as MyInventory).CargoPercentage == 1f);

										missing.Clear();
										block.GetMissingComponents(missing);
										if (missing.Count != length)
										{
											length = missing.Count;
											tryBuild = true;
										}
									}
									
									if (tryBuild)
										break;
								}
							}

							projectors.Clear();
						}
					}
				}
			}
		}
		
		//Loads the turret with ammo.
		public override void UpdateBeforeSimulation100()
		{
			MyObjectBuilder_AmmoMagazine amFactory = new MyObjectBuilder_AmmoMagazine()
			{
				SubtypeName = "LaserAmmo2"
			};
			MyObjectBuilder_PhysicalObject poFactory = amFactory;
			SerializableDefinitionId id = poFactory.GetId();

			//Add magazines if the turret is empty.
			var turretInventory = (m_turret as MyEntity).GetInventoryBase(0);
			MyFixedPoint targetValue = new MyFixedPoint();
			targetValue.RawValue = 300;
			if (turretInventory.CurrentVolume < targetValue)
			{
				turretInventory.AddItems(900, poFactory);
			}
			
			
			//If the gun is enabled and we're the server
			if ((MyAPIGateway.Multiplayer.IsServer || !MyAPIGateway.Multiplayer.MultiplayerActive) && (Entity as IMyFunctionalBlock).Enabled)
			{
				//If we're currently firing.
				if ((Entity as IMyGunObject<MyGunBase>).IsShooting)
				{
					//Target information
					IHitInfo target;
					Vector3D forwardVector = (m_turret as MyEntity).Subparts["GatlingTurretBase1"].Subparts["GatlingTurretBase2"].Subparts["GatlingBarrel"].WorldMatrix.Forward;
					m_target = (m_turret as IMyCubeBlock).PositionComp.GetPosition() + forwardVector * m_maxrange;
					MyAPIGateway.Physics.CastRay(m_turret.PositionComp.GetPosition(),m_target,out target);

					//Do stuff only if we actually hit something.
					if (target != null)
					{
						IMyEntity e = target.HitEntity;
						List<IMySlimBlock> projectors = new List<IMySlimBlock>();
						
						if (!tryBuild)
						{
							//If there are projectors, look for projection.
							(e as IMyCubeGrid).GetBlocks(projectors, x => x.FatBlock is IMyProjector && x.CubeGrid == e);
						
							//Get a list of projected blocks in the welder aoe.
							List<IMySlimBlock> projectedBlocks = new List<IMySlimBlock>();
							foreach (IMySlimBlock proj in projectors)
							{
								var currGrid = (proj.FatBlock as IMyProjector).ProjectedGrid;
								if (currGrid == null)
									continue;
								
								currGrid.GetBlocks(projectedBlocks);
								
								//Build each projected block if possible.
								foreach (var block in projectedBlocks.ToList())
								{
									if ((proj.FatBlock as IMyProjector).CanBuild(block, false) != BuildCheckResult.OK)
										continue;
									
									if (MyAPIGateway.Session.CreativeMode)
									{
										(proj.FatBlock as IMyProjector).Build(block, 0, m_turret.EntityId, false);
										break;
									}
									
									var itemID = ((MyCubeBlockDefinition) block.BlockDefinition).Components[0].Definition.Id;
									if (PutItems((MyFixedPoint) 1, itemID))
									{
										m_inventory.RemoveItemsOfType((MyFixedPoint) 1, itemID);
										(proj.FatBlock as IMyProjector).Build(block, 0, m_turret.EntityId, false);
										break;
									}
								}
							}
						}
					}
				}
			}
		}

		//Attaches a cargo container inventory to the turret.
		private bool HookCargo()
		{
			var block = Container.Entity as IMyCubeBlock;

			// Get the block directly below, and see if it's a cargo container
			var down = Base6Directions.GetFlippedDirection(block.Orientation.Up);
			var downvec = Base6Directions.GetIntVector(down);
			Vector3I cargopos = block.Position + (downvec * 1);

			var cargo = (Container.Entity as IMyCubeBlock).CubeGrid.GetCubeBlock(cargopos);

			if (cargo != null && cargo.FatBlock != null && cargo.FatBlock is IMyCargoContainer)
			{
				m_storage = cargo.FatBlock as IMyCubeBlock;
				m_inventory = (m_storage as MyEntity).GetInventory();
				return true;
			}
			return false;
		}
		
		//Puts amount of itemID into the turret inventory if possible.
		private bool PutItems(MyFixedPoint? amount, MyDefinitionId itemID)
		{
			var currAmount = m_inventory.GetItemAmount(itemID);
			if (currAmount > amount)
				return true;
			
			var missingAmount = amount - currAmount;
			if (missingAmount > (m_inventory as MyInventory).ComputeAmountThatFits(itemID))
				return false;
			
			IMyCubeGrid myGrid = (Container.Entity as IMyCubeBlock).CubeGrid;
			List<IMySlimBlock> cargos = new List<IMySlimBlock>();
			myGrid.GetBlocks(cargos, x => x.FatBlock is IMyCargoContainer);
			
			foreach (IMySlimBlock cargo in cargos)
			{
				if (missingAmount <= 0)
					return true;
				
				var container = cargo.FatBlock;
				var container_inventory = (container as MyEntity).GetInventory();
				var amt = container_inventory.GetItemAmount(itemID);
				if (amt > 0)
				{
					var amtTransferred = (amt - missingAmount > 0) ?  missingAmount : amt;
					if (amtTransferred.HasValue)
					{
						(container_inventory as MyInventory).RemoveItemsOfType(amtTransferred.Value, itemID);
						
						MyObjectBuilder_Component comp = new MyObjectBuilder_Component()
						{
							SubtypeName = itemID.SubtypeName
						};
						(m_inventory as MyInventory).AddItems(amtTransferred.Value, comp);
						
						missingAmount -= amtTransferred.Value;
					}
				}
			}
			
			return false;
		}
    }
	
	//From Digi.
	public class Log
	{
		private const string MOD_NAME = "RepairTurret";
		private const string LOG_FILE = "info.log";

		private static System.IO.TextWriter writer = null;
		private static IMyHudNotification notify = null;
		private static int indent = 0;
		private static StringBuilder cache = new StringBuilder();
		public static void IncreaseIndent()
		{
			indent++;
		}
		public static void DecreaseIndent()
		{
			if (indent > 0)
				indent--;
		}
		public static void ResetIndent()
		{
			indent = 0;
		}
		public static void Error(Exception e)
		{
			Error(e.ToString());
		}
		public static void Error(string msg)
		{
			Info("ERROR: " + msg);
			try
			{
				MyAPIGateway.Utilities.ShowNotification(MOD_NAME + " error - open %AppData%/SpaceEngineers/Storage/..._" + MOD_NAME + "/" + LOG_FILE + " for details", 10000, MyFontEnum.Red);
			}
			catch (Exception e)
			{
				Info("ERROR: Could not send notification to local client: " + e.ToString());
			}
		}
		public static void Info(string msg)
		{
			Write(msg);
		}
		private static void Write(string msg)
		{
			if (writer == null)
			{
				if (MyAPIGateway.Utilities == null)
					throw new Exception("API not initialied but got a log message: " + msg);
				writer = MyAPIGateway.Utilities.WriteFileInLocalStorage(LOG_FILE, typeof(Log));
			}
			cache.Clear();
			cache.Append(DateTime.Now.ToString("[HH:mm:ss] "));
			for (int i = 0; i < indent; i++)
			{
				cache.Append("\t");
			}
			cache.Append(msg);
			writer.WriteLine(cache);
			writer.Flush();
			cache.Clear();
		}
		public static void Close()
		{
			if (writer != null)
			{
				writer.Flush();
				writer.Close();
				writer = null;
			}
			indent = 0;
			cache.Clear();
		}
	}
}