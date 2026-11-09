using System;
using System.Collections.Generic;
using UnityEngine;

namespace LastBell.Run
{
    // Hostile bullets (Sylvara v3's petals and seeds; any boss may fire them). Moved on the combat tick. A bullet hits
    // at most one player, once, and is gone; there is no post-hit immunity, so every bullet that touches you lands.
    // A hit goes through PlayerController.ReceiveHit as a one-off Strike hazard: difficulty scaling and Brace work as
    // for any other blow, with no knockback, stun or flinch. Cleared with every other transient at defeat and transitions.
    public sealed class BulletSystem
    {
        public sealed class Bullet
        {
            public Vector3 Pos, Vel;
            public float Radius = BulletArt.PetalHitRadius, Life = 6, Age;
            public int Damage = 12;
            public int Colour;                    // 0 hurts everyone; otherwise only players whose colour matches (Two-Colour Choir)
            public string Source = "Bullet";
            public string AttackId;               // its boss attack (BossAttackIcons), copied onto the hit
            public string Tag;                    // which Hard addition fired it (hit bookkeeping for the sweep), or null
            public Action<Bullet> Split;          // runs when the fuse (Life) ends without a hit: seeds burst into petals
            // a seed that bursts into BurstRays petals at angle BurstOff (+ i * 360/n), rolled at launch: its last PreviewFor s
            // show those exact rays at the burst point (presentation only; Sylvara P4, 7 Nov proposal)
            public int BurstRays; public float BurstOff, BurstSpeed;
            public bool Dead;
        }

        public const float Height = 1.0f;         // drawn at chest height; the hit test is planar
        public const float PreviewFor = 0.6f;     // a seed shows its burst rays for its last 0.6 s
        public const float BurstFrom = 0.3f;      // a seed's petals start 0.3 m out from where it bursts (the preview and the bots read it)
        public const float PreviewRayWidth = 2 * BulletArt.PetalHitRadius;   // each ray track is a petal's full hit width
        public int PreviewBars => view.RayBars;   // pooled preview bars (built ahead by Prewarm)
        readonly CombatWorld w;
        readonly List<Bullet> live = new List<Bullet>(), spawned = new List<Bullet>();
        public int Count => live.Count;
        public int Peak { get; private set; }
        public int Fired { get; private set; }
        public IReadOnlyList<Bullet> Live => live;
        // the encounter tells the system each player's colour (0 = none); null means nobody has one
        public Func<PlayerController, int> ColourOf;
        public float Bound = 14.1f;               // bullets leave past this radius (the map's player bound + 0.6 m)
        public event Action<PlayerController, Bullet, int> Hit;   // tests listen
        readonly BulletView view = new BulletView();

        public BulletSystem(CombatWorld world) { w = world; }

        public Bullet Fire(Vector3 pos, float yawRad, float speed, int damage, float life = 6, float radius = BulletArt.PetalHitRadius, int colour = 0, string source = "Bullet", Action<Bullet> split = null)
        {
            var b = new Bullet
            {
                Pos = Sim.Flat(pos), Vel = new Vector3(Mathf.Cos(yawRad), 0, Mathf.Sin(yawRad)) * speed,
                Damage = damage, Life = life, Radius = radius, Colour = colour, Source = source, Split = split,
            };
            spawned.Add(b); Fired++;   // joins on the next step, so a burst fired mid-step is never moved twice
            return b;
        }

