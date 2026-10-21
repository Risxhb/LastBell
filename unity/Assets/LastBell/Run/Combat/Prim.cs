using System.Collections.Generic;
using UnityEngine;

namespace LastBell.Run
{
    public enum PrimitiveKind { Cube, Sphere, Cylinder, Capsule, Cone }

    // Code-built meshes and URP Lit materials for labelled stand-ins (placeholder bosses, bell marks). Meshes are
    // generated, not GameObject.CreatePrimitive, because the project ships without the Physics module (no colliders).
    // Materials come from Resources/Run/*.mat so the URP Lit variants (opaque, emissive) survive player-build stripping.
    public static class Prim
    {
        static readonly Dictionary<PrimitiveKind, Mesh> meshes = new Dictionary<PrimitiveKind, Mesh>();
        static Material lit, litGlow;

        public static Transform Make(PrimitiveKind kind, Transform parent, Vector3 localPos, Vector3 scale, Color color, float glow = 0, Quaternion? rot = null)
        {
            var go = new GameObject(kind.ToString());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos; go.transform.localScale = scale; go.transform.localRotation = rot ?? Quaternion.identity;
            go.AddComponent<MeshFilter>().sharedMesh = MeshOf(kind);
            var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = Material(color, glow);
            return go.transform;
        }

        public static Material Material(Color color, float glow = 0)
        {
            if (lit == null)
            {
                lit = Resources.Load<Material>("Run/PrimLit");
                litGlow = Resources.Load<Material>("Run/PrimLitGlow");
                if (lit == null) lit = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                if (litGlow == null) { litGlow = new Material(lit); litGlow.EnableKeyword("_EMISSION"); }
            }
            var m = new Material(glow > 0 ? litGlow : lit);
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Smoothness", 0.45f); m.SetFloat("_Metallic", 0.3f); m.SetFloat("_Cull", 0);
            if (glow > 0) { m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", color * glow); }
            return m;
        }

        public static void SetGlow(Renderer r, Color c)
        {
            if (r == null) return; var m = r.material; m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", c);
        }

        public static Mesh MeshOf(PrimitiveKind k)
        {
            if (meshes.TryGetValue(k, out var m) && m != null) return m;
            m = k switch
            {
                PrimitiveKind.Cube => Box(),
                PrimitiveKind.Sphere => Lathe(Sphere(12), 24),
                PrimitiveKind.Cylinder => Lathe(new[] { new Vector2(0, -1), new Vector2(0.5f, -1), new Vector2(0.5f, 1), new Vector2(0, 1) }, 28),
                PrimitiveKind.Capsule => Lathe(Capsule(8), 24),
                _ => Lathe(new[] { new Vector2(0, -0.5f), new Vector2(0.5f, -0.5f), new Vector2(0, 0.5f) }, 28),
            };
            m.name = "Prim_" + k; meshes[k] = m; return m;
        }

        static Vector2[] Sphere(int n)
        {
            var p = new Vector2[n + 1];
            for (int i = 0; i <= n; i++) { float a = Mathf.PI * i / n - Mathf.PI / 2; p[i] = new Vector2(Mathf.Cos(a) * 0.5f, Mathf.Sin(a) * 0.5f); }
            p[0].x = 0; p[n].x = 0; return p;
        }
        static Vector2[] Capsule(int n)
        {
            var p = new List<Vector2>();
            for (int i = 0; i <= n; i++) { float a = Mathf.PI / 2 * i / n - Mathf.PI / 2; p.Add(new Vector2(Mathf.Cos(a) * 0.5f, -0.5f + Mathf.Sin(a) * 0.5f)); }
            for (int i = 0; i <= n; i++) { float a = Mathf.PI / 2 * i / n; p.Add(new Vector2(Mathf.Cos(a) * 0.5f, 0.5f + Mathf.Sin(a) * 0.5f)); }
            var arr = p.ToArray(); arr[0].x = 0; arr[arr.Length - 1].x = 0; return arr;
        }

        // surface of revolution around Y from a profile (x = radius, y = height); flat-ish shading via split normals
        static Mesh Lathe(Vector2[] prof, int seg)
        {
            var v = new List<Vector3>(); var t = new List<int>();
            for (int s = 0; s <= seg; s++)
            {
                float a = 2 * Mathf.PI * s / seg; float c = Mathf.Cos(a), sn = Mathf.Sin(a);
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

        static Mesh Box()
        {
            var v = new List<Vector3>(); var t = new List<int>(); var nrm = new List<Vector3>();
            Vector3[] dirs = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
            foreach (var d in dirs)
            {
                var u = Vector3.Cross(d, Mathf.Abs(d.y) > 0.5f ? Vector3.forward : Vector3.up); var w = Vector3.Cross(d, u);
                int b = v.Count;
                v.Add((d - u - w) * 0.5f); v.Add((d + u - w) * 0.5f); v.Add((d + u + w) * 0.5f); v.Add((d - u + w) * 0.5f);
                for (int i = 0; i < 4; i++) nrm.Add(d);
                t.Add(b); t.Add(b + 2); t.Add(b + 1); t.Add(b); t.Add(b + 3); t.Add(b + 2);
            }
            var m = new Mesh(); m.SetVertices(v); m.SetNormals(nrm); m.SetTriangles(t, 0); m.RecalculateBounds();
            // winding check: flip if the first face points inward
            var tris = m.triangles; var a = v[tris[0]]; var bb = v[tris[1]]; var cc = v[tris[2]];
            if (Vector3.Dot(Vector3.Cross(bb - a, cc - a), nrm[tris[0]]) < 0)
            {
                for (int i = 0; i < tris.Length; i += 3) { int x = tris[i + 1]; tris[i + 1] = tris[i + 2]; tris[i + 2] = x; }
                m.triangles = tris;
            }
            return m;
        }
    }
}
