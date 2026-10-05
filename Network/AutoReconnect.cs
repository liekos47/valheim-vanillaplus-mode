using HarmonyLib;
using UnityEngine;

namespace ValheimVanillaPlus
{
    // Auto reconnect: when a server session ends with "disconnected", the
    // main menu counts down [AutoReconnect] DelaySeconds and joins the same server again with the same
    // character, up to MaxAttempts times (failed attempts count too). The server password you typed is
    // remembered in memory only (never written anywhere) and re-entered for you. Never retries a kick, a ban,
    // a full server, a wrong version or a wrong password. Press Cancel (or Esc) to stop.
    internal static class AutoReconnect
    {
        private static readonly AccessTools.FieldRef<FejdStartup, ServerJoinData> JoinServerField =
            AccessTools.FieldRefAccess<FejdStartup, ServerJoinData>("m_joinServer");
        private static readonly System.Reflection.MethodInfo SetServerPassword =
            AccessTools.PropertySetter(typeof(FejdStartup), nameof(FejdStartup.ServerPassword));

        private static ServerJoinData _last = ServerJoinData.None;
        internal static string Password; // memory only
        private static bool _pending;
        private static float _at;
        private static int _attempts;
        private static string _reason = "";
        private static GUIStyle _style;

        // Remember the server every time you join one.
        internal static void OnJoin(FejdStartup fs)
        {
            var data = JoinServerField(fs);
            if (data.IsValid) _last = data;
        }

        // Called when the main menu shows (or would show) a connection error.
        internal static void OnConnectError(FejdStartup fs, ZNet.ConnectionStatus status)
        {
            if (!VanillaPlusPlugin.AutoReconnectOn || !_last.IsValid) return;
            bool retry = status == ZNet.ConnectionStatus.ErrorDisconnected                || (status == ZNet.ConnectionStatus.ErrorConnectFailed && _attempts > 0); // server still restarting
            if (status == ZNet.ConnectionStatus.ErrorPassword) Password = null; // don't keep a wrong one
            if (!retry) { if (status != ZNet.ConnectionStatus.None) Stop(); return; }
            if (_attempts >= VanillaPlusPlugin.AutoReconnectMaxAttempts.Value) { _reason = "Gave up reconnecting"; Stop(); return; }

            _pending = true;
            _reason = status == ZNet.ConnectionStatus.ErrorConnectFailed ? "Couldn't connect" : "Disconnected";
            _at = Time.unscaledTime + Mathf.Max(3f, VanillaPlusPlugin.AutoReconnectDelay.Value);
            VanillaPlusPlugin.Log.LogInfo($"Auto reconnect: {_reason} ({status}); retry {_attempts + 1}/{VanillaPlusPlugin.AutoReconnectMaxAttempts.Value} in {VanillaPlusPlugin.AutoReconnectDelay.Value:0} s");
        }

        // Once in a world again, the next disconnect starts a fresh set of attempts.
        internal static void OnInWorld() { if (!_pending) _attempts = 0; }

        public static void Update()
        {
            if (!_pending) return;
            var fs = FejdStartup.instance;
            if (fs == null || !VanillaPlusPlugin.AutoReconnectOn || Input.GetKeyDown(KeyCode.Escape)) { Stop(); return; }
            if (Time.unscaledTime < _at) return;

            _pending = false;
            _attempts++;
            VanillaPlusPlugin.Log.LogInfo($"Auto reconnect: joining again (attempt {_attempts})");
            fs.m_connectionFailedPanel.SetActive(false);
            if (!string.IsNullOrEmpty(Password)) SetServerPassword?.Invoke(null, new object[] { Password });
            fs.SetServerToJoin(_last);
            fs.JoinServer();
        }

        private static void Stop() { _pending = false; _attempts = 0; }

        public static void Draw()
        {
            if (!_pending || FejdStartup.instance == null) return;
            if (_style == null) _style = new GUIStyle(GUI.skin.box) { fontSize = 18, alignment = TextAnchor.MiddleCenter };
            float left = Mathf.Max(0f, _at - Time.unscaledTime);
            var r = new Rect(Screen.width / 2f - 230f, 30f, 460f, 44f);
            GUI.Box(r, $"{_reason} — reconnecting in {left:0} s (attempt {_attempts + 1}/{VanillaPlusPlugin.AutoReconnectMaxAttempts.Value})", _style);
            if (GUI.Button(new Rect(r.center.x - 50f, r.yMax + 4f, 100f, 26f), "Cancel")) Stop();
        }
    }

    [HarmonyPatch(typeof(FejdStartup), nameof(FejdStartup.JoinServer))]
    internal static class AutoReconnect_Join
    {
        private static void Prefix(FejdStartup __instance) => AutoReconnect.OnJoin(__instance);
    }

    [HarmonyPatch(typeof(FejdStartup), "ShowConnectError")]
    internal static class AutoReconnect_Error
    {
        private static void Postfix(FejdStartup __instance, ZNet.ConnectionStatus statusOverride) =>
            AutoReconnect.OnConnectError(__instance,
                statusOverride == ZNet.ConnectionStatus.None ? ZNet.GetConnectionStatus() : statusOverride);
    }

    [HarmonyPatch(typeof(ZNet), "OnPasswordEntered")]
    internal static class AutoReconnect_Password
    {
        private static void Prefix(string pwd) { if (!string.IsNullOrEmpty(pwd)) AutoReconnect.Password = pwd; }
    }
}