        public void Step()
        {
            live.AddRange(spawned); spawned.Clear();
            for (int i = 0; i < live.Count; i++)
            {
                var b = live[i]; if (b.Dead) continue;
                b.Pos += b.Vel * Sim.Dt; b.Age += Sim.Dt;
                foreach (var p in w.Players)
                {
                    if (p.Down) continue;
                    if (b.Colour != 0 && (ColourOf == null || ColourOf(p) != b.Colour)) continue;
                    if (Sim.Flat(p.transform.position - b.Pos).magnitude >= b.Radius + Hazard.Margin) continue;
                    b.Dead = true;
                    var h = new Hazard
                    {
                        Source = b.Source, AttackId = b.AttackId, Shape = HazardShape.Circle, Kind = HazardKind.Strike, Radius = b.Radius,
                        Origin = b.Pos - b.Vel.normalized * 0.5f, Damage = b.Damage, Knockback = 0, Stun = 0, Flinch = false,
                    };
                    int hp0 = p.Hp; p.ReceiveHit(h);
                    Hit?.Invoke(p, b, hp0 - p.Hp);
                    break;
                }
            }
            for (int i = 0; i < live.Count; i++)
            {
                var b = live[i];
                if (!b.Dead && b.Split != null && b.Age >= b.Life) { b.Dead = true; b.Split(b); }
            }
            live.RemoveAll(b => b.Dead || b.Age >= b.Life || new Vector2(b.Pos.x, b.Pos.z).magnitude > Bound);
            Peak = Mathf.Max(Peak, live.Count);
        }

        public void ClearAll() { live.Clear(); spawned.Clear(); view.Hide(); }
        // LAN clients: the list holds the host's bullets (never stepped here); the client sets each one's place and look
        // every frame and draws them.
        public void SetReplicaCount(int n)
        {
            while (live.Count < n) live.Add(new Bullet());
            if (live.Count > n) live.RemoveRange(n, live.Count - n);
        }
        public void ResetStats() { Peak = 0; Fired = 0; }

        // presentation, every frame
        public void Draw(Transform root) => view.Draw(root, live);
        // builds the burst-preview bars and materials ahead of the fight (Ready), so the first Seed Storm preview builds nothing
        // 320 covers 16 seeds previewing at once (ten rays, a track and a fill each; Last Storm's 14-seed rings stay under it)
        public const int PrewarmBars = 320;
        public void Prewarm(Transform root, int rayBars = PrewarmBars) => view.Prewarm(root, rayBars);
        // the world (or LAN replica) is released: destroy the view's own materials. Clears and phase changes keep the pools.
        public void Dispose() { live.Clear(); spawned.Clear(); view.Dispose(); }
        public bool Disposed => view.Disposed;

        // Pooled per look (BulletArt): coral, brass and cobalt petals, the Storm Seed and the Waterfall Jet's water drops.
        // Shared materials per look, so hundreds of bullets stay cheap under the SRP batcher. Every look fits its hit circle.
        sealed class BulletView
        {
            public const int CoralPetal = 0, BrassPetal = 1, CobaltPetal = 2, Seed = 3, Water = 4, Kinds = 5;
            static readonly Color Coral = new Color(1f, 0.40f, 0.26f), CoralCore = new Color(1f, 0.86f, 0.66f),
                Brass = new Color(0.86f, 0.56f, 0.20f), BrassCore = new Color(1f, 0.84f, 0.40f),
                Cobalt = new Color(0.20f, 0.34f, 0.95f), CobaltCore = new Color(0.72f, 0.88f, 1f),
                WaterC = new Color(0.86f, 0.92f, 0.94f), SeedFallback = new Color(0.30f, 0.45f, 0.38f);
            readonly List<Transform>[] pools = { new List<Transform>(), new List<Transform>(), new List<Transform>(), new List<Transform>(), new List<Transform>() };
            readonly int[] shown = new int[Kinds];
            readonly List<Transform> rays = new List<Transform>(); int raysShown;   // seed burst previews: a dim track and a bright fill per ray
            Material rayDim, rayFill;
            public int RayBars => rays.Count;
            public bool Disposed => mats == null && rayDim == null && rayFill == null;
            public void Dispose()
            {
                // the materials this view made (Prim.Material makes new ones); the objects go with the world's root
                if (mats != null) foreach (var set in mats) foreach (var m in set) if (m) UnityEngine.Object.Destroy(m);
                if (rayDim) UnityEngine.Object.Destroy(rayDim); if (rayFill) UnityEngine.Object.Destroy(rayFill);
                mats = null; rayDim = rayFill = null;
                if (parent) UnityEngine.Object.Destroy(parent.gameObject);
                parent = null; foreach (var p in pools) p.Clear(); Array.Clear(shown, 0, Kinds); rays.Clear(); raysShown = 0;
            }
            Material[][] mats;
            Transform parent;

