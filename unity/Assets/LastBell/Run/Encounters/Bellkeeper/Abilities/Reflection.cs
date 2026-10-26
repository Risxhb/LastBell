using System.Collections.Generic;
using UnityEngine;

namespace LastBell.Run
{
    // Reflection of the Hour (`bellkeeper.reflection`), Bellkeeper ability set v2 (proposal).
    // Brief: bosses/4-shattered-hour-bellkeeper/abilities-v2/03-reflection.md. Contract: BellkeeperAbility.cs.
    //
    // One Ground circle per standing player, centred on that player's position point-mirrored through the boss
    // (2·boss − player), clamped to inside the map bound. The images track their player (and the boss) until they lock
    // 2.0 s before resolving. Solo adds a second image at the player's own position. Co-op answer: the party gathers on
    // one side of the boss so every image lands on the other side.
    //
    // Presentation: the shared coral telegraph is the real hit shape; inside it stands a porcelain after-image of the
    // mirrored player (identity, never coral), turned the way the reflection faces. A thin ivory thread runs from each
    // player through the boss to their image while it still tracks and is cut on lock. On resolve the after-image
    // shatters in a chime burst.
    public sealed class ReflectionAbility : BellkeeperAbility
    {
        public override string Id => BellkeeperAbilities.Reflection;
        public override string Name => "Reflection of the Hour";

        // Medium numbers (brief). Easy: warning ×1.25 with lock 2.5 s before, radius −0.4 m. Hard: unchanged.
        public const int Damage = 35;
        public const int RecoveryTicks = 60;
        public const float RadiusSmallParty = 3.0f, RadiusLargeParty = 2.5f, EasyRadiusCut = 0.4f;
        public const int LargePartyFrom = 5;
        public int WarnTicks => Level == Difficulty.Easy ? 300 : 240;
        public int LockBeforeTicks => Level == Difficulty.Easy ? 150 : 120;
        public int LockAtTicks => WarnTicks - LockBeforeTicks;
        public float RadiusFor(int standing) => (standing >= LargePartyFrom ? RadiusLargeParty : RadiusSmallParty) - (Level == Difficulty.Easy ? EasyRadiusCut : 0);

        sealed class Image { public Hazard H; public PlayerController P; public bool Own; public Transform Fig, Thread; }
        readonly List<Image> images = new List<Image>();
        public int ImagesShown => images.Count;   // tests: live after-image presentation (0 after resolve or cancel)
        static Material porcelain, thread;

        // 2·boss − player on the floor, clamped to inside the play bound
        public Vector3 MirrorOf(Vector3 player) => ClampToBound(2 * Sim.Flat(Boss.transform.position) - Sim.Flat(player));
        public static Vector3 ClampToBound(Vector3 p)
        {
            p.y = 0; float b = CombatWorld.PlayerBound;
            if (CombatWorld.SquareBound) { p.x = Mathf.Clamp(p.x, -b, b); p.z = Mathf.Clamp(p.z, -b, b); }
            else if (p.magnitude > b) p = p.normalized * b;
            return p;
        }

        public override IEnumerable<int> Cast()
        {
            Boss.ShowAttack(Name);
            if (Boss.Presenter) Boss.Presenter.PlayOnce("EchoTell", 1.53f);
            ClearImages();
            int warn = Tuning?.WarnTicks ?? WarnTicks, lockAt = Mathf.Max(0, warn - LockBeforeTicks), standing = StandingCount;
            float radius = RadiusFor(standing);
            foreach (var p in W.Players.ToArray())
            {
                if (p.Down) continue;
                AddImage(p, false, radius, lockAt, warn);
                if (standing == 1) AddImage(p, true, radius, lockAt, warn);   // solo: a second image at your own position
            }
            for (int i = 0; i < warn; i++)
            {
                Boss.Glow(i / (float)warn);
                foreach (var img in images) PlaceFigure(img, i, lockAt);
                yield return 1;
            }
            ClearImages();
            yield return RecoveryTicks;
        }

