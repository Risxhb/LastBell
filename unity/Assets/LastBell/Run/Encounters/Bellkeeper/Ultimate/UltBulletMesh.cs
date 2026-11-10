using System.Collections.Generic;
using UnityEngine;

namespace LastBell.Run
{
    // The Ultimate's bullets as small bells (graphics audit, 8 Nov: they were Prim spheres). One lathe mesh, built once in code:
    // a flared lip, a waisted body, a shoulder and a crown loop, standing upright. It fits inside the unit sphere (radius 0.5)
    // the old sphere filled, so a bullet scaled by the same size covers the same ground. Presentation only: hits stay
    // the mechanics' own distance checks.
    // The graphics pass of 8 Nov can be switched off for before/after screenshots (-lastbellTestAbility ult_shots): Old = the
    // Prim spheres and bars, no cast clips.
    // OldMarks: only the 10 Nov marker pass off (the Prim plates, posts, half line, debt rings and INVERTED marks it replaced)
    public static class UltLook { public static bool Old, OldMarks; public static bool PrimMarks => Old || OldMarks; }

    public static class UltBulletMesh
    {
        static Mesh bell;

        // profile (radius, height) from the mouth up; the mouth is closed with a flat disc
        static readonly Vector2[] Profile =
        {
            new Vector2(0.000f, -0.40f), new Vector2(0.440f, -0.40f), new Vector2(0.470f, -0.36f), new Vector2(0.400f, -0.30f),
            new Vector2(0.330f, -0.16f), new Vector2(0.290f, 0.02f), new Vector2(0.285f, 0.16f), new Vector2(0.250f, 0.26f),
            new Vector2(0.160f, 0.32f), new Vector2(0.070f, 0.34f), new Vector2(0.075f, 0.40f), new Vector2(0.060f, 0.45f),
            new Vector2(0.000f, 0.46f),
        };

        public static Mesh Bell
        {
            get
            {
                if (bell) return bell;
                const int seg = 14;
                var v = new List<Vector3>(); var tri = new List<int>();
                int rows = Profile.Length;
                for (int i = 0; i < rows; i++)
                    for (int s = 0; s <= seg; s++)
                    {
                        float a = s * Mathf.PI * 2 / seg;
                        v.Add(new Vector3(Mathf.Cos(a) * Profile[i].x, Profile[i].y, Mathf.Sin(a) * Profile[i].x));
                    }
                for (int i = 0; i < rows - 1; i++)
                    for (int s = 0; s < seg; s++)
                    {
                        int a = i * (seg + 1) + s, b = a + seg + 1;
                        tri.Add(a); tri.Add(b); tri.Add(a + 1);
                        tri.Add(a + 1); tri.Add(b); tri.Add(b + 1);
                    }
                bell = new Mesh { name = "Ultimate bullet bell" };
                bell.SetVertices(v); bell.SetTriangles(tri, 0);
                bell.RecalculateNormals(); bell.RecalculateBounds();
                return bell;
            }
        }

        // a bullet object under `parent` with this mesh and material (the pools' replacement for Prim.Make(Sphere))
        public static Transform Make(Transform parent, Material m, string name = "bullet")
        {
            if (UltLook.Old) { var s = Prim.Make(PrimitiveKind.Sphere, parent, Vector3.zero, Vector3.one, Color.white); var sr = s.GetComponent<Renderer>(); sr.sharedMaterial = m; sr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; return s; }
            var go = new GameObject(name); go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = Bell;
            var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = m;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            return go.transform;
        }
    }
}
