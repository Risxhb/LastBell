using System.Collections.Generic;
using UnityEngine;

namespace LastBell.Run
{
    // Mould Press presentation (stand-in): a brass gear-tooth rim and a molten fill rising with the countdown on each
    // mould, a large ivory numeral (the headcount) with one tick per player inside, and a loud chime each second. The
    // coral/amber circle itself is the hazard's telegraph. Solo tower II is dimmed until tower I resolves. Each tower's
    // pieces leave the moment its hazard leaves W.Hazards.Live (resolve, boss death, ClearTransient); the whole object
    // removes itself when none is left. Nothing here decides timing or hits.
    public sealed class MouldPressFx : MonoBehaviour
    {
        static readonly Color Ivory = new Color(1f, 0.95f, 0.82f), Molten = new Color(1.0f, 0.45f, 0.08f),
            BrassTooth = new Color(0.76f, 0.55f, 0.27f), InkShadow = new Color(0.063f, 0.094f, 0.145f, 0.9f);
        // the telegraph shader's own defaults (FX_SH_Telegraph), restored when a dimmed tower wakes
        static readonly Color TeleFill = new Color(0.92f, 0.41f, 0.36f), TeleBorder = new Color(1.25f, 0.5f, 0.36f);
        const int Teeth = 16;

        sealed class Tower { public Hazard H; public GameObject Root; public Transform Fill; public bool Dim; public int Inside; }

        public bool HideCounts;   // Molten Crown memory cycles: the moulds show "?" instead of their count
        CombatWorld w;
        readonly List<Tower> towers = new List<Tower>();
        AudioSource audioSrc;
        int lastSecond = -1;
        static AudioClip chime;
        static Material toothMat, fillMat;
        static GUIStyle numStyle;

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
            if (toothMat == null)
            {
                var lit = Shader.Find("Universal Render Pipeline/Lit"); var unlit = Shader.Find("Universal Render Pipeline/Unlit");
                toothMat = new Material(lit); toothMat.color = BrassTooth; toothMat.SetFloat("_Metallic", 0.85f); toothMat.SetFloat("_Smoothness", 0.55f);
                fillMat = new Material(unlit); fillMat.color = Molten;
            }
            var t = new Tower { H = h, Dim = dim, Root = new GameObject("Mould " + h.Source) };
            t.Root.transform.SetParent(transform, false);
            t.Root.transform.position = new Vector3(h.Origin.x, 0, h.Origin.z);
            for (int i = 0; i < Teeth; i++)
            {
                var tooth = Piece(PrimitiveType.Cube, t.Root.transform, toothMat);
                float a = 360f * i / Teeth;
                tooth.localRotation = Quaternion.Euler(0, a, 0);
                tooth.localPosition = Quaternion.Euler(0, a, 0) * Vector3.forward * (h.Radius + 0.12f) + Vector3.up * 0.09f;
                tooth.localScale = new Vector3(0.32f, 0.18f, 0.22f);
            }
            t.Fill = Piece(PrimitiveType.Cylinder, t.Root.transform, fillMat);
            t.Fill.localPosition = Vector3.up * 0.05f;
            if (dim) h.Tele?.Tint(TeleFill * 0.45f, TeleBorder * 0.5f);
            towers.Add(t);
        }

        static Transform Piece(PrimitiveType type, Transform parent, Material m)
        {
            var go = GameObject.CreatePrimitive(type);
            Destroy(go.GetComponent<Collider>());
            var r = go.GetComponent<MeshRenderer>(); r.sharedMaterial = m; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            go.transform.SetParent(parent, false);
            return go.transform;
        }

        void Update() => Step();

        void Step()
        {
            for (int i = towers.Count - 1; i >= 0; i--)
            {
                var t = towers[i];
                if (w == null || !Contains(w.Hazards.Live, t.H)) { Destroy(t.Root); towers.RemoveAt(i); continue; }
            }
            if (towers.Count == 0) { Destroy(gameObject); return; }

            // a dimmed tower wakes once every tower before it has resolved
            if (towers[0].Dim) { towers[0].Dim = false; towers[0].H.Tele?.Tint(TeleFill, TeleBorder); }

            int soonest = int.MaxValue;
            foreach (var t in towers)
            {
                var h = t.H;
                float k = t.Dim ? 0 : Mathf.Clamp01((float)h.Age / Mathf.Max(1, h.ResolveAt));
                float d = 2 * h.Radius * 0.9f * Mathf.Max(0.05f, k);
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

        static bool Contains(IReadOnlyList<Hazard> live, Hazard h) { for (int i = 0; i < live.Count; i++) if (live[i] == h) return true; return false; }

        void OnGUI()
        {
            var cam = Camera.main; if (cam == null) return;
            if (numStyle == null) numStyle = new GUIStyle(GUI.skin.label) { fontSize = 64, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            foreach (var t in towers)
            {
                var sp = cam.WorldToScreenPoint(t.H.Origin + Vector3.up * 2.6f);
                if (sp.z <= 0) continue;
                var c = new Vector2(sp.x, Screen.height - sp.y);
                float a = t.Dim ? 0.4f : 1f;
                var rect = new Rect(c.x - 50, c.y - 44, 100, 80);
                string label = HideCounts ? "?" : t.H.ShareCount.ToString();
                numStyle.normal.textColor = InkShadow * new Color(1, 1, 1, a);
                GUI.Label(new Rect(rect.x + 3, rect.y + 3, rect.width, rect.height), label, numStyle);
                numStyle.normal.textColor = new Color(Ivory.r, Ivory.g, Ivory.b, a);
                GUI.Label(rect, label, numStyle);
                // one slot per needed player: thin ivory tick when filled, dark when empty
                int slots = HideCounts ? t.Inside : Mathf.Max(t.H.ShareCount, t.Inside);
                float x0 = c.x - (slots * 18 - 6) / 2f;
                for (int i = 0; i < slots; i++)
                {
                    var r = new Rect(x0 + i * 18, c.y + 36, 12, 5);
                    var old = GUI.color;
                    GUI.color = i < t.Inside ? new Color(Ivory.r, Ivory.g, Ivory.b, a) : new Color(0.1f, 0.12f, 0.16f, 0.8f * a);
                    GUI.DrawTexture(r, Texture2D.whiteTexture);
                    GUI.color = old;
                }
            }
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
