using System;
using System.Collections.Generic;
using System.Linq;
using LastBell.VFX;
using UnityEngine;

namespace LastBell.Run.Networking
{
    // Read-only representation of the host's world. It does not run CombatWorld.Step or boss scripts. The session
    // owner creates it after loading the selected arena and is responsible for surfacing errors and returning to menu.
    // The idle RunDirector holds the replica (AttachLanReplica) so the normal RunHud draws it, fed by host snapshots.
    // Snapshots arrive about 30 times a second. Each actor keeps a short history stamped with the host's tick and is drawn
    // a little in the past (about 1.5 snapshot intervals), between the two snapshots around that moment, so motion stays
    // continuous at any frame rate instead of stepping at the network rate.
    public sealed class LanClientWorld : MonoBehaviour
    {
        sealed class Track
        {
            struct Sample { public double T; public Vector3 P; public float Yaw, H; }
            readonly List<Sample> samples = new List<Sample>();
            public bool Ready => samples.Count > 0;
            public void Clear() => samples.Clear();
            // t: host time in seconds (tick / 60). A jump further than TeleportDistance starts the history again (snap).
            public void Add(double t, Vector3 p, float yaw, float h)
            {
                if (samples.Count > 0) { var last = samples[samples.Count - 1]; if (t <= last.T) return; if ((p - last.P).sqrMagnitude > TeleportDistance * TeleportDistance) samples.Clear(); }
                samples.Add(new Sample { T = t, P = p, Yaw = yaw, H = h });
                if (samples.Count > 32) samples.RemoveAt(0);
            }
            public void At(double t, out Vector3 p, out float yaw, out float h)
            {
                int n = samples.Count; var first = samples[0]; var last = samples[n - 1];
                if (n == 1 || t <= first.T) { p = first.P; yaw = first.Yaw; h = first.H; return; }
                if (t >= last.T) { p = last.P; yaw = last.Yaw; h = last.H; return; }
                int i = 0; while (i < n - 2 && samples[i + 1].T <= t) i++;
                var a = samples[i]; var b = samples[i + 1]; float u = (float)((t - a.T) / Math.Max(1e-6, b.T - a.T));
                p = Vector3.Lerp(a.P, b.P, u); yaw = Mathf.LerpAngle(a.Yaw, b.Yaw, u); h = Mathf.Lerp(a.H, b.H, u);
                if (i > 1) samples.RemoveRange(0, i - 1);   // keep one sample behind the pair in use
            }
        }
        sealed class Actor { public PlayerController Player; public bool Down; public readonly Track Track = new Track(); public float ShownHeight; public Vector3 LastSnapshot; }
        const float TeleportDistance = 4f;
        readonly Dictionary<int, Actor> actors = new Dictionary<int, Actor>();
        readonly Track bossTrack = new Track();
        struct BeatSample { public double T, Beat; public bool Haste, Transitioning; public int TransitionTick, StateAge; }
        readonly List<BeatSample> beats = new List<BeatSample>();   // boss TempoClock samples by host time (Tempo Tyrant, Oracle)
        // boss-drawn moving objects (orbs, thrown weights): one view each, moved along the host's positions
        sealed class PropView { public Transform View; public byte Kind; public readonly Track Track = new Track(); public double GoneAt = -1; }
        readonly Dictionary<int, PropView> props = new Dictionary<int, PropView>();
        // stand-ins for the host's hazards, for effects that watch one (Aurelion's Zenith Collapse, Orrery Closing, Last Light)
        sealed class HazardMirror { public Hazard H; public double T; public int Age; public double GoneAt = -1; }
        readonly Dictionary<int, HazardMirror> mirrors = new Dictionary<int, HazardMirror>();
        LastLightFloorFx lastLight;
        public int PropsShown { get; private set; }            // diagnostics: most boss props drawn at once
        public int ReplicaEffects { get; private set; }        // diagnostics: Aurelion effects started from the host's calls
        double shownBeat; bool beatSet, shownHaste;   // the beat given to the replica's clock: smooth, so the music never chases jitter
        // Own-movement prediction: this client's character is drawn where the host has it plus the moves the host has not
        // applied yet (walk or sprint at the host's current speed factor), and eased onto the host's answer. Dashes, hits
        // and knockback still come from the host.
        struct SentMove { public int Seq; public Vector2 Move; public bool Sprint; }
        readonly List<SentMove> unacked = new List<SentMove>();
        Vector3 predictedShown; bool predictedReady;
        public float PredictionLead { get; private set; }   // diagnostics: how far ahead of the host's last state it draws
        double clockOffset; bool clockSet;   // local time minus host time, smoothed
        LanLobby lobby; CombatWorld world; BossEncounter boss; CharacterPresenter bossPresenter; BellkeeperFx fx; PlayerFx playerFx;
        LanFxReplay fxReplay;   // the host's recorded effect calls, replayed on this replica
        LanHazardView hazardView; KeyboardMouseInput localInput; LanWorldFrame current; RunDirector director; RunHud hud;
        // performance line in the player log every 5 s: frame rate, snapshot rate and size, time spent applying and drawing
        readonly System.Diagnostics.Stopwatch costWatch = new System.Diagnostics.Stopwatch();
        double receiveMs, presentMs; int statFrames, statSnapshots; long statBytes; float statSince = -1;
        Transform root; Camera cam; float inputAccumulator, lastArrival = -1, interval = 1f / 30; int inputSequence; string error;
        static readonly bool SmokeInput = Array.Exists(Environment.GetCommandLineArgs(), a => a.Equals("-lastbellLanSmokeClient", StringComparison.OrdinalIgnoreCase));
        public string Error => error ?? lobby?.Error;
        public LanWorldFrame Current => current;
        public LanFxReplay Effects => fxReplay;
        public int LookFailures { get; private set; }
        public static LanClientWorld Create(LanLobby lobby)
        {
            if (lobby == null || lobby.IsHost || !lobby.FightStarted || !lobby.Connected) throw new InvalidOperationException("A started client lobby is required.");
            var go = new GameObject("LAN client view");
            var view = go.AddComponent<LanClientWorld>();
            try { view.Initialize(lobby); return view; }
            catch { Destroy(go); throw; }
        }
        void Initialize(LanLobby session)
        {
            lobby = session;
            director = RunDirector.Instance;
            if (!director || director.World != null) throw new InvalidOperationException("An idle run scene is required.");
            var shop = director.GetComponent<Shop.RunShopFlow>(); if (shop) shop.enabled = false;
            director.enabled = false;
            root = new GameObject("LAN replica world").transform;
            fx = BellkeeperFx.Create(); fx.transform.SetParent(root, false);
            playerFx = PlayerFx.Create(root);
            world = new CombatWorld(root, fx, playerFx);
            foreach (var member in lobby.Members.OrderBy(m => m.Id))
            {
                var player = PlayerController.Spawn(world, member.ClassName, Vector3.zero, 180);
                // the replica is drawn from snapshots, not stepped: the render smoother would fight the interpolation below
                var smoother = player.GetComponent<SimSmoother>(); if (smoother) smoother.enabled = false;
                actors.Add(member.Id, new Actor { Player = player });
            }
            if (!actors.TryGetValue(lobby.LocalId, out var local)) throw new InvalidOperationException("Local player is absent from the roster.");
            director.AttachLanReplica(world, local.Player, actors.Where(a => a.Key != lobby.LocalId).OrderBy(a => a.Key).Select(a => a.Value.Player));
            hud = director.GetComponent<RunHud>();
            RunHud.LanClientView = true;
            RunHud.LanLeave = () => { if (LanSessionCoordinator.Instance) LanSessionCoordinator.Instance.Leave(); };
            if (hud) hud.enabled = true;
            fxReplay = new LanFxReplay(playerFx, fx, id => actors.TryGetValue(id, out var a) ? a.Player : null, () => boss) { Replica = PlayReplica };
            hazardView = new LanHazardView(fx); localInput = new KeyboardMouseInput(); KeyboardMouseInput.Blocked = false;
            cam = Camera.main;
            lobby.WorldReceived += Receive;
            BossEncounter.NetIdOf = p => { foreach (var a in actors) if (a.Value.Player == p) return a.Key; return -1; };
            BossEncounter.NetPlayerOf = id => actors.TryGetValue(id, out var a) ? a.Player : null;
        }
        void Receive(int tick, byte[] payload)
        {
            costWatch.Restart(); statSnapshots++; statBytes += payload != null ? payload.Length : 0;
            try { ReceiveFrame(payload); } finally { receiveMs += costWatch.Elapsed.TotalMilliseconds; }
        }
        void ReceiveFrame(byte[] payload)
        {
            // the envelope tick is the host's session sequence; frame.Tick restarts from zero after a retry.
            if (!LanWorldFrame.TryDecode(payload, out var frame) || frame.Players.Count != actors.Count || frame.Players.Any(p => !actors.ContainsKey(p.Id)))
            { error = "Invalid fight state from host."; return; }
            float now = Time.unscaledTime;
            if (lastArrival >= 0) interval = Mathf.Clamp(Mathf.Lerp(interval, now - lastArrival, 0.2f), 1f / 60, 0.25f);
            lastArrival = now;
            // a retry restarts the host's world tick: start every history again
            bool restarted = current != null && frame.Tick < current.Tick;
            if (restarted) { foreach (var a in actors.Values) a.Track.Clear(); bossTrack.Clear(); beats.Clear(); beatSet = false; clockSet = false; fxReplay.Reset(); playerFx.ResetAll(); ClearProps(); ClearMirrors(); }
            fxReplay.Enqueue(frame.Effects);
            double hostTime = frame.Tick * (double)Sim.Dt, offset = now - hostTime;
            if (!clockSet || Math.Abs(offset - clockOffset) > 0.5) { clockOffset = offset; clockSet = true; } else clockOffset += (offset - clockOffset) * 0.05;
            int elapsedTicks = current == null || restarted ? 0 : Mathf.Clamp(frame.Tick-current.Tick,0,6); current = frame;
            foreach (var state in frame.Players)
            {
                var actor = actors[state.Id]; var p = actor.Player; Vector3 pos = new Vector3(state.X, state.Y, state.Z);
                actor.Track.Add(hostTime, pos, state.Yaw, state.Height);
                p.Presenter.Locomotion = elapsedTicks > 0 ? Vector3.Distance(actor.LastSnapshot, pos) / (elapsedTicks * Sim.Dt) : 0;
                actor.LastSnapshot = pos;
                p.ApplyNetworkState(state.Hp, state.MaxHp, state.Stamina, state.MaxStamina, state.Potions, state.Down);
                p.Kit.NetCooldowns = state.Cooldowns; p.Kit.NetSlotStates = state.SlotStates; p.Kit.NetClassState = state.ClassState;
                p.Marks.Clear(); p.Marks.AddRange(state.Marks);
                if (state.Down != actor.Down) { if (state.Down) p.Presenter.Down(); else p.Presenter.GetUp(); actor.Down = state.Down; }
                p.Presenter.ApplyNetworkPose(state.Clip,state.ClipSpeed,state.Overlay,state.OverlaySpeed,state.Hidden);
                p.Presenter.Tick(p);
            }
            if (frame.Boss != null)
            {
                if (!boss)
                {
                    int index = Array.FindIndex(RunDirector.Bosses, b => b.Name.StartsWith(lobby.Encounter, StringComparison.Ordinal));
                    if (index < 0) { error = "Encounter missing from this build."; return; }
                    boss = RunDirector.Bosses[index].Spawn(world); boss.enabled = false; world.Boss = boss; bossPresenter = boss.GetComponentInChildren<CharacterPresenter>(true);
                    var smoother = boss.GetComponent<SimSmoother>(); if (smoother) smoother.enabled = false;
                }
                var b = frame.Boss;
                bossTrack.Add(hostTime, new Vector3(b.X, b.Y, b.Z), b.Yaw, 0);
                if (b.PoseKind == 1 || b.PoseKind == 2)
                {
                    beats.Add(new BeatSample { T = hostTime, Beat = b.Beat, Haste = b.BeatHaste, Transitioning = b.Transitioning, TransitionTick = b.TransitionTick, StateAge = b.StateAge });
                    if (beats.Count > 120) beats.RemoveRange(0, beats.Count - 120);
                }
                if (boss is TempoTyrantEncounter hud) hud.ApplyReplicaHud(b.Frenzy, b.Announcement, b.Numbers);
                else if (boss is ChimeboundOracleEncounter enraged) enraged.ApplyReplica(b.Frenzy);
                ReceiveProps(hostTime, b.Props);
                boss.ApplyNetworkState(b.Hp, b.MaxHp, b.Dead, b.Phase, b.Attack, b.Extra);
                if (bossPresenter) bossPresenter.ApplyNetworkPose(b.Clip,b.ClipSpeed,b.Overlay,b.OverlaySpeed,b.Hidden);
                if (b.PoseKind==1 && boss is TempoTyrantEncounter tyrant && tyrant.Rig && Enum.IsDefined(typeof(TyrantPose),b.Pose))
                {
                    tyrant.Rig.Haste=b.Haste; tyrant.Rig.Frenzy=b.Frenzy;
                    if ((int)tyrant.Rig.Current!=b.Pose) tyrant.Rig.Pose((TyrantPose)b.Pose);
                    for(int i=0;i<elapsedTicks;i++)tyrant.Rig.Step();
                }
                else if (b.PoseKind==2 && boss is ChimeboundOracleEncounter oracle && oracle.Rig && Enum.IsDefined(typeof(OraclePose),b.Pose))
                {
                    oracle.Rig.Haste=b.Haste;
                    if ((int)oracle.Rig.Current!=b.Pose) oracle.Rig.Pose((OraclePose)b.Pose);
                    for(int i=0;i<elapsedTicks;i++)oracle.Rig.Step();
                }
            }
            director.ApplyLanState(Enum.IsDefined(typeof(RunState), frame.RunState) ? (RunState)frame.RunState : RunState.Combat, frame.StateTicks, frame.Victory, boss);
            ReceiveMirrors(hostTime, frame.Hazards);
            hazardView.Apply(frame);   // projectiles are drawn by the host's own recorded effect calls (LanFxReplay)
        }
        void Update()
        {
            if (Error != null || lobby == null) return;
            lobby.Pump(); if (Error != null || !actors.TryGetValue(lobby.LocalId, out var local)) return;
            costWatch.Restart(); Present(); presentMs += costWatch.Elapsed.TotalMilliseconds;
            Stats();
            // Input is useless until the host's fight is running, and sending it while the host is still loading only fills its queue.
            if (current == null) { inputAccumulator = 0; return; }
            localInput.Poll(); inputAccumulator += Mathf.Min(Time.deltaTime, .1f);
            while (inputAccumulator >= Sim.Dt)
            {
                inputAccumulator -= Sim.Dt;
                var intent = localInput.Read(local.Player);
                intent.Move = WorldMove(intent.Move);
                if (SmokeInput) intent = SmokeIntent(local.Player);
                unacked.Add(new SentMove { Seq = inputSequence, Move = intent.Move, Sprint = intent.Sprint });
                if (unacked.Count > 240) unacked.RemoveAt(0);
                var packet = LanUnityInput.Encode(intent);
                if (!packet.Valid || !lobby.SendInput(inputSequence++, packet.Encode())) { error = "Could not send player input."; return; }
            }
        }
        // WASD is camera-relative on screen; the host applies it as world x / z (PlayerInput.WorldMove), so turn it with this
        // client's camera, not the host's.
        Vector2 WorldMove(Vector2 screen)
        {
            if (screen.sqrMagnitude < 1e-6f) return Vector2.zero;
            var c = cam ? cam : Camera.main;
            Vector3 fwd = c ? Sim.Flat(c.transform.forward).normalized : Vector3.forward;
            if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
            Vector3 right = Vector3.Cross(Vector3.up, fwd), move = fwd * screen.y + right * screen.x;
            return Vector2.ClampMagnitude(new Vector2(move.x, move.z), 1);
        }
        // Automated client: walk to the boss and use every ability slot in turn, so the host can check class actions arrive.
        PlayerInput SmokeIntent(PlayerController me)
        {
            var intent = default(PlayerInput);
            if (current == null || current.Boss == null) return intent;
            var target = new Vector3(current.Boss.X, current.Boss.Y, current.Boss.Z);
            intent.Aim = target;
            var to = target - me.transform.position; to.y = 0;
            if (to.magnitude > 6) intent.Move = new Vector2(to.x, to.z).normalized;
            else if (inputSequence % 120 < 60) intent.Move = new Vector2(-to.z, to.x).normalized;   // strafe around the boss half the time, so prediction and smoothing see movement
            int phase = inputSequence % 120;
            intent.P0 = phase % 24 == 0;
            intent.P1 = phase == 30; intent.H1 = phase >= 30 && phase < 55;   // Disintegrate is a held beam
            intent.P2 = phase == 60; intent.P3 = phase == 90; intent.P4 = phase == 110;
            return intent;
        }
        void Stats()
        {
            float now = Time.unscaledTime; statFrames++;
            if (statSince < 0) { statSince = now; return; }
            float span = now - statSince; if (span < 5) return;
            Debug.Log($"[LAN] client: {statFrames / span:0} fps, {statSnapshots / span:0} snapshots/s, {(statSnapshots > 0 ? statBytes / statSnapshots : 0)} bytes each, apply {receiveMs / span:0.0} ms/s, draw {presentMs / span:0.0} ms/s");
            statSince = now; statFrames = statSnapshots = 0; statBytes = 0; receiveMs = presentMs = 0;
        }
        // Draw every actor between its previous and newest snapshot pose.
        void Present()
        {
            if (!clockSet) return;
            // draw this far behind the newest snapshot: 1.5 intervals covers a late packet without stepping
            double delay = Mathf.Clamp(interval * 1.5f + 0.01f, 0.03f, 0.35f), renderTime = Time.unscaledTime - clockOffset - delay;
            foreach (var pair in actors)
            {
                var actor = pair.Value;
                if (!actor.Track.Ready) continue;
                actor.Track.At(renderTime, out var pos, out float yaw, out float h);
                if (pair.Key == lobby.LocalId && Predict(out var predicted)) { pos = predicted; actor.Track.At(double.MaxValue, out _, out yaw, out _); }
                actor.Player.transform.SetPositionAndRotation(pos, Quaternion.Euler(0, yaw, 0));
                actor.ShownHeight = h; actor.Player.Presenter.HeightOffset = h;
            }
            if (boss && bossTrack.Ready)
            {
                bossTrack.At(renderTime, out var pos, out float yaw, out _);
                boss.transform.SetPositionAndRotation(pos, Quaternion.Euler(0, yaw, 0));
            }
            // the Tempo Tyrant's music follows its clock: give the replica the host's beat at the drawn time, so the
            // song lines up with what this screen shows (the beat runs on linearly between the host's samples)
            var clock = boss is TempoTyrantEncounter tyrant ? tyrant.Clock : boss is ChimeboundOracleEncounter oracle ? oracle.Clock : null;
            if (clock != null && beats.Count > 0)
            {
                int i = beats.Count - 1; while (i > 0 && beats[i].T > renderTime) i--;
                var s = beats[i]; double bpm = s.Haste ? clock.HasteBpm : clock.NormalBpm;
                double target = s.Beat + Math.Max(0, renderTime - s.T) * bpm / 60.0;
                // runs on at the tempo by wall time and eases toward the drawn beat (the drawn time wobbles with packet
                // timing); jumps only on a tempo change, where the music crossfades anyway, or when far off
                if (!beatSet || s.Haste != shownHaste || Math.Abs(target - shownBeat) > 0.5) { shownBeat = target; shownHaste = s.Haste; beatSet = true; }
                else { float dt = Time.unscaledDeltaTime; shownBeat += dt * bpm / 60.0; shownBeat += (target - shownBeat) * (1 - Math.Exp(-dt / 1.5)); }
                int age = s.StateAge + (int)Math.Max(0, Math.Floor((renderTime - s.T) / Sim.Dt));
                clock.ApplyReplica(shownHaste ? Tempo.Haste : Tempo.Normal, shownBeat, s.Transitioning, s.TransitionTick, age);
            }
            PresentProps(renderTime); PresentMirrors(renderTime);
            // the boss's own looks (glow, phase looks, poses, overlays), from the newest host state
            if (boss && current?.Boss != null)
            {
                try { boss.ReplicaPresent(current.Boss, Time.deltaTime); }
                catch (Exception ex) { LookFailures++; if (LookFailures <= 5) Debug.LogWarning("[LAN] boss looks: " + ex); }
            }
            // effects play when the drawn timeline reaches the tick the host made them on
            fxReplay.PlayUntil((int)Math.Floor(renderTime / Sim.Dt));
            if (director.State == RunState.Combat)
                foreach (var actor in actors.Values) if (actor.Player.ClassName == "Arcanist" && !actor.Player.Down) playerFx.ArcanistTick(actor.Player);   // glyph socket ring upkeep (the kit does this each tick on the host)
        }
        // The host's newest state for this client's character, moved on by every input the host had not applied yet.
        bool Predict(out Vector3 shown)
        {
            shown = Vector3.zero;
            var me = current?.Players.Find(p => p.Id == lobby.LocalId);
            if (me == null || me.Down || current.RunState != (int)RunState.Combat || me.AckInput < 0) { predictedReady = false; PredictionLead = 0; return false; }
            unacked.RemoveAll(m => m.Seq <= me.AckInput);
            var target = new Vector3(me.X, me.Y, me.Z);
            foreach (var m in unacked)
            {
                float speed = (m.Sprint && me.Stamina > 0 ? PlayerController.SprintSpeed : PlayerController.WalkSpeed) * me.MoveScale;
                target += new Vector3(m.Move.x, 0, m.Move.y) * speed * Sim.Dt;
            }
            PredictionLead = Vector3.Distance(target, new Vector3(me.X, me.Y, me.Z));
            if (!predictedReady || (target - predictedShown).sqrMagnitude > TeleportDistance * TeleportDistance) { predictedShown = target; predictedReady = true; }
            else predictedShown = Vector3.Lerp(predictedShown, target, 1 - Mathf.Exp(-Time.unscaledDeltaTime * 25));
            shown = predictedShown; return true;
        }
        void LateUpdate()
        {
            if (!cam) cam = Camera.main;
            if (!cam || lobby == null || !actors.TryGetValue(lobby.LocalId, out var local)) return;
            var offset = new Vector3(0, 13f, 9.7f); var target = local.Player.transform.position;
            cam.orthographic = false; cam.transform.position = Vector3.Lerp(cam.transform.position, target + offset, 1 - Mathf.Exp(-Time.deltaTime * 12));
            cam.transform.rotation = Quaternion.LookRotation(-offset + Vector3.up); cam.fieldOfView = 44;
        }
        GUIStyle nameStyle;
        void OnGUI()
        {
            // RunHud draws the fight; this adds Leave and a name over each other player.
            if (GUI.Button(new Rect(Screen.width - 150, 20, 130, 35), "Leave session")) { if (LanSessionCoordinator.Instance) LanSessionCoordinator.Instance.Leave(); return; }
            if (current == null || lobby == null || !cam) return;
            if (nameStyle == null) nameStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.LowerCenter, fontSize = 14, normal = { textColor = new Color(0.95f, 0.92f, 0.82f) } };
            foreach (var pair in actors)
            {
                if (pair.Key == lobby.LocalId) continue;
                var point = cam.WorldToScreenPoint(pair.Value.Player.transform.position + Vector3.up * (2.3f + pair.Value.ShownHeight));
                if (point.z <= 0) continue;
                GUI.Label(new Rect(point.x - 90, Screen.height - point.y - 24, 180, 24), NameOf(pair.Key), nameStyle);
            }
        }
        // ---------------- boss props ----------------
        readonly HashSet<int> propSeen = new HashSet<int>();
        void ReceiveProps(double hostTime, List<LanWorldFrame.Prop> list)
        {
            propSeen.Clear();
            foreach (var p in list)
            {
                propSeen.Add(p.Id);
                if (!props.TryGetValue(p.Id, out var view) || view.Kind != p.Kind)
                {
                    if (view != null && view.View) Destroy(view.View.gameObject);
                    view = new PropView { Kind = p.Kind, View = boss ? boss.MakeNetProp(p.Kind) : null }; props[p.Id] = view;
                    if (view.View) view.View.gameObject.SetActive(false);   // shown once the drawn time reaches it
                }
                view.GoneAt = -1; view.Track.Add(hostTime, new Vector3(p.X, p.Y, p.Z), 0, 0);
            }
            foreach (var pair in props) if (!propSeen.Contains(pair.Key) && pair.Value.GoneAt < 0) pair.Value.GoneAt = hostTime;
        }
        readonly List<int> drop = new List<int>();
        void PresentProps(double renderTime)
        {
            drop.Clear(); int shown = 0;
            foreach (var pair in props)
            {
                var v = pair.Value;
                if (v.GoneAt >= 0 && renderTime >= v.GoneAt) { if (v.View) Destroy(v.View.gameObject); drop.Add(pair.Key); continue; }
                if (!v.View || !v.Track.Ready) continue;
                v.Track.At(renderTime, out var pos, out _, out _);
                v.View.position = pos; if (!v.View.gameObject.activeSelf) v.View.gameObject.SetActive(true); shown++;
            }
            foreach (int id in drop) props.Remove(id);
            PropsShown = Mathf.Max(PropsShown, shown);
        }
        void ClearProps() { foreach (var v in props.Values) if (v.View) Destroy(v.View.gameObject); props.Clear(); }

