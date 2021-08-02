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
	//From Digi.
	public class Log
	{
		private const string MOD_NAME = "Economics";
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