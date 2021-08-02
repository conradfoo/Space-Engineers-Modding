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
using Sandbox.ModAPI.Interfaces.Terminal;
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

namespace Economy
{
	[MyEntityComponentDescriptor(typeof(MyObjectBuilder_MyProgrammableBlock), false, "TradingTerminal")]
	public class TradingTerminal : MyGameLogicComponent
	{
		public struct tradePoint
		{
			public List<IMySlimBlock> m_connectors;
			public Dictionary<string, MyFixedPoint> m_inventory;
			public List<IMySlimBlock> t_connectors;
			public Dictionary<string, MyFixedPoint> t_inventory;
			public bool isConnected;
			public long partnerId;
			public float balance;
		}
		
		MyObjectBuilder_EntityBase m_objectBuilder; 
		
		bool controlInit = false;
		static IMyTerminalControlListbox m_tradingMaterialsList;
		static IMyTerminalControlListbox m_tradingPartners;
        static IMyTerminalControlButton m_buy;
        static IMyTerminalControlButton m_sell;
        static IMyTerminalControlSlider m_amount;
		
		
		string selectedItem;
		float amount = 0f;
		public float price = 0f;
	
		bool init = false; //Determines whether or not grids are connected.
		Dictionary<string, MyObjectBuilder_PhysicalObject> itemDefinitions = new Dictionary<string, MyObjectBuilder_PhysicalObject>();
		Dictionary<string, float> prices = new Dictionary<string, float>();
		public float balance = 0f;
		
		List<IMySlimBlock> gridConnectors = new List<IMySlimBlock>();
		List<long> connectedEntities = new List<long>();
		tradePoint currentTradePoint;
		
		private IMyEntity m_block;
		
		//Initializes object builders, update times, and ensures this is the only trade terminal on the grid.
		public override void Init(MyObjectBuilder_EntityBase objectBuilder)
		{
			m_objectBuilder = objectBuilder;
			m_block = Container.Entity;
			
			Entity.NeedsUpdate |= MyEntityUpdateEnum.EACH_100TH_FRAME | MyEntityUpdateEnum.BEFORE_NEXT_FRAME;
			
			List<IMySlimBlock> tmplist = new List<IMySlimBlock>();
			(Container.Entity as IMyCubeBlock).CubeGrid.GetBlocks(tmplist, x => x.FatBlock?.BlockDefinition.SubtypeId == "TradingTerminal");
						
			if (tmplist.Count > 0)
			{
				var msg = "Only one trade terminal allowed per grid!";
				MyAPIGateway.Utilities.ShowNotification(msg, 5000, MyFontEnum.Red);
				throw new ArgumentException(msg);
			}
			
			currentTradePoint.m_connectors = new List<IMySlimBlock>();
			currentTradePoint.m_inventory = new Dictionary<string, MyFixedPoint>();
			currentTradePoint.t_connectors = new List<IMySlimBlock>();
			currentTradePoint.t_inventory = new Dictionary<string, MyFixedPoint>();
			currentTradePoint.isConnected = false;
			currentTradePoint.partnerId = 0;
			currentTradePoint.balance = 0f;
		}
		
		public override MyObjectBuilder_EntityBase GetObjectBuilder(bool copy = false)
		{
			if (copy)
				return (MyObjectBuilder_EntityBase) m_objectBuilder.Clone();
			else
				return m_objectBuilder;
		}
		
		public override void Close()
		{
			Log.Close();
		}
		
