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

namespace SalvageTurret
{
  [MyEntityComponentDescriptor(typeof(MyObjectBuilder_LargeGatlingTurret), new string[] { "SalvageTurret" })]
  public class SalvageBeam : MyGameLogicComponent
  {
    private IMyCubeBlock m_turret;
    private IMyCubeBlock m_storage;
	private IMyInventory m_inventory;
    private Vector3D m_target = Vector3D.Zero;
    private MyObjectBuilder_EntityBase ObjectBuilder;

    private float m_maxrange = 200;
    private float SPHERE_RADIUS = 1.5f;
    private float GRIND_MULTIPLIER = 0.1f;
	private int DRILL_SPEED = 100;
    private float m_speed;
	private bool initted = false;

    public override void Init(MyObjectBuilder_EntityBase objectBuilder)
    {
      base.Init(objectBuilder);
      m_turret = Entity as IMyCubeBlock;
      NeedsUpdate |= MyEntityUpdateEnum.EACH_FRAME | MyEntityUpdateEnum.EACH_100TH_FRAME;
      ObjectBuilder = objectBuilder;
      if (!HookCargo())
      {
        var msg = "Salvage turret must be placed on cargo container";
        MyAPIGateway.Utilities.ShowNotification(msg, 5000, MyFontEnum.Red);
        throw new ArgumentException(msg);
      }
    }

    public override MyObjectBuilder_EntityBase GetObjectBuilder(bool copy = false)
    {
      return copy ? ObjectBuilder.Clone() as MyObjectBuilder_EntityBase : ObjectBuilder;
    }

    public override void UpdateBeforeSimulation()
    {
      //Set the speed if it hasn't been.
	  if (!initted)
	  {
		m_speed = GRIND_MULTIPLIER*MyAPIGateway.Session.GrinderSpeedMultiplier;
		initted = true;
	  }
	  
	  //If gun is enabled and we're the server.
      if ((MyAPIGateway.Multiplayer.IsServer || !MyAPIGateway.Multiplayer.MultiplayerActive) && (Entity as IMyFunctionalBlock).Enabled)
      {
        //If we're currently firing.
        if ((Entity as IMyGunObject<MyGunBase>).IsShooting)
        {
          //Find out where the turret is currently pointing, and cast a ray in that direction, detecting the first entity hit.
          IHitInfo target;
          Vector3D forwardVector = (m_turret as MyEntity).Subparts["GatlingTurretBase1"].Subparts["GatlingTurretBase2"].Subparts["GatlingBarrel"].WorldMatrix.Forward;
          m_target = (m_turret as IMyCubeBlock).PositionComp.GetPosition() + forwardVector * m_maxrange;
          MyAPIGateway.Physics.CastRay(m_turret.PositionComp.GetPosition(),m_target,out target);

          //Do stuff only if we actually hit something.
          if (target != null)
          {
            //The target and the area that we are grinding.
            IMyEntity e = target.HitEntity;
            BoundingSphereD grindSphere = new BoundingSphereD(target.Position,SPHERE_RADIUS);

            //Grind out blocks if the target is a real grid.
            if (e is IMyCubeGrid && e.Physics != null && ((IMyCubeGrid)e != ((IMyCubeBlock)Entity).CubeGrid) && (target.Position - (m_turret as IMyCubeBlock).PositionComp.GetPosition()).Length() < m_maxrange)
            {
              //Grab a list of blocks to grind and the connected inventory where the grinding results go.
              var blocks = (e as IMyCubeGrid).GetBlocksInsideSphere(ref grindSphere); //IMySlimBlock

              foreach (var block in blocks.ToList())
              {
                //If the block isn't fully deconstructed, destroy it a bit.
                if (!block.IsFullyDismounted)
                {
                  block.DecreaseMountLevel(m_speed,m_inventory);
                  block.MoveItemsFromConstructionStockpile(m_inventory);
                }

                //Flow from rexxar's shipyard mod. Thanks!
                //If the block has been fully deconstructed, remove it, taking any inventory items.
                if (block.IsFullyDismounted)
                {
                  //Log.Info("Dismounting block.");
                  var ent = block.FatBlock as MyEntity;

                  //Transfer inventory items.
                  if (ent != null && ent.HasInventory)
                  {
                    //Log.Info("Removing inventory.");
                    for (int i = 0; i < ent.InventoryCount; ++i)
                    {
                      var inv = ent.GetInventory(i);
                      if (inv == null)
                        continue;
                      if (inv.Empty())
                        continue;

                      var items = inv.GetItems();
                      foreach (MyPhysicalInventoryItem item in items.ToList())
                      {
                        (m_inventory as MyInventory).TransferItemsFrom(inv,item, item.Amount);
                        //inv.Remove(item,item.Amount);
                        //inventory.Add(item,item.Amount/2);
                      }
                    }
                  }

                  //Remove block.
                  //Log.Info("Despawning block.");
                  block.SpawnConstructionStockpile();
                  (e as IMyCubeGrid).RazeBlock(block.Position);

                  return;
                }
              }
            }

            //Mine out a piece of the voxel, if the target is a voxel.
            if (e is IMyVoxelBase)
            {
			  //Log.Info("Initiating voxel action.");
			  //MyAPIGateway.Utilities.ShowNotification("Mining into asteroid.", 5000, MyFontEnum.Red);
			  List<IMyVoxelBase> hitVoxels = new List<IMyVoxelBase>();
			  grindSphere = new BoundingSphereD(target.Position,SPHERE_RADIUS + 1);
			  //Log.Info("Finding voxels to cut.");
			  hitVoxels = MyAPIGateway.Session.VoxelMaps.GetAllOverlappingWithSphere(ref grindSphere);
			  
			  foreach (IMyVoxelBase voxel in hitVoxels)
			  {
				CutVoxel(ref grindSphere, voxel);
			  }
            }
          }
        }
      }
    }

