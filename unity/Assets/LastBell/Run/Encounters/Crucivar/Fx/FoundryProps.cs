using System.Collections.Generic;
using UnityEngine;

namespace LastBell.Run
{
    // Brass Foundry props for Crucivar's Phase 2 and 3 mechanics (graphics audit 8 Nov: these were Prim stand-ins). All
    // meshes are built in code once and shared: an iron-and-brass lava turret with a molten mouth and a spout facing the
    // arena centre, a cast-iron mould holding a molten casting, a real chain of links between them, a gear-toothed brass
    // mould ring and a molten fill disc for the Mould Towers, and the molten slug every turret shot carries. Presentation
    // only: sizes, hitboxes and timings stay with the abilities. Materials go through Prim.Material, whose Resources
    // materials survive player-build stripping.
    public static class FoundryProps
    {
        public static readonly Color IronCol = new Color(0.17f, 0.15f, 0.14f), BrassCol = new Color(0.78f, 0.56f, 0.27f),
            MoltenCol = new Color(1f, 0.5f, 0.14f), CrustCol = new Color(0.32f, 0.12f, 0.06f);
        static Material iron, brass, molten, crust, slugMat;
        public static Material Iron => iron ? iron : iron = Tune(Prim.Material(IronCol), 0.7f, 0.35f);
        public static Material Brass => brass ? brass : brass = Tune(Prim.Material(BrassCol), 0.9f, 0.6f);
        public static Material Molten => molten ? molten : molten = Prim.Material(MoltenCol, 2.6f);
        public static Material Crust => crust ? crust : crust = Prim.Material(CrustCol, 0.9f);
        public static Material SlugMat { get { if (!slugMat) { slugMat = Prim.Material(MoltenCol, 2.0f); slugMat.enableInstancing = true; } return slugMat; } }
        static Material Tune(Material m, float metal, float smooth) { m.SetFloat("_Metallic", metal); m.SetFloat("_Smoothness", smooth); return m; }

        // ---------------- mesh helpers ----------------
        // a profile (x = radius, y = height) turned round the Y axis; flat = hard-edged facets for a cast look
        public static Mesh Lathe(Vector2[] prof, int seg, float yaw0 = 0)
        {
            var v = new List<Vector3>(); var t = new List<int>();
            for (int s = 0; s <= seg; s++)
            {
                float a = yaw0 + 2 * Mathf.PI * s / seg; float c = Mathf.Cos(a), sn = Mathf.Sin(a);
                foreach (var p in prof) v.Add(new Vector3(p.x * c, p.y, p.x * sn));
            }
            int n = prof.Length;
            for (int s = 0; s < seg; s++)
                for (int i = 0; i < n - 1; i++)
                {
                    int a0 = s * n + i, a1 = a0 + 1, b0 = (s + 1) * n + i, b1 = b0 + 1;
                    t.Add(a0); t.Add(a1); t.Add(b0); t.Add(b0); t.Add(a1); t.Add(b1);
                }
            var m = new Mesh(); m.SetVertices(v); m.SetTriangles(t, 0); m.RecalculateNormals(); m.RecalculateBounds(); return m;
        }

        // a box from min to max corners, added into a running vertex/triangle list (for combined meshes)
        static void AddBox(List<Vector3> v, List<int> t, Matrix4x4 m, Vector3 size)
        {
            var h = size * 0.5f; int b = v.Count;
            Vector3[] c = { new Vector3(-h.x, -h.y, -h.z), new Vector3(h.x, -h.y, -h.z), new Vector3(h.x, h.y, -h.z), new Vector3(-h.x, h.y, -h.z),
                            new Vector3(-h.x, -h.y, h.z), new Vector3(h.x, -h.y, h.z), new Vector3(h.x, h.y, h.z), new Vector3(-h.x, h.y, h.z) };
            int[][] f = { new[] { 0, 3, 2, 1 }, new[] { 4, 5, 6, 7 }, new[] { 0, 1, 5, 4 }, new[] { 3, 7, 6, 2 }, new[] { 0, 4, 7, 3 }, new[] { 1, 2, 6, 5 } };
            foreach (var q in f)
            {
                int s = v.Count;
                foreach (int i in q) v.Add(m.MultiplyPoint3x4(c[i]));
                t.Add(s); t.Add(s + 1); t.Add(s + 2); t.Add(s); t.Add(s + 2); t.Add(s + 3);
            }
        }
        static Mesh Finish(List<Vector3> v, List<int> t, string name)
        {
            var m = new Mesh { name = name }; if (v.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            m.SetVertices(v); m.SetTriangles(t, 0); m.RecalculateNormals(); m.RecalculateBounds(); return m;
        }
        static Transform Part(Transform parent, string name, Mesh mesh, Material mat, Vector3 pos, Quaternion? rot = null, Vector3? scale = null)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false);
            go.transform.localPosition = pos; go.transform.localRotation = rot ?? Quaternion.identity; go.transform.localScale = scale ?? Vector3.one;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = mat; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            return go.transform;
        }