		//Updates the list of active connectors on the grid, and updates the list of connected entities with trade terminals. TODO: Implement trade beacon and bank accounts.
		public override void UpdateBeforeSimulation100()
		{
			//Update connector list.
			gridConnectors.Clear();
			(Container.Entity as IMyCubeBlock).CubeGrid.GetBlocks(gridConnectors, x => x.FatBlock is IMyShipConnector && x.FatBlock?.CubeGrid == (Container.Entity as IMyCubeBlock).CubeGrid && x.FatBlock?.OwnerId == (Container.Entity as IMyCubeBlock).OwnerId);
			
			//Update connected entities list. Only connect to grids with a trade beacon and a bank account
			connectedEntities.Clear();
			foreach (IMySlimBlock c in gridConnectors)
			{
				if ((c.FatBlock as IMyShipConnector).IsConnected)
				{	
					IMyShipConnector connectedBlock = (c.FatBlock as IMyShipConnector).OtherConnector;
					
					//Don't try to trade with yourself...
					if (connectedBlock.OwnerId != c.FatBlock.OwnerId && !connectedEntities.Contains(connectedBlock.OwnerId))
						connectedEntities.Add(connectedBlock.OwnerId);
				}
			}
			
			if (connectedEntities.Count <= 0)
			{
				init = false;
				
				currentTradePoint.m_connectors.Clear();
				currentTradePoint.m_inventory.Clear();
				currentTradePoint.t_connectors.Clear();
				currentTradePoint.t_inventory.Clear();
				currentTradePoint.partnerId = 0;
				currentTradePoint.isConnected = false;
				
				m_amount.UpdateVisual();
				m_buy.UpdateVisual();
				m_sell.UpdateVisual();
				m_tradingMaterialsList.UpdateVisual();
				m_tradingPartners.UpdateVisual();
			}
			else
			{
				init = true;
				Log.Info("Client: Balance request periodic update.");
				MessageUtils.SendMessageToServer(new MessageGetBalanceRequest(){ playerID = (m_block as IMyCubeBlock).OwnerId, RequestingEntityId = m_block.EntityId });
				
			}
		}
		
		public override void UpdateOnceBeforeFrame()
		{
			InitTerminalControls();
			Log.Info("Registering.");
			(Container.Entity as IMyProgrammableBlock).AppendingCustomInfo += Custom_AppendingCustomInfo;
			(Container.Entity as IMyTerminalBlock).RefreshCustomInfo();
			Log.Info("Registered.");
		}
		
