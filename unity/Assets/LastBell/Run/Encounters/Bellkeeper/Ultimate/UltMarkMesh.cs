using System.Collections.Generic;
using UnityEngine;

namespace LastBell.Run
{
    // Code-built meshes for the Ultimate's duty markers (graphics audit, 8 Nov: they were Prim cubes). Presentation only, and
    // each fills the footprint of what it replaces, so the rules read the same:
    //   Cross  the counterweight ✕: two arms with pointed ends and a hub, flat, `size` metres corner to corner
    //   Chain  a tether as interlocking oval links along z from -0.5 to 0.5 (a unit cube's span: scale z to the length), alternately flat and upright
    public static class UltMarkMesh
    {
        static Mesh cross, chain;

        public static Mesh Cross
        {
            get
            {
                if (cross) return cross;
                // one arm along z (unit length, 0.145 wide) with pointed ends, then the same rotated 90°; a small octagon hub
                var v = new List<Vector3>(); var tri = new List<int>();
                void Arm(float yaw)
                {
                    var q = Quaternion.Euler(0, yaw, 0); int b = v.Count; float w = 0.0727f, h = 0.5f, tip = 0.43f;
                    foreach (var p in new[] { new Vector3(0, 0, h), new Vector3(w, 0, tip), new Vector3(w, 0, -tip), new Vector3(0, 0, -h), new Vector3(-w, 0, -tip), new Vector3(-w, 0, tip) })
                        v.Add(q * p);
                    tri.AddRange(new[] { b, b + 1, b + 5, b + 1, b + 2, b + 5, b + 2, b + 4, b + 5, b + 2, b + 3, b + 4 });
                }
                Arm(45); Arm(-45);
                int c = v.Count; v.Add(new Vector3(0, 0.002f, 0));
                for (int i = 0; i < 8; i++) { float a = i * Mathf.PI / 4 + Mathf.PI / 8; v.Add(new Vector3(Mathf.Sin(a), 0.002f, Mathf.Cos(a)) * 0.13f + new Vector3(0, 0.002f, 0)); }
                for (int i = 0; i < 8; i++) { tri.Add(c); tri.Add(c + 1 + i); tri.Add(c + 1 + (i + 1) % 8); }
                cross = new Mesh { name = "Ultimate counterweight cross" };
                cross.SetVertices(v); cross.SetTriangles(tri, 0); cross.RecalculateNormals(); cross.RecalculateBounds();
                return cross;
            }
        }