        // ---------------- lava turret (Foundry Uprising, Molten Crown's lone turret) ----------------
        static Mesh turretBody, turretBands, turretMouth, turretSpout, turretSpoutTip, rivets;
        // about 2.4 m tall on a 0.85 m plinth: octagonal cast-iron barrel, two brass bands with rivets, a glowing crucible
        // mouth, and a spout at 1.55 m pointing at the arena centre (the shots leave from the turret's position)
        public static Transform Turret(Transform parent, string name)
        {
            if (!turretBody)
            {
                turretBody = Lathe(new[] { new Vector2(0, 0), new Vector2(0.88f, 0), new Vector2(0.88f, 0.22f), new Vector2(0.72f, 0.34f), new Vector2(0.62f, 1.55f),
                    new Vector2(0.7f, 1.72f), new Vector2(0.7f, 1.95f), new Vector2(0.56f, 2.08f), new Vector2(0.44f, 2.0f) }, 8, Mathf.PI / 8);
                turretBands = Combine(Lathe(new[] { new Vector2(0.68f, 0.55f), new Vector2(0.74f, 0.6f), new Vector2(0.74f, 0.78f), new Vector2(0.68f, 0.83f) }, 8, Mathf.PI / 8),
                                      Lathe(new[] { new Vector2(0.64f, 1.18f), new Vector2(0.7f, 1.22f), new Vector2(0.7f, 1.38f), new Vector2(0.64f, 1.42f) }, 8, Mathf.PI / 8));
                turretMouth = Lathe(new[] { new Vector2(0.45f, 2.0f), new Vector2(0.3f, 1.9f), new Vector2(0, 1.86f) }, 16);
                turretSpout = Lathe(new[] { new Vector2(0, 0), new Vector2(0.26f, 0), new Vector2(0.2f, 0.9f), new Vector2(0.26f, 0.95f), new Vector2(0.26f, 1.05f), new Vector2(0.16f, 1.05f) }, 10);
                turretSpoutTip = Lathe(new[] { new Vector2(0.17f, 1.04f), new Vector2(0, 1.0f) }, 10);
                var v = new List<Vector3>(); var tr = new List<int>();
                foreach (float y in new[] { 0.69f, 1.30f })
                    for (int k = 0; k < 8; k++)
                    {
                        float a = k * 45f + 22.5f, r = y < 1 ? 0.75f : 0.71f;
                        AddBox(v, tr, Matrix4x4.TRS(Quaternion.Euler(0, a, 0) * new Vector3(0, y, r), Quaternion.Euler(0, a, 45), Vector3.one), new Vector3(0.07f, 0.07f, 0.07f));
                    }
                rivets = Finish(v, tr, "Foundry_Rivets");
            }
            var root = new GameObject(name).transform; root.SetParent(parent, false);
            var look = new GameObject("Turret body").transform; look.SetParent(root, false);
            Part(look, "Barrel", turretBody, Iron, Vector3.zero);
            Part(look, "Bands", turretBands, Brass, Vector3.zero);
            Part(look, "Rivets", rivets, Brass, Vector3.zero);
            Part(look, "Mouth", turretMouth, Molten, Vector3.zero);
            var spout = Part(look, "Spout", turretSpout, Brass, new Vector3(0, 1.5f, 0.45f), Quaternion.Euler(80, 0, 0));
            Part(spout, "Spout glow", turretSpoutTip, Molten, Vector3.zero);
            root.gameObject.AddComponent<FaceCentre>().Body = look;
            return root;
        }

