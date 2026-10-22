using System;
using UnityEngine.SceneManagement;

namespace LastBell.Run
{
    // The maps a run can be played on. Each map has its own run scene: a copy of the arena's scene plus a RunDirector, made by
    // Last Bell > Run Loop > Rebuild Run Scenes (RunBuild). The menu switches maps by loading that scene. Every arena shares
    // the key-art layout convention (centre origin, entry south at +z, floor at y = 0, walkable radius >= 13.5 m), so sockets,
    // boss positions and the follow camera are the same on all of them. Each map has its own player bound (Map.Bound) so you
    // can walk up to that arena's wall: each value is the wall's inner face minus the player's 0.45 m body and a small margin.
    // Pick one from the command line with  LastBell.exe -lastbellMap <id>  (e.g. -lastbellMap brass-foundry).
    public static class RunMaps
    {
        public sealed class Map
        {
            public string Id, Name, Note, SourceScene, RunScene;
            public float Bound;     // how far the player's centre may go from the arena centre: just inside that arena's wall
            public bool Square;     // Bound is a half-width on x and z (the key-art dais) rather than a radius
        }

        public static readonly Map[] All =
        {
            new Map { Id = "test", Name = "Test Map", Note = "The Bellkeeper's Dais (key art)",
                SourceScene = "Assets/LastBell/Arenas/KeyArt/KeyArtTestArena.unity", RunScene = "Assets/LastBell/Run/RunArena.unity",
                Bound = 12.5f, Square = true },        // square parapet: posts' inner face at ±13.1 m (arena-env.js WL)
            new Map { Id = "skyward-spire", Name = "Skyward Spire", Note = "Area 1",
                SourceScene = "Assets/LastBell/Areas/Area1/SkywardSpire_Arena.unity", RunScene = "Assets/LastBell/Run/Maps/Run_SkywardSpire.unity",
                Bound = 13.9f },                       // balustrade wall at 14.65 m (inner face 14.4)
            new Map { Id = "brass-foundry", Name = "Brass Foundry", Note = "Area 2",
                SourceScene = "Assets/LastBell/Areas/Area2/BrassFoundry_Arena.unity", RunScene = "Assets/LastBell/Run/Maps/Run_BrassFoundry.unity",
                Bound = 13.7f },                       // rim wall at 14.35 m (inner face 14.2)
            new Map { Id = "verdant-chimes", Name = "Verdant Chimes", Note = "Area 3",
                SourceScene = "Assets/LastBell/Areas/Area3/VerdantChimes_Arena.unity", RunScene = "Assets/LastBell/Run/Maps/Run_VerdantChimes.unity",
                Bound = 13.5f },                       // boundary ring at 14.25 m (inner face 14.0)
            new Map { Id = "shattered-hour", Name = "Shattered Hour", Note = "Area 4 · final arena",
                SourceScene = "Assets/LastBell/Areas/Area4/ShatteredHour_FinalArena.unity", RunScene = "Assets/LastBell/Run/Maps/Run_ShatteredHour.unity",
                Bound = 13.0f },                       // boundary wall at 13.65 m (inner face 13.5)
        };

        // index of the map whose run scene is loaded, or -1 (e.g. an arena scene opened directly in the editor)
        public static int Current
        {
            get
            {
                var path = SceneManager.GetActiveScene().path;
                for (int i = 0; i < All.Length; i++) if (string.Equals(All[i].RunScene, path, StringComparison.OrdinalIgnoreCase)) return i;
                return -1;
            }
        }

        public static string CurrentName { get { int i = Current; return i >= 0 ? All[i].Name : SceneManager.GetActiveScene().name; } }

        public static int IndexOf(string id)
        {
            for (int i = 0; i < All.Length; i++) if (string.Equals(All[i].Id, id, StringComparison.OrdinalIgnoreCase)) return i;
            return -1;
        }

        public static bool InBuild(int i) => i >= 0 && i < All.Length && SceneUtility.GetBuildIndexByScenePath(All[i].RunScene) >= 0;

        public static void Load(int i) { SceneManager.LoadScene(All[i].RunScene, LoadSceneMode.Single); }

        // -lastbellMap <id> from the command line, or null
        public static string RequestedId
        {
            get
            {
                var args = Environment.GetCommandLineArgs();
                int i = Array.FindIndex(args, a => a.Equals("-lastbellMap", StringComparison.OrdinalIgnoreCase));
                return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
            }
        }
    }
}