        public static Mesh Chain
        {
            get
            {
                if (chain) return chain;
                const int links = 18, around = 10, tube = 4;
                var v = new List<Vector3>(); var tri = new List<int>();
                float step = 1f / links;
                for (int l = 0; l < links; l++)
                {
                    float cz = (l + 0.5f) * step - 0.5f; bool upright = l % 2 == 1;
                    float rz = step * 0.78f, rx = 0.32f, r = 0.12f;   // link half-length (overlaps its neighbours), half-width, wire
                    int b = v.Count;
                    for (int i = 0; i < around; i++)
                    {
                        float a = i * Mathf.PI * 2 / around; var c = new Vector3(Mathf.Cos(a) * rx, 0, Mathf.Sin(a) * rz);
                        var n = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)).normalized;
                        for (int j = 0; j < tube; j++)
                        {
                            float t = j * Mathf.PI * 2 / tube; var p = c + n * Mathf.Cos(t) * r + Vector3.up * Mathf.Sin(t) * r;
                            if (upright) p = new Vector3(p.y, p.x, p.z);   // turn the link a quarter round its own axis
                            v.Add(p + new Vector3(0, 0, cz));
                        }
                    }
                    for (int i = 0; i < around; i++)
                        for (int j = 0; j < tube; j++)
                        {
                            int a0 = b + i * tube + j, a1 = b + i * tube + (j + 1) % tube, b0 = b + ((i + 1) % around) * tube + j, b1 = b + ((i + 1) % around) * tube + (j + 1) % tube;
                            // outward faces clockwise (Unity's front); the upright links are mirrored (x and y swapped), so their order flips
                            if (upright) { tri.Add(a0); tri.Add(b0); tri.Add(a1); tri.Add(a1); tri.Add(b0); tri.Add(b1); }
                            else { tri.Add(a0); tri.Add(a1); tri.Add(b0); tri.Add(a1); tri.Add(b1); tri.Add(b0); }
                        }
                }
                chain = new Mesh { name = "Ultimate chain" };
                chain.SetVertices(v); chain.SetTriangles(tri, 0); chain.RecalculateNormals(); chain.RecalculateBounds();
                return chain;
            }
        }

        // ---------------- the 10 Nov marker pass (proposal; overview request, graphics audit follow-up) ----------------
        // Each mesh sits in the unit space of the Prim it replaces, so the caller's position and scale (the footprint) are
        // unchanged and the caller's material still says what the marker means. Presentation only.
        //   Dial    an hour plate / floor disc (Prim cylinder space: r 0.5, top at y 1): a centre disc, a thin engraved gap and a
        //           bezel ring, bridged by 12 hour studs; the outline is the same circle
        //   Beacon  a spot post (cylinder space): a footed shaft that tapers up to a collar and a small bell finial
        //   Rule    a floor line (Prim cube space): a strip pinched at even steps like a brass rule, with pointed ends
        //   Bar     a wide line (cube space): a smooth strip with chevron ends (the paid debt's chime bar)
        //   DebtRing(r)  the paid debt's porcelain ring at r (world metres): 28 curved links with rounded ends on the old 0.6 x 0.22 m segments
        //   InvRing / InvTicks  the INVERTED mark: a cobalt ring (r 0.95, 0.07 wide) and four ivory clapper tongues pointing in
        static Mesh dial, beacon, rule, bar, invRing, invTicks;
        static readonly Dictionary<float, Mesh> debtRings = new Dictionary<float, Mesh>();

        static Mesh Finish(string name, List<Vector3> v, List<int> tri)
        {
            var m = new Mesh { name = name }; m.SetVertices(v); m.SetTriangles(tri, 0); m.RecalculateNormals(); m.RecalculateBounds(); return m;
        }
        static Vector3 P(float ang, float r, float y) => new Vector3(Mathf.Sin(ang) * r, y, Mathf.Cos(ang) * r);
        static void Quad(List<Vector3> v, List<int> tri, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            int i = v.Count; v.Add(a); v.Add(b); v.Add(c); v.Add(d);
            tri.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });   // the Prim materials draw both sides (_Cull 0)
        }
        // a flat band round the centre between rIn(angle) and rOut(angle), at height y
        static void Band(List<Vector3> v, List<int> tri, int n, System.Func<float, float> rIn, System.Func<float, float> rOut, float y)
        {
            for (int i = 0; i < n; i++)
            {
                float a0 = i * Mathf.PI * 2 / n, a1 = (i + 1) * Mathf.PI * 2 / n;
                Quad(v, tri, P(a0, rIn(a0), y), P(a0, rOut(a0), y), P(a1, rOut(a1), y), P(a1, rIn(a1), y));
            }
        }

        public static Mesh Dial
        {
            get
            {
                if (dial) return dial;
                var v = new List<Vector3>(); var tri = new List<int>();
                Band(v, tri, 48, a => 0f, a => 0.40f, 1f);        // the centre
                Band(v, tri, 48, a => 0.43f, a => 0.5f, 1f);      // the bezel
                for (int h = 0; h < 12; h++)                       // hour studs across the gap
                {
                    float a = h * Mathf.PI / 6, w = h % 3 == 0 ? 0.055f : 0.035f;
                    Quad(v, tri, P(a - w, 0.38f, 1.02f), P(a - w, 0.5f, 1.02f), P(a + w, 0.5f, 1.02f), P(a + w, 0.38f, 1.02f));
                }
                // a skirt round the outside so it has an edge seen from the side, down to the old cylinder's base
                for (int i = 0; i < 48; i++) { float a0 = i * Mathf.PI / 24, a1 = (i + 1) * Mathf.PI / 24; Quad(v, tri, P(a0, 0.5f, -1f), P(a0, 0.5f, 1f), P(a1, 0.5f, 1f), P(a1, 0.5f, -1f)); }
                return dial = Finish("Ultimate dial", v, tri);
            }
        }

        public static Mesh Beacon
        {
            get
            {
                if (beacon) return beacon;
                // (radius, height) up the post in cylinder space; 28 round
                var prof = new[] { new Vector2(0f, -1f), new Vector2(0.5f, -1f), new Vector2(0.5f, -0.93f), new Vector2(0.3f, -0.88f), new Vector2(0.24f, 0.55f),
                    new Vector2(0.42f, 0.6f), new Vector2(0.42f, 0.66f), new Vector2(0.22f, 0.7f), new Vector2(0.3f, 0.76f), new Vector2(0.5f, 0.84f),
                    new Vector2(0.34f, 0.9f), new Vector2(0.16f, 0.97f), new Vector2(0f, 1f) };
                var v = new List<Vector3>(); var tri = new List<int>(); const int n = 28;
                for (int k = 0; k < prof.Length - 1; k++)
                    for (int i = 0; i < n; i++)
                    {
                        float a0 = i * Mathf.PI * 2 / n, a1 = (i + 1) * Mathf.PI * 2 / n;
                        Quad(v, tri, P(a0, prof[k].x, prof[k].y), P(a0, prof[k + 1].x, prof[k + 1].y), P(a1, prof[k + 1].x, prof[k + 1].y), P(a1, prof[k].x, prof[k].y));
                    }
                return beacon = Finish("Ultimate beacon", v, tri);
            }
        }

        public static Mesh Rule
        {
            get
            {
                if (rule) return rule;
                var v = new List<Vector3>(); var tri = new List<int>(); const int n = 192, steps = 16;
                float W(float z)
                {
                    float e = 0.5f - Mathf.Abs(z); if (e < 0.03f) return 0.5f * e / 0.03f;   // pointed ends
                    float c = Mathf.Cos(2 * Mathf.PI * steps * (z + 0.5f) / 1f); return 0.5f * (1 - 0.35f * Mathf.Pow(Mathf.Max(0, c), 8));   // pinches
                }
                Strip(v, tri, n, W);
                return rule = Finish("Ultimate rule", v, tri);
            }
        }

        // a wide line (cube space): a smooth strip whose ends close to a shallow chevron (the paid debt's chime bar)
        public static Mesh Bar
        {
            get
            {
                if (bar) return bar;
                var v = new List<Vector3>(); var tri = new List<int>();
                Strip(v, tri, 24, z => { float e = 0.5f - Mathf.Abs(z); return e < 0.04f ? 0.5f * (0.4f + 0.6f * e / 0.04f) : 0.5f; });
                return bar = Finish("Ultimate bar", v, tri);
            }
        }

        // a strip along z (cube space, -0.5..0.5) whose half-width at z is W(z): its top and sides
        static void Strip(List<Vector3> v, List<int> tri, int n, System.Func<float, float> W)
        {
            for (int i = 0; i < n; i++)
            {
                float z0 = -0.5f + i / (float)n, z1 = -0.5f + (i + 1) / (float)n, w0 = W(z0), w1 = W(z1);
                Quad(v, tri, new Vector3(-w0, 0.5f, z0), new Vector3(-w1, 0.5f, z1), new Vector3(w1, 0.5f, z1), new Vector3(w0, 0.5f, z0));
                Quad(v, tri, new Vector3(-w0, -0.5f, z0), new Vector3(-w0, 0.5f, z0), new Vector3(-w1, 0.5f, z1), new Vector3(-w1, -0.5f, z1));   // the sides
                Quad(v, tri, new Vector3(w0, -0.5f, z0), new Vector3(w1, -0.5f, z1), new Vector3(w1, 0.5f, z1), new Vector3(w0, 0.5f, z0));
            }
        }

        public static Mesh DebtRing(float r)
        {
            if (debtRings.TryGetValue(r, out var m) && m) return m;
            var v = new List<Vector3>(); var tri = new List<int>();
            // 28 porcelain links on the old segments' footprint: each spans 0.6 m of arc and 0.22 m across, its ends rounded
            float half = 0.3f / r; const int k = 12;
            float H(float t) => 0.11f * Mathf.Sqrt(Mathf.Max(0, 1 - Mathf.Pow(2 * t - 1, 6)));
            for (int i = 0; i < 28; i++)
            {
                float c = i * Mathf.PI * 2 / 28;
                for (int j = 0; j < k; j++)
                {
                    float t0 = j / (float)k, t1 = (j + 1) / (float)k, a0 = c - half + 2 * half * t0, a1 = c - half + 2 * half * t1;
                    Quad(v, tri, P(a0, r - H(t0), 0.025f), P(a0, r + H(t0), 0.025f), P(a1, r + H(t1), 0.025f), P(a1, r - H(t1), 0.025f));
                }
            }
            return debtRings[r] = Finish("Ultimate debt ring", v, tri);
        }

        public static Mesh InvRing
        {
            get
            {
                if (invRing) return invRing;
                var v = new List<Vector3>(); var tri = new List<int>();
                Band(v, tri, 48, a => 0.915f, a => 0.985f, 0.01f);
                return invRing = Finish("Ultimate INVERTED ring", v, tri);
            }
        }

        public static Mesh InvTicks
        {
            get
            {
                if (invTicks) return invTicks;
                var v = new List<Vector3>(); var tri = new List<int>();
                for (int s = 0; s < 4; s++)   // 0.36 m from r 0.9 inward, 0.08 wide at the ring, narrowing to a tongue tip
                {
                    float a = s * Mathf.PI / 2; var d = new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a)); var p = new Vector3(d.z, 0, -d.x); var y = Vector3.up * 0.0125f;
                    Quad(v, tri, d * 0.9f + p * 0.04f + y, d * 0.9f - p * 0.04f + y, d * 0.6f - p * 0.036f + y, d * 0.6f + p * 0.036f + y);
                    Quad(v, tri, d * 0.6f + p * 0.036f + y, d * 0.6f - p * 0.036f + y, d * 0.54f - p * 0.014f + y, d * 0.54f + p * 0.014f + y);
                }
                return invTicks = Finish("Ultimate INVERTED ticks", v, tri);
            }
        }

        // a flat arc band (world metres) from rIn to rOut between two bearings (degrees, the Ultimate's sin/cos convention), at y 0
        static readonly Dictionary<(float, float, float, float), Mesh> arcs = new Dictionary<(float, float, float, float), Mesh>();
        public static Mesh ArcBand(float rIn, float rOut, float fromDeg, float toDeg)
        {
            var key = (rIn, rOut, fromDeg, toDeg);
            if (arcs.TryGetValue(key, out var m) && m) return m;
            var v = new List<Vector3>(); var tri = new List<int>();
            int n = Mathf.Max(8, Mathf.CeilToInt((toDeg - fromDeg) / 2f));
            for (int i = 0; i < n; i++)
            {
                float a0 = Mathf.Deg2Rad * Mathf.Lerp(fromDeg, toDeg, i / (float)n), a1 = Mathf.Deg2Rad * Mathf.Lerp(fromDeg, toDeg, (i + 1) / (float)n);
                Quad(v, tri, P(a0, rIn, 0), P(a0, rOut, 0), P(a1, rOut, 0), P(a1, rIn, 0));
            }
            return arcs[key] = Finish("Ultimate arc band", v, tri);
        }

        // a marker: the themed mesh in the replaced Prim's place (same position, scale and rotation), or that Prim itself with
        // UltLook.PrimMarks (before/after screenshots). The caller sets the material, as it did on the Prim.
        public static Transform Mark(PrimitiveKind was, Mesh themed, Transform parent, Vector3 localPos, Vector3 scale, Quaternion? rot = null)
        {
            if (UltLook.PrimMarks) return Prim.Make(was, parent, localPos, scale, Color.white, 0, rot);
            var go = new GameObject(themed.name); var t = go.transform; t.SetParent(parent, false);
            t.localPosition = localPos; t.localScale = scale; t.localRotation = rot ?? Quaternion.identity;
            go.AddComponent<MeshFilter>().sharedMesh = themed;
            var r = go.AddComponent<MeshRenderer>(); r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            return t;
        }

        public static Transform Make(Mesh mesh, Transform parent, Material m, string name)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false);
            if (UltLook.Old)   // the Prim bars these replaced (before/after screenshots)
            {
                void Bar(Vector3 scale, float yaw) { var b = Prim.Make(PrimitiveKind.Cube, go.transform, Vector3.zero, scale, Color.white, 0, Quaternion.Euler(0, yaw, 0)); var br = b.GetComponent<Renderer>(); br.sharedMaterial = m; br.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; }
                if (mesh == Cross) { Bar(new Vector3(0.145f, 0.03f, 1f), 45); Bar(new Vector3(0.145f, 0.03f, 1f), -45); } else Bar(new Vector3(1 / 3f, 1 / 3f, 1f), 0);
                go.AddComponent<MeshRenderer>().enabled = false;   // callers swap the material on this renderer: keep one, unseen
                return go.transform;
            }
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = m;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            return go.transform;
        }
    }
}
