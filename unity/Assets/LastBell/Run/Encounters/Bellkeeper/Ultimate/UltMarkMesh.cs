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