        // the turret's body turns to face the arena centre wherever it stands (host and LAN client alike)
        public sealed class FaceCentre : MonoBehaviour
        {
            public Transform Body; Vector3 last = new Vector3(1e9f, 0, 0);
            void LateUpdate()
            {
                if (!Body) return;
                var p = transform.position; if ((p - last).sqrMagnitude < 1e-4f) return; last = p;
                var d = new Vector3(-p.x, 0, -p.z); if (d.sqrMagnitude > 1e-4f) Body.rotation = Quaternion.LookRotation(d.normalized, Vector3.up);
            }
        }

        // ---------------- molten casting (the Foundry Uprising's breakable part) ----------------
        static Mesh mouldCup, mouldHoops, castingTop, castingCore;
        // a cast-iron mould about 1.9 m tall and as wide as the casting's hit radius, brass hoops, a bell-shaped molten
        // casting rising out of it with a cooling crust at the rim; the part's HP bar and numeral stay as they were
        public static Transform Casting(Transform parent, string name, float radius)
        {
            if (!mouldCup)
            {
                float r = radius;
                mouldCup = Lathe(new[] { new Vector2(0, 0.02f), new Vector2(r * 0.92f, 0), new Vector2(r, 0.12f), new Vector2(r * 0.9f, 0.95f),
                    new Vector2(r * 0.98f, 1.05f), new Vector2(r * 0.98f, 1.18f), new Vector2(r * 0.8f, 1.2f), new Vector2(r * 0.78f, 1.1f) }, 12);
                mouldHoops = Combine(Lathe(new[] { new Vector2(r * 0.97f, 0.28f), new Vector2(r * 1.03f, 0.32f), new Vector2(r * 1.03f, 0.44f), new Vector2(r * 0.96f, 0.48f) }, 12),
                                     Lathe(new[] { new Vector2(r * 0.93f, 0.7f), new Vector2(r * 0.99f, 0.74f), new Vector2(r * 0.99f, 0.86f), new Vector2(r * 0.92f, 0.9f) }, 12));
                castingCore = Lathe(new[] { new Vector2(r * 0.78f, 1.1f), new Vector2(r * 0.76f, 1.3f), new Vector2(r * 0.6f, 1.62f), new Vector2(r * 0.38f, 1.82f),
                    new Vector2(r * 0.16f, 1.9f), new Vector2(0, 1.92f) }, 20);
                castingTop = Lathe(new[] { new Vector2(r * 0.8f, 1.14f), new Vector2(r * 0.79f, 1.24f), new Vector2(r * 0.7f, 1.26f) }, 20);
            }
            var cast = new GameObject(name).transform; cast.SetParent(parent, false);
            Part(cast, "Mould", mouldCup, Iron, Vector3.zero);
            Part(cast, "Hoops", mouldHoops, Brass, Vector3.zero);
            Part(cast, "Casting", castingCore, Molten, Vector3.zero);
            Part(cast, "Crust", castingTop, Crust, Vector3.zero);
            return cast;
        }

