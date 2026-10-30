using System;
using System.Collections.Generic;
using UnityEngine;

namespace LastBell.Run
{
    // Mechanic icons by Codex (ability-icons 54f7d22, approved by Chzy18 2026-10-24): 12 friendly co-op markers (order
    // I-VIII, brass and cobalt teams, carried mark, tether partner) and 16 hostile mechanic icons (spread, soak tower,
    // cone, ...), keyed by mechanic ID. Data lives in Resources/Run/MechanicIcons/catalog.json (id, set, name, meaning,
    // icon file). Code names a mechanic only through the constants below; nothing is looked up by file name.
    //
    // Several icons look alike at 64 px (brass team vs ring, tether partner vs stretch tether, carried mark vs pass the
    // mark), so anything that draws one must print the mechanic name next to it. The icon alone never identifies it.
    //
    // Friendly markers show identity only and never hurt (gpt/08-design-rules.md §5). Hostile icons tag a hazard
    // (Hazard.MechanicId) or a player (PlayerController.Marks) with the mechanic it is.
    public static class MechanicIcons
    {
        // friendly
        public const string OrderI = "order_i", OrderII = "order_ii", OrderIII = "order_iii", OrderIV = "order_iv",
                            OrderV = "order_v", OrderVI = "order_vi", OrderVII = "order_vii", OrderVIII = "order_viii";
        public const string TeamBrass = "team_brass", TeamCobalt = "team_cobalt", CarriedMark = "carried_mark", TetherPartner = "tether_partner";
        // hostile
        public const string Spread = "spread", SoakTower = "soak_tower", LineStack = "line_stack", StretchTether = "stretch_tether",
                            PassTheMark = "pass_the_mark", MovementStop = "movement_stop", MovementMove = "movement_move",
                            OneSideCleave = "one_side_cleave", PlayerMirror = "player_mirror", Pull = "pull", ShrinkingEdge = "shrinking_edge",
                            Cone = "cone", Ring = "ring", Lane = "lane", DelayedCircle = "delayed_circle", LowBar = "low_bar";

        static readonly string[] OrderIds = { OrderI, OrderII, OrderIII, OrderIV, OrderV, OrderVI, OrderVII, OrderVIII };
        // 1..8 -> "order_i".."order_viii", anything else null
        public static string Order(int n) => n >= 1 && n <= OrderIds.Length ? OrderIds[n - 1] : null;

        [Serializable]
        public sealed class Mechanic
        {
            public string id, set, name, meaning, icon;
            public bool Friendly => set == "friendly";
        }
        [Serializable] public sealed class Catalog { public string source; public Mechanic[] mechanics; }

        public const string ResourceDir = "Run/MechanicIcons/";
        static Mechanic[] all;
        static readonly Dictionary<string, Mechanic> byId = new Dictionary<string, Mechanic>();
        static readonly Dictionary<string, Texture2D> textures = new Dictionary<string, Texture2D>();
        static readonly Dictionary<string, Sprite> sprites = new Dictionary<string, Sprite>();

        public static IReadOnlyList<Mechanic> All { get { Load(); return all; } }

        static void Load()
        {
            if (all != null) return;
            var ta = Resources.Load<TextAsset>(ResourceDir + "catalog");
            all = ta != null ? JsonUtility.FromJson<Catalog>(ta.text)?.mechanics ?? Array.Empty<Mechanic>() : Array.Empty<Mechanic>();
            if (ta == null) Debug.LogError("MechanicIcons: Resources/" + ResourceDir + "catalog.json is missing");
            foreach (var m in all) byId[m.id] = m;
        }

        public static Mechanic Get(string id) { Load(); return id != null && byId.TryGetValue(id, out var m) ? m : null; }

        public static Texture2D Icon(Mechanic m)
        {
            if (m == null) return null;
            if (!textures.TryGetValue(m.id, out var t)) textures[m.id] = t = Resources.Load<Texture2D>(ResourceDir + m.icon);
            return t;
        }
        public static Texture2D Icon(string id) => Icon(Get(id));

        // for uGUI / sprite renderers; IMGUI draws the Texture2D
        public static Sprite IconSprite(string id)
        {
            var m = Get(id); var t = Icon(m); if (t == null) return null;
            if (!sprites.TryGetValue(m.id, out var s)) sprites[m.id] = s = Sprite.Create(t, new Rect(0, 0, t.width, t.height), new Vector2(0.5f, 0.5f), 100);
            return s;
        }
    }
}