		//Initializes the buy and sell buttons, the amount to buy/sell slider, the listbox of materials that can be sold/purchased, and the listbox of trade partners.
		private void InitTerminalControls()
		{
			if (controlInit)
				return;
			
			controlInit = true;
			
			Log.Info("Initializing controls");
			
			//Make sure the block is a trading terminal and you own it.
			Func<IMyTerminalBlock,bool> isCorrectType = delegate(IMyTerminalBlock b) { return b.BlockDefinition.SubtypeId == "TradingTerminal" /*&& b.OwnerId == MyAPIGateway.Players.GetPlayerControllingEntity(b.SlimBlock as IMyEntity).IdentityId*/; };
			
			MyAPIGateway.TerminalControls.CustomControlGetter -= TerminalControls_CustomControlGetter;
			MyAPIGateway.TerminalControls.CustomControlGetter += TerminalControls_CustomControlGetter;
			
			MyAPIGateway.TerminalControls.CustomActionGetter -= TerminalControls_CustomActionGetter;
			MyAPIGateway.TerminalControls.CustomActionGetter += TerminalControls_CustomActionGetter;
			
			var sep = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSeparator, IMyProgrammableBlock>(string.Empty);
            sep.Visible = isCorrectType;
            MyAPIGateway.TerminalControls.AddControl<IMyProgrammableBlock>(sep);

			m_tradingMaterialsList = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlListbox, IMyProgrammableBlock>("Econ.TradingList");
			m_tradingPartners = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlListbox, IMyProgrammableBlock>("Econ.Partners");
			m_buy = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlButton, IMyProgrammableBlock>("Econ.Buy");
			m_sell = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlButton, IMyProgrammableBlock>("Econ.Sell");
			m_amount = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSlider, IMyProgrammableBlock>("Econ.Amount");
			
			//Buy button. Checks to see if material can be purchased before enabling. Action takes care of credit and material transfer.
			Log.Info("Initialize buy button.");
			if (m_buy != null)
			{
				m_buy.Title = MyStringId.GetOrCompute("Buy");
				m_buy.Visible = isCorrectType;
				m_buy.Enabled = (b) => isCorrectType(b) && canBuy(b);
				m_buy.Tooltip = MyStringId.GetOrCompute("Buys the selected item.");
				m_buy.Action = (b) =>
				{
					b.GameLogic.GetAs<TradingTerminal>().Buy();
					m_tradingMaterialsList.UpdateVisual();
					m_sell.UpdateVisual();
					m_buy.UpdateVisual();
					m_amount.UpdateVisual();
				};
			}
			
			//Sell button. Checks to see if material can be sold before enabling. Action takes care of credit and material transfer.
			Log.Info("Initialize sell button.");
			if (m_sell != null)
			{
				m_sell.Title = MyStringId.GetOrCompute("Sell");
				m_sell.Visible = isCorrectType;
				m_sell.Enabled = (b) => isCorrectType(b) && canSell(b);
				m_sell.Tooltip = MyStringId.GetOrCompute("Sells the selected item.");
				m_sell.Action = (b) =>
				{
					b.GameLogic.GetAs<TradingTerminal>().Sell();
					m_tradingMaterialsList.UpdateVisual();
					m_sell.UpdateVisual();
					m_buy.UpdateVisual();
					m_amount.UpdateVisual();
				};
			}
			
			//The slider that adjusts how much of the selected item is sold/bought. Sets the amount.
			Log.Info("Initialize amount slider.");
			if (m_amount != null)
			{
				m_amount.Title = MyStringId.GetOrCompute("Amount");
				m_amount.Visible = isCorrectType;
				m_amount.Enabled = (b) => isCorrectType(b) && b.GameLogic.GetAs<TradingTerminal>().selectedItem != null && b.GameLogic.GetAs<TradingTerminal>().currentTradePoint.isConnected;
				m_amount.Tooltip = MyStringId.GetOrCompute("Amount to buy/sell.");
				m_amount.Getter = (b) => isCorrectType(b) ? b.GameLogic.GetAs<TradingTerminal>().amount : 0;
				m_amount.Setter = (b, v) => 
				{
					if (!isCorrectType(b))
						return;
					b.GameLogic.GetAs<TradingTerminal>().amount = v; //MessageUtils.SendMessageToAll(new MessageSetAmount() {EntityId = b.EntityId, amount = v});
					b.RefreshCustomInfo();
					if (b.IsWorking)
					{
						(b as IMyProgrammableBlock).GetActionWithName("OnOff_Off").Apply(b as IMyProgrammableBlock);
						(b as IMyProgrammableBlock).GetActionWithName("OnOff_On").Apply(b as IMyProgrammableBlock);
					}
					m_amount.UpdateVisual();
					m_sell.UpdateVisual();
					m_buy.UpdateVisual();
				};
				m_amount.Writer = (b, s) =>
				{
					s.Append(m_amount.Getter(b).ToString());
				};
				
				m_amount.SetLimits(0,1);
			}
			
			//The listbox that displays a list of items that can be bought/sold. Sets the slider limits and selectedItem.
			Log.Info("Initialize trading material list.");
			if (m_tradingMaterialsList != null)
			{
				m_tradingMaterialsList.ListContent = GetGridInventory;
				m_tradingMaterialsList.ItemSelected = (b, v) =>
				{
					b.GameLogic.GetAs<TradingTerminal>().updateMaterials(b, v);
				};
				m_tradingMaterialsList.Title = MyStringId.GetOrCompute("Selected Commodity");
				m_tradingMaterialsList.Tooltip = MyStringId.GetOrCompute("Selects a material to buy/sell.");
				m_tradingMaterialsList.Visible = isCorrectType;
				m_tradingMaterialsList.Enabled = (b) => isCorrectType(b) && b.GameLogic.GetAs<TradingTerminal>().currentTradePoint.isConnected;
				m_tradingMaterialsList.VisibleRowsCount = 6;
				m_tradingMaterialsList.Multiselect = false;
			}
			
			//The listbox that displays a list of available trading partners. Sets the trading partner and updates the trading inventories for both grids. 
			Log.Info("Initialize trading partner list.");
			if (m_tradingPartners != null)
			{
				m_tradingPartners.ListContent = GetConnectedPartners;
				m_tradingPartners.ItemSelected = (b, v) =>
				{	
					b.GameLogic.GetAs<TradingTerminal>().SetCurrentTradePoint((long) v[0].UserData);
					m_tradingMaterialsList.UpdateVisual();
				};
				m_tradingPartners.Title = MyStringId.GetOrCompute("Selected Partner");
				m_tradingPartners.Tooltip = MyStringId.GetOrCompute("Selects a connected partner to trade with.");
				m_tradingPartners.Visible = isCorrectType;
				m_tradingPartners.Enabled = (b) => isCorrectType(b) && b.GameLogic.GetAs<TradingTerminal>().connectedEntities.Count > 0;
				m_tradingPartners.VisibleRowsCount = 2;
				m_tradingPartners.Multiselect = false;
			}
			
			//IMPORTANT: This runs on each block....but added controls are visible to all blocks on the same grid. Manually adding them back in CustomControlGetter.
			MyAPIGateway.TerminalControls.AddControl<IMyProgrammableBlock>(m_tradingPartners);
			MyAPIGateway.TerminalControls.AddControl<IMyProgrammableBlock>(m_tradingMaterialsList);
			MyAPIGateway.TerminalControls.AddControl<IMyProgrammableBlock>(m_amount);
			MyAPIGateway.TerminalControls.AddControl<IMyProgrammableBlock>(m_buy);
			MyAPIGateway.TerminalControls.AddControl<IMyProgrammableBlock>(m_sell);
		}
		