        // ---------------- hazard stand-ins and Aurelion effects ----------------
        readonly HashSet<int> mirrorSeen = new HashSet<int>();
        void ReceiveMirrors(double hostTime, List<LanWorldFrame.HazardState> hazards)
        {
            mirrorSeen.Clear();
            foreach (var s in hazards)
            {
                mirrorSeen.Add(s.Id);
                if (!mirrors.TryGetValue(s.Id, out var m)) { m = new HazardMirror { H = new Hazard { Replica = true, Source = s.Source } }; mirrors[s.Id] = m; world.Hazards.AddReplica(m.H); }
                var h = m.H;
                h.MechanicId = string.IsNullOrEmpty(s.Mechanic) ? null : s.Mechanic; h.JumpClearable = s.JumpClearable; h.Soak = s.Soak; h.ShareCount = s.ShareCount; h.FillToLock = s.FillToLock;
                h.Shape = (HazardShape)s.Shape; h.Origin = new Vector3(s.X, s.Y, s.Z); h.Yaw = s.Yaw; h.Radius = s.Radius; h.Inner = s.Inner; h.HalfAngle = s.HalfAngle;
                h.Length = s.Length; h.Width = s.Width; h.LockAt = s.LockAt; h.ResolveAt = s.ResolveAt; h.ActiveTicks = s.ActiveTicks; h.ClipRadius = s.ClipRadius;
                m.T = hostTime; m.Age = s.Age; m.GoneAt = -1;
            }
            foreach (var pair in mirrors) if (!mirrorSeen.Contains(pair.Key) && pair.Value.GoneAt < 0) pair.Value.GoneAt = hostTime;
        }
        void PresentMirrors(double renderTime)
        {
            drop.Clear();
            foreach (var pair in mirrors)
            {
                var m = pair.Value;
                if (m.GoneAt >= 0 && renderTime >= m.GoneAt) { m.H.Cancelled = true; world.Hazards.RemoveReplica(m.H); drop.Add(pair.Key); continue; }
                m.H.Age = Math.Max(0, m.Age + (int)Math.Floor((renderTime - m.T) / Sim.Dt));
            }
            foreach (int id in drop) mirrors.Remove(id);
        }
        void ClearMirrors()
        {
            foreach (var m in mirrors.Values) { m.H.Cancelled = true; world?.Hazards.RemoveReplica(m.H); }
            mirrors.Clear();
            if (lastLight) lastLight.End();
            lastLight = null;
        }
        Hazard Mirror(LanWorldFrame.EffectArg a) => a.Kind == LanWorldFrame.EffectArg.Integer && mirrors.TryGetValue(a.I, out var m) ? m.H : null;
        // the host's LanFxTarget.Replica calls
        bool PlayReplica(string method, LanWorldFrame.EffectArg[] a)
        {
            switch (method)
            {
                case "ZenithCollapse":
                    { var h = a.Length == 1 ? Mirror(a[0]) : null; if (h == null) return false; ZenithCollapseFx.Create(world, h); ReplicaEffects++; return true; }
                case "OrreryClosing":
                    {
                        if (a.Length != 3) return false;
                        var rim = Mirror(a[0]); var crown = Mirror(a[1]); if (rim == null || crown == null) return false;
                        OrreryClosingFx.Create(world, rim, crown, a[2].I); ReplicaEffects++; return true;
                    }
                case "LastLightBegin":
                    if (lastLight) lastLight.End();
                    lastLight = LastLightFloorFx.Replica(world, boss ? boss.transform : null); ReplicaEffects++; return true;
                case "LastLightSector": { var h = a.Length == 1 ? Mirror(a[0]) : null; if (h == null || !lastLight) return false; lastLight.AddSector(h); return true; }
                case "LastLightEnd": if (lastLight) lastLight.End(); lastLight = null; return true;
                default: return false;
            }
        }

        // The lobby name and the drawn class of a player in this fight (name tags; diagnostics).
        public string ClassOf(int id) => actors.TryGetValue(id, out var a) && a.Player ? a.Player.ClassName : null;
        public string NameOf(int id)
        {
            foreach (var m in lobby.Members) if (m.Id == id && !string.IsNullOrEmpty(m.Name)) return m.Name;
            return id == 0 ? "Host" : "Player " + id;
        }
        void OnDestroy()
        {
            if (lobby != null) lobby.WorldReceived -= Receive;
            RunHud.LanClientView = false; RunHud.LanLeave = null; BossEncounter.NetIdOf = null; BossEncounter.NetPlayerOf = null;
            if (director) director.DetachLanReplica();
            hazardView?.Clear();
            try { ClearProps(); ClearMirrors(); } catch (Exception ex) { Debug.Log("[LAN] client cleanup: " + ex.Message); }
            // the scene may already have destroyed the replica boss and players; a cleanup error must not escape OnDestroy
            try { if (world != null) world.ClearTransient(); } catch (Exception ex) { Debug.Log("[LAN] client cleanup: " + ex.Message); }
            if (root) Destroy(root.gameObject);
        }
    }
}
