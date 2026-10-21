using UnityEngine;

namespace LastBell.VFX
{
    // Basic effect meshes built in code (docs/design/spell-vfx-pipeline.md §4: U along the motion, V across it).
    // All lie in the XZ plane, centred on the origin, forward = +Z, in metres.
    public static class FxMeshes
    {
        // flat quad covering [x0,x1] x [z0,z1]; the telegraph shader reads object-space xz, so no UVs are needed
        public static Mesh Quad(float x0, float x1, float z0, float z1)
        {
            var m = new Mesh { name = "FX_MS_Quad" };
            m.vertices = new[] { new Vector3(x0, 0, z0), new Vector3(x1, 0, z0), new Vector3(x0, 0, z1), new Vector3(x1, 0, z1) };
            m.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) };
            m.triangles = new[] { 0, 2, 1, 1, 2, 3 };
            m.RecalculateBounds(); return m;
        }

        // flat ring: U runs outward (inner 0 -> outer 1), V around
        public static Mesh Annulus(float inner, float outer, int seg = 96) =>
            Sector(inner, outer, 180f, seg, radialU: true, name: "FX_MS_Annulus");

        // flat fan between radii: radialU false = U across the fan (from its right edge to its left edge), V outward
        public static Mesh Sector(float inner, float outer, float halfAngle, int seg = 48, bool radialU = false, string name = "FX_MS_Sector")
        {
            var v = new Vector3[(seg + 1) * 2]; var uv = new Vector2[v.Length]; var t = new int[seg * 6];
            for (int i = 0; i <= seg; i++)
            {
                float f = (float)i / seg, a = Mathf.Deg2Rad * Mathf.Lerp(halfAngle, -halfAngle, f);   // +x (right) first
                var d = new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a));
                v[i * 2] = d * inner; v[i * 2 + 1] = d * outer;
                uv[i * 2] = radialU ? new Vector2(0, f) : new Vector2(f, 0);
                uv[i * 2 + 1] = radialU ? new Vector2(1, f) : new Vector2(f, 1);
                if (i < seg) { int k = i * 6, b = i * 2; t[k] = b; t[k + 1] = b + 1; t[k + 2] = b + 2; t[k + 3] = b + 1; t[k + 4] = b + 3; t[k + 5] = b + 2; }
            }
            var m = new Mesh { name = name, vertices = v, uv = uv, triangles = t }; m.RecalculateBounds(); return m;
        }

        // open cylinder wall of radius 1 and height 1 (scale it): U around, V up
        public static Mesh Cylinder(int seg = 48)
        {
            var v = new Vector3[(seg + 1) * 2]; var uv = new Vector2[v.Length]; var t = new int[seg * 6];
            for (int i = 0; i <= seg; i++)
            {
                float f = (float)i / seg, a = f * Mathf.PI * 2;
                var d = new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a));
                v[i * 2] = d; v[i * 2 + 1] = d + Vector3.up; uv[i * 2] = new Vector2(f, 0); uv[i * 2 + 1] = new Vector2(f, 1);
                if (i < seg) { int k = i * 6, b = i * 2; t[k] = b; t[k + 1] = b + 1; t[k + 2] = b + 2; t[k + 3] = b + 1; t[k + 4] = b + 3; t[k + 5] = b + 2; }
            }
            var m = new Mesh { name = "FX_MS_Cylinder", vertices = v, uv = uv, triangles = t }; m.RecalculateBounds(); return m;
        }
    }
}