		private bool canBuy(IMyTerminalBlock block)
		{
			if (!block.GameLogic.GetAs<TradingTerminal>().init || !block.GameLogic.GetAs<TradingTerminal>().currentTradePoint.isConnected || block.GameLogic.GetAs<TradingTerminal>().selectedItem == null)
				return false;
			if (!block.GameLogic.GetAs<TradingTerminal>().currentTradePoint.t_inventory.ContainsKey(block.GameLogic.GetAs<TradingTerminal>().selectedItem))
				return false;
			
			Log.Info("Client: Price request from");
			Log.Info(block.EntityId.ToString());
			MessageUtils.SendMessageToServer(new MessageGetPriceRequest(){ RequestingEntityId = block.EntityId, itemId = block.GameLogic.GetAs<TradingTerminal>().selectedItem });
			return (MyFixedPoint) block.GameLogic.GetAs<TradingTerminal>().amount <= block.GameLogic.GetAs<TradingTerminal>().currentTradePoint.t_inventory[block.GameLogic.GetAs<TradingTerminal>().selectedItem] && block.GameLogic.GetAs<TradingTerminal>().balance >= 2*block.GameLogic.GetAs<TradingTerminal>().amount*block.GameLogic.GetAs<TradingTerminal>().price;
		}
		
		private bool canSell(IMyTerminalBlock block)
		{	
			if (!block.GameLogic.GetAs<TradingTerminal>().init || !block.GameLogic.GetAs<TradingTerminal>().currentTradePoint.isConnected || block.GameLogic.GetAs<TradingTerminal>().selectedItem == null)
				return false;
			if (!block.GameLogic.GetAs<TradingTerminal>().currentTradePoint.m_inventory.ContainsKey(block.GameLogic.GetAs<TradingTerminal>().selectedItem))
				return false;
			
			Log.Info("Client: Price request from");
			Log.Info(block.EntityId.ToString());
			MessageUtils.SendMessageToServer(new MessageGetPriceRequest(){ RequestingEntityId = block.EntityId, itemId = block.GameLogic.GetAs<TradingTerminal>().selectedItem });
			return (MyFixedPoint) block.GameLogic.GetAs<TradingTerminal>().amount <= block.GameLogic.GetAs<TradingTerminal>().currentTradePoint.m_inventory[block.GameLogic.GetAs<TradingTerminal>().selectedItem] && block.GameLogic.GetAs<TradingTerminal>().currentTradePoint.balance >= block.GameLogic.GetAs<TradingTerminal>().amount*block.GameLogic.GetAs<TradingTerminal>().price;
		}
		
