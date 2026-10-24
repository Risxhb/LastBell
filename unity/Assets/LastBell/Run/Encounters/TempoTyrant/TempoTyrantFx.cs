using UnityEngine;

namespace LastBell.Run
{
    // Threefold Severance impact presentation: a flat sword-arc ribbon that follows the blade path (cleaves 1 and 2) or a
    // straight forward streak (overhead 3), drawn with the project's ColorDissolve "Band" shader (additive). Three pooled
    // ribbons with reusable meshes: nothing is allocated per strike, and everything is destroyed with the boss. The
    // ribbon lives ~0.25 s and is never brighter or longer-lived than the next live warning. Presentation only.
    public sealed class TempoTyrantFx : MonoBehaviour
    {
        sealed class Ribbon { public GameObject go; public Mesh mesh; public MeshRenderer mr; public float t = -1, life = Life; public Color high = Hot; public bool ring; }
        static readonly Color Hot = new Color(1.6f, 1.1f, 0.6f);
        const float Life = 0.26f;
        static Material baseMat;
        static readonly int PMode = Shader.PropertyToID("_Mode"), PLow = Shader.PropertyToID("_ColorLow"), PHigh = Shader.PropertyToID("_ColorHigh"),
            PEdge = Shader.PropertyToID("_ColorEdge"), PProgress = Shader.PropertyToID("_Progress"), PWidth = Shader.PropertyToID("_Width"),
            PTail = Shader.PropertyToID("_Tail"), PDissolve = Shader.PropertyToID("_Dissolve"), PIntensity = Shader.PropertyToID("_Intensity");
        readonly Ribbon[] pool = new Ribbon[5]; int next;
        MaterialPropertyBlock mpb;
        public int Played { get; private set; }

        void Awake()
        {
            if (!baseMat)
            {
                var src = Resources.Load<Material>("Run/FX_ColorDissolve");
                baseMat = src ? new Material(src) : new Material(Shader.Find("LastBell/FX/ColorDissolve"));
                baseMat.name = "TempoTyrant_Arc"; baseMat.SetFloat("_SrcBlend", 1); baseMat.SetFloat("_DstBlend", 1);
            }
            mpb = new MaterialPropertyBlock();
            for (int i = 0; i < pool.Length; i++)
            {
                var go = new GameObject("Severance arc");
                go.transform.SetParent(transform.parent, false);
                var m = new Mesh { name = "SeveranceArc" }; m.MarkDynamic();
                go.AddComponent<MeshFilter>().sharedMesh = m;
                var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = baseMat;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false;
                go.SetActive(false);
                pool[i] = new Ribbon { go = go, mesh = m, mr = mr };
            }
        }

        void OnDestroy() { foreach (var r in pool) if (r != null) { if (r.mesh) Destroy(r.mesh); if (r.go) Destroy(r.go); } }

        Ribbon Take() { var r = pool[next]; next = (next + 1) % pool.Length; return r; }

