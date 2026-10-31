using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace LastBell.Run
{
    // Boss attack icons by Codex (ability-icons 0d0555a, approved by Chzy18 2026-10-23): one 256 px icon per v2 attack of
    // the four main bosses, keyed by the attack's runtime ID ("bellkeeper.leaning_bell"). Data lives in
    // Resources/Run/BossAttackIcons/catalog.json (id, boss, name, tag, shape, icon file, extra hazard Source spellings).
    //
    // Several icons look alike at 64 px (the half-discs, ring-around-disc and linked-disc groups), so anything that draws
    // one must print the attack name and the boss next to it (Label). The icon alone never identifies the attack.
    //
    // A hazard is matched to an attack by Hazard.AttackId when its ability sets it (preferred), otherwise by its Source
    // text: the attack name or an alias, ignoring case, spaces, punctuation and a leading "The", as a prefix, within the
    // current boss ("Starfall Mirror (reflection)" -> aurelion.starfall_mirror). Anything else (v1 moves, test hazards)
    // resolves to null and is shown as text only.
    public static class BossAttackIcons
    {
        [Serializable]
        public sealed class Attack
        {
            public string id, boss, bossName, name, tag, shape, icon;
            public string[] aliases;
            public string Label => name + " · " + bossName;
        }
        [Serializable] public sealed class Catalog { public string source; public Attack[] attacks; }

        public const string ResourceDir = "Run/BossAttackIcons/";
        static Attack[] all;
        static readonly Dictionary<string, Attack> byId = new Dictionary<string, Attack>();
        static readonly List<(string key, Attack a)> keys = new List<(string, Attack)>();   // longest first
        static readonly Dictionary<string, Attack> bySource = new Dictionary<string, Attack>();
        static readonly Dictionary<string, Texture2D> textures = new Dictionary<string, Texture2D>();
        static readonly Dictionary<string, Sprite> sprites = new Dictionary<string, Sprite>();

        public static IReadOnlyList<Attack> All { get { Load(); return all; } }

        static void Load()
        {
            if (all != null) return;
            var ta = Resources.Load<TextAsset>(ResourceDir + "catalog");
            all = ta != null ? JsonUtility.FromJson<Catalog>(ta.text)?.attacks ?? Array.Empty<Attack>() : Array.Empty<Attack>();
            if (ta == null) Debug.LogError("BossAttackIcons: Resources/" + ResourceDir + "catalog.json is missing");
            foreach (var a in all)
            {
                byId[a.id] = a;
                keys.Add((Key(a.name), a));
                if (a.aliases != null) foreach (var al in a.aliases) keys.Add((Key(al), a));
            }
            keys.Sort((x, y) => y.key.Length.CompareTo(x.key.Length));
        }

        // "The Last Toll Tide" -> "lasttolltide"
        static string Key(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.TrimStart(); if (s.StartsWith("the ", StringComparison.OrdinalIgnoreCase)) s = s.Substring(4);
            var sb = new StringBuilder(s.Length);
            foreach (var c in s) if (char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
            return sb.ToString();
        }

        public static Attack Get(string id) { Load(); return id != null && byId.TryGetValue(id, out var a) ? a : null; }

        public static Texture2D Icon(Attack a)
        {
            if (a == null) return null;
            if (!textures.TryGetValue(a.id, out var t)) textures[a.id] = t = Resources.Load<Texture2D>(ResourceDir + a.icon);
            return t;
        }
        public static Texture2D Icon(string id) => Icon(Get(id));

        // for uGUI / sprite renderers; IMGUI draws the Texture2D
        public static Sprite IconSprite(string id)
        {
            var a = Get(id); var t = Icon(a); if (t == null) return null;
            if (!sprites.TryGetValue(a.id, out var s)) sprites[a.id] = s = Sprite.Create(t, new Rect(0, 0, t.width, t.height), new Vector2(0.5f, 0.5f), 100);
            return s;
        }

        // "aurelion" / "crucivar" / "sylvara" / "bellkeeper", or null for anything else
        public static string BossKey(BossEncounter b)
        {
            if (b == null) return null;
            var n = b.DisplayName.ToLowerInvariant();
            foreach (var k in new[] { "aurelion", "crucivar", "sylvara", "bellkeeper" }) if (n.Contains(k)) return k;
            return null;
        }

        // explicit attack ID first; otherwise the Source text, limited to the given boss when known
        public static Attack Resolve(string attackId, string source, string boss = null)
        {
            Load();
            var a = Get(attackId); if (a != null) return a;
            if (string.IsNullOrEmpty(source)) return null;
            var cacheKey = (boss ?? "") + "|" + source;
            if (bySource.TryGetValue(cacheKey, out a)) return a;
            var k = Key(source);
            foreach (var e in keys)
                if (e.key.Length > 0 && k.StartsWith(e.key) && (boss == null || e.a.boss == boss)) { a = e.a; break; }
            bySource[cacheKey] = a;
            return a;
        }
        public static Attack Resolve(Hazard h, BossEncounter boss) => h == null ? null : Resolve(h.AttackId, h.Source, BossKey(boss));
    }
}