		private void Buy()
		{
			//Subtract credits, initialize transfer amount.
			MyFixedPoint amtNeeded = (MyFixedPoint) amount;
			//IMyPlayer me = MyAPIGateway.Players.GetPlayerControllingEntity(m_block);
			
			Log.Info("Client: sent balance change request.");
			MessageUtils.SendMessageToServer(new MessageChangeBalance(){playerID = (m_block as IMyCubeBlock).OwnerId, amount = -2*(float)amtNeeded*price});
			MessageUtils.SendMessageToServer(new MessageChangeBalance(){playerID = currentTradePoint.t_connectors[0].FatBlock.OwnerId, amount = 2*(float)amtNeeded*price});
			balance -= amount*2*price;
			currentTradePoint.balance += amount*2*price;
			
			//Get inventories connected to the currEntity and transfer the amount needed.
			foreach (IMySlimBlock currConnection in currentTradePoint.t_connectors)
			{
				List<IMySlimBlock> s = new List<IMySlimBlock>();
				(currConnection.FatBlock as IMyCubeBlock).CubeGrid.GetBlocks(s, x => x.FatBlock is IMyCargoContainer && x.FatBlock?.OwnerId == currConnection.FatBlock.OwnerId && x.FatBlock.GetInventory().IsConnectedTo(currConnection.FatBlock.GetInventory()));
				s.Add(currConnection);
			
				//Transfer items to connector. (Do with add/remove for ownership issues.
				foreach (IMySlimBlock cargo in s)
				{
					var curr_inventory = cargo.FatBlock.GetInventory();
					var item = (curr_inventory as MyInventory).FindItem(itemDefinitions[selectedItem].GetObjectId());
				
					if (item.HasValue)
					{
						MyFixedPoint amtTransferred = (MyFixedPoint) Math.Min((float) item.Value.Amount, (float) amtNeeded);
						(curr_inventory as MyInventory).RemoveItemsOfType(amtTransferred, itemDefinitions[selectedItem]);
						((currConnection.FatBlock as IMyShipConnector).OtherConnector.GetInventory() as MyInventory).AddItems(amtTransferred, itemDefinitions[selectedItem]);
					
						amtNeeded -= amtTransferred;
						if (amtNeeded <= 0)
							break;
					}
				}
				
				if (amtNeeded <= 0)
					break;
			}
			
			//Update inventories appropriately.
			if ((MyFixedPoint) amount == currentTradePoint.t_inventory[selectedItem])
				currentTradePoint.t_inventory.Remove(selectedItem);
			else
				currentTradePoint.t_inventory[selectedItem] -= (MyFixedPoint) amount;
			
			if (currentTradePoint.m_inventory.ContainsKey(selectedItem))
				currentTradePoint.m_inventory[selectedItem] += (MyFixedPoint) amount;
			else
				currentTradePoint.m_inventory.Add(selectedItem, (MyFixedPoint) amount);
			
			(Container.Entity as IMyTerminalBlock).RefreshCustomInfo();
			if ((m_block as IMyTerminalBlock).IsWorking)
			{
				(m_block as IMyProgrammableBlock).GetActionWithName("OnOff_Off").Apply(m_block as IMyProgrammableBlock);
				(m_block as IMyProgrammableBlock).GetActionWithName("OnOff_On").Apply(m_block as IMyProgrammableBlock);
			}
		}
		
		private void Sell()
		{
			//Add credits, initialize transfer.
			MyFixedPoint amtNeeded = (MyFixedPoint) amount;
			//IMyPlayer me = MyAPIGateway.Players.GetPlayerControllingEntity(m_block);
			
			Log.Info("Client: sent balance change request.");
			MessageUtils.SendMessageToServer(new MessageChangeBalance(){playerID = (m_block as IMyCubeBlock).OwnerId, amount = (float)amtNeeded*price});
			MessageUtils.SendMessageToServer(new MessageChangeBalance(){playerID = currentTradePoint.t_connectors[0].FatBlock.OwnerId, amount = -(float)amtNeeded*price});
			
			balance += amount*price;
			currentTradePoint.balance -= (float) amtNeeded*price;

			//Get inventories connected to the currEntity and transfer the amount needed.
			foreach (IMySlimBlock currConnection in currentTradePoint.m_connectors)
			{
				List<IMySlimBlock> s = new List<IMySlimBlock>();
				(currConnection.FatBlock as IMyCubeBlock).CubeGrid.GetBlocks(s, x => x.FatBlock is IMyCargoContainer && x.FatBlock?.OwnerId == currConnection.FatBlock.OwnerId && x.FatBlock.GetInventory().IsConnectedTo(currConnection.FatBlock.GetInventory()));
				s.Add(currConnection);
			
				//Transfer items to connector. (Do with add/remove for ownership issues.
				foreach (IMySlimBlock cargo in s)
				{
					var curr_inventory = cargo.FatBlock.GetInventory();
					var item = (curr_inventory as MyInventory).FindItem(itemDefinitions[selectedItem].GetObjectId());
				
					if (item.HasValue)
					{
						MyFixedPoint amtTransferred = (MyFixedPoint) Math.Min((float) item.Value.Amount, (float) amtNeeded);
						(curr_inventory as MyInventory).RemoveItemsOfType(amtTransferred, itemDefinitions[selectedItem]);
						((currConnection.FatBlock as IMyShipConnector).OtherConnector.GetInventory() as MyInventory).AddItems(amtTransferred, itemDefinitions[selectedItem]);
					
						amtNeeded -= amtTransferred;
						if (amtNeeded <= 0)
							break;
					}
				}
				
				if (amtNeeded <= 0)
					break;
			}
			
			//Update inventories appropriately.
			if ((MyFixedPoint) amount == currentTradePoint.m_inventory[selectedItem])
				currentTradePoint.m_inventory.Remove(selectedItem);
			else
				currentTradePoint.m_inventory[selectedItem] -= (MyFixedPoint) amount;
			
			if (currentTradePoint.t_inventory.ContainsKey(selectedItem))
				currentTradePoint.t_inventory[selectedItem] += (MyFixedPoint) amount;
			else
				currentTradePoint.t_inventory.Add(selectedItem, (MyFixedPoint) amount);
			
			(Container.Entity as IMyTerminalBlock).RefreshCustomInfo();
			if ((m_block as IMyTerminalBlock).IsWorking)
			{
				(m_block as IMyProgrammableBlock).GetActionWithName("OnOff_Off").Apply(m_block as IMyProgrammableBlock);
				(m_block as IMyProgrammableBlock).GetActionWithName("OnOff_On").Apply(m_block as IMyProgrammableBlock);
			}
		}
		