        // horizontal cleave: an arc around the boss at blade-tip radius, sweeping from his side across his front
        public void Arc(Vector3 centre, float yaw, int side)
        {
            var r = Take(); const int seg = 24; float radius = 3.6f, width = 0.22f, a0 = yaw + side * 100f, a1 = yaw - side * 10f;
            var v = new Vector3[(seg + 1) * 2]; var uv = new Vector2[v.Length]; var tri = new int[seg * 6];
            for (int i = 0; i <= seg; i++)
            {
                float u = i / (float)seg, a = Mathf.Lerp(a0, a1, u) * Mathf.Deg2Rad;
                var d = new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a));
                float w = width * Mathf.Sin(Mathf.PI * Mathf.Clamp01(u * 1.15f));   // tapered ends
                v[i * 2] = d * (radius - w) + Vector3.up * 1.0f; v[i * 2 + 1] = d * (radius + w) + Vector3.up * 1.0f;
                uv[i * 2] = new Vector2(u, 0); uv[i * 2 + 1] = new Vector2(u, 1);
                if (i < seg) { int b = i * 2, t = i * 6; tri[t] = b; tri[t + 1] = b + 1; tri[t + 2] = b + 2; tri[t + 3] = b + 1; tri[t + 4] = b + 3; tri[t + 5] = b + 2; }
            }
            Play(r, centre, v, uv, tri, Hot, 0.16f);
        }

        // overhead strike: a straight streak ahead of him along the band's centre
        public void Streak(Vector3 origin, float yaw, float start, float length) => Streak(origin, yaw, start, length, Hot, 0.32f);
        public void Streak(Vector3 origin, float yaw, float start, float length, Color colour, float width)
        {
            var r = Take(); const int seg = 12; var f = Sim.Dir(yaw); var side = Vector3.Cross(Vector3.up, f);
            var v = new Vector3[(seg + 1) * 2]; var uv = new Vector2[v.Length]; var tri = new int[seg * 6];
            for (int i = 0; i <= seg; i++)
            {
                float u = i / (float)seg, w = width * (1 - 0.6f * u);
                var c = f * (start + length * u) + Vector3.up * 0.08f;
                v[i * 2] = c - side * w; v[i * 2 + 1] = c + side * w;
                uv[i * 2] = new Vector2(u, 0); uv[i * 2 + 1] = new Vector2(u, 1);
                if (i < seg) { int b = i * 2, t = i * 6; tri[t] = b; tri[t + 1] = b + 1; tri[t + 2] = b + 2; tri[t + 3] = b + 1; tri[t + 4] = b + 3; tri[t + 5] = b + 2; }
            }
            Play(r, origin, v, uv, tri, colour, Life);
        }

        // a thin ring pulse on the floor (Crown edges, charge landing, frenzy tell): 0.15-0.25 s, never a lingering bowl
        public void Rim(Vector3 centre, float radius, Color colour)
        {
            var r = Take(); const int seg = 48; float width = 0.16f;
            var v = new Vector3[(seg + 1) * 2]; var uv = new Vector2[v.Length]; var tri = new int[seg * 6];
            for (int i = 0; i <= seg; i++)
            {
                float u = i / (float)seg, a = u * Mathf.PI * 2; var d = new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a));
                v[i * 2] = d * (radius - width) + Vector3.up * 0.07f; v[i * 2 + 1] = d * (radius + width) + Vector3.up * 0.07f;
                uv[i * 2] = new Vector2(u, 0); uv[i * 2 + 1] = new Vector2(u, 1);
                if (i < seg) { int b = i * 2, t = i * 6; tri[t] = b; tri[t + 1] = b + 1; tri[t + 2] = b + 2; tri[t + 3] = b + 1; tri[t + 4] = b + 3; tri[t + 5] = b + 2; }
            }
            Play(r, centre, v, uv, tri, colour, 0.22f, ring: true);
        }

        void Play(Ribbon r, Vector3 at, Vector3[] v, Vector2[] uv, int[] tri, Color high, float life, bool ring = false)
        {
            r.high = high; r.life = life; r.ring = ring;
            r.mesh.Clear(); r.mesh.vertices = v; r.mesh.uv = uv; r.mesh.triangles = tri; r.mesh.RecalculateBounds();
            r.go.transform.SetPositionAndRotation(new Vector3(at.x, 0, at.z), Quaternion.identity);
            r.t = 0; r.go.SetActive(true); Played++;
        }

        void Update()
        {
            foreach (var r in pool)
            {
                if (r == null || r.t < 0) continue;
                r.t += Time.deltaTime; float u = r.t / r.life;
                if (u >= 1) { r.t = -1; r.go.SetActive(false); continue; }
                mpb.SetFloat(PMode, 0);
                mpb.SetColor(PLow, r.high * 0.12f); mpb.SetColor(PHigh, r.high); mpb.SetColor(PEdge, r.high * 0.8f);
                // a ring glows all round at once; arcs and streaks sweep along their length
                mpb.SetFloat(PProgress, r.ring ? 1.4f : Mathf.Lerp(0.1f, 1.25f, Mathf.Clamp01(u * 1.6f)));
                mpb.SetFloat(PWidth, 0.12f); mpb.SetFloat(PTail, r.ring ? 4f : 0.55f);
                mpb.SetFloat(PDissolve, Mathf.Clamp01((u - 0.35f) / 0.65f)); mpb.SetFloat(PIntensity, r.ring ? 0.9f : 0.6f);
                r.mr.SetPropertyBlock(mpb);
            }
        }
    }
}