        void AddImage(PlayerController p, bool own, float radius, int lockAt, int warn)
        {
            var img = new Image { P = p, Own = own };
            img.H = Spawn(new Hazard
            {
                Source = Name, Shape = HazardShape.Circle, Kind = HazardKind.Ground, Radius = radius,
                Origin = Where(img), LockAt = lockAt, ResolveAt = warn, Damage = Tuning?.Damage ?? Damage, Knockback = 1.5f, Stun = 15,
                Track = hz => { if (img.P && !img.P.Down) hz.Origin = Where(img); },
                OnResolve = hz =>
                {
                    W.BossFx.ChimeResolve(hz.Origin, hz.Radius);
                    Boss.Flare(0.6f);
                    Drop(img);
                },
            });
            BuildFigure(img);
            images.Add(img);
        }
        Vector3 Where(Image img) => img.Own ? ClampToBound(img.P.transform.position) : MirrorOf(img.P.transform.position);

        // ---------------- presentation (stand-in meshes) ----------------
        void BuildFigure(Image img)
        {
            if (porcelain == null) porcelain = Prim.Material(new Color(0.93f, 0.9f, 0.82f), 0.55f);
            if (thread == null) thread = Prim.Material(new Color(0.85f, 0.83f, 0.76f), 0.35f);
            var root = new GameObject(img.Own ? "Reflection image (own)" : "Reflection image").transform;
            Part(PrimitiveKind.Capsule, root, new Vector3(0, 0.62f, 0), new Vector3(0.5f, 0.42f, 0.38f), porcelain);   // body
            Part(PrimitiveKind.Sphere, root, new Vector3(0, 1.32f, 0), Vector3.one * 0.36f, porcelain);                 // head
            Part(PrimitiveKind.Cone, root, new Vector3(0, 0.75f, 0.28f), new Vector3(0.22f, 0.3f, 0.22f), porcelain,    // facing wedge
                 Quaternion.Euler(90, 0, 0));
            img.Fig = root;
            if (!img.Own)
            {
                var t = new GameObject("Reflection thread").transform;
                Part(PrimitiveKind.Cube, t, new Vector3(0, 0, 0.5f), new Vector3(0.06f, 0.02f, 1f), thread);
                img.Thread = t;
            }
            PlaceFigure(img, 0, 1);
        }
        static void Part(PrimitiveKind k, Transform parent, Vector3 pos, Vector3 scale, Material mat, Quaternion? rot = null)
        {
            var go = new GameObject(k.ToString()); go.transform.SetParent(parent, false);
            go.transform.localPosition = pos; go.transform.localScale = scale; go.transform.localRotation = rot ?? Quaternion.identity;
            go.AddComponent<MeshFilter>().sharedMesh = Prim.MeshOf(k);
            var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        void PlaceFigure(Image img, int tick, int lockAt)
        {
            if (!img.Fig) return;
            var c = img.H.Origin;
            bool locked = tick >= lockAt;
            // the reflection faces the mirrored way (a point reflection turns a heading by 180°); your own image faces with you
            if (!locked && img.P) img.Fig.rotation = Quaternion.Euler(0, img.P.transform.eulerAngles.y + (img.Own ? 0 : 180), 0);
            // the after-image rises out of the floor over the first 0.4 s, then trembles ("cracks") once locked
            float rise = Mathf.Clamp01(tick / 24f), shiver = locked ? 0.03f * Mathf.Sin(tick * 1.7f) : 0;
            img.Fig.position = new Vector3(c.x + shiver, Mathf.Lerp(-1.4f, 0, rise), c.z);
            if (!img.Thread) return;
            bool show = !locked && img.P && !img.P.Down;
            img.Thread.gameObject.SetActive(show);
            if (!show) return;
            var from = Sim.Flat(img.P.transform.position); var d = Sim.Flat(c) - from;
            img.Thread.position = from + Vector3.up * 0.04f;
            img.Thread.rotation = d.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(d) : Quaternion.identity;
            img.Thread.localScale = new Vector3(1, 1, d.magnitude);
        }

        void Drop(Image img)
        {
            if (img.Fig) Object.Destroy(img.Fig.gameObject);
            if (img.Thread) Object.Destroy(img.Thread.gameObject);
            img.Fig = img.Thread = null;
            images.Remove(img);
        }
        void ClearImages() { for (int i = images.Count - 1; i >= 0; i--) Drop(images[i]); }

        public override void Cancel()
        {
            ClearImages();
            base.Cancel();
        }
    }
}