		private void updateMaterials(IMyTerminalBlock reference, List<MyTerminalControlListBoxItem> v)
		{
			selectedItem = v[0].Text.ToString();
			float max = 0f;
			if (currentTradePoint.m_inventory.ContainsKey(selectedItem))
				max = (float) currentTradePoint.m_inventory[selectedItem];
			if (currentTradePoint.t_inventory.ContainsKey(selectedItem))
				max = Math.Max(max, (float) currentTradePoint.t_inventory[selectedItem]);
			m_amount.SetLimits(0f,max);
			m_amount.Setter(reference, 0f);
			reference.RefreshCustomInfo();
			if ((m_block as IMyTerminalBlock).IsWorking)
			{
				(m_block as IMyProgrammableBlock).GetActionWithName("OnOff_Off").Apply(m_block as IMyProgrammableBlock);
				(m_block as IMyProgrammableBlock).GetActionWithName("OnOff_On").Apply(m_block as IMyProgrammableBlock);
			}
			m_amount.UpdateVisual();
			m_buy.UpdateVisual();
			m_sell.UpdateVisual();
		}
		
		//Sets currentTradePoint to connectors and inventories connected to currEntity.
		private void SetCurrentTradePoint(long currEntity)
		{	
			currentTradePoint.isConnected = true;
			currentTradePoint.partnerId = currEntity;
			
			//Get all unique subgrids connected to the currEntity.
			currentTradePoint.m_connectors.Clear();
			currentTradePoint.t_connectors.Clear();
			foreach (IMySlimBlock c in gridConnectors)
			{
				var connector = c.FatBlock;
				if ((connector as IMyShipConnector).IsConnected)
				{
					if ((connector as IMyShipConnector).OtherConnector.OwnerId == currEntity)
					{
						if (currentTradePoint.m_connectors.Count > 0)
						{
							bool wasAdded = false;
							foreach (IMySlimBlock bl in currentTradePoint.m_connectors)
							{
								if (connector.GetInventory().IsConnectedTo(bl.FatBlock.GetInventory()))
									break;
								
								currentTradePoint.m_connectors.Add(c);
								wasAdded = true;
							}
							
							if (wasAdded)
							{
								foreach (IMySlimBlock pconnector in currentTradePoint.t_connectors)
								{
									if (pconnector.FatBlock.GetInventory().IsConnectedTo((c.FatBlock as IMyShipConnector).OtherConnector.GetInventory()))
										break;
									
									currentTradePoint.t_connectors.Add((c.FatBlock as IMyShipConnector).OtherConnector.SlimBlock);
								}
							}
						}
						else
						{
							currentTradePoint.m_connectors.Add(c);
							currentTradePoint.t_connectors.Add((c.FatBlock as IMyShipConnector).OtherConnector.SlimBlock);
						}
					}
				}
			}
			
			
			//Populate trading inventory available to sell to currEntity.
			currentTradePoint.m_inventory.Clear();
			currentTradePoint.m_inventory = GetTradingInventory(currentTradePoint.m_connectors);
			
			//Populate the inventory available to purchase from currEntity
			Log.Info("Populating other grid.");
			currentTradePoint.t_inventory.Clear();
			currentTradePoint.t_inventory = GetTradingInventory(currentTradePoint.t_connectors);
			
			//Get balance for trade.
			List<IMySlimBlock> tmplist = new List<IMySlimBlock>();
			(currentTradePoint.t_connectors[0].FatBlock as IMyCubeBlock).CubeGrid.GetBlocks(tmplist, x => x.FatBlock?.BlockDefinition.SubtypeId == "TradingTerminal");
			if (tmplist.Count > 0)
				currentTradePoint.balance = tmplist[0].FatBlock.GameLogic.GetAs<TradingTerminal>().balance;
		}
		
