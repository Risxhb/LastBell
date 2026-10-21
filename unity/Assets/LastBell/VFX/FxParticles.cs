using UnityEngine;

namespace LastBell.VFX
{
    // A small pooled CPU particle batch drawn as one mesh (the project ships without the ParticleSystem module).
    // Billboards face the camera; with stretch > 0 they stretch along their velocity (sparks). Fixed capacity, no
    // allocation after creation. Colour comes from the material (FX_SH_ColorDissolve in Dot mode); vertex alpha fades.
    public sealed class FxParticles : MonoBehaviour
    {
        struct P { public Vector3 pos, vel; public float size, life, age, alpha; }

        P[] ps; int count;
        Mesh mesh; Vector3[] verts; Color[] cols; Vector2[] uvs;
        float gravity, drag, stretch;

        public static FxParticles Create(Transform parent, string name, Material mat, int capacity, float gravity, float drag, float stretch)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false);
            var f = go.AddComponent<FxParticles>();
            f.ps = new P[capacity]; f.gravity = gravity; f.drag = drag; f.stretch = stretch;
            f.verts = new Vector3[capacity * 4]; f.cols = new Color[capacity * 4]; f.uvs = new Vector2[capacity * 4];
            var tris = new int[capacity * 6];
            for (int i = 0; i < capacity; i++)
            {
                int v = i * 4, t = i * 6;
                tris[t] = v; tris[t + 1] = v + 1; tris[t + 2] = v + 2; tris[t + 3] = v + 2; tris[t + 4] = v + 1; tris[t + 5] = v + 3;
                f.uvs[v] = new Vector2(0, 0); f.uvs[v + 1] = new Vector2(1, 0); f.uvs[v + 2] = new Vector2(0, 1); f.uvs[v + 3] = new Vector2(1, 1);
            }
            f.mesh = new Mesh { name = name }; f.mesh.MarkDynamic();
            f.mesh.vertices = f.verts; f.mesh.uv = f.uvs; f.mesh.colors = f.cols; f.mesh.triangles = tris;
            f.mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 200);
            go.AddComponent<MeshFilter>().sharedMesh = f.mesh;
            var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false;
            return f;
        }

        public void Emit(Vector3 pos, Vector3 vel, float size, float life, float alpha = 1)
        {
            if (count >= ps.Length) return;   // full: the oldest keep playing, new ones are dropped
            ps[count++] = new P { pos = pos, vel = vel, size = size, life = life, alpha = alpha };
        }

        void LateUpdate()
        {
            float dt = Time.deltaTime; var cam = Camera.main;
            if (!cam) return;
            Vector3 right = cam.transform.right, up = cam.transform.up, fwd = cam.transform.forward;
            for (int i = 0; i < count; i++)
            {
                ref var p = ref ps[i];
                p.age += dt;
                if (p.age >= p.life) { ps[i] = ps[--count]; i--; continue; }
                p.vel += Vector3.down * gravity * dt; p.vel *= Mathf.Max(0, 1 - drag * dt); p.pos += p.vel * dt;
            }
            for (int i = 0; i < ps.Length; i++)
            {
                int v = i * 4;
                if (i >= count) { verts[v] = verts[v + 1] = verts[v + 2] = verts[v + 3] = Vector3.zero; continue; }
                var p = ps[i]; float u = p.age / p.life;
                float s = p.size * (1 - 0.6f * u) * 0.5f;
                Vector3 a = right * s, b = up * s;
                if (stretch > 0 && p.vel.sqrMagnitude > 0.01f)
                {
                    // long axis along the screen-space velocity
                    var vs = Vector3.ProjectOnPlane(p.vel, fwd); float len = vs.magnitude;
                    b = vs / len * (s + len * stretch); a = Vector3.Cross(fwd, vs / len) * s * 0.6f;
                }
                verts[v] = p.pos - a - b; verts[v + 1] = p.pos + a - b; verts[v + 2] = p.pos - a + b; verts[v + 3] = p.pos + a + b;
                var c = new Color(1, 1, 1, p.alpha * (u < 0.4f ? 1 : 1 - (u - 0.4f) / 0.6f));
                cols[v] = cols[v + 1] = cols[v + 2] = cols[v + 3] = c;
            }
            mesh.vertices = verts; mesh.colors = cols;
        }
    }
}