        // ---------------- chain between a turret and its casting ----------------
        static Mesh link;
        public const float LinkPitch = 0.42f;
        // A chain of alternating oval links lying along a line; rebuilt only when its ends move.
        public sealed class Chain : MonoBehaviour
        {
            Vector3 a, b; bool dirty = true; MeshFilter mf; Mesh mesh;
            public void SetEnds(Vector3 from, Vector3 to) { if ((from - a).sqrMagnitude + (to - b).sqrMagnitude < 1e-6f) return; a = from; b = to; dirty = true; }
            public int Links { get; private set; }
            void Awake()
            {
                mf = gameObject.AddComponent<MeshFilter>(); var r = gameObject.AddComponent<MeshRenderer>(); r.sharedMaterial = Brass;
                mesh = new Mesh { name = "Foundry_Chain" }; mf.sharedMesh = mesh;
            }
            void LateUpdate()
            {
                if (!dirty) return; dirty = false;
                transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity); transform.localScale = Vector3.one;
                var d = b - a; float len = d.magnitude; if (len < 0.1f) { mesh.Clear(); Links = 0; return; }
                var dir = d / len; var rot = Quaternion.LookRotation(dir, Vector3.up);
                var lm = LinkMesh(); var lv = lm.vertices; var lt = lm.triangles;
                var v = new List<Vector3>(); var t = new List<int>();
                int n = Mathf.Max(1, Mathf.FloorToInt(len / LinkPitch)); Links = n;
                for (int i = 0; i < n; i++)
                {
                    var p = a + dir * ((i + 0.5f) * len / n);
                    var m = Matrix4x4.TRS(p, rot * Quaternion.Euler(0, 0, i % 2 == 0 ? 0 : 90), Vector3.one);
                    int s = v.Count; foreach (var q in lv) v.Add(m.MultiplyPoint3x4(q)); foreach (int k in lt) t.Add(s + k);
                }
                mesh.Clear(); if (v.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                mesh.SetVertices(v); mesh.SetTriangles(t, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds();
            }
        }
        // one oval link: a torus stretched along Z (0.5 m long, 0.26 m wide, 0.05 m bar)
        static Mesh LinkMesh()
        {
            if (link) return link;
            const int seg = 12, ring = 6; const float rx = 0.11f, rz = 0.22f, bar = 0.035f;
            var v = new List<Vector3>(); var t = new List<int>();
            for (int i = 0; i <= seg; i++)
            {
                float u = 2 * Mathf.PI * i / seg; var c = new Vector3(Mathf.Cos(u) * rx, 0, Mathf.Sin(u) * rz);
                var nOut = new Vector3(Mathf.Cos(u) / rx, 0, Mathf.Sin(u) / rz).normalized;
                for (int j = 0; j <= ring; j++)
                {
                    float w = 2 * Mathf.PI * j / ring;
                    v.Add(c + nOut * Mathf.Cos(w) * bar + Vector3.up * Mathf.Sin(w) * bar);
                }
            }
            for (int i = 0; i < seg; i++)
                for (int j = 0; j < ring; j++)
                {
                    int a0 = i * (ring + 1) + j, a1 = a0 + 1, b0 = a0 + ring + 1, b1 = b0 + 1;
                    t.Add(a0); t.Add(b0); t.Add(a1); t.Add(a1); t.Add(b0); t.Add(b1);
                }
            link = Finish(v, t, "Foundry_Link"); return link;
        }

        // ---------------- Mould Towers ----------------
        static readonly Dictionary<float, Mesh> rings = new Dictionary<float, Mesh>();
        static Mesh fillDisc, fillCrust;
        // a brass mould ring just outside the soak circle (inner edge at radius + 0.05): a low collar with 16 gear teeth
        public static Mesh MouldRing(float radius)
        {
            if (rings.TryGetValue(radius, out var m) && m) return m;
            var v = new List<Vector3>(); var t = new List<int>();
            var collar = Lathe(new[] { new Vector2(radius + 0.05f, 0), new Vector2(radius + 0.05f, 0.16f), new Vector2(radius + 0.12f, 0.2f), new Vector2(radius + 0.24f, 0.2f),
                new Vector2(radius + 0.3f, 0.12f), new Vector2(radius + 0.3f, 0) }, 48);
            Append(v, t, collar, Matrix4x4.identity);
            for (int i = 0; i < 16; i++)
            {
                float a = 360f * i / 16;
                AddBox(v, t, Matrix4x4.TRS(Quaternion.Euler(0, a, 0) * new Vector3(0, 0.12f, radius + 0.38f), Quaternion.Euler(0, a, 0), Vector3.one), new Vector3(0.3f, 0.24f, 0.22f));
            }
            m = Finish(v, t, "Foundry_MouldRing"); rings[radius] = m; return m;
        }
        // the molten fill: a disc of radius exactly 1 (scale it to the soak radius), slightly domed
        public static Mesh FillDisc()
        {
            if (fillDisc) return fillDisc;
            fillDisc = Lathe(new[] { new Vector2(1f, 0), new Vector2(1f, 0.5f), new Vector2(0.8f, 0.85f), new Vector2(0.4f, 1f), new Vector2(0, 1f) }, 48);
            fillDisc.name = "Foundry_MouldFill"; return fillDisc;
        }
        // a darker crust ring on the fill's edge, same unit radius
        public static Mesh FillCrust()
        {
            if (fillCrust) return fillCrust;
            fillCrust = Lathe(new[] { new Vector2(1f, 0.52f), new Vector2(0.97f, 0.62f), new Vector2(0.88f, 0.7f) }, 48);
            fillCrust.name = "Foundry_MouldCrust"; return fillCrust;
        }