		//Returns a dictionary such that the amount of each itemid is associated with the itemid, for inventories connected to connector.
		private Dictionary<string, MyFixedPoint> GetTradingInventory(List<IMySlimBlock> connectors)
		{
			Dictionary<string, MyFixedPoint> myInventory = new Dictionary<string, MyFixedPoint>();
			foreach (IMySlimBlock connector in connectors)
			{
				//Generate a list of connected inventories.
				List<IMySlimBlock> connectedCargos = new List<IMySlimBlock>();
				(connector.FatBlock as IMyCubeBlock).CubeGrid.GetBlocks(connectedCargos, x => (x.FatBlock is IMyCargoContainer) && x.FatBlock.GetInventory().IsConnectedTo(connector.FatBlock.GetInventory()) && x.FatBlock.CubeGrid == connector.FatBlock.CubeGrid);
				connectedCargos.Add(connector);
				
				foreach (IMySlimBlock cargo in connectedCargos)
				{
					//Get the items in each inventory.
					var curr_inventory = cargo.FatBlock.GetInventory();
					List<IMyInventoryItem> items = curr_inventory.GetItems();
					
					foreach (IMyInventoryItem item in items)
					{
						//If the item isn't listed, add it. Otherwise increase the amount
						var builder = item.Content as MyObjectBuilder_PhysicalObject;
						var itemString = builder.SubtypeId + " " + builder.TypeId.ToString().Substring(16);
						if (!myInventory.ContainsKey(itemString))
							myInventory.Add(itemString, item.Amount);
						else
							myInventory[itemString] += item.Amount;
						
						//Update item string => itemid mappings if something new appears.
						if (!itemDefinitions.ContainsKey(itemString))
						{
							itemDefinitions.Add(itemString, builder);
						}
					}
				}
			}
			
			return myInventory;
		}
		
		//Populates the trading material list with available materials.
		static void GetGridInventory(IMyTerminalBlock block, List<MyTerminalControlListBoxItem> list, List<MyTerminalControlListBoxItem> selected)
		{
			if (!block.GameLogic.GetAs<TradingTerminal>().init || !block.GameLogic.GetAs<TradingTerminal>().currentTradePoint.isConnected)
			{
				list.Add(new MyTerminalControlListBoxItem(MyStringId.NullOrEmpty, MyStringId.NullOrEmpty,null));
				return;
			}
			
			
			foreach (KeyValuePair<string, MyFixedPoint> p in block.GameLogic.GetAs<TradingTerminal>().currentTradePoint.m_inventory)
			{
				list.Add(new MyTerminalControlListBoxItem(MyStringId.GetOrCompute(p.Key), MyStringId.NullOrEmpty, null));
			}
			
			foreach (KeyValuePair<string, MyFixedPoint> p in block.GameLogic.GetAs<TradingTerminal>().currentTradePoint.t_inventory)
			{
				if (!block.GameLogic.GetAs<TradingTerminal>().currentTradePoint.m_inventory.ContainsKey(p.Key))
					list.Add(new MyTerminalControlListBoxItem(MyStringId.GetOrCompute(p.Key), MyStringId.NullOrEmpty, null));
			}
		}
		
		//Populates the trading partner list.
		static void GetConnectedPartners(IMyTerminalBlock block, List<MyTerminalControlListBoxItem> list, List<MyTerminalControlListBoxItem> selected)
		{
			if (!block.GameLogic.GetAs<TradingTerminal>().init)
			{
				list.Add(new MyTerminalControlListBoxItem(MyStringId.NullOrEmpty, MyStringId.NullOrEmpty, 0));
				return;
			}
			
			foreach (long id in block.GameLogic.GetAs<TradingTerminal>().connectedEntities)
			{
				List<IMyIdentity> players = new List<IMyIdentity>();
				MyAPIGateway.Players.GetAllIdentites(players, p => p.IdentityId == id);
				if (players.Count > 0)
					list.Add(new MyTerminalControlListBoxItem(MyStringId.GetOrCompute(players[0].DisplayName), MyStringId.NullOrEmpty, id));
				else
					list.Add(new MyTerminalControlListBoxItem(MyStringId.GetOrCompute(id.ToString()), MyStringId.NullOrEmpty, id));
			}
		}
		
