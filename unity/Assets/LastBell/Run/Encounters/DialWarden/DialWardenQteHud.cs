using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;

namespace LastBell.Run
{
    // The parry prompt box, plus the F7-only diagnostics overlay.
    //
    // The box floats above the braced local player's head: a small dark box with a thin gold edge holding one key cap
    // per input. Directions are arrows with their W/A/S/D letter in a corner; the last cap is E. Done caps dim to gold,
    // the current one is bright with a thick gold edge, upcoming ones are muted. A ring closes onto the E cap, landing
    // exactly on the hand's arrival, and turns bright gold inside the parry window. Success flashes the box gold; a
    // failure turns the caps red and shakes the box. No words. Presentation only: ParryQte owns the rules.
    public sealed class DialWardenQteHud : MonoBehaviour
    {
        public static bool ShowDiagnostics;
        DialWardenEncounter b;
        GUIStyle letterSt, keySt, diagSt;
        readonly StringBuilder sb = new StringBuilder();
        static Texture2D arrow, ring;

        // virtual (1080p) sizes
        const float Cap = 46, Gap = 8, Pad = 10, Above = 2.7f;
        public static readonly Color CapFill = new Color(0.03f, 0.04f, 0.07f, 0.92f), Red = new Color(1f, 0.38f, 0.32f, 1f);

