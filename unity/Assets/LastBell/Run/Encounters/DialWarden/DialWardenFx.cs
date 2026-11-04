using UnityEngine;

namespace LastBell.Run
{
    // Dial Warden presentation that the shared telegraphs can't draw:
    //   - the Mainspring wave: a glowing ring wall with gaps, rebuilt each tick at the radius the rules use;
    //   - the thrown counterweight (Tip the Scales), a brass ball that arcs from his hand to its landing point.
    // Drawn with the project's additive ColorDissolve "Band" shader like the Tempo Tyrant's arcs. Presentation only:
    // DialWardenRoutes owns every hit test.
    public sealed class DialWardenFx : MonoBehaviour
    {
        static Material baseMat;
        static readonly int PMode = Shader.PropertyToID("_Mode"), PLow = Shader.PropertyToID("_ColorLow"), PHigh = Shader.PropertyToID("_ColorHigh"),
            PEdge = Shader.PropertyToID("_ColorEdge"), PProgress = Shader.PropertyToID("_Progress"), PWidth = Shader.PropertyToID("_Width"),
            PTail = Shader.PropertyToID("_Tail"), PDissolve = Shader.PropertyToID("_Dissolve"), PIntensity = Shader.PropertyToID("_Intensity");
        public static readonly Color WaveOut = new Color(2.2f, 1.5f, 0.6f), WaveIn = new Color(1.0f, 1.4f, 2.4f), WaveMidnight = new Color(2.4f, 0.5f, 0.35f);

        sealed class WaveView { public GameObject go; public Mesh mesh; public MeshRenderer mr; public bool on; }
        readonly WaveView[] waves = new WaveView[2];
        MaterialPropertyBlock mpb;
        public int WaveFrames { get; private set; }

        void Awake()
        {
            if (!baseMat)
            {
                var src = Resources.Load<Material>("Run/FX_ColorDissolve");
                baseMat = src ? new Material(src) : new Material(Shader.Find("LastBell/FX/ColorDissolve"));
                baseMat.name = "DialWarden_Wave"; baseMat.SetFloat("_SrcBlend", 1); baseMat.SetFloat("_DstBlend", 1);
            }
            mpb = new MaterialPropertyBlock();
            for (int i = 0; i < waves.Length; i++)
            {
                var go = new GameObject("Mainspring wave " + i); go.transform.SetParent(transform.parent, false);
                var m = new Mesh { name = "MainspringWave" }; m.MarkDynamic();
                go.AddComponent<MeshFilter>().sharedMesh = m;
                var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = baseMat;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false;
                go.SetActive(false);
                waves[i] = new WaveView { go = go, mesh = m, mr = mr };
            }
        }

        void OnDestroy()
        {
            foreach (var w in waves) if (w != null) { if (w.mesh) Destroy(w.mesh); if (w.go) Destroy(w.go); }
            if (weight) Destroy(weight.gameObject);
            if (weight2) Destroy(weight2.gameObject);
        }

        // a ring wall at radius r around `centre`, open where the gaps are (yaw centres, +-half degrees)
        public void Wave(int slot, Vector3 centre, float r, float[] gaps, float half, Color colour)
        {
            if (Networking.LanFx.Record != null) { var g = new float[4]; int gn = gaps == null ? 0 : Mathf.Min(4, gaps.Length); for (int i = 0; i < gn; i++) g[i] = gaps[i]; var rec = Networking.LanFx.Record; if (rec != null) rec(Networking.LanFxTarget.BossPart, "DialWardenFx.Wave", new object[] { slot, centre, r, gn, g[0], g[1], g[2], g[3], half, colour }); }
            var w = waves[slot]; WaveFrames++;
            const float step = 2f, thick = DialWardenRoutes.WaveHalf, wall = 0.75f;
            int seg = Mathf.RoundToInt(360f / step);
            var v = new System.Collections.Generic.List<Vector3>(); var uv = new System.Collections.Generic.List<Vector2>(); var tri = new System.Collections.Generic.List<int>();
            for (int i = 0; i < seg; i++)
            {
                float a0 = i * step, a1 = a0 + step, mid = a0 + step / 2;
                bool open = false;
                if (gaps != null && half > 0) foreach (var g in gaps) if (Mathf.Abs(Mathf.DeltaAngle(mid, g)) <= half) open = true;
                if (open) continue;
                var d0 = Sim.Dir(a0); var d1 = Sim.Dir(a1);
                // floor band
                Quad(v, uv, tri, d0 * (r - thick) + Vector3.up * 0.06f, d1 * (r - thick) + Vector3.up * 0.06f, d0 * (r + thick) + Vector3.up * 0.06f, d1 * (r + thick) + Vector3.up * 0.06f, i / (float)seg);
                // a low wall standing on the band's centre line, so the wave reads from the follow camera
                Quad(v, uv, tri, d0 * r, d1 * r, d0 * r + Vector3.up * wall, d1 * r + Vector3.up * wall, i / (float)seg);
            }
            w.mesh.Clear(); w.mesh.SetVertices(v); w.mesh.SetUVs(0, uv); w.mesh.SetTriangles(tri, 0); w.mesh.RecalculateBounds();
            w.go.transform.SetPositionAndRotation(new Vector3(centre.x, 0, centre.z), Quaternion.identity);
            mpb.SetFloat(PMode, 0); mpb.SetColor(PLow, colour * 0.15f); mpb.SetColor(PHigh, colour); mpb.SetColor(PEdge, colour * 0.8f);
            mpb.SetFloat(PProgress, 1.4f); mpb.SetFloat(PWidth, 0.12f); mpb.SetFloat(PTail, 4f); mpb.SetFloat(PDissolve, 0); mpb.SetFloat(PIntensity, 1.0f);
            w.mr.SetPropertyBlock(mpb);
            if (!w.on) { w.on = true; w.go.SetActive(true); }
        }
        public void HideWave(int slot) { { var rec = Networking.LanFx.Record; if (rec != null) rec(Networking.LanFxTarget.BossPart, "DialWardenFx.HideWave", new object[] { slot }); }  var w = waves[slot]; if (w != null && w.on) { w.on = false; w.go.SetActive(false); } }
        public void HideAll() { { var rec = Networking.LanFx.Record; if (rec != null) rec(Networking.LanFxTarget.BossPart, "DialWardenFx.HideAll", new object[] {  }); }  for (int i = 0; i < waves.Length; i++) HideWave(i); HideWeights(); }