		static void TerminalControls_CustomControlGetter(IMyTerminalBlock block, List<IMyTerminalControl> controls)
        {
			if (block is IMyProgrammableBlock)
            {
				string subtype = (block as IMyProgrammableBlock).BlockDefinition.SubtypeId;
				
				if (subtype == "TradingTerminal")
				{
					foreach (var control in controls.ToList())
					{
						//Logger.Instance.LogMessage("Control: " + control.Id);
						switch (control.Id)
						{
							
							case "OnOff":
							case "ShowInTerminal":
							case "Name":
							case "ShowOnHUD":
								break;
							default:
								controls.Remove(control);
								break;
												
						}
					}
					
					controls.Add(m_tradingPartners);
					controls.Add(m_tradingMaterialsList);
					controls.Add(m_amount);
					controls.Add(m_buy);
					controls.Add(m_sell);
				}
            }
        }
		
		static void TerminalControls_CustomActionGetter(IMyTerminalBlock block, List<IMyTerminalAction> actions)
		{
			if (block is IMyProgrammableBlock)
			{
				string subtype = (block as IMyProgrammableBlock).BlockDefinition.SubtypeId;

				if (subtype == "TradingTerminal")
				{
					foreach (var action in actions.ToList())
						actions.Remove(action);
				}
			}
		}
			
		//Note that this doesn't refresh the controls. We force an update by power cycling the block.
		//(m_block as IMyProgrammableBlock).GetActionWithName("OnOff_Off").Apply(m_block as IMyProgrammableBlock);
		//(m_block as IMyProgrammableBlock).GetActionWithName("OnOff_On").Apply(m_block as IMyProgrammableBlock);
		public static void Custom_AppendingCustomInfo(IMyTerminalBlock block, StringBuilder s)
		{	
			if (block.BlockDefinition.SubtypeId == "TradingTerminal")
			{
				StringBuilder b = new StringBuilder();
				if (block.GameLogic.GetAs<TradingTerminal>().currentTradePoint.isConnected && block.GameLogic.GetAs<TradingTerminal>().selectedItem != null)
				{
					b.Append(block.GameLogic.GetAs<TradingTerminal>().selectedItem);
					b.Append(": ");
					b.AppendLine();
					b.Append("Buy Price: ");
					b.Append((block.GameLogic.GetAs<TradingTerminal>().price*2).ToString() + "c");
					b.AppendLine();
					b.Append("Sell Price: ");
					b.Append((block.GameLogic.GetAs<TradingTerminal>().price).ToString() + "c");
					b.AppendLine();
					b.Append("Your stock: ");
					if (block.GameLogic.GetAs<TradingTerminal>().currentTradePoint.m_inventory.ContainsKey(block.GameLogic.GetAs<TradingTerminal>().selectedItem))
						b.Append(block.GameLogic.GetAs<TradingTerminal>().currentTradePoint.m_inventory[block.GameLogic.GetAs<TradingTerminal>().selectedItem].ToString());
					else
						b.Append("0");
					b.AppendLine();
					b.Append("Partner's stock: ");
					if (block.GameLogic.GetAs<TradingTerminal>().currentTradePoint.t_inventory.ContainsKey(block.GameLogic.GetAs<TradingTerminal>().selectedItem))
						b.Append(block.GameLogic.GetAs<TradingTerminal>().currentTradePoint.t_inventory[block.GameLogic.GetAs<TradingTerminal>().selectedItem].ToString());
					else
						b.Append("0");
					b.AppendLine();
					//b.Append("Current transaction value (buy/sell): ");
					//b.Append((100*block.GameLogic.GetAs<TradingTerminal>().amount).ToString() + "c / " + (50*block.GameLogic.GetAs<TradingTerminal>().amount).ToString() + "c");
					
					b.Append("Your current balance: ");
					b.Append(block.GameLogic.GetAs<TradingTerminal>().balance.ToString() + "c");
					
					s.Append(b);
				}
			}
		}
	}
}