        public void Bind(DialWardenEncounter boss) { b = boss; }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.f7Key.wasPressedThisFrame) ShowDiagnostics = !ShowDiagnostics;
        }

        void OnGUI()
        {
            if (!b) return;
            GUI.depth = 4;
            if (Event.current.type == EventType.Repaint) DrawPrompt();
            if (ShowDiagnostics && !b.Dead) Diagnostics();
        }

        // ---------------- the prompt box ----------------
        void DrawPrompt()
        {
            var d = RunDirector.Instance; var me = d ? d.Player : null; var cam = Camera.main;
            if (!me || !cam) return;
            var q = b.PromptFor(me, out int sinceResult);
            if (q == null) return;
            float fade = q.Open ? 1 : 1 - Mathf.Clamp01((sinceResult - 18) / 18f);   // results hold 0.3 s, then fade 0.3 s
            if (fade <= 0) return;
            var sp = cam.WorldToScreenPoint(me.transform.position + Vector3.up * Above);
            if (sp.z < 0) return;
            float s = UiTheme.Scale;
            int n = q.Sequence.Length;
            float w = (n * Cap + (n - 1) * Gap + 2 * Pad) * s, h = (Cap + 2 * Pad) * s;
            float shake = q.Result == ParryQte.Outcome.Fail ? Mathf.Sin(sinceResult * 1.9f) * 6f * s * Mathf.Clamp01(1 - sinceResult / 18f) : 0;
            var box = new Rect(sp.x - w / 2 + shake, Screen.height - sp.y - h, w, h);
            int now = b.Tick;
            var old = GUI.color;

            // box: dark, thin gold edge; success flashes the edge bright, failure turns it red
            Color edge = q.Result == ParryQte.Outcome.Success ? Color.Lerp(UiTheme.GoldBright, UiTheme.Gold, sinceResult / 18f)
                       : q.Result == ParryQte.Outcome.Fail ? Red : UiTheme.GoldDim;
            UiTheme.Fill(new Rect(box.x + 2 * s, box.y + 3 * s, box.width, box.height), A(new Color(0, 0, 0, 0.35f), fade));
            UiTheme.Fill(box, A(UiTheme.InkStrip, fade));
            UiTheme.Border(box, A(edge, fade), Mathf.Max(1, Mathf.Round(s * (q.Result == ParryQte.Outcome.Open ? 1 : 2))));

            letterSt ??= UiTheme.Style(11, true, TextAnchor.LowerRight, UiTheme.IvoryMuted);
            keySt ??= UiTheme.Style(22, true, TextAnchor.MiddleCenter, UiTheme.Ivory);
            for (int i = 0; i < n; i++)
            {
                var k = q.Sequence[i];
                var c = new Rect(box.x + (Pad + i * (Cap + Gap)) * s, box.y + Pad * s, Cap * s, Cap * s);
                bool done = i < q.Index, current = q.Open && i == q.Index, wrong = q.Result == ParryQte.Outcome.Fail && i == q.Index;
                Color ink = wrong ? Red : done ? UiTheme.Gold : current ? UiTheme.Ivory : UiTheme.IvoryMicro;
                if (q.Result == ParryQte.Outcome.Success) ink = UiTheme.GoldBright;
                Color capEdge = wrong ? Red : current ? UiTheme.GoldBright : done ? A(UiTheme.Gold, 0.7f) : A(UiTheme.GoldDim, 0.6f);
                UiTheme.Fill(c, A(CapFill, fade));
                UiTheme.Border(c, A(capEdge, fade), Mathf.Max(1, Mathf.Round(s * (current || wrong ? 2 : 1))));
                if (k == QteKey.Parry) UiTheme.Label(c, "E", keySt, A(ink, fade));
                else
                {
                    float sz = Cap * 0.56f * s; var ar = new Rect(c.center.x - sz / 2, c.center.y - sz / 2, sz, sz);
                    var m = GUI.matrix; GUIUtility.RotateAroundPivot(ArrowAngle(k), c.center);
                    GUI.color = A(ink, fade); GUI.DrawTexture(ar, Arrow()); GUI.matrix = m; GUI.color = old;
                    UiTheme.Label(new Rect(c.x, c.y, c.width - 3 * s, c.height - 1 * s), Letter(k), letterSt, A(done || current ? UiTheme.IvoryMuted : UiTheme.IvoryMicro, fade));
                }
                // the closing ring on the parry cap: from 2.1x the cap at the prompt's opening to the cap's edge at arrival
                if (k == QteKey.Parry && q.Open)
                {
                    float span = Mathf.Max(1, q.ArriveTick - q.OpenTick), u = Mathf.Clamp01((now - q.OpenTick) / span);
                    float rs = Mathf.Lerp(2.1f, 1.08f, u) * Cap * s;
                    var rr = new Rect(c.center.x - rs / 2, c.center.y - rs / 2, rs, rs);
                    bool inWindow = q.InWindow(now);
                    GUI.color = A(inWindow ? UiTheme.GoldBright : current ? UiTheme.Ivory : UiTheme.IvoryMuted, fade * (inWindow ? 1f : 0.8f));
                    GUI.DrawTexture(rr, Ring()); GUI.color = old;
                }
            }
        }

        static Color A(Color c, float a) { c.a *= a; return c; }
        static float ArrowAngle(QteKey k) => k == QteKey.Right ? 90 : k == QteKey.Down ? 180 : k == QteKey.Left ? 270 : 0;
        static string Letter(QteKey k) => k == QteKey.Up ? "W" : k == QteKey.Left ? "A" : k == QteKey.Down ? "S" : "D";

        // an upward arrow: a triangular head on a short stem, anti-aliased (texture rows run bottom-up)
        static Texture2D Arrow()
        {
            if (arrow) return arrow;
            const int S = 64; arrow = new Texture2D(S, S, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < S; y++) for (int x = 0; x < S; x++)
            {
                float px = x + 0.5f - S / 2f, py = y + 0.5f;                     // py: 0 at the bottom, S at the top
                // head: apex at py 60, base at py 30, half-width 26 at the base
                float headY = 30, apex = 60, half = 26 * Mathf.Clamp01((apex - py) / (apex - headY));
                float head = py >= headY - 0.5f && py <= apex ? Mathf.Clamp01(half - Mathf.Abs(px) + 0.5f) * Mathf.Clamp01(py - headY + 1f) : 0;
                float stem = py >= 6 && py < headY + 1 ? Mathf.Clamp01(8.5f - Mathf.Abs(px)) * Mathf.Clamp01(py - 5f) : 0;
                arrow.SetPixel(x, y, new Color(1, 1, 1, Mathf.Max(head, stem)));
            }
            arrow.Apply(); return arrow;
        }
        static Texture2D Ring()
        {
            if (ring) return ring;
            const int S = 128; ring = new Texture2D(S, S, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < S; y++) for (int x = 0; x < S; x++)
            {
                float dx = x + 0.5f - S / 2f, dy = y + 0.5f - S / 2f, r = Mathf.Sqrt(dx * dx + dy * dy);
                ring.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01(3f - Mathf.Abs(r - 60f))));
            }
            ring.Apply(); return ring;
        }

        // ---------------- F7 diagnostics (developer only, off by default) ----------------
        void Diagnostics()
        {
            diagSt ??= new GUIStyle(GUI.skin.label) { fontSize = 14, richText = false };
            diagSt.normal.textColor = new Color(0.85f, 0.95f, 1f);
            sb.Clear();
            sb.AppendLine("[DEV] Dial Warden (F7)");
            sb.AppendLine($"{(b.Phase2 ? "Phase 2" : "Phase 1")}{(b.HoldHp ? " (HP held)" : "")}{(b.Midnight ? "  MIDNIGHT" : "")}   {b.Bpm:0} BPM   block {b.Blocks}");
            sb.AppendLine("Now: " + b.Current);
            if (b.LapActive) sb.AppendLine($"Hand: step {b.HandSteps:0.0} {(b.Clockwise ? "cw" : "ccw")}  lit {string.Join(",", b.LitHours)}  catches {b.Catches}  misses {b.LapMisses}");
            if (b.ScalesActive) sb.AppendLine($"Scales: throw {b.Throw}/{DialWardenEncounter.Throws}  balanced {b.Balanced}  off {b.Unbalanced}");
            sb.AppendLine($"Hits  beam {b.BeamHits}  weight {b.WeightHits}  hand {b.HandHits}  break {b.BreakHits}  wave {b.WaveHits}  implode {b.ImplosionHits}");
            GUI.Label(new Rect(16, Screen.height * 0.30f, 760, 160), sb.ToString(), diagSt);
        }
    }
}
