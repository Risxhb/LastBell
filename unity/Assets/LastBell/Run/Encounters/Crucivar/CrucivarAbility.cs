using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LastBell.Run
{
    // One Crucivar ability-set-v2 ability (bosses/2-brass-foundry-crucivar/abilities-v2/ABILITIES.md). Each ability is one
    // class in Abilities/ that spawns its hazards through W.Hazards and yields ticks like an encounter script. The phase
    // plan (CrucivarEncounter.V2Script) decides when it runs, walks the boss to the anvil and plays the recovery after;
    // an ability's Run returns when its last damaging hazard has resolved (standing pools may outlive it).
    public abstract class CrucivarAbility
    {
        public abstract string Id { get; }
        protected CrucivarEncounter Boss { get; private set; }
        protected CombatWorld W { get; private set; }
        public void Bind(CrucivarEncounter boss, CombatWorld world) { Boss = boss; W = world; }

        // Easy stretches every warning by 1.25 (ABILITIES.md "Easy"); recoveries are not warnings and stay as authored.
        public float WarnScale => W.Difficulty == Difficulty.Easy ? 1.25f : 1f;
        protected int T(float seconds) => Sim.Ticks(seconds * WarnScale);
        protected bool Hard => W.Difficulty == Difficulty.Hard;
        protected bool Easy => W.Difficulty == Difficulty.Easy;
        // ability set v3 (ABILITIES-v3.md): every ability reads its numbers per cast, so tests can switch sets live
        protected static bool V3 => CrucivarArgs.V3;
        protected List<PlayerController> Standing() => W.Players.FindAll(p => !p.Down);
        protected static Vector3 Flat(Vector3 v) => Sim.Flat(v);

        public static float Rim => CombatWorld.PlayerBound;   // 13.7 m in the Brass Foundry
        // the four die bays, 6 m out on the diagonals, in the tie-break order NE, SE, SW, NW. The run layout puts the
        // entry to the south at +z, so north is -z and east is +x.
        public const float BayRadius = 6f;
        public static readonly string[] BayNames = { "NE", "SE", "SW", "NW" };
        static readonly Vector3[] BayDirs = { new Vector3(1, 0, -1), new Vector3(1, 0, 1), new Vector3(-1, 0, 1), new Vector3(-1, 0, -1) };
        public static Vector3 Bay(int i) => BayDirs[((i % 4) + 4) % 4].normalized * BayRadius;
    }

    // Mould Press's resolve, for Second Press: per tower its centre, numeral, bay index and the players inside at resolve.
    public sealed class MouldResult
    {
        // arrays in resolve order; Sequential = the solo version (tower I, then II), where Second Press takes the last one
        public Vector3[] Centres; public int[] Numerals, Bays, Counts;
        public bool Sequential;
    }

    // Launch switches (practice only):
    //   -lastbellAbilitySet v1|v2|v3   v3 (default) is the "much harder" set (ABILITIES-v3.md); v2 the first new set; v1 the
    //                               original five attacks, kept for comparison
    //   -lastbellAbility <id>       fire one v2 ability on loop, e.g. crucivar.mould_press
    public static class CrucivarArgs
    {
        public static string Arg(string name)
        {
            var a = Environment.GetCommandLineArgs();
            int i = Array.FindIndex(a, x => x.Equals(name, StringComparison.OrdinalIgnoreCase));
            return i >= 0 && i + 1 < a.Length ? a[i + 1] : null;
        }
        public static string SetOverride;   // tests force a set without a command line
        public static string Set => (SetOverride ?? Arg("-lastbellAbilitySet") ?? "v3").ToLowerInvariant();
        public static bool V1 => Set == "v1";
        public static bool V3 => Set != "v1" && Set != "v2";
        public static string Solo => Arg("-lastbellAbility");
        public static readonly string[] Ids = { "crucivar.mould_press", "crucivar.gear_chain", "crucivar.bellows_draw", "crucivar.great_casting", "crucivar.second_press", "crucivar.anvil_sweep", "crucivar.slag_spit" };
        public static bool Known(string id) => id != null && Ids.Contains(id);
    }
}
