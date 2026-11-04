using System;
using System.IO;
using UnityEngine;

namespace LastBell.Run.Networking
{
    // Owns one started host session across the scene transition. The menu/session owner is responsible for
    // creating this before launching the selected map and surfacing Error/disposing it on disconnect or return.
    [DefaultExecutionOrder(-1000)]
    public sealed class LanHostFightPump : MonoBehaviour
    {
        public LanLobby Lobby { get; private set; }
        public LanHostRoster Roster { get; private set; }
        public LanFxCapture Fx { get; private set; }   // recorded effect calls, sent with the next snapshot
        public string Error { get; private set; }
        // World ticks restart when the host retries, so the wire uses a session sequence that only increases.
        CombatWorld sentWorld; int lastSent = -3, sequence;
        // 30 snapshots a second (every 2 ticks of 60): clients smooth between them (LanClientWorld).
        const int SendEveryTicks = 2;
        readonly System.Diagnostics.Stopwatch costWatch = new System.Diagnostics.Stopwatch();
        double sendMs; int statFrames, statSent; long statBytes; float statSince = -1;
        public static LanHostFightPump Create(LanLobby lobby)
        {
            var roster = new LanHostRoster(lobby);
            try
            {
                var go = new GameObject("LAN host session"); DontDestroyOnLoad(go);
                var pump = go.AddComponent<LanHostFightPump>(); pump.Lobby = lobby; pump.Roster = roster; pump.Fx = new LanFxCapture(roster); return pump;
            }
            catch { roster.Dispose(); throw; }
        }
        void Update()
        {
            if (Error != null || Lobby == null) return;
            Lobby.Pump();
            Error = Lobby.Error ?? Roster.Error;
        }
        void LateUpdate()
        {
            if (Error != null || Lobby == null) return;
            Stats();
            var d = RunDirector.Instance;
            if (!d || d.World == null) return;
            if (d.World != sentWorld) { sentWorld = d.World; lastSent = int.MinValue / 2; Fx?.Clear(); }   // a retry's effects start fresh
            if (d.World.Tick - lastSent < SendEveryTicks) return;
            LanWorldFrame state = null;
            costWatch.Restart();
            try
            {
                state = LanWorldCapture.Capture(d, Roster); Fx?.Drain(state.Effects);
                byte[] bytes;
                try { bytes = state.Encode(); }
                catch (InvalidDataException) when (state.Effects.Count > 0)
                {
                    // too large or an odd value: drop this batch of effects rather than the session
                    Debug.LogWarning("[LAN] dropped " + state.Effects.Count + " effects that did not fit a snapshot");
                    state.Effects.Clear(); bytes = state.Encode();
                }
                if (!Lobby.SendWorld(sequence, bytes)) Error = Lobby.Error ?? "Could not send the fight state.";
                else { lastSent = state.Tick; sequence++; statSent++; statBytes += bytes.Length; }
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is InvalidDataException || ex is ArgumentException)
            {
                Error = "Could not capture the fight state: " + ex.Message;
                Debug.LogWarning("[LAN] " + Error + " " + Describe(state));
            }
            finally { sendMs += costWatch.Elapsed.TotalMilliseconds; }
        }
        // performance line in the player log every 5 s: frame rate, snapshots sent and their size, capture + send cost
        void Stats()
        {
            float now = Time.unscaledTime; statFrames++;
            if (statSince < 0) { statSince = now; return; }
            float span = now - statSince; if (span < 5) return;
            Debug.Log($"[LAN] host: {statFrames / span:0} fps, {statSent / span:0} snapshots/s, {(statSent > 0 ? statBytes / statSent : 0)} bytes each, capture+send {sendMs / span:0.0} ms/s");
            statSince = now; statFrames = statSent = 0; statBytes = 0; sendMs = 0;
        }
        // Diagnostic summary of a snapshot the encoder rejected.
        static string Describe(LanWorldFrame f)
        {
            if (f == null) return "(no snapshot)";
            var text = new System.Text.StringBuilder();
            text.Append("tick ").Append(f.Tick).Append(" state ").Append(f.RunState).Append(" stateTicks ").Append(f.StateTicks);
            if (f.Boss != null) { var b = f.Boss; text.Append(" | boss hp ").Append(b.Hp).Append('/').Append(b.MaxHp).Append(" pos ").Append(b.X).Append(',').Append(b.Y).Append(',').Append(b.Z).Append(" yaw ").Append(b.Yaw).Append(" pose ").Append(b.PoseKind).Append(':').Append(b.Pose).Append(" clipSpeed ").Append(b.ClipSpeed).Append('/').Append(b.OverlaySpeed).Append(" text ").Append((b.Phase ?? "").Length).Append(',').Append((b.Attack ?? "").Length).Append(',').Append((b.Extra ?? "").Length).Append(',').Append((b.Clip ?? "").Length).Append(',').Append((b.Overlay ?? "").Length); }
            foreach (var p in f.Players) text.Append(" | p").Append(p.Id).Append(" hp ").Append(p.Hp).Append('/').Append(p.MaxHp).Append(" pos ").Append(p.X).Append(',').Append(p.Y).Append(',').Append(p.Z).Append(" h ").Append(p.Height).Append(" st ").Append(p.Stamina).Append('/').Append(p.MaxStamina).Append(" pot ").Append(p.Potions).Append(" marks ").Append(p.Marks.Count).Append(" clipSpeed ").Append(p.ClipSpeed).Append('/').Append(p.OverlaySpeed).Append(" cd ").Append(string.Join(",", p.Cooldowns));
            text.Append(" | hazards ").Append(f.Hazards.Count).Append(" telegraphs ").Append(f.Telegraphs.Count).Append(" projectiles ").Append(f.Projectiles.Count);
            return text.ToString();
        }
        void OnDestroy()
        {
            Fx?.Dispose(); Fx = null;
            Roster?.Dispose(); Roster = null;
            Lobby?.Dispose(); Lobby = null;
        }
    }
}