    public override void Close()
    {
      //Log.Close();
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
	
	private void CutVoxel(ref BoundingSphereD sphere, IMyVoxelBase voxel)
	{
		//Log.Info("Cutting.");
		//Do this so we don't have to read the entire storage.
		Vector3D minSphere = sphere.Center - sphere.Radius;
		Vector3D maxSphere = sphere.Center + sphere.Radius;
		
		//This doesn't exist for planets.
		//Log.Info("Getting cutting bounds");
		var p1 = new Vector3I(minSphere - voxel.PositionLeftBottomCorner);
		var p2 = new Vector3I(maxSphere - voxel.PositionLeftBottomCorner);
		var min = Vector3I.Min(p1, p2);
        var max = Vector3I.Max(p1, p2);
			  
		//Read in voxel contents.
		//Log.Info("Reading voxel contents.");
		var currStorage = voxel.Storage;
		var localStorage = new MyStorageData();
		localStorage.Resize(min-1, max+1);
		currStorage.ReadRange(localStorage, MyStorageDataTypeFlags.ContentAndMaterial, 0, min-1, max+1);
			  
		//Get the material at each point, transfer it to the inventory, and remove it from the voxel.
		//Log.Info("Modifying voxel contents.");
		Vector3I localCoords = max - min;
		Vector3I p;
		for (p.Z = 0; p.Z <= localCoords.Z; p.Z++)
			for (p.Y = 0; p.Y <= localCoords.Y; p.Y++)
				for (p.X = 0; p.X <= localCoords.X; p.X++)
				{
					//Only remove pieces if the point is inside the aoe of the turret.
					if (sphere.Contains((Vector3D)p + min + voxel.PositionLeftBottomCorner) != ContainmentType.Disjoint)
					{
						//Get the current type of material, and calculate how much is left after this tick.
						//Log.Info("Determining material inside voxel.");
						var material = MyDefinitionManager.Static.GetVoxelMaterialDefinition(localStorage.Material(ref p));
						var currAmount = Convert.ToInt32(localStorage.Content(ref p));
						//var remAmount = 0;
						//Log.Info("Determining amount of material remaining after cut.");
						var remAmount = Math.Max(0, currAmount - DRILL_SPEED);
						try
						{
							MyFixedPoint minedOreAmount = (MyFixedPoint)((float)(currAmount - remAmount)*0.204*material.MinedOreRatio);
							//Transfer to inventory.
							//Log.Info("Transferring material to inventory.");
							var builder = new MyObjectBuilder_Ore(){ SubtypeName = material.MinedOre };
							m_inventory.AddItems(minedOreAmount, builder);
						}
						catch
						{
							MyFixedPoint minedOreAmount = (MyFixedPoint)((float)(currAmount - remAmount)*0.204);
						}
							
						//Clean out asteroid.
						localStorage.Content(ref p, Convert.ToByte(remAmount));
					}
				}
			  
		//Write out result to the asteroid.
		//Log.Info("Writing out to voxel storage.");
		currStorage.WriteRange(localStorage, MyStorageDataTypeFlags.ContentAndMaterial, min-1, max+1);
	}
  }

  //From Digi.
  /*
	public class Log
	{
		private const string MOD_NAME = "SalvageTurret";
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
	*/
}