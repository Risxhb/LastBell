using System;
using System.Collections.Generic;
using LastBell.Run;
using LastBell.VFX;
using UnityEngine;

namespace LastBell.Menu
{
    // Choreography only: no CombatWorld, player input, damage or encounter state.
    public sealed class MenuBattle : MonoBehaviour
    {
        [Range(30, 45)] public float LoopSeconds = 36;
        public Vector3 CameraPosition = new Vector3(11, 24, 19);
        public Vector3 CameraTarget = new Vector3(0, 1, 0);
        [Range(0.25f, 5)] public float OrbitDegreesPerSecond = 2;
        [Range(25, 60)] public float FieldOfView = 40;
        public Vector3 BossPosition = new Vector3(0, 0, -1.7f);
        public Vector3 VanguardPosition = new Vector3(-1.6f, 0, 1);
        public Vector3 ArcanistPosition = new Vector3(-5.2f, 0, 4.3f);
        public Vector3 ChimesingerPosition = new Vector3(5, 0, 3.8f);
        public string BossResource = "Bellkeeper2";
        public int PartySize = 3;
        public int StageIndex;
        public bool DriveCamera = true;
        public int CompletedLoops { get; private set; }
        public float Elapsed { get; private set; }
        public int ActorCount => actors.Count;
        public int EffectCount => effects.Count;
        public bool AllModelsPresent => actors.TrueForAll(a => a.Presenter.HasModel);
        public IReadOnlyList<CharacterPresenter> Presenters => actors.ConvertAll(a => a.Presenter);
        public readonly float[] ActorTravel = new float[9];
        public int IndependentMotionFrames { get; private set; }
        public int PlantedCastFrames { get; private set; }
        public int CastSlideFrames { get; private set; }

        sealed class Actor
        {
            public Transform Root; public CharacterPresenter Presenter; public Vector3 Home; public PlayerController Pose; public float PlantedUntil;
        }
        sealed class Effect
        {
            public LineRenderer Line; public float Start, Duration; public Vector3 From, To;
            public bool Projectile; public float Radius; public Color Color;
        }
        readonly List<Actor> actors = new List<Actor>();
        readonly List<Effect> effects = new List<Effect>();
        readonly List<Material> ownedMaterials = new List<Material>();
        float previous = -0.01f;
        Camera view;
        MenuFxLifetime spellFx;
        bool previousNoShake;
        public int SpellsPresented { get; private set; }
        static readonly Color Gold = new Color(1, 0.72f, 0.25f), Teal = new Color(0.2f, 0.9f, 1), Violet = new Color(0.65f, 0.48f, 1);

