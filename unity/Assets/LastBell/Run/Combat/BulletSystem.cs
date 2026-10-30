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
            public float Radius = 0.35f, Life = 6, Age;
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

        public Bullet Fire(Vector3 pos, float yawRad, float speed, int damage, float life = 6, float radius = 0.35f, int colour = 0, string source = "Bullet", Action<Bullet> split = null)
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
        public void ResetStats() { Peak = 0; Fired = 0; }

        // presentation, every frame
        public void Draw(Transform root) => view.Draw(root, live);

        // Pooled spheres, one shared emissive material per colour. Hundreds of bullets stay cheap under the SRP batcher.
        sealed class BulletView
        {
            static readonly Color Petal = new Color(1f, 0.48f, 0.36f), Brass = new Color(0.85f, 0.64f, 0.25f),
                Cobalt = new Color(0.36f, 0.52f, 1f), Seed = new Color(0.62f, 0.85f, 0.35f);
            readonly List<Transform> pool = new List<Transform>();
            readonly List<int> poolKind = new List<int>();
            Material[] mats;
            Transform parent;
            int shown;

            int KindOf(Bullet b) => b.Radius >= 0.5f ? 3 : b.Colour;
            public void Draw(Transform root, List<Bullet> bullets)
            {
                if (!root) return;
                if (!parent) { parent = new GameObject("Hostile Bullets").transform; parent.SetParent(root, false); pool.Clear(); poolKind.Clear(); }
                mats ??= new[] { Prim.Material(Petal, 2.2f), Prim.Material(Brass, 2.2f), Prim.Material(Cobalt, 2.2f), Prim.Material(Seed, 1.6f) };
                int n = 0;
                foreach (var b in bullets)
                {
                    if (b.Dead) continue;
                    int k = KindOf(b);
                    if (n == pool.Count) { pool.Add(Prim.Make(PrimitiveKind.Sphere, parent, Vector3.zero, Vector3.one, Petal)); poolKind.Add(-1); }
                    var t = pool[n];
                    if (poolKind[n] != k) { t.GetComponent<MeshRenderer>().sharedMaterial = mats[k]; poolKind[n] = k; }
                    if (!t.gameObject.activeSelf) t.gameObject.SetActive(true);
                    t.position = b.Pos + Vector3.up * Height;
                    float d = b.Radius * 2;
                    t.localScale = k == 0 ? new Vector3(d, d * 0.6f, d * 1.5f) : Vector3.one * d;   // petals are long along their flight
                    if (k == 0 && b.Vel.sqrMagnitude > 1e-4f) t.rotation = Quaternion.LookRotation(b.Vel);
                    n++;
                }
                for (int i = n; i < shown; i++) if (pool[i]) pool[i].gameObject.SetActive(false);
                shown = n;
            }
            public void Hide() { for (int i = 0; i < shown && i < pool.Count; i++) if (pool[i]) pool[i].gameObject.SetActive(false); shown = 0; }
        }
    }
}
