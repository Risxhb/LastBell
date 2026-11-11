using System.Collections.Generic;
using UnityEngine;

namespace LastBell.Run
{
    // Mould Towers presentation (Molten Crown): a code-built brass mould ring with gear teeth (FoundryProps, since 8 Nov; was a
    // kit of 16 cubes) and a molten fill rising with the countdown on each
    // mould, a large ivory numeral (the headcount) with one tick per player inside, and a loud chime each second. The
    // coral/amber circle itself is the hazard's telegraph. Solo tower II is dimmed until tower I resolves. Each tower's
    // pieces leave (back to the kit pool) the moment its hazard leaves W.Hazards.Live (resolve, boss death, ClearTransient); the whole object
    // removes itself when none is left. Nothing here decides timing or hits.
    public sealed class MouldPressFx : MonoBehaviour
    {
        // the telegraph shader's own defaults (FX_SH_Telegraph), restored when a dimmed tower wakes
        static readonly Color TeleFill = new Color(0.92f, 0.41f, 0.36f), TeleBorder = new Color(1.25f, 0.5f, 0.36f);

        sealed class Tower { public Hazard H; public GameObject Root; public Transform Fill; public bool Dim; public int Inside; }

        public bool HideCounts;   // Molten Crown memory cycles: the moulds show "?" instead of their count
        CombatWorld w;
        readonly List<Tower> towers = new List<Tower>();
        AudioSource audioSrc;
        int lastSecond = -1;
        static AudioClip chime;

        public static MouldPressFx Show(CombatWorld world, List<Hazard> hazards, bool sequential)
        {
            var fx = new GameObject("Mould Press FX").AddComponent<MouldPressFx>();
            fx.w = world;
            for (int i = 0; i < hazards.Count; i++) fx.Add(hazards[i], sequential && i > 0);
            fx.audioSrc = fx.gameObject.AddComponent<AudioSource>();
            fx.audioSrc.spatialBlend = 0; fx.audioSrc.volume = 1; fx.audioSrc.playOnAwake = false;
            fx.Step();
            return fx;
        }

        void Add(Hazard h, bool dim)
        {
            var t = new Tower { H = h, Dim = dim, Root = TakeKit() };
            t.Root.name = "Mould " + h.Source;
            t.Root.transform.SetParent(transform, false);
            t.Root.transform.position = new Vector3(h.Origin.x, 0, h.Origin.z);
            var rt = t.Root.transform;
            rt.GetChild(0).GetComponent<MeshFilter>().sharedMesh = FoundryProps.MouldRing(h.Radius);   // just outside the soak circle
            t.Fill = rt.GetChild(1);
            t.Root.SetActive(true);
            if (dim) h.Tele?.Tint(TeleFill * 0.45f, TeleBorder * 0.5f);
            towers.Add(t);
        }

        // Tower kits (a ring and a fill) are pooled: building and destroying 17 pieces per tower in the cast's tick cost a
        // 27 ms frame in Phase 3 (perf report crucivar-p3-2026-11-04). WarmUp builds the kits on the Ready screen.
        static readonly Stack<GameObject> kits = new Stack<GameObject>();
        static Transform pool;


        public static void WarmUp(int count = 4)
        {
            Chime();
            var made = new List<GameObject>();
            for (int i = 0; i < count; i++) made.Add(TakeKit());
            foreach (var k in made) GiveKit(k);
        }

        static GameObject TakeKit()
        {
            while (kits.Count > 0) { var k = kits.Pop(); if (k) return k; }   // a scene change destroyed the pool
            var root = new GameObject("Mould kit"); root.SetActive(false);
            Piece(FoundryProps.MouldRing(MoltenCrown.TowerRadius), root.transform, FoundryProps.Brass);   // child 0: the ring (re-meshed per radius in Add)
            var fill = Piece(FoundryProps.FillDisc(), root.transform, FoundryProps.Molten);              // child 1: the fill, unit radius
            fill.localPosition = Vector3.up * 0.02f;
            Piece(FoundryProps.FillCrust(), fill, FoundryProps.Crust);
            return root;
        }

        static void GiveKit(GameObject k)
        {
            if (!k) return;
            if (pool == null) { var go = new GameObject("Mould kit pool"); go.SetActive(false); pool = go.transform; }
            k.SetActive(false); k.transform.SetParent(pool, false);
            kits.Push(k);
        }

        static Transform Piece(Mesh mesh, Transform parent, Material m)
        {
            var go = new GameObject("piece");
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = m; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            go.transform.localPosition = Vector3.zero;
            go.transform.SetParent(parent, false);
            return go.transform;
        }


        void Update() => Step();

