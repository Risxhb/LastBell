using UnityEngine;

namespace LastBell.Areas.Area2
{
    // Small CPU particle emitter for the Brass Foundry's ambient FX (embers, sparks, steam, smoke). The project ships
    // without Unity's ParticleSystem module, so this draws camera-facing quads into one mesh with vertex-colour alpha
    // (use a URP Particles/Unlit material). It prewarms when enabled, animates in Play mode, and Simulate() lets the
    // batch capture step it deterministically. Built by Editor/BrassFoundryArenaBuilder.cs.
    [ExecuteAlways, RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class Area2Particles : MonoBehaviour
    {
        public enum Shape { Ring, Box, Point }

        public Shape shape = Shape.Ring;
        public Vector3 center;
        public float radius = 1, thickness = 0.1f;
        public Vector3 boxSize = Vector3.one;
        public int capacity = 200;
        [Tooltip("Particles per second.")] public float rate = 20;
        [Tooltip("Seconds between bursts (0 = none).")] public float burstInterval;
        public int burstCount;
        [Tooltip("Bursts spawn together at one random point of the shape (sparks, steam puffs).")] public bool burstAtOnePoint = true;
        public Vector2 lifetime = new Vector2(4, 8), size = new Vector2(0.05f, 0.12f);
        [Tooltip("Size multiplier at birth (x) and death (y).")] public Vector2 grow = Vector2.one;
        public Vector3 velocityMin, velocityMax;
        public float gravity, drag, wander;
        [Range(0, 1)] public float fadeIn = 0.15f, fadeOut = 0.4f;
        public int seed = 1;

        struct P { public Vector3 pos, vel; public float age, life, size, phase; }
        P[] ps; int count;
        Mesh mesh; Vector3[] verts; Color[] cols; Vector2[] uvs;
        float emitAcc, burstClock;
        System.Random rnd;

        void OnEnable() { Init(); Simulate(lifetime.y); }

        void OnDisable() { if (mesh) { if (Application.isPlaying) Destroy(mesh); else DestroyImmediate(mesh); } }

        void Init()
        {
            rnd = new System.Random(seed); count = 0; emitAcc = 0; burstClock = 0;
            ps = new P[Mathf.Max(1, capacity)];
            verts = new Vector3[ps.Length * 4]; cols = new Color[ps.Length * 4]; uvs = new Vector2[ps.Length * 4];
            var tris = new int[ps.Length * 6];
            for (int i = 0; i < ps.Length; i++)
            {
                int v = i * 4, t = i * 6;
                tris[t] = v; tris[t + 1] = v + 1; tris[t + 2] = v + 2; tris[t + 3] = v; tris[t + 4] = v + 2; tris[t + 5] = v + 3;
                uvs[v] = new Vector2(0, 0); uvs[v + 1] = new Vector2(0, 1); uvs[v + 2] = new Vector2(1, 1); uvs[v + 3] = new Vector2(1, 0);
            }
            mesh = new Mesh { name = "Area2Particles", hideFlags = HideFlags.HideAndDontSave };
            mesh.MarkDynamic(); mesh.vertices = verts; mesh.uv = uvs; mesh.colors = cols; mesh.triangles = tris;
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 2000);
            GetComponent<MeshFilter>().sharedMesh = mesh;
        }

        float R() => (float)rnd.NextDouble();
        float R(float a, float b) => a + (b - a) * R();
        Vector3 R(Vector3 a, Vector3 b) => new Vector3(R(a.x, b.x), R(a.y, b.y), R(a.z, b.z));

        Vector3 SpawnPoint()
        {
            switch (shape)
            {
                case Shape.Ring: { float a = R() * Mathf.PI * 2, r = radius + R(-0.5f, 0.5f) * thickness; return center + new Vector3(Mathf.Cos(a) * r, 0, Mathf.Sin(a) * r); }
                case Shape.Box: return center + Vector3.Scale(boxSize, new Vector3(R() - 0.5f, R() - 0.5f, R() - 0.5f));
                default: return center;
            }
        }

        void Spawn(Vector3 at)
        {
            if (count >= ps.Length) return;
            ps[count++] = new P { pos = at, vel = R(velocityMin, velocityMax), age = 0, life = R(lifetime.x, lifetime.y), size = R(size.x, size.y), phase = R() * 6.2832f };
        }

        // Deterministic fixed-step simulation (used by the batch capture and on enable).
        public void Simulate(float seconds)
        {
            if (ps == null) Init();
            for (float s = 0; s < seconds; s += 1 / 30f) Step(1 / 30f);
            Rebuild();
        }

        void Step(float dt)
        {
            emitAcc += rate * dt;
            while (emitAcc >= 1) { Spawn(SpawnPoint()); emitAcc -= 1; }
            if (burstInterval > 0)
            {
                burstClock -= dt;
                if (burstClock <= 0)
                {
                    burstClock = burstInterval * R(0.6f, 1.4f);
                    var at = SpawnPoint();
                    for (int i = 0; i < burstCount; i++) Spawn(burstAtOnePoint ? at : SpawnPoint());
                }
            }
            float k = Mathf.Exp(-drag * dt);
            for (int i = 0; i < count; i++)
            {
                ref var p = ref ps[i];
                p.age += dt;
                if (p.age >= p.life) { ps[i] = ps[--count]; i--; continue; }
                p.vel.y -= gravity * dt; p.vel *= k;
                p.pos += (p.vel + new Vector3(Mathf.Sin(p.age * 1.3f + p.phase), 0, Mathf.Cos(p.age * 1.1f + p.phase)) * wander) * dt;
            }
        }

        void Update()
        {
            if (!Application.isPlaying || ps == null) return;
            Step(Mathf.Min(Time.deltaTime, 0.1f));
        }

        void LateUpdate() { if (Application.isPlaying) Rebuild(); }

        void Rebuild()
        {
            if (mesh == null) return;
            var cam = Camera.main;
            Vector3 right = cam ? cam.transform.right : Vector3.right, up = cam ? cam.transform.up : Vector3.up;
            var inv = transform.worldToLocalMatrix;
            for (int i = 0; i < ps.Length; i++)
            {
                int v = i * 4;
                if (i >= count) { verts[v] = verts[v + 1] = verts[v + 2] = verts[v + 3] = Vector3.zero; continue; }
                var p = ps[i]; float u = p.age / p.life;
                float s = p.size * Mathf.Lerp(grow.x, grow.y, u) * 0.5f;
                float a = Mathf.Min(fadeIn > 0 ? u / fadeIn : 1, fadeOut > 0 ? (1 - u) / fadeOut : 1);
                Vector3 c = inv.MultiplyPoint3x4(p.pos), rx = inv.MultiplyVector(right) * s, uy = inv.MultiplyVector(up) * s;
                verts[v] = c - rx - uy; verts[v + 1] = c - rx + uy; verts[v + 2] = c + rx + uy; verts[v + 3] = c + rx - uy;
                var col = new Color(1, 1, 1, Mathf.Clamp01(a));
                cols[v] = cols[v + 1] = cols[v + 2] = cols[v + 3] = col;
            }
            mesh.vertices = verts; mesh.colors = cols;
        }
    }
}