        void Start()
        {
            view = Camera.main;
            SpawnParty();
            previousNoShake = BellkeeperFx.NoScreenshake; BellkeeperFx.NoScreenshake = true;
            spellFx = MenuFxLifetime.Acquire();
            spellFx.Player.Shield(actors[1].Pose, 0, false);
            spellFx.Player.DashTrail(actors[1].Pose);
            spellFx.Boss.ChargeTrail(actors[0].Root, false);
            // Fixed-size reusable effect pool. No allocations/spawns per cycle.
            for (int i = 0; i < 18; i++)
            {
                var go = new GameObject("Choreography ribbon " + i); go.transform.SetParent(transform, false);
                var line = go.AddComponent<LineRenderer>(); line.useWorldSpace = true;
                var mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                mat.SetColor("_BaseColor", Gold * 1.5f); ownedMaterials.Add(mat); line.sharedMaterial = mat;
                line.widthMultiplier = 0.035f; line.numCornerVertices = 3; line.enabled = false;
                effects.Add(new Effect { Line = line, Start = -100 });
            }
            // Small floating lights, authored from a fixed seed rather than an unbounded particle emitter.
            for (int i = 0; i < 22; i++)
            {
                var mote = new GameObject("Clock dust " + i); mote.transform.SetParent(transform, false);
                var filter = mote.AddComponent<MeshFilter>(); filter.sharedMesh = Prim.MeshOf(PrimitiveKind.Sphere);
                var renderer = mote.AddComponent<MeshRenderer>();
                var material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                material.SetColor("_BaseColor", Gold * 1.6f); ownedMaterials.Add(material); renderer.sharedMaterial = material;
                mote.transform.localScale = Vector3.one * (0.025f + i % 3 * 0.012f);
            }
        }
        void SpawnParty()
        {
            Spawn(BossResource, BossPosition, true);
            Spawn("Vanguard", VanguardPosition);
            Spawn("Arcanist", ArcanistPosition);
            Spawn("Chimesinger", ChimesingerPosition);
            string[] classes = { "Vanguard", "Arcanist", "Chimesinger" };
            for (int i = 3; i < PartySize; i++)
            {
                float angle = (i - 3) * 2.39996f + StageIndex;
                Spawn(classes[(i + StageIndex) % 3], P(Mathf.Cos(angle) * 7, Mathf.Sin(angle) * 7));
            }
        }
        public void SetStage(int index, string boss, int partySize)
        {
            StageIndex = index; BossResource = boss; PartySize = Mathf.Clamp(partySize, 3, 8);
            // Release graphs and presenter-owned material instances before replacing the cast.
            foreach (var a in actors)
            {
                a.Root.gameObject.SetActive(false);
                foreach (var renderer in a.Root.GetComponentsInChildren<Renderer>(true))
                    foreach (var material in renderer.sharedMaterials)
                        if (material && (renderer is TrailRenderer || material.name.EndsWith("(Instance)", StringComparison.Ordinal))) Destroy(material);
                Destroy(a.Root.gameObject);
            }
            actors.Clear(); Array.Clear(ActorTravel, 0, ActorTravel.Length);
            if (spellFx) { spellFx.Player.ResetAll(); spellFx.Boss.ResetAll(); }
            foreach (var e in effects) { e.Start = -100; e.Line.enabled = false; }
            previous = -0.01f; Elapsed = 0; CompletedLoops = 0;
            if (view) SpawnParty(); // Before Start, the fields alone configure the first cast.
        }
        void Spawn(string resource, Vector3 home, bool boss = false)
        {
            var root = new GameObject(resource + " (menu only)").transform; root.SetParent(transform, false); root.position = home;
            var presenter = boss ? CharacterPresenter.Create(root, resource, 5, 0.2f, 0, null) : CharacterPresenter.ForPlayer(root, resource);
            // Existing spell renderers accept a PlayerController for pose/height. These adapters remain disabled,
            // have no World, Kit or Input, and are never stepped: only their Transform is read by the VFX.
            var pose = boss ? null : root.gameObject.AddComponent<PlayerController>();
            if (pose) { pose.enabled = false; pose.MovementEnabled = false; }
            actors.Add(new Actor { Root = root, Presenter = presenter, Home = home, Pose = pose });
        }
        void Update()
        {
            if (actors.Count < 4) return;
            Elapsed += Time.deltaTime;
            float t = Mathf.Repeat(Elapsed, LoopSeconds) / LoopSeconds * 36;
            int loops = Mathf.FloorToInt(Elapsed / LoopSeconds);
            if (loops != CompletedLoops) { CompletedLoops = loops; previous = -0.01f; }
            // Each actor has its own world-space track and timing. Casting plants the actor; the
            // remaining route is caught up through locomotion after recovery, never by dragging a formation.
            float angle = t / 36 * Mathf.PI * 2;
            Vector3 bossDelta = Vector3.zero; bool independent = false;
            for (int i = 0; i < actors.Count; i++)
            {
                var a = actors[i]; Vector3 old = a.Root.position;
                int route = (i - 1 + 3) % 3;
                Vector3 desired = i == 0 ? BossPosition + Track(RouteTimes, RouteOffsets, t)
                    : Track(PartyTimes[route], PartyPositions[route], Mathf.Repeat(t + (i > 3 ? i * 1.7f : 0), 36)) + a.Home - PartyPositions[route][0];
                desired = Vector3.ClampMagnitude(desired, 11.2f); desired.y = 0;
                bool planted = Elapsed < a.PlantedUntil;
                float moveSpeed = i == 0 ? (t >= 18.4f && t < 19.2f ? 12 : 3.2f) : i == 1 ? (t >= 7.8f && t < 8.3f ? 12 : 6) : 4.8f;
                if (!planted) a.Root.position = Vector3.MoveTowards(old, desired, moveSpeed * Time.deltaTime);
                Vector3 delta = a.Root.position - old;
                float distance = delta.magnitude;
                ActorTravel[i] += distance;
                if (i == 0) bossDelta = delta;
                else if ((delta - bossDelta).sqrMagnitude > 0.000001f) independent = true;
                if (i > 0 && planted) { PlantedCastFrames++; if (distance > 0.001f) CastSlideFrames++; }
                float speed = distance / Mathf.Max(Time.deltaTime, 0.001f);
                Vector3 face = speed > 0.15f ? delta : i == 0 ? actors[1].Root.position - a.Root.position : actors[0].Root.position - a.Root.position;
                face.y = 0;
                if (face.sqrMagnitude > 0.000001f) a.Root.rotation = Quaternion.RotateTowards(a.Root.rotation, Quaternion.LookRotation(face), 720 * Time.deltaTime);
                a.Presenter.Locomotion = speed;
                if (i == 0) a.Presenter.TickBoss(speed > 0.15f ? "Walk" : "Idle", Mathf.Max(0.2f, speed / 1.75f)); else a.Presenter.Tick(null);
            }
            if (independent) IndependentMotionFrames++;
            for (int i = 4; i < actors.Count; i++)
            {
                float beat = 1 + (i - 4) * .7f;
                foreach (float at in new[] { beat, beat + 4, beat + 8, beat + 12, beat + 18, beat + 24, beat + 29 })
                    if (Crossed(t, at))
                    {
                        var a = actors[i]; string role = a.Root.name;
                        string clip = role.StartsWith("Vanguard") ? "SunderingBlow" : role.StartsWith("Arcanist") ? "ArcLance" : "Bellstrike";
                        a.PlantedUntil = Elapsed + .65f;
                        a.Root.LookAt(new Vector3(actors[0].Root.position.x, 0, actors[0].Root.position.z));
                        a.Presenter.PlayOnce(clip, .65f); Bolt(i, role.StartsWith("Arcanist") ? Teal : Gold); SpellsPresented++;
                    }
            }
            // Attack releases are separated from animation cues so bolts/impacts match contact poses.
            Cue(t, 0.5f, 1, "SteelRhythm1", 0.5f); Cue(t, 1.1f, 1, "SteelRhythm2", 0.5f);
            Cue(t, 1.8f, 1, "SteelRhythm3", 0.7f); Cue(t, 3, 1, "SunderingBlow", 0.85f);
            Cue(t, 1.4f, 2, "TimeSeal", 0.6f); Cue(t, 4, 2, "ArcLance", 0.8f);
            Cue(t, 2.5f, 3, "Bellstrike", 0.7f); Cue(t, 5.2f, 3, "HarmonicRefrain", 0.8f);
            Cue(t, 7.1f, 0, "BellSweep", 2.6f);
            Cue(t, 7.8f, 1, "Dodge", 0.5f); Cue(t, 8, 2, "Dodge", 0.5f);
            Cue(t, 11.8f, 1, "LastWord", 1.6f); Cue(t, 12.3f, 3, "Crescendo", 1.1f);
            Cue(t, 14.5f, 0, "HollowToll", 2.6f);
            Cue(t, 14.9f, 1, "BraceRaise", 0.2f); Cue(t, 15.2f, 1, "BraceHold", 1.1f);
            Cue(t, 16.5f, 1, "BraceLower", 0.4f); Cue(t, 17, 3, "MendingChorus", 0.8f);
            Cue(t, 17.8f, 0, "PendulumCharge", 1.4f);
            Cue(t, 19.2f, 0, "ShatteredChime", 3.1f);
            Cue(t, 24.4f, 0, "Vulnerable", 3);
            Cue(t, 24.8f, 1, "SteelRhythm1", 0.5f); Cue(t, 25.4f, 1, "SteelRhythm2", 0.5f);
            Cue(t, 26.1f, 1, "SteelRhythm3", 0.7f); Cue(t, 27.2f, 1, "SunderingBlow", 0.85f);
            Cue(t, 26.5f, 2, "TimeSeal", 0.6f); Cue(t, 28, 2, "ArcLance", 0.8f);
            Cue(t, 29.5f, 2, "Convergence", 0.8f); Cue(t, 30.2f, 3, "Crescendo", 1.1f);
            // Interleaved ranged basics, rather than every actor attacking on the same beat.
            foreach (float at in BasicTimes)
            {
                Cue(t, at, 2, "Clockbolt", 0.45f); Cue(t, at + 0.7f, 3, "ResonantNote", 0.5f);
                if (Crossed(t, at + 0.18f)) Bolt(2, Teal);
                if (Crossed(t, at + 0.92f)) Bolt(3, Gold);
            }
            if (Crossed(t, 4.35f) || Crossed(t, 28.35f)) Bolt(2, Teal, 0.16f);
            SpellCues(t);
            AnimateEffects();
            int moteIndex = 0;
            foreach (Transform child in transform)
            {
                if (!child.name.StartsWith("Clock dust", StringComparison.Ordinal)) continue;
                float seed = moteIndex++ * 2.39996f;
                child.localPosition = new Vector3(Mathf.Cos(seed + angle) * (5 + moteIndex % 7),
                    0.6f + Mathf.Repeat(seed * 0.4f + Elapsed * 0.15f, 4), Mathf.Sin(seed + angle) * 9);
            }
            previous = t;
        }
        static readonly float[] BasicTimes = { 0.8f, 3.1f, 5.9f, 10.4f, 13.3f, 17.7f, 23, 25.2f, 31.8f, 34 };
        static readonly float[] SlashTimes = { 0.72f, 1.32f, 2.15f, 25.02f, 25.62f, 26.45f };
        static readonly float[] RouteTimes = { 0, 4.4f, 6.6f, 10, 11.5f, 17.8f, 18.4f, 19.2f, 27.6f, 31.5f, 34, 36 };
        static readonly Vector3[] RouteOffsets = { P(0,0), P(0,0), P(3,1), P(3,1), P(2,3.5f), P(2,3.5f),
            P(2,3.5f), P(-3,3.5f), P(-3,3.5f), P(-2,-1), P(-2,-1), P(0,0) };
        static readonly float[][] PartyTimes =
        {
            new float[] { 0,4.1f,6.7f,7.8f,8.3f,9.7f,11.6f,13.8f,14.8f,15.2f,16.7f,17.7f,21.8f,24.4f,28.3f,31.8f,33.8f,36 },
            new float[] { 0,4.9f,6.5f,9.4f,10,14.2f,15.4f,18.3f,20.2f,23.5f,26,30.6f,34.5f,36 },
            new float[] { 0,3.5f,4.6f,7.4f,8.6f,11.7f,12.1f,14.8f,15.8f,17.8f,19.5f,23.9f,26,28.5f,29.6f,31.4f,33.7f,36 }
        };
        static readonly Vector3[][] PartyPositions =
        {
            new[] { P(-1.6f,1),P(-1.6f,1),P(1.4f,2.4f),P(1.4f,2.4f),P(6,4),P(6,4),P(0.5f,4.4f),P(0.5f,4.4f),
                P(0.5f,4.4f),P(1,3.3f),P(1,3.3f),P(1,7),P(1,7),P(-4.5f,4.4f),P(-4.5f,4.4f),P(-3.4f,-0.1f),P(-3.4f,-0.1f),P(-1.6f,1) },
            new[] { P(-5.2f,4.3f),P(-5.2f,4.3f),P(-7,-1.5f),P(-7,-1.5f),P(-6,4.8f),P(-6,4.8f),P(-7,6),P(-7,6),
                P(-7,-3),P(-7,-3),P(0,-5),P(0,-5),P(-5.2f,4.3f),P(-5.2f,4.3f) },
            new[] { P(5,3.8f),P(5,3.8f),P(7,1),P(7,1),P(8,-2),P(8,-2),P(6,-4),P(6,-4),P(7,-4),P(7,-4),
                P(4,-6),P(4,-6),P(-6,-5),P(-6,-5),P(-7,0),P(-7,0),P(3.5f,6),P(5,3.8f) }
        };
        static Vector3 P(float x, float z) => new Vector3(x, 0, z);
        static Vector3 Track(float[] times, Vector3[] positions, float t)
        {
            for (int i = 1; i < times.Length; i++)
                if (t <= times[i]) return Vector3.Lerp(positions[i - 1], positions[i], Mathf.SmoothStep(0, 1, Mathf.InverseLerp(times[i - 1], times[i], t)));
            return positions[positions.Length - 1];
        }
        void SpellCues(float t)
        {
            var fx = spellFx.Player; var bossFx = spellFx.Boss; var target = actors[0].Root;
            var vanguard = actors[1].Pose; var arcanist = actors[2].Pose; var chime = actors[3].Pose;
            var hit = target.position + Vector3.up * 2.2f;
            foreach (float at in SlashTimes)
                if (Crossed(t, at)) { fx.Slash(vanguard, 3.1f, 120, at == 2.15f || at == 26.45f, at == 1.32f || at == 25.62f); SpellsPresented++; bossFx.BossHit(hit); }
            if (Crossed(t, 3.4f) || Crossed(t, 27.6f)) { fx.Sunder(vanguard, 3.3f, true, true); SpellsPresented++; }
            if (Crossed(t, 11.8f)) fx.UltWindup(vanguard, 48);
            if (Crossed(t, 12.7f)) { fx.UltSlam(vanguard, vanguard.transform.position + vanguard.transform.forward * 2.5f); SpellsPresented++; }
            if (Crossed(t, 15.2f)) fx.Shield(vanguard, 0, true);
            if (Crossed(t, 16.5f)) fx.Shield(vanguard, 0, false);
            if (Crossed(t, 7.8f)) fx.DashTrail(vanguard);
            if (Crossed(t, 8)) fx.ClockstepTrail(arcanist);
            if (Crossed(t, 1.75f) || Crossed(t, 26.85f)) { fx.SealCast(arcanist.transform.position + Vector3.up, hit, true); SpellsPresented++; }
            if (Crossed(t, 4) || Crossed(t, 28)) fx.LanceCharge(arcanist, 20, true);
            if (Crossed(t, 4.35f) || Crossed(t, 28.35f))
            {
                Vector3 from = arcanist.transform.position + Vector3.up;
                fx.Lance(from, (hit - from).normalized, Vector3.Distance(from, hit) + 1, true, true); SpellsPresented++;
            }
            if (Crossed(t, 29.9f)) { fx.SealBurst(target.position, true); SpellsPresented++; }
            if (Crossed(t, 2.85f)) { fx.Bellstrike(chime.transform.position + Vector3.up, hit, true, false); SpellsPresented++; }
            if (Crossed(t, 5.6f)) { fx.Refrain(chime, 9, 32, true); SpellsPresented++; }
            if (Crossed(t, 12.9f) || Crossed(t, 30.8f)) { fx.Crescendo(target.position, true, 2, true); SpellsPresented++; }
            if (Crossed(t, 17)) { fx.Chorus(chime.transform.position, 5); SpellsPresented++; }
            if (BossResource != "Bellkeeper2")
            {
                // Presentation cues only; use the authored model's own attack vocabulary.
                if (Crossed(t, 8.4f) || Crossed(t, 15.8f) || Crossed(t, 20.5f))
                { Ring(target.position, BossResource == "Crucivar" ? 6 : 8, BossResource == "Sylvara" ? new Color(.4f, 1, .65f) : Gold, .8f); SpellsPresented++; }
                return;
            }
            if (Crossed(t, 8.4f)) { bossFx.SweepResolve(target.position, target.eulerAngles.y, 6.5f, 40); SpellsPresented++; }
            if (Crossed(t, 15.8f)) { bossFx.TollResolve(target.position, 2.6f, 6.8f); SpellsPresented++; }
            if (Crossed(t, 17.8f)) bossFx.ChargeTrail(target, true);
            if (Crossed(t, 19.2f)) { bossFx.ChargeTrail(target, false); bossFx.ChargeLand(target.position); SpellsPresented++; }
            if (Crossed(t, 20.5f)) bossFx.ChimeResolve(actors[1].Root.position, 1.4f);
            if (Crossed(t, 21.3f)) bossFx.ChimeResolve(actors[2].Root.position, 1.4f);
            if (Crossed(t, 22.1f)) bossFx.ChimeResolve(actors[3].Root.position, 1.4f);
        }
        bool Crossed(float t, float at) => previous < at && t >= at;
        void Cue(float t, float at, int actor, string clip, float duration)
        {
            if (!Crossed(t, at)) return;
            var a = actors[actor];
            if (actor == 0 && BossResource != "Bellkeeper2")
            {
                if (BossResource == "Aurelion") clip = clip == "BellSweep" ? "MeridianCut" : clip == "HollowToll" ? "ZenithPins" : "ProcessionalThrust";
                else if (BossResource == "Sylvara") clip = clip == "BellSweep" ? "PruningArc" : clip == "HollowToll" ? "Rootscript" : "Bloom";
                else clip = clip == "BellSweep" ? "Diebreaker" : clip == "HollowToll" ? "SlagCast" : "RailRam";
                if (!a.Presenter.Has(clip)) clip = "Idle";
            }
            if (clip != "Dodge" && clip != "PendulumCharge") a.PlantedUntil = Mathf.Max(a.PlantedUntil, Elapsed + duration);
            if (actor > 0 && clip != "Dodge")
            {
                Vector3 aim = actors[0].Root.position - a.Root.position; aim.y = 0;
                if (aim.sqrMagnitude > 0.001f) a.Root.rotation = Quaternion.LookRotation(aim);
            }
            a.Presenter.PlayOnce(clip, duration);
        }
        Effect Reserve(Color color, float duration)
        {
            var e = effects.Find(f => Elapsed - f.Start > f.Duration) ?? effects[0];
            e.Start = Elapsed; e.Duration = duration; e.Color = color; e.Line.enabled = true;
            e.Line.sharedMaterial.SetColor("_BaseColor", color * 1.7f); return e;
        }
        void Bolt(int from, Color color, float width = 0.055f)
        {
            spellFx.Player.NoteCast(actors[from].Root.position + Vector3.up, actors[from].Root.forward);
            var e = Reserve(color, width > 0.1f ? 0.28f : 0.5f); e.Projectile = true;
            e.From = actors[from].Root.position + Vector3.up * 1.2f;
            e.To = actors[0].Root.position + Vector3.up * 2.2f; e.Line.widthMultiplier = width;
        }
        void Ring(Vector3 center, float radius, Color color, float duration)
        {
            var e = Reserve(color, duration); e.Projectile = false; e.From = center + Vector3.up * 0.07f;
            e.Radius = radius; e.Line.widthMultiplier = 0.045f;
        }
        void AnimateEffects()
        {
            foreach (var e in effects)
            {
                float f = (Elapsed - e.Start) / Mathf.Max(0.01f, e.Duration);
                if (f >= 1) { e.Line.enabled = false; continue; }
                if (e.Projectile)
                {
                    spellFx.Player.ProjectileHead(Vector3.Lerp(e.From, e.To, f), e.Color);
                    e.Line.positionCount = 2;
                    e.Line.SetPosition(0, Vector3.Lerp(e.From, e.To, Mathf.Max(0, f - 0.22f)));
                    e.Line.SetPosition(1, Vector3.Lerp(e.From, e.To, f));
                }
                else
                {
                    e.Line.positionCount = 65;
                    float radius = e.Radius * Mathf.Lerp(0.7f, 1, f);
                    for (int i = 0; i <= 64; i++)
                    { float a = i / 64f * Mathf.PI * 2; e.Line.SetPosition(i, e.From + new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a)) * radius); }
                    e.Line.widthMultiplier = 0.045f * (1 - f);
                }
            }
        }
        void LateUpdate()
        {
            if (!view || !DriveCamera) return;
            view.orthographic = false; view.fieldOfView = FieldOfView;
            Vector3 position = CameraTarget + Quaternion.Euler(0, Elapsed * OrbitDegreesPerSecond, 0) * (CameraPosition - CameraTarget);
            view.transform.SetPositionAndRotation(position, Quaternion.LookRotation(CameraTarget - position));
        }
        void OnDestroy()
        {
            BellkeeperFx.NoScreenshake = previousNoShake;
            if (spellFx) spellFx.Release();
            foreach (var material in ownedMaterials) if (material) Destroy(material);
            // CharacterPresenter owns PlayableGraphs but its runtime material instances need releasing with this scene.
            foreach (var renderer in GetComponentsInChildren<Renderer>())
                if (!(renderer is LineRenderer)) foreach (var material in renderer.sharedMaterials)
                    if (material && (renderer is TrailRenderer || material.name.EndsWith("(Instance)", StringComparison.Ordinal))) Destroy(material);
        }
    }
}