        // ---------------- turret shot ----------------
        static Mesh slug;
        // a molten slug about 0.7 m long and as wide as the shot's hit radius at most (0.5 m): a teardrop, nose forward (+Z)
        public static Mesh Slug()
        {
            if (slug) return slug;
            slug = Lathe(new[] { new Vector2(0, -0.42f), new Vector2(0.12f, -0.36f), new Vector2(0.22f, -0.18f), new Vector2(0.26f, 0.02f), new Vector2(0.2f, 0.18f), new Vector2(0, 0.3f) }, 12);
            var v = slug.vertices; for (int i = 0; i < v.Length; i++) v[i] = new Vector3(v[i].x, -v[i].z, v[i].y);   // turn the lathe axis Y onto Z (a rotation, so the winding holds)
            slug.vertices = v; slug.RecalculateNormals(); slug.RecalculateBounds(); slug.name = "Foundry_Slug";
            return slug;
        }

        // Draws a molten slug on every live Crucivar turret shot, instanced in one call per 1023 (host and LAN client: it
        // reads the live hazard list). The ground circle stays as the hit shape, tinted ember so the slug reads as the shot.
        public sealed class SlugView : MonoBehaviour
        {
            CombatWorld w; readonly Matrix4x4[] mats = new Matrix4x4[1023]; RenderParams rp; bool rpSet;
            public int Drawn { get; private set; }
            public static SlugView Ensure(CombatWorld world)
            {
                if (world == null || !world.Root) return null;
                var v = world.Root.GetComponentInChildren<SlugView>(true);
                if (v) return v;
                v = new GameObject("Molten slugs").AddComponent<SlugView>(); v.transform.SetParent(world.Root, false); v.w = world;
                return v;
            }
            public static bool IsShot(Hazard h) => h.Source != null && h.Source.EndsWith(" bullet") && h.AttackId != null && h.AttackId.StartsWith("crucivar.");
            void LateUpdate()
            {
                if (w == null) return;
                if (!rpSet) { rp = new RenderParams(SlugMat) { shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off, receiveShadows = false }; rpSet = true; }
                int n = 0; Drawn = 0; var mesh = Slug(); float t = Time.time;
                var live = w.Hazards.Live;
                for (int i = 0; i < live.Count; i++)
                {
                    var h = live[i]; if (!IsShot(h)) continue;
                    var d = new Vector3(h.Drift.x, 0, h.Drift.z); if (d.sqrMagnitude < 1e-4f) d = Vector3.forward;
                    float wob = 1f + 0.08f * Mathf.Sin(t * 18f + i);
                    mats[n++] = Matrix4x4.TRS(new Vector3(h.Origin.x, 0.55f, h.Origin.z), Quaternion.LookRotation(d.normalized, Vector3.up), new Vector3(wob, wob, 2f - wob));
                    if (n == mats.Length) { Graphics.RenderMeshInstanced(rp, mesh, 0, mats, n); Drawn += n; n = 0; }
                }
                if (n > 0) { Graphics.RenderMeshInstanced(rp, mesh, 0, mats, n); Drawn += n; }
            }
        }

        static Mesh Combine(params Mesh[] parts)
        {
            var v = new List<Vector3>(); var t = new List<int>();
            foreach (var p in parts) Append(v, t, p, Matrix4x4.identity);
            return Finish(v, t, "Foundry_Combined");
        }
        static void Append(List<Vector3> v, List<int> t, Mesh m, Matrix4x4 at)
        {
            int s = v.Count; foreach (var q in m.vertices) v.Add(at.MultiplyPoint3x4(q)); foreach (int k in m.triangles) t.Add(s + k);
        }
    }
}
