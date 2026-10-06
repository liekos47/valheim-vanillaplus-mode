using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;

namespace ValheimVanillaPlus
{
    // Backups of the mod's settings file, waypoints file and death history file, saved to
    // BepInEx/config/ValheimVanillaPlus-exports/<date_time>/. Import copies a backup over the
    // current files and reloads them without restarting the game. Only files on your own PC.
    internal static class ConfigBackup
    {
        public static string ExportRoot => Path.Combine(Paths.ConfigPath, "ValheimVanillaPlus-exports");
        private static string CfgFile => VanillaPlusPlugin.ConfigFilePath;
        private static string WaypointsFile => Waypoints.FilePath;
        private static string DeathsFile => DeathLog.FilePath;

        // Marks the backup Import makes of what you had just before it replaced it.
        private const string BeforeImport = "_before-import";

        public static string Export(string suffix = "")
        {
            VanillaPlusPlugin.SaveConfig();
            string dir = Path.Combine(ExportRoot, DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss") + suffix);
            Directory.CreateDirectory(dir);
            File.Copy(CfgFile, Path.Combine(dir, Path.GetFileName(CfgFile)), true);
            if (File.Exists(WaypointsFile)) File.Copy(WaypointsFile, Path.Combine(dir, Path.GetFileName(WaypointsFile)), true);
            if (File.Exists(DeathsFile)) File.Copy(DeathsFile, Path.Combine(dir, Path.GetFileName(DeathsFile)), true);
            VanillaPlusPlugin.Log.LogInfo($"Config exported to {dir}");
            Invalidate();
            return Path.GetFileName(dir);
        }

        private static List<string> _cache = new List<string>();
        private static float _cacheTime = -999f;

        // Newest first. Cached for 2 s (the menu redraws several times per frame).
        public static List<string> Exports()
        {
            if (UnityEngine.Time.unscaledTime - _cacheTime < 2f) return _cache;
            _cacheTime = UnityEngine.Time.unscaledTime;
            _cache = Directory.Exists(ExportRoot)
                ? Directory.GetDirectories(ExportRoot)
                    .Where(d => File.Exists(Path.Combine(d, Path.GetFileName(CfgFile))))
                    .OrderByDescending(d => d).Select(Path.GetFileName).ToList()
                : new List<string>();
            return _cache;
        }

        private static void Invalidate() => _cacheTime = -999f;

        // Does this backup hold waypoints too?
        public static bool HasWaypoints(string name) =>
            File.Exists(Path.Combine(ExportRoot, name, Path.GetFileName(WaypointsFile)));

        // Returns the name of the backup made of your current settings and waypoints before they
        // were replaced, or null when there was nothing to import.
        public static string Import(string name)
        {
            string dir = Path.Combine(ExportRoot, name);
            string cfg = Path.Combine(dir, Path.GetFileName(CfgFile));
            if (!File.Exists(cfg)) return null;

            string saved = Export(BeforeImport); // so an import can always be undone
            File.Copy(cfg, CfgFile, true);
            string waypoints = Path.Combine(dir, Path.GetFileName(WaypointsFile));
            if (File.Exists(waypoints)) File.Copy(waypoints, WaypointsFile, true);

            string deaths = Path.Combine(dir, Path.GetFileName(DeathsFile));
            if (File.Exists(deaths)) File.Copy(deaths, DeathsFile, true);

            VanillaPlusPlugin.ReloadConfig();
            Waypoints.Reload();
            DeathLog.Load();
            VanillaPlusPlugin.Log.LogInfo($"Config imported from {dir} (what you had is saved as {saved})");
            Invalidate();
            return saved;
        }

        public static void Delete(string name)
        {
            string dir = Path.Combine(ExportRoot, name);
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            VanillaPlusPlugin.Log.LogInfo($"Config backup deleted: {dir}");
            Invalidate();
        }

        public static void OpenFolder()
        {
            Directory.CreateDirectory(ExportRoot);
            System.Diagnostics.Process.Start("explorer.exe", $"\"{ExportRoot}\"");
        }
    }
}
