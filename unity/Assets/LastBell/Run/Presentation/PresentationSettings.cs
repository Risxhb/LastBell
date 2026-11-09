using System;
using UnityEngine;

namespace LastBell.Run.Presentation
{
    // Presentation toggles (Presentation lane, 8 Nov 2026; proposal, done as recommended). All on by default. A future
    // Settings > Gameplay page binds these properties; they read PlayerPrefs once and save on set, so per-frame reads are free.
    // None of them changes gameplay: each only shows or hides feedback.
    //   -lastbellNoHitFeel   turns damage numbers, hit-stop and the hit vignette off for one launch (tests, captures)
    //   -lastbellNoBossMoments  turns the stronger intro and the defeat moment off for one launch
    public static class PresentationSettings
    {
        const string Prefix = "LastBell.Presentation.";
        static bool Arg(string a) => Array.Exists(Environment.GetCommandLineArgs(), x => x.Equals(a, StringComparison.OrdinalIgnoreCase));
        static readonly bool noHitFeel = Arg("-lastbellNoHitFeel"), noMoments = Arg("-lastbellNoBossMoments");

        static int numbers = -1, hitStop = -1, hitFlash = -1, moments = -1;
        static bool Get(ref int cache, string key, bool off)
        {
            if (cache < 0) cache = PlayerPrefs.GetInt(Prefix + key, 1) != 0 ? 1 : 0;
            return cache == 1 && !off;
        }
        static void Set(ref int cache, string key, bool on)
        {
            cache = on ? 1 : 0; PlayerPrefs.SetInt(Prefix + key, cache); PlayerPrefs.Save();
        }

        // floating numbers for damage dealt to the boss
        public static bool DamageNumbers { get => Get(ref numbers, "DamageNumbers", noHitFeel || TestOff); set => Set(ref numbers, "DamageNumbers", value); }
        // the few-frame animation freeze on big hits on the boss and when a player goes down
        public static bool HitStop { get => Get(ref hitStop, "HitStop", noHitFeel || TestOff); set => Set(ref hitStop, "HitStop", value); }
        // the coral edge vignette when your character is hit, and the grey-out while you are down
        public static bool HitFlash { get => Get(ref hitFlash, "HitFlash", noHitFeel || TestOff); set => Set(ref hitFlash, "HitFlash", value); }
        // the stronger intro card, the entrance framing and the defeat moment (slow motion, flash, "Defeated" card)
        public static bool BossMoments { get => Get(ref moments, "BossMoments", noMoments || MomentsTestOff); set => Set(ref moments, "BossMoments", value); }

        // autotest only: turn features off without touching the saved choice (RunAutoTest.Presentation)
        public static bool TestOff, MomentsTestOff;
    }
}
