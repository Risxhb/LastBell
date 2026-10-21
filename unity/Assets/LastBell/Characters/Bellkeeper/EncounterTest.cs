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
    // post-hit immunity like the browser prototype, and a push out of the boss's body. Its hits on the boss come from
    // VanguardTestController.OnHurtFrame (fired on the frame the blade connects, with the kit's reach, arc and damage);
    // damage is x10 against the boss's 1800 HP, and reach is measured to the boss's body, not its centre.
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

        const int DamageScale = 10;
        VanguardTestController vanguard;

        public void Bind(BellkeeperTestController b, BellkeeperFx f)
        {
            boss = b; fx = f;
            vanguard = GetComponent<VanguardTestController>();
            if (vanguard) { vanguard.OnHurtFrame -= HurtFrame; vanguard.OnHurtFrame += HurtFrame; }
        }
        void OnDestroy() { if (vanguard) vanguard.OnHurtFrame -= HurtFrame; }

        void HurtFrame(string action, Vector3 origin, Vector3 forward, float reach, float arcDeg, int damage)
        {
            if (Down || boss == null || boss.Dead) return;
            var to = boss.transform.position - origin; to.y = 0; forward.y = 0;
            if (to.magnitude > reach + BellkeeperTestController.BodyRadius) return;
            if (arcDeg < 360 && Vector3.Angle(forward, to) > arcDeg / 2) return;
            var contact = boss.transform.position - to.normalized * BellkeeperTestController.BodyRadius + Vector3.up * 2.2f;
            boss.TakeHit(damage * DamageScale, contact);
        }

        public void ResetVitals()
        {
            Hp = MaxHp; Potions = MaxPotions; sinceHit = sincePotion = 99;
            SetControllable(true); EncounterHud.ClearBanner();
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
            float dt = Time.deltaTime; sinceHit += dt; sincePotion += dt;
            var kb = Keyboard.current;
            if (kb != null && kb.fKey.wasPressedThisFrame) DrinkPotion();
        }

        public bool DrinkPotion()
        {
            if (Down || Potions == 0 || Hp >= MaxHp || sincePotion < PotionLockout) return false;
            int healed = Mathf.Min(PotionHeal, MaxHp - Hp);
            Hp += healed; Potions--; sincePotion = 0;
            EncounterHud.Float(transform.position + Vector3.up * 2.4f, "+" + healed, new Color(0.45f, 1f, 0.8f));
            fx?.PotionHeal(transform);
            return true;
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
                // bottom centre, so it stays near the action on wide screens
                float px = w / 2 - 170;
                GUI.Label(new Rect(px, h - 74, 340, 20), $"Vanguard  {player.Hp} / {PlayerVitals.MaxHp}", small);
                Bar(new Rect(px, h - 52, 340, 16), (float)player.Hp / PlayerVitals.MaxHp, new Color(0.96f, 0.79f, 0.41f), new Color(0.1f, 0.12f, 0.2f));
                string pots = new string('●', player.Potions) + new string('○', PlayerVitals.MaxPotions - player.Potions);
                GUI.Label(new Rect(px, h - 32, 400, 22), $"Potions {pots}  [F]   ·   R reset", small);
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
