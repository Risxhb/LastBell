using System;
using LastBell.Run;
using UnityEngine;

namespace LastBell.Menu
{
    // One selection shared by the normal selection screen and alternate presentation.
    public static class SavedHeroSelection
    {
        const string Key = "LastBell.Menu.Hero";
        public static string Current
        {
            get { var hero = PlayerPrefs.GetString(Key, TestRushSetup.SavedHero); return Array.IndexOf(RunDirector.Classes, hero) >= 0 ? hero : RunDirector.Classes[0]; }
            set { if (Array.IndexOf(RunDirector.Classes, value) < 0) return; TestRushSetup.SavedHero = value; PlayerPrefs.SetString(Key, value); PlayerPrefs.Save(); }
        }
    }
}
