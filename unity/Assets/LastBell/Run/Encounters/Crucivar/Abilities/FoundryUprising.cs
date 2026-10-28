using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LastBell.Run
{
    // Ability set v4, Phase 2 "Foundry Uprising" (proposal, abilities-v4/P2-FOUNDRY-UPRISING.md; Chzy18's turret add idea,
    // 2026-10-27, its own phase with the boss fighting). One Run is one uprising:
    //   0.0  2-4 lava turrets rise from the rim (2.5 s, harmless), each with a Molten Casting poured 7.5 m out on the far
    //        side of the arena: same numeral, a chain between them. A casting is a BossPart (melee and ranged hit it);
    //        half the damage it takes also hits Crucivar (painshare). Killing it sinks its turret.
    //  2.5  turrets fire: every pattern first draws its real path for 2.0 s (Lane Stream, Ring Wave, Spiral Arm), then
    //        0.5 m bullets (Strike, 20, one hit each). Each turret repeats every 4 s; the turrets are staggered 1.3 s.
    //  4.0  Ember Brand; from the second uprising also Furnace Spin at 11 s and Backdraft at 19 s (introduced, then layered)
    //  30.0 Pour Flood: the rim ring that has filled since the rise lands while any turret stands, downing the whole party.
    // Solo: two turrets in sequence (the second rises when the first sinks), 36 s. Easy: paths 2.5 s, 40 s.
    // Each uprising steps up: the second alternates two patterns per turret, the third cuts the rest to 0.5 s.
    // Hard (Cast in Order): killing a casting out of numeral order makes every standing turret fire a Ring Wave at once.
    public sealed class FoundryUprising : CrucivarAbility
    {
        public override string Id => "crucivar.foundry_uprising";

        public const float Rise = 2.5f, PathWarn = 2.0f, FireTime = 1.0f, Stagger = 1.3f, Deadline = 30f, SoloDeadline = 36f, EasyDeadline = 40f;
        public const float CastRing = 7.5f, CastRadius = 1.2f, CastHp = 150f, Painshare = 0.5f;   // demo HP, set from measured DPS later
        public const float BulletRadius = 0.5f, LaneSpacing = 3.5f, LaneSpeed = 6f, RingSpeed = 5f, RingGapHalf = 8f, RingGapAt = 26f;
        public const float SpiralTurn = 30f, SpiralSpeed = 6f, SpiralFor = 1.6f, SpiralEvery = 0.12f;
        public const int BulletDmg = 20;
        public enum Pattern { Lane, Ring, Spiral }

        public sealed class Turret
        {
            public int K; public Vector3 Pos; public bool Up = true; public BossPart Casting; public float CastMax;
            public Pattern A, B; public int Shots, NextFire; public Transform Look, CastLook; public LineRenderer Chain;
            public readonly List<Hazard> Pending = new List<Hazard>();
        }

        public readonly List<Turret> Turrets = new List<Turret>();
        public readonly Dictionary<Hazard, Vector3> Velocity = new Dictionary<Hazard, Vector3>();   // bullets, for the watch bots
        public int Uprisings { get; private set; }
        public int Floods { get; private set; }
        public Hazard Flood { get; private set; }
        int total, spawned, t;
        readonly List<(Turret tu, float a0, int start)> spirals = new List<(Turret, float, int)>();

        public int TurretCount(int fighters) => fighters <= 3 ? 2 : fighters <= 6 ? 3 : 4;

        public IEnumerable<int> Run()
        {
            Uprisings++;
            Turrets.Clear(); Velocity.Clear(); spirals.Clear(); t = 0;
            int n = Mathf.Max(1, W.Players.Count);
            total = TurretCount(n); spawned = 0;
            if (n == 1) SpawnPair(0); else for (int k = 0; k < total; k++) SpawnPair(k);
            int deadline = Sim.Ticks(Easy ? EasyDeadline : n == 1 ? SoloDeadline : Deadline);
            Flood = W.Hazards.Spawn(new Hazard
            {
                Source = "Pour Flood", Shape = HazardShape.Ring, Kind = HazardKind.Ground, Origin = Vector3.zero, Inner = Rim - 1.2f, Radius = Rim + Hazard.Margin + 0.5f,
                LockAt = 0, ResolveAt = deadline, ShareTotal = 1, WipeIfEmpty = true, Includes = _ => false,   // nobody can soak it: it wipes
            });
            Flood.OnResolve = _ => Floods++;   // counted here: the wipe can end the script before the loop sees it
            bool layered = Uprisings > 1;
            IEnumerator<int> draft = null; int draftWait = 0;
            for (t = 0; ; t++)
            {
                if (t == Sim.Ticks(4f)) { Boss.SetCurrent("Ember Brand"); Boss.Rite.EmberBrand(); }
                if (layered && t == Sim.Ticks(11f)) { Boss.SetCurrent("Furnace Spin"); Boss.Rite.FurnaceSpin(); }
                if (layered && t == Sim.Ticks(19f)) { Boss.SetCurrent("Backdraft"); draft = Boss.Rite.Backdraft().GetEnumerator(); draftWait = 0; }
                if (draft != null && --draftWait <= 0) { if (draft.MoveNext()) draftWait = draft.Current; else draft = null; }
                foreach (var tu in Turrets.ToArray()) if (tu.Up && t >= tu.NextFire) Fire(tu, Next(tu));
                StepSpirals();
                if (t % Sim.Hz == 0)
                {
                    int left = Mathf.CeilToInt((deadline - t) / (float)Sim.Hz);
                    Boss.Label($"Foundry Uprising {Uprisings}  ·  POUR FLOOD IN {left} s");
                    foreach (var tu in Turrets) if (tu.Up) RunHud.Float(tu.Pos + Vector3.up * 3f, left.ToString(), left <= 10 ? new Color(1f, 0.45f, 0.32f) : new Color(1f, 0.95f, 0.82f));
                }
                if (spawned >= total && Turrets.All(x => !x.Up)) { Cancel(Flood); Flood = null; break; }
                if (t >= deadline) { Flood = null; break; }   // the flood landed: the party is down
                yield return 1;
            }
            Boss.Label("Casting Heat");
        }

        Pattern Next(Turret tu)
        {
            var p = Uprisings >= 2 && tu.Shots % 2 == 1 ? tu.B : tu.A;
            tu.Shots++;
            tu.NextFire = t + T(PathWarn) + Sim.Ticks(FireTime) + Sim.Ticks(Uprisings >= 3 ? 0.5f : 1.0f);
            return p;
        }

        void SpawnPair(int k)
        {
            spawned++;
            float yaw = k * 360f / total + (Uprisings - 1) * 34f;
            var pats = new[] { Pattern.Lane, Pattern.Ring, Pattern.Spiral };
            var tu = new Turret
            {
                K = k, Pos = Sim.Dir(yaw) * (Rim + 0.4f), A = pats[(k + Uprisings - 1) % 3], B = pats[(k + Uprisings) % 3],
                NextFire = t + Sim.Ticks(Rise + (W.Players.Count == 1 ? 0 : Stagger * k)), CastMax = CastHp * (1 + 0.7f * (Mathf.Max(1, W.Players.Count) - 1)),
            };
            float hp = tu.CastMax;
            tu.Casting = new BossPart { Name = "Molten Casting " + Numeral(k), Pos = Sim.Dir(yaw + 180f) * CastRing, BodyRadius = CastRadius, HitHeight = 1.0f };
            tu.Casting.TakeHit = (dmg, contact) =>
            {
                if (!tu.Up) return 0;
                int dealt = Mathf.Min(dmg, Mathf.CeilToInt(hp)); hp -= dealt;
                RunHud.Float(contact + Vector3.up * 0.5f, dealt.ToString(), new Color(1f, 0.86f, 0.45f));
                if (hp <= 0) Sink(tu);
                if (Boss.Solid) Boss.TakeHit(Mathf.Max(1, Mathf.RoundToInt(dealt * Painshare)), Boss.transform.position + Vector3.up * Boss.HitHeight);
                return dealt;
            };
            tu.Casting.OnCleared = _ => DestroyLooks(tu);
            Turrets.Add(tu);
            W.Parts.Add(tu.Casting);
            Build(tu);
            Boss.Rig.Pour(true);
        }

        public float CastingHp(Turret tu) => tu.Up ? tu.CastMax : 0;   // tests read the part's HP through TakeHit; this is the max

        void Sink(Turret tu)
        {
            if (!tu.Up) return;
            bool outOfOrder = Hard && Turrets.Any(x => x.Up && x.K < tu.K);
            tu.Up = false; tu.Casting.Alive = false; W.Parts.Remove(tu.Casting);
            foreach (var h in tu.Pending) Cancel(h);
            tu.Pending.Clear();
            W.BossFx.SlagSplash(tu.Casting.Pos, CastRadius + 0.8f);
            if (tu.Look) tu.Look.gameObject.AddComponent<Sinker>();
            if (tu.CastLook) Object.Destroy(tu.CastLook.gameObject);
            if (tu.Chain) Object.Destroy(tu.Chain.gameObject);
            if (outOfOrder) foreach (var x in Turrets) if (x.Up) Fire(x, Pattern.Ring);   // Hard: Cast in Order
            if (W.Players.Count == 1 && spawned < total) SpawnPair(spawned);
        }

        // ---------------- patterns: the path first, then the bullets ----------------
        void Fire(Turret tu, Pattern p)
        {
            var inward = Flat(-tu.Pos).normalized;
            float baseYaw = Sim.YawTo(tu.Pos, Vector3.zero, 0);
            int warn = T(PathWarn);
            if (p == Pattern.Lane)
            {
                float yaw = baseYaw + (tu.Shots % 2 == 0 ? 12f : -12f);
                var d = Sim.Dir(yaw); var side = Vector3.Cross(Vector3.up, d);
                foreach (float o in new[] { -LaneSpacing, 0, LaneSpacing })
                {
                    var start = tu.Pos + side * o;
                    float len = Exit(start, d);
                    if (len <= 0) continue;
                    Path(tu, "Lane Stream", HazardShape.Lane, start, yaw, len, BulletRadius * 2, 0, 0, warn, () =>
                    {
                        for (int j = 0; j < 5; j++) Bullet(tu, start - d * (j * 1.2f), d * LaneSpeed, "Lane Stream bullet");
                    });
                }
            }
            else if (p == Pattern.Ring)
            {
                float flip = tu.Shots % 2 == 0 ? 1f : -1f, g1 = -RingGapAt * flip, g2 = RingGapAt * flip;   // gap angles from inward
                // the swept area as cones from the turret, leaving the two gaps open (and the half that faces the lava)
                float mid = RingGapAt - RingGapHalf, side = (90f - (RingGapAt + RingGapHalf)) / 2f;
                bool spawnedBullets = false;
                void Shot()
                {
                    if (spawnedBullets) return; spawnedBullets = true;
                    for (float a = -90f; a <= 90f; a += 3.4f)
                    {
                        if (Mathf.Abs(Mathf.DeltaAngle(a, g1)) < RingGapHalf || Mathf.Abs(Mathf.DeltaAngle(a, g2)) < RingGapHalf) continue;
                        Bullet(tu, tu.Pos, Sim.Dir(baseYaw + a) * RingSpeed, "Ring Wave bullet");
                    }
                }
                Path(tu, "Ring Wave", HazardShape.Cone, tu.Pos, baseYaw, Rim * 2 + 1, 0, mid, 0, warn, Shot);
                Path(tu, "Ring Wave", HazardShape.Cone, tu.Pos, baseYaw + RingGapAt + RingGapHalf + side, Rim * 2 + 1, 0, side, 0, warn, Shot);
                Path(tu, "Ring Wave", HazardShape.Cone, tu.Pos, baseYaw - RingGapAt - RingGapHalf - side, Rim * 2 + 1, 0, side, 0, warn, Shot);
            }
            else
            {
                float a0 = baseYaw - 60f + (tu.Shots % 2 == 0 ? 0 : 60f), sweep = SpiralTurn * SpiralFor;
                bool started = false;
                for (int k = 0; k < 3; k++)
                    Path(tu, "Spiral Arm", HazardShape.Cone, tu.Pos, a0 + k * 120f + sweep / 2, Rim * 2 + 1, 0, sweep / 2 + 4f, 0, warn, () =>
                    {
                        if (started) return; started = true;
                        spirals.Add((tu, a0, t));
                    });
            }
        }

        void StepSpirals()
        {
            int every = Mathf.Max(1, Sim.Ticks(SpiralEvery)), dur = Sim.Ticks(SpiralFor);
            spirals.RemoveAll(sp => !sp.tu.Up || t - sp.start >= dur);
            foreach (var sp in spirals)
            {
                if ((t - sp.start) % every != 0) continue;
                float a = sp.a0 + (t - sp.start) * Sim.Dt * SpiralTurn;
                for (int k = 0; k < 3; k++) Bullet(sp.tu, sp.tu.Pos, Sim.Dir(a + k * 120f) * SpiralSpeed, "Spiral Arm bullet");
            }
        }

        // a harmless preview of the real hit area; its countdown ends as the bullets leave
        void Path(Turret tu, string name, HazardShape shape, Vector3 origin, float yaw, float length, float width, float half, float radius, int warn, System.Action fire)
        {
            var h = W.Hazards.Spawn(new Hazard
            {
                Source = name + " path", Shape = shape, Kind = HazardKind.Strike, Origin = Flat(origin), Yaw = yaw, Length = length, Width = width,
                Radius = shape == HazardShape.Cone ? length : radius, HalfAngle = half, LockAt = 0, ResolveAt = warn, Damage = 0, Includes = _ => false,
            });
            h.OnResolve = _ => { tu.Pending.Remove(h); if (tu.Up && Boss.Solid) fire(); };
            tu.Pending.Add(h);
        }

        void Bullet(Turret tu, Vector3 at, Vector3 vel, string name)
        {
            at = Flat(at);
            float life = Exit(at, vel.normalized) / vel.magnitude;
            if (life <= 0.05f) return;
            int ticks = Mathf.Max(2, Sim.Ticks(life));
            var h = new Hazard
            {
                Source = name, Shape = HazardShape.Circle, Kind = HazardKind.Strike, Origin = at, Radius = BulletRadius,
                ResolveAt = 1, ActiveTicks = ticks, LockAt = ticks + 2, Damage = BulletDmg, Knockback = 0.5f, Stun = 6,
            };
            h.Track = hz => hz.Origin += vel * Sim.Dt;
            h.OnSpent = hz => Velocity.Remove(hz);
            Velocity[h] = vel;
            W.Hazards.Spawn(h);
        }

        // distance along d from p until it leaves the arena (0 if it never enters)
        static float Exit(Vector3 p, Vector3 d)
        {
            p = Flat(p); d = Flat(d).normalized;
            float b = Vector3.Dot(p, d), c = p.sqrMagnitude - (Rim + Hazard.Margin) * (Rim + Hazard.Margin), disc = b * b - c;
            if (disc <= 0) return 0;
            return Mathf.Max(0, -b + Mathf.Sqrt(disc));
        }

        // a hazard that should go quietly: no hit, no wipe, cools like a slag pool this tick
        static void Cancel(Hazard h)
        {
            if (h == null) return;
            h.Includes = _ => false; h.WipeIfEmpty = false; h.ShareTotal = 0; h.Damage = 0; h.OnResolve = null; h.OnSpent = null;
            h.BurnPerSecond = 0.0001f; h.ActiveTicks = 0; h.ResolveAt = h.Age;
        }

        // a lone turret with no casting (Molten Crown keeps one firing Lane Streams all phase)
        public Turret LoneTurret(Vector3 pos) => new Turret { K = 0, Pos = pos, A = Pattern.Lane, B = Pattern.Lane };
        public void FireLane(Turret tu) { tu.Shots++; Fire(tu, Pattern.Lane); }

        public static string Numeral(int k) => new[] { "I", "II", "III", "IV" }[k];

        // ---------------- stand-in looks ----------------
        void Build(Turret tu)
        {
            var root = new GameObject("Lava turret " + Numeral(tu.K)).transform; root.SetParent(W.Root, false);
            root.position = tu.Pos + Vector3.down * 2.2f;
            Prim.Make(PrimitiveKind.Cylinder, root, Vector3.up * 1.1f, new Vector3(1.4f, 1.1f, 1.4f), new Color(0.22f, 0.19f, 0.17f));
            Prim.Make(PrimitiveKind.Cylinder, root, Vector3.up * 2.3f, new Vector3(0.9f, 0.15f, 0.9f), new Color(1f, 0.55f, 0.18f), 2.5f);
            Label(root, Numeral(tu.K), 3.2f);
            root.gameObject.AddComponent<Riser>().Init(Rise);
            tu.Look = root;

            var cast = new GameObject("Molten Casting " + Numeral(tu.K)).transform; cast.SetParent(W.Root, false);
            cast.position = tu.Casting.Pos;
            Prim.Make(PrimitiveKind.Capsule, cast, Vector3.up * 1.0f, new Vector3(CastRadius * 2, 1.0f, CastRadius * 2), new Color(0.85f, 0.52f, 0.16f), 1.6f);
            Label(cast, Numeral(tu.K), 2.6f);
            tu.CastLook = cast;

            var chain = new GameObject("Casting chain " + Numeral(tu.K)).AddComponent<LineRenderer>(); chain.transform.SetParent(W.Root, false);
            chain.positionCount = 2; chain.SetPosition(0, tu.Pos + Vector3.up * 0.4f); chain.SetPosition(1, tu.Casting.Pos + Vector3.up * 0.4f);
            chain.startWidth = chain.endWidth = 0.09f; chain.sharedMaterial = Prim.Material(new Color(1f, 0.95f, 0.82f), 1.2f);
            tu.Chain = chain;
        }

        static void Label(Transform parent, string text, float height)
        {
            var go = new GameObject("Numeral"); go.transform.SetParent(parent, false); go.transform.localPosition = Vector3.up * height;
            var tm = go.AddComponent<TextMesh>();
            tm.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); go.GetComponent<MeshRenderer>().sharedMaterial = tm.font.material;
            tm.anchor = TextAnchor.MiddleCenter; tm.characterSize = 0.16f; tm.fontSize = 64; tm.fontStyle = FontStyle.Bold; tm.color = new Color(1f, 0.95f, 0.82f); tm.text = text;
            go.AddComponent<FaceCamera>();
        }

        static void DestroyLooks(Turret tu)
        {
            if (tu.Look) Object.Destroy(tu.Look.gameObject);
            if (tu.CastLook) Object.Destroy(tu.CastLook.gameObject);
            if (tu.Chain) Object.Destroy(tu.Chain.gameObject);
        }

        sealed class Riser : MonoBehaviour
        {
            float dur, t0; Vector3 from;
            public void Init(float seconds) { dur = seconds; t0 = Time.time; from = transform.position; }
            void Update() { float k = Mathf.Clamp01((Time.time - t0) / dur); transform.position = from + Vector3.up * 2.2f * Mathf.SmoothStep(0, 1, k); if (k >= 1) Destroy(this); }
        }
        sealed class Sinker : MonoBehaviour
        {
            float t0; Vector3 from;
            void Start() { t0 = Time.time; from = transform.position; }
            void Update() { float k = Mathf.Clamp01((Time.time - t0) / 1.2f); transform.position = from + Vector3.down * 2.4f * k; if (k >= 1) Destroy(gameObject); }
        }
        sealed class FaceCamera : MonoBehaviour
        {
            void LateUpdate() { var cam = Camera.main; if (cam) transform.rotation = Quaternion.LookRotation(transform.position - cam.transform.position); }
        }
    }
}