        static void Quad(System.Collections.Generic.List<Vector3> v, System.Collections.Generic.List<Vector2> uv, System.Collections.Generic.List<int> tri,
            Vector3 a, Vector3 b, Vector3 c, Vector3 d, float u)
        {
            int k = v.Count; v.Add(a); v.Add(b); v.Add(c); v.Add(d);
            uv.Add(new Vector2(u, 0)); uv.Add(new Vector2(u, 0)); uv.Add(new Vector2(u, 1)); uv.Add(new Vector2(u, 1));
            tri.Add(k); tri.Add(k + 2); tri.Add(k + 1); tri.Add(k + 1); tri.Add(k + 2); tri.Add(k + 3);
        }

        // ---------------- the thrown counterweight ----------------
        static readonly Color Brass = new Color(0.85f, 0.66f, 0.32f);
        Transform weight, weight2;
        Transform MakeWeight(string name)
        {
            var t = Prim.Make(PrimitiveKind.Sphere, transform.parent, Vector3.zero, Vector3.one * 0.85f, Brass, 0.6f);
            t.name = name; t.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            t.gameObject.SetActive(false); return t;
        }
        // u 0..1 along a throw arc from `from` to `to` (peak 3 m); u >= 1 rests on the floor
        public void Weight(int slot, Vector3 from, Vector3 to, float u)
        {
            { var rec = Networking.LanFx.Record; if (rec != null) rec(Networking.LanFxTarget.BossPart, "DialWardenFx.Weight", new object[] { slot, from, to, u }); }
            ref Transform t = ref slot == 0 ? ref weight : ref weight2;
            if (!t) t = MakeWeight(slot == 0 ? "Counterweight (thrown)" : "Counterweight (falling star)");
            u = Mathf.Clamp01(u);
            var p = Vector3.Lerp(from, to, u) + Vector3.up * (4f * 3f * u * (1 - u));
            if (u >= 1) p = new Vector3(to.x, 0.42f, to.z);
            t.position = p; t.gameObject.SetActive(true);
        }
        // the second weight (below 50%) falls straight down from the sky
        public void Falling(int slot, Vector3 to, float u) => Weight(slot, to + Vector3.up * 14f, to, u);
        public void HideWeights() { { var rec = Networking.LanFx.Record; if (rec != null) rec(Networking.LanFxTarget.BossPart, "DialWardenFx.HideWeights", new object[] {  }); }  if (weight) weight.gameObject.SetActive(false); if (weight2) weight2.gameObject.SetActive(false); }
        public bool WeightShown => weight && weight.gameObject.activeSelf;
            // LAN client: the host's calls, replayed on the replica boss
        public bool Replay(string method, Networking.LanWorldFrame.EffectArg[] a)
        {
            switch (method)
            {
                case "DialWardenFx.Wave":
                    if (a.Length != 10) return false;
                    { int gn = Mathf.Clamp(a[3].I, 0, 4); var g = new float[gn]; for (int i = 0; i < gn; i++) g[i] = a[4 + i].X;
                      Wave(a[0].I, new Vector3(a[1].X, a[1].Y, a[1].Z), a[2].X, g, a[8].X, new Color(a[9].X, a[9].Y, a[9].Z, a[9].W)); }
                    return true;
                case "DialWardenFx.HideWave": if (a.Length != 1) return false; HideWave(a[0].I); return true;
                case "DialWardenFx.HideAll": HideAll(); return true;
                case "DialWardenFx.Weight": if (a.Length != 4) return false; Weight(a[0].I, new Vector3(a[1].X, a[1].Y, a[1].Z), new Vector3(a[2].X, a[2].Y, a[2].Z), a[3].X); return true;
                case "DialWardenFx.HideWeights": HideWeights(); return true;
                default: return false;
            }
        }
    }
}
