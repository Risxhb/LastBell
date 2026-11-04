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
            public Action<Bullet> Split;          // runs when the fuse (Life) ends without a hit: seeds burst into petals
            public bool Dead;
        }

        public const float Height = 1.0f;         // drawn at chest height; the hit test is planar
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
                        Source = b.Source, Shape = HazardShape.Circle, Kind = HazardKind.Strike, Radius = b.Radius,
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
            public void Draw(Transform root, List<Bullet> bullets)
            {
                if (!root) return;
                if (!parent) { parent = new GameObject("Hostile Bullets").transform; parent.SetParent(root, false); foreach (var p in pools) p.Clear(); Array.Clear(shown, 0, Kinds); }
                mats ??= new[]
                {
                    new[] { Prim.Material(Coral, 0.9f), Prim.Material(CoralCore, 3f) },   // low enough that the coral reads as coral, not white
                    new[] { Prim.Material(Brass, 1.6f), Prim.Material(BrassCore, 4f) },
                    new[] { Prim.Material(Cobalt, 1.8f), Prim.Material(CobaltCore, 4f) },
                    new[] { Prim.Material(SeedFallback, 0.4f) },
                    new[] { Prim.Material(WaterC, 0.8f), Prim.Material(Coral, 3f) },
                };
                var n = new int[Kinds]; float now = Time.time;
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
                    }
                    else
                    {
                        // nose first, flat, with a slow roll so the petal catches the light; scaled to the hit radius
                        float roll = k == Water ? 0 : 25f * Mathf.Sin(b.Age * 3f + b.GetHashCode() * 0.1f);
                        t.rotation = Quaternion.LookRotation(fwd) * Quaternion.Euler(0, 0, roll);
                        t.localScale = Vector3.one * (b.Radius / (k == Water ? BulletArt.DropHitRadius : BulletArt.PetalHitRadius));
                    }
                }
                for (int k = 0; k < Kinds; k++)
                {
                    for (int i = n[k]; i < shown[k]; i++) if (pools[k][i]) pools[k][i].gameObject.SetActive(false);
                    shown[k] = n[k];
                }
            }
            public void Hide()
            {
                for (int k = 0; k < Kinds; k++) { for (int i = 0; i < shown[k] && i < pools[k].Count; i++) if (pools[k][i]) pools[k][i].gameObject.SetActive(false); shown[k] = 0; }
            }
        }
    }
}
