using UnityEngine;

namespace LastBell.Run
{
    // The Ultimate's own difficulty table (DIFFICULTY-PROPOSAL-2026-10-27.md, accepted by Chzy18 27 Oct): its numbers as
    // designed are Hard (gpt/08 §5, the exception to "Medium is the baseline"); Easy and Medium are scaled down from them.
    // Every level keeps every mechanic and one-shot; solo still gets only the HP fit and the push timers.
    public sealed partial class BellkeeperEncounter
    {
        //                                          Easy   Medium Hard
        public static float[] UltDamageTaken =     { 0.55f, 0.75f, 1f };   // boss damage to players
        public static float[] UltHpScale =         { 0.8f,  0.9f,  1f };   // the bar, after the party fit
        public static float[] UltTimerScale =      { 1.2f,  1.1f,  1f };   // push timers
        public static float[] UltStandSeconds =    { 12f,   11f,   10f };  // P5's Last Stand before the bell falls
        public static float[] UltWarnLead =        { 0.3f,  0.15f, 0f };   // lethal warnings show this much earlier; the hit lands when it always did (Medium 0.15 s since 5 Nov)

        static int Lv(Difficulty d) => Mathf.Clamp((int)d, 0, 2);
        // the run-loop checks' idle sparring target (TestIdle) keeps the run-wide scale their exact-damage probes expect
        public override float PlayerDamageScale(Difficulty d) => TestIdle ? base.PlayerDamageScale(d) : UltDamageTaken[Lv(d)];
        float UltHpFor(Difficulty d) => UltHpScale[Lv(d)];
        float UltTimerFor(Difficulty d) => UltTimerScale[Lv(d)];
        public float UltStandFor => UltStandSeconds[Lv(W.Difficulty)];
        public float UltWarnLeadFor => UltWarnLead[Lv(W.Difficulty)];
    }
}