        void Step()
        {
            for (int i = towers.Count - 1; i >= 0; i--)
            {
                var t = towers[i];
                if (w == null || !Contains(w.Hazards.Live, t.H)) { GiveKit(t.Root); towers.RemoveAt(i); continue; }
            }
            if (towers.Count == 0) { Destroy(gameObject); return; }

            // a dimmed tower wakes once every tower before it has resolved
            if (towers[0].Dim) { towers[0].Dim = false; towers[0].H.Tele?.Tint(TeleFill, TeleBorder); }

            int soonest = int.MaxValue;
            foreach (var t in towers)
            {
                var h = t.H;
                float k = t.Dim ? 0 : Mathf.Clamp01((float)h.Age / Mathf.Max(1, h.ResolveAt));
                // the fill grows to exactly the soak radius and never past it (8 Nov playtest: it outgrew the circle, because
                // the built-in cylinder is wider than the 1 m the old scale assumed); sized from the mesh's own bounds
                float d = FillScale(h.Radius * Mathf.Max(0.05f, k));
                t.Fill.localScale = new Vector3(d, 0.02f + 0.06f * k, d);
                t.Inside = 0;
                foreach (var p in w.Players) if (!p.Down && h.Hits(p)) t.Inside++;
                soonest = Mathf.Min(soonest, h.ResolveAt - h.Age);
            }

            // one chime per second of the soonest mould's countdown, higher on the last second
            int sec = Mathf.CeilToInt(soonest / (float)Sim.Hz);
            if (sec != lastSecond && sec > 0)
            {
                lastSecond = sec;
                audioSrc.pitch = sec <= 1 ? 1.26f : 1f;
                audioSrc.PlayOneShot(Chime(), 1f);
            }
        }

        // the fill's world radius for a given scale: the mesh's own half width times the scale
        public static float FillRadius(float scale) => FoundryProps.FillDisc().bounds.extents.x * scale;
        public static float FillScale(float radius) => radius / Mathf.Max(0.01f, FoundryProps.FillDisc().bounds.extents.x);
        // for the test: how far past its soak circle any fill reaches now (<= 0: inside), and the fullest fill / radius
        public float FillOverhang() { float o = float.MinValue; foreach (var t in towers) o = Mathf.Max(o, FillRadius(t.Fill.localScale.x) - t.H.Radius); return o; }
        public float FillReach() { float r = 0; foreach (var t in towers) r = Mathf.Max(r, FillRadius(t.Fill.localScale.x) / t.H.Radius); return r; }

        static bool Contains(IReadOnlyList<Hazard> live, Hazard h) { for (int i = 0; i < live.Count; i++) if (live[i] == h) return true; return false; }

        void OnGUI()
        {
            var cam = Camera.main; if (cam == null) return;
            foreach (var t in towers)
            {
                var sp = cam.WorldToScreenPoint(t.H.Origin + Vector3.up * 2.6f);
                if (sp.z <= 0) continue;
                var c = new Vector2(sp.x, Screen.height - sp.y);
                float a = t.Dim ? 0.4f : 1f;
                EncounterCallouts.Numeral(new Vector2(c.x, c.y - UiTheme.V(4)), HideCounts ? "?" : t.H.ShareCount.ToString(), a);
                // one slot per needed player: thin ivory tick when filled, dark when empty
                int slots = HideCounts ? t.Inside : Mathf.Max(t.H.ShareCount, t.Inside);
                SlotTicks(c.x, c.y + UiTheme.V(36), slots, t.Inside, a);
            }
        }

        // the soak's player slots under its numeral (shared with the Rivet Cantor's Chorus): filled Ivory, empty ink
        public static void SlotTicks(float cx, float y, int slots, int filled, float alpha = 1)
        {
            float step = UiTheme.V(18), w = UiTheme.V(12), h = Mathf.Max(2, UiTheme.V(5)), x0 = cx - (slots * step - (step - w)) / 2f;
            for (int i = 0; i < slots; i++)
                UiTheme.Fill(new Rect(x0 + i * step, y, w, h), i < filled ? EncounterCallouts.Alpha(UiTheme.Ivory, alpha) : EncounterCallouts.Alpha(UiTheme.InkDeep, 0.8f * alpha));
        }

        // a struck-bell partial stack, made once (no audio assets in the project yet)
        static AudioClip Chime()
        {
            if (chime != null) return chime;
            const int rate = 44100; const float len = 1.1f, f0 = 880f;
            var data = new float[(int)(rate * len)];
            float[] ratio = { 1f, 2.0f, 2.76f, 5.4f }, amp = { 0.55f, 0.3f, 0.22f, 0.1f }, decay = { 3.2f, 4.5f, 6f, 9f };
            for (int i = 0; i < data.Length; i++)
            {
                float t = i / (float)rate, s = 0;
                for (int k = 0; k < ratio.Length; k++) s += amp[k] * Mathf.Exp(-decay[k] * t) * Mathf.Sin(2 * Mathf.PI * f0 * ratio[k] * t);
                data[i] = Mathf.Clamp(s * Mathf.Min(1, t * 400), -1, 1);
            }
            chime = AudioClip.Create("Mould chime", data.Length, 1, rate, false);
            chime.SetData(data, 0);
            return chime;
        }
    }
}
