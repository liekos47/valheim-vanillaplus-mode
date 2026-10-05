using System;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using Mono.Cecil;
using UnityEngine;

namespace ValheimVanillaPlus
{
    // Hot reload: when a new build of this plugin lands in BepInEx/plugins while the game is running,
    // it is loaded in place of the running one, without restarting Valheim.
    //   1. The plugin's file is watched (its write time, once a second). A change that has been stable
    //      for a second is a finished build.
    //   2. The new DLL is read into memory and given a unique assembly name (Mono would otherwise hand
    //      back the copy it already has), then loaded.
    //   3. The running plugin shuts itself down (VanillaPlusPlugin.Teardown: what it added to the game's
    //      screens is taken out again, then all Harmony patches are removed).
    //   4. The new plugin is added to the same BepInEx object and starts up as on a normal launch,
    //      reading the same config file.
    // The old code stays in memory, unused, until the game closes (.NET can't unload it), so a long
    // session with many reloads slowly uses a little more memory. Anything the old build left behind
    // that it doesn't know how to undo needs a real restart; if something looks off after a reload,
    // restart once before reporting it.
    internal static class HotReload
    {
        private static string DllPath => Path.Combine(Paths.PluginPath, "ValheimVanillaPlus", "ValheimVanillaPlus.dll");
        private static DateTime _loaded, _pending;
        private static float _nextCheck, _pendingSince;

        public static void Init()
        {
            try { _loaded = File.GetLastWriteTimeUtc(DllPath); } catch { }
        }

        // True when a new, finished build is waiting.
        public static bool Due()
        {
            if (!VanillaPlusPlugin.HotReloadEnabled.Value || Time.unscaledTime < _nextCheck) return false;
            _nextCheck = Time.unscaledTime + 1f;
            DateTime now;
            try { now = File.GetLastWriteTimeUtc(DllPath); } catch { return false; }
            if (now == _loaded) return false;
            if (now != _pending) { _pending = now; _pendingSince = Time.unscaledTime; return false; }
            return Time.unscaledTime - _pendingSince >= 1f;
        }

        public static void Reload(VanillaPlusPlugin old)
        {
            Type type;
            try
            {
                byte[] bytes = File.ReadAllBytes(DllPath);
                _loaded = File.GetLastWriteTimeUtc(DllPath);
                using (var definition = AssemblyDefinition.ReadAssembly(new MemoryStream(bytes)))
                using (var renamed = new MemoryStream())
                {
                    definition.Name.Name += "-" + DateTime.Now.Ticks;
                    definition.Write(renamed);
                    var assembly = Assembly.Load(renamed.ToArray());
                    type = assembly.GetTypes().First(t => typeof(BaseUnityPlugin).IsAssignableFrom(t) && !t.IsAbstract);
                }
            }
            catch (Exception e)
            {
                // Nothing has been touched yet: the running build carries on.
                VanillaPlusPlugin.Log.LogWarning($"Hot reload: couldn't load the new build, keeping the running one ({e.GetType().Name}: {e.Message})");
                return;
            }

            VanillaPlusPlugin.Log.LogInfo($"Hot reload: switching to the build from {_loaded.ToLocalTime():HH:mm:ss}");
            var host = old.gameObject;
            old.Teardown();
            UnityEngine.Object.Destroy(old);
            try { host.AddComponent(type); }
            catch (Exception e) { Debug.LogError($"[Valheim Vanilla Plus] Hot reload failed while starting the new build - restart Valheim. {e}"); }
        }
    }
}