            public static int KindOf(Bullet b) => b.Radius >= 0.5f ? Seed : b.Source == "Waterfall Jet" ? Water : b.Colour == 1 ? BrassPetal : b.Colour == 2 ? CobaltPetal : CoralPetal;
            Transform Make(int k)
            {
                if (k == Seed) return BulletArt.MakeSeed(parent, mats[Seed][0]);
                var go = new GameObject(k == Water ? "Water Drop" : "Petal");
                go.transform.SetParent(parent, false);
                go.AddComponent<MeshFilter>().sharedMesh = k == Water ? BulletArt.Drop : BulletArt.Petal(k == CobaltPetal);
                var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterials = mats[k];
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;   // hundreds of them; the floor stays readable
                return go.transform;
            }
            public void Prewarm(Transform root, int rayBars)
            {
                if (!Ensure(root)) return;
                while (rays.Count < rayBars) { var t = NewBar(); t.gameObject.SetActive(false); }
            }
            bool Ensure(Transform root)
            {
                if (!root) return false;
                if (!parent) { parent = new GameObject("Hostile Bullets").transform; parent.SetParent(root, false); foreach (var p in pools) p.Clear(); Array.Clear(shown, 0, Kinds); rays.Clear(); raysShown = 0; }
                mats ??= new[]
                {
                    new[] { Prim.Material(Coral, 0.9f), Prim.Material(CoralCore, 3f) },   // low enough that the coral reads as coral, not white
                    new[] { Prim.Material(Brass, 1.6f), Prim.Material(BrassCore, 4f) },
                    new[] { Prim.Material(Cobalt, 1.8f), Prim.Material(CobaltCore, 4f) },
                    new[] { Prim.Material(SeedFallback, 0.4f) },
                    new[] { Prim.Material(WaterC, 0.8f), Prim.Material(Coral, 3f) },
                };
                rayDim ??= Prim.Material(new Color(1f, 0.40f, 0.26f) * 0.55f, 0.6f); rayFill ??= Prim.Material(new Color(1f, 0.70f, 0.45f), 3f);
                return true;
            }
            public void Draw(Transform root, List<Bullet> bullets)
            {
                if (!Ensure(root)) return;
                var n = new int[Kinds]; float now = Time.time;
                int nr = 0;
                foreach (var b in bullets)
                {
                    if (b.Dead) continue;
                    int k = KindOf(b); var pool = pools[k];
                    if (n[k] == pool.Count) pool.Add(Make(k));
                    var t = pool[n[k]++];
                    if (!t.gameObject.activeSelf) t.gameObject.SetActive(true);
                    t.position = b.Pos + Vector3.up * Height;
                    var fwd = b.Vel.sqrMagnitude > 1e-4f ? b.Vel : Vector3.forward;
                    if (k == Seed)
                    {
                        // the fuse: the coral core swells and brightens to the burst, and the husk shivers in the last quarter
                        float f = b.Life > 0 ? Mathf.Clamp01(b.Age / b.Life) : 0;
                        t.rotation = Quaternion.Euler(0, b.Age * 40f, 0);
                        float shiver = f > 0.75f ? 1 + 0.04f * Mathf.Sin(now * 60f) * (f - 0.75f) * 4 : 1;
                        t.localScale = Vector3.one * (b.Radius / 0.6f) * shiver;
                        var core = t.Find("Fuse"); if (core) core.localScale = Vector3.one * (0.55f + 0.4f * f * f);
                        float left = b.Life - b.Age;
                        if (b.BurstRays > 0 && left <= PreviewFor) nr = Preview(b, left, nr);
                    }
                    else
                    {
                        // nose first, flat, with a slow roll so the petal catches the light; scaled to the hit radius
                        float roll = k == Water ? 0 : 25f * Mathf.Sin(b.Age * 3f + b.GetHashCode() * 0.1f);
                        t.rotation = Quaternion.LookRotation(fwd) * Quaternion.Euler(0, 0, roll);
                        t.localScale = Vector3.one * (b.Radius / (k == Water ? BulletArt.DropHitRadius : BulletArt.PetalHitRadius));
                    }
                }
                for (int i = nr; i < raysShown; i++) if (rays[i]) rays[i].gameObject.SetActive(false);
                raysShown = nr;
                for (int k = 0; k < Kinds; k++)
                {
                    for (int i = n[k]; i < shown[k]; i++) if (pools[k][i]) pools[k][i].gameObject.SetActive(false);
                    shown[k] = n[k];
                }
            }
            // the burst preview: at the point the seed will burst, each petal's real swept corridor (its full hit width, 2 x
            // PetalHitRadius, with the circle's reach past both ends) as a dim floor track over the first 0.4 s of its flight (where
            // the petals still nearly touch), with a bright fill of the same width that runs out along it as the fuse counts down,
            // reaching the end at the burst. The track is the hit footprint, not a thin guide line (review SYL-PREVIEW-SHAPE, 7 Nov).
            public const float RayFrom = BurstFrom, RayFor = 0.4f, RayWidth = PreviewRayWidth, RayY = 0.06f;
            int Preview(Bullet b, float left, int nr)
            {
                var at = b.Pos + b.Vel * left; float travel = b.BurstSpeed * RayFor, fill = Mathf.Clamp01(1 - left / PreviewFor);
                float r = BulletArt.PetalHitRadius, from = RayFrom - r, len = travel + 2 * r;   // centre path plus the end caps
                for (int i = 0; i < b.BurstRays; i++)
                {
                    float a = b.BurstOff + i * Mathf.PI * 2 / b.BurstRays; var d = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                    nr = Bar(nr, rayDim, at + d * (from + len * 0.5f), d, len, RayWidth, RayY);
                    if (fill > 0.01f) nr = Bar(nr, rayFill, at + d * (from + len * 0.5f * fill), d, len * fill, RayWidth, RayY + 0.01f);
                }
                return nr;
            }
            Transform NewBar()
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Quad); go.name = "Burst Ray"; UnityEngine.Object.Destroy(go.GetComponent<Collider>());
                go.transform.SetParent(parent, false); var r = go.GetComponent<MeshRenderer>(); r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
                r.sharedMaterial = rayDim; rays.Add(go.transform); return go.transform;
            }
            int Bar(int nr, Material m, Vector3 mid, Vector3 dir, float len, float width, float y)
            {
                if (nr == rays.Count) NewBar();
                var t = rays[nr]; if (!t.gameObject.activeSelf) t.gameObject.SetActive(true);
                var mr = t.GetComponent<MeshRenderer>(); if (mr.sharedMaterial != m) mr.sharedMaterial = m;
                t.position = new Vector3(mid.x, y, mid.z);
                t.rotation = Quaternion.LookRotation(Vector3.down, dir);   // flat on the floor, long side along the ray
                t.localScale = new Vector3(width, len, 1);
                return nr + 1;
            }
            public void Hide()
            {
                for (int i = 0; i < raysShown && i < rays.Count; i++) if (rays[i]) rays[i].gameObject.SetActive(false); raysShown = 0;
                for (int k = 0; k < Kinds; k++) { for (int i = 0; i < shown[k] && i < pools[k].Count; i++) if (pools[k][i]) pools[k][i].gameObject.SetActive(false); shown[k] = 0; }
            }
        }
    }
}
