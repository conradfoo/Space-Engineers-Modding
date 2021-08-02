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

using System.Collections.Concurrent;
using ParallelTasks;

namespace NanoRepairSystem
{
	[MySessionComponentDescriptor(MyUpdateOrder.AfterSimulation)]
   public class NanoRepairRegistry : MySessionComponentBase
   {
		public static Dictionary<long,HashSet<IMySlimBlock>> damagedBlocks;
		public static bool init = false;
		
		public void Init()
		{
			MyAPIGateway.Session.DamageSystem.RegisterAfterDamageHandler(0, AfterDamageHandlerNoDamageByBuildAndRepairSystem);
			damagedBlocks = new Dictionary<long,HashSet<IMySlimBlock>>();
			
			init = true;
		}
		
		public override void UpdateAfterSimulation()
		{
			if (!init && MyAPIGateway.Session != null && MyAPIGateway.Session.Player != null)
				Init();
			
			base.UpdateAfterSimulation();
		}
		
		public void AfterDamageHandlerNoDamageByBuildAndRepairSystem(object target, MyDamageInformation info)
		{
			try
			{
				if (info.Amount > 0)
				{
					var tblock = target as IMySlimBlock;
					if (tblock != null)
					{
						if (damagedBlocks.ContainsKey(tblock.CubeGrid.EntityId))
						{
							damagedBlocks[tblock.CubeGrid.EntityId].Add(tblock);
						}
					}
				}
			}
			catch
			{
				return;
			}
		}
   }
}