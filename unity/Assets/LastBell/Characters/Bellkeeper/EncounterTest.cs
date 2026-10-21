using System.Collections.Generic;
using LastBell.VFX;
using UnityEngine;
using UnityEngine.InputSystem;

namespace LastBell.Characters
{
    // Health, potions and the HUD for the Bellkeeper test fight. Not the real combat rules (those belong in
    // Core/ClassTest and a future simulation); just enough to play the boss in the arena.
    //
    // PlayerVitals goes on the Vanguard test character at runtime: 100 HP, 5 potions (F, +40 HP each), 0.75 s of
    // post-hit immunity like the browser prototype, and a push out of the boss's body. Its hits on the boss are read
    // from the same buttons VanguardTestController uses (that script belongs to the characters session and has no hit
    // events yet), with the prototype's ranges and arcs and its damage x10 against the boss's 1800 HP.
    public sealed class PlayerVitals : MonoBehaviour
    {
        public const int MaxHp = 100, MaxPotions = 5, PotionHeal = 40;
        const float Immunity = 0.75f, PotionLockout = 1f, BodyRadius = 1.6f;

        public int Hp { get; private set; } = MaxHp;
        public int Potions { get; private set; } = MaxPotions;
        public bool Down => Hp <= 0;

        BellkeeperTestController boss;
        BellkeeperFx fx;
        float sinceHit = 99, sincePotion = 99;

        // ---- Vanguard hit reading (prototype numbers: 1 m = 40 units, damage x10) ----
        struct Swing { public float at, reach, arc; public int damage; }
        readonly List<Swing> pending = new List<Swing>();
        float busyUntil, t, lastDodge = -99, lastCombo = -99, finalHourReady;
        int comboStep;

        public void Bind(BellkeeperTestController b, BellkeeperFx f) { boss = b; fx = f; }

        public void ResetVitals()
        {
            Hp = MaxHp; Potions = MaxPotions; sinceHit = sincePotion = 99; pending.Clear(); finalHourReady = 0;
            SetControllable(true);
        }

        // returns false when immunity ate the hit
        public bool TakeHit(int damage, string source)
        {
            if (Down || sinceHit < Immunity) return false;
            Hp = Mathf.Max(0, Hp - damage); sinceHit = 0;
            EncounterHud.Float(transform.position + Vector3.up * 2.4f, "-" + damage, new Color(1f, 0.42f, 0.36f));
            fx?.PlayerHit(transform.position);
            if (Down) { SetControllable(false); EncounterHud.Banner($"Down: {source}. R to reset"); }
            return true;
        }

        void SetControllable(bool on)
        {
            var v = GetComponent<VanguardTestController>(); if (v) v.enabled = on;
        }

        void Update()
        {
            float dt = Time.deltaTime; t += dt; sinceHit += dt; sincePotion += dt;
            var kb = Keyboard.current; var mouse = Mouse.current;
            if (Down) { pending.Clear(); return; }

            if (kb != null && kb.fKey.wasPressedThisFrame && Potions > 0 && Hp < MaxHp && sincePotion >= PotionLockout)
            {
                int healed = Mathf.Min(PotionHeal, MaxHp - Hp);
                Hp += healed; Potions--; sincePotion = 0;
                EncounterHud.Float(transform.position + Vector3.up * 2.4f, "+" + healed, new Color(0.45f, 1f, 0.8f));
                fx?.PotionHeal(transform);
            }

            if (boss != null && !boss.Dead && t >= busyUntil)
            {
                if (mouse != null && mouse.rightButton.wasPressedThisFrame) lastDodge = t;
                if (mouse != null && mouse.leftButton.wasPressedThisFrame)
                {
                    if (t - lastDodge < 0.45f) Queue(0.12f, 0.45f, 170, 120, 30);                      // Dash Attack (lunge 4.5)
                    else
                    {
                        comboStep = t - lastCombo < 0.7f && comboStep < 3 ? comboStep + 1 : 1; lastCombo = t;
                        if (comboStep < 3) Queue(0.14f, 0.3f, 160, 110, 20); else Queue(0.2f, 0.45f, 175, 170, 40);
                    }
                }
                else if (kb != null && kb.digit2Key.wasPressedThisFrame) Queue(0.35f, 0.7f, 170 + 160, 100, 90);   // Pendulum Rush travels ~4 m
                else if (kb != null && kb.digit3Key.wasPressedThisFrame && t >= finalHourReady)
                { Queue(0.45f, 1f, 270, 170, 180); finalHourReady = t + 6f; }                                       // Final Hour, 360-tick cooldown
            }
            for (int i = pending.Count - 1; i >= 0; i--)
            {
                if (t < pending[i].at) continue;
                var s = pending[i]; pending.RemoveAt(i);
                if (boss == null || boss.Dead) continue;
                var to = boss.transform.position - transform.position; to.y = 0;
                float reach = s.reach / 40f + BellkeeperTestController.BodyRadius;
                if (to.magnitude <= reach && Vector3.Angle(transform.forward, to) <= s.arc / 2)
                    boss.TakeHit(s.damage, boss.transform.position - to.normalized * BellkeeperTestController.BodyRadius + Vector3.up * 2.2f);
            }
        }

        void Queue(float delay, float busy, float reachUnits, float arcDeg, int damage)
        {
            pending.Add(new Swing { at = t + delay, reach = reachUnits, arc = arcDeg, damage = damage });
            busyUntil = t + busy;
        }

        void LateUpdate()
        {
            // the boss's body is solid: push the player out of it
            if (boss == null) return;
            var d = transform.position - boss.transform.position; d.y = 0;
            if (d.magnitude < BodyRadius)
            {
                var n = d.sqrMagnitude > 1e-4f ? d.normalized : -boss.transform.forward;
                var p = boss.transform.position + n * BodyRadius; p.y = transform.position.y; transform.position = p;
            }
        }
    }

    // Boss bar, player bar and potions, floating numbers and banners (IMGUI; a stand-in for the real HUD).
    public sealed class EncounterHud : MonoBehaviour
    {
        struct FloatText { public Vector3 pos; public string text; public Color color; public float age; }
        static readonly List<FloatText> floats = new List<FloatText>();
        static string banner;
        public BellkeeperTestController boss; public PlayerVitals player;
        GUIStyle big, small, num;

        public static void Float(Vector3 world, string text, Color c) => floats.Add(new FloatText { pos = world, text = text, color = c });
        public static void Banner(string text) => banner = text;
        public static void ClearBanner() => banner = null;

        void Update()
        {
            for (int i = floats.Count - 1; i >= 0; i--)
            {
                var f = floats[i]; f.age += Time.deltaTime; f.pos += Vector3.up * Time.deltaTime * 1.2f; floats[i] = f;
                if (f.age > 0.9f) floats.RemoveAt(i);
            }
        }

        static void Bar(Rect r, float frac, Color fill, Color back)
        {
            var old = GUI.color;
            GUI.color = new Color(0, 0, 0, 0.6f); GUI.DrawTexture(new Rect(r.x - 2, r.y - 2, r.width + 4, r.height + 4), Texture2D.whiteTexture);
            GUI.color = back; GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = fill; GUI.DrawTexture(new Rect(r.x, r.y, r.width * Mathf.Clamp01(frac), r.height), Texture2D.whiteTexture);
            GUI.color = old;
        }

        void OnGUI()
        {
            big ??= new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            small ??= new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold };
            num ??= new GUIStyle(GUI.skin.label) { fontSize = 20, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            float w = Screen.width, h = Screen.height;
            if (boss != null)
            {
                float bw = Mathf.Min(720, w * 0.6f);
                GUI.Label(new Rect(w / 2 - bw / 2, 14, bw, 24), "THE BELLKEEPER", big);
                Bar(new Rect(w / 2 - bw / 2, 42, bw, 14), (float)boss.Hp / BellkeeperTestController.MaxHp, new Color(0.92f, 0.41f, 0.36f), new Color(0.1f, 0.12f, 0.2f));
                GUI.Label(new Rect(w / 2 + bw / 2 - 120, 56, 120, 20), $"{boss.Hp} / {BellkeeperTestController.MaxHp}", small);
            }
            if (player != null)
            {
                GUI.Label(new Rect(20, h - 74, 300, 20), $"Vanguard  {player.Hp} / {PlayerVitals.MaxHp}", small);
                Bar(new Rect(20, h - 52, 300, 16), (float)player.Hp / PlayerVitals.MaxHp, new Color(0.96f, 0.79f, 0.41f), new Color(0.1f, 0.12f, 0.2f));
                string pots = new string('●', player.Potions) + new string('○', PlayerVitals.MaxPotions - player.Potions);
                GUI.Label(new Rect(20, h - 32, 400, 22), $"Potions {pots}  [F]   ·   R reset", small);
            }
            var cam = Camera.main;
            if (cam)
                foreach (var f in floats)
                {
                    var s = cam.WorldToScreenPoint(f.pos); if (s.z < 0) continue;
                    var c = f.color; c.a = 1 - Mathf.Clamp01((f.age - 0.5f) / 0.4f); num.normal.textColor = c;
                    GUI.Label(new Rect(s.x - 60, h - s.y - 14, 120, 28), f.text, num);
                }
            if (banner != null)
            {
                big.normal.textColor = Color.white;
                GUI.Label(new Rect(0, h * 0.3f, w, 40), banner, big);
            }
        }
    }
}
