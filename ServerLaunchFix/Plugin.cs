using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using BepInEx;
using BepInEx.IL2CPP;
using UnityEngine;
using HarmonyLib;
using ProjectM.Shared;
using BepInEx.Unity.IL2CPP;

namespace ServerLaunchFix
{
    [BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
    public class ServerLaunchFixPlugin : BasePlugin
    {
        public static ServerLaunchFixPlugin Instance;
        private Harmony _harmony;
        public override void Load()
        {
            if (ServerLaunchFix.IsClient)
            {
                _harmony = new Harmony(PluginInfo.PLUGIN_GUID);
                _harmony.PatchAll();
            }

            Instance = this;
        }

        public override bool Unload()
        {
            _harmony?.UnpatchSelf();
            return true;
        }
    }

    class ServerLaunchFix
    {
        public static readonly ServerLaunchFix Instance = new();
        private readonly Stack<Dictionary<string, string>> _environmentStack = new();

        public void PushDoorstopEnvironment()
        {
            var doorstopEnv = new Dictionary<string, string>();
            foreach (var key in Environment.GetEnvironmentVariables().Keys.Cast<string>())
            {
                if (!key.StartsWith("DOORSTOP_")) continue;
                doorstopEnv[key] = Environment.GetEnvironmentVariable(key);
                Environment.SetEnvironmentVariable(key, null);
            }
            _environmentStack.Push(doorstopEnv);
        }

        public void PopDoorstopEnvironment()
        {
            var doorstopEnv = _environmentStack.Pop();
            foreach (var (key, val) in doorstopEnv)
            {
                Environment.SetEnvironmentVariable(key, val);
            }
        }

        private static string PrepareServerBepInEx()
        {
            var profileDir = Path.GetDirectoryName(Paths.BepInExRootPath);
            if (profileDir == null)
            {
                ServerLaunchFixPlugin.Instance.Log.LogError("Unable to find BepInEx install dir!");
                ServerLaunchFixPlugin.Instance.Log.LogError("Server mods might not work");
                return null;
            }

            var serverBepInExDir = Path.Combine(profileDir, "BepInEx_Server");
            if (!Directory.Exists(serverBepInExDir))
            {
                Directory.CreateDirectory(serverBepInExDir);
            }
            return Path.Combine(serverBepInExDir, "core", "BepInEx.Unity.IL2CPP.dll");
        }
        
        public static bool IsClient => Application.productName == "VRising";

        public static bool IsServerExe(string filePath)
        {
            return Path.GetFileName(filePath) == "VRisingServer.exe";
        }

        public static string BuildServerLaunchExtraArgs()
        {
            var doorstopTarget = PrepareServerBepInEx();
            if (doorstopTarget != null)
            {
                return $" --doorstop-enable true --doorstop-target-assembly \"{doorstopTarget}\"";
            }

            return "";
        }
    }

    [HarmonyPatch(typeof(StunProcess_PInvoke), nameof(StunProcess_PInvoke.Start))]
    static class ProcessLaunchPatch
    {
        static void Prefix(string fileName, ref string arguments, string dir, bool hidden, bool redirectStandardInput)
        {
            if (ServerLaunchFix.IsServerExe(fileName))
            {
                ServerLaunchFix.Instance.PushDoorstopEnvironment();
                arguments += ServerLaunchFix.BuildServerLaunchExtraArgs();
            }
        }

        static void Postfix(string fileName, ref string arguments, string dir, bool hidden, bool redirectStandardInput)
        {
            if (ServerLaunchFix.IsServerExe(fileName))
            {
                ServerLaunchFix.Instance.PopDoorstopEnvironment();
            }
        }
    }
}
