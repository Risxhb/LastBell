using System;
using UnityEngine;

namespace LastBell.Run
{
    // Area-specific minibosses. When an area is selected, its authored stand-ins replace the generic ones in their run
    // slot (0..2); otherwise the run keeps the generic Dial Warden / Pendulum Sentinel / Chimebound Oracle. Pick the area
    // with  LastBell.exe -lastbellArea verdant-chimes  or RunRoster.Override (the autotest uses it).
    public static class RunRoster
    {
        sealed class Entry
        {
            public string Area, Name; public int Slot;
            public Func<CombatWorld, Vector3, BossEncounter> Spawn;
        }

        static readonly Entry[] entries =
        {
            new Entry { Area = "verdant-chimes", Slot = 1, Name = "Silt Carillon", Spawn = (w, at) => BossEncounter.Spawn<SiltCarillon>(w, at, 0) },
        };

        public static string Override;

        public static string Area
        {
            get
            {
                if (!string.IsNullOrEmpty(Override)) return Override;
                var args = Environment.GetCommandLineArgs();
                int i = Array.FindIndex(args, a => a.Equals("-lastbellArea", StringComparison.OrdinalIgnoreCase));
                if (i >= 0 && i + 1 < args.Length) return args[i + 1];
                return "";   // TODO: the loaded map's id once RunMaps (map selection) lands on this branch
            }
        }

        static Entry Find(int slot)
        {
            var area = Area;
            foreach (var e in entries) if (e.Slot == slot && string.Equals(e.Area, area, StringComparison.OrdinalIgnoreCase)) return e;
            return null;
        }

        // the area's name for a run slot, or null to keep the generic one
        public static string Name(int slot) => Find(slot)?.Name;

        // the area's encounter for a run slot, or null to keep the generic one
        public static BossEncounter Spawn(int slot, CombatWorld w, Vector3 at) => Find(slot)?.Spawn(w, at);
    }
}
