using System.Collections;
using System.Linq;
using UnityEngine;

namespace LastBell.Run
{
    // Boss attack icons (BossAttackIcons): every approved v2 attack ID resolves to its 256 px icon, name and boss; known
    // hazard Source spellings from the boss branches resolve to the right attack; the death recap records an attack ID per
    // hit and, when the killing blow is a catalogued attack, shows its icon (screenshots with the details tooltip pinned).
    public sealed partial class RunAutoTest
    {
        // the Aurelion IDs among the 20 Chzy18 approved on 2026-10-23 (ability-icons 0d0555a), written out here so the catalog
        // can't drift unnoticed. Crucivar and Sylvara icons stay local until those bosses ship; normal Bellkeeper v2 is retired.
        static readonly string[] ApprovedAttackIds =
        {
            "aurelion.meridian_lances", "aurelion.starfall_mirror", "aurelion.noon_shadow", "aurelion.orrery_closing", "aurelion.hour_hands",
        };

        void BossAttackIconChecks()
        {
            Check("boss attack icon catalog lists exactly the 5 approved Aurelion IDs",
                  BossAttackIcons.All.Count == 5 && ApprovedAttackIds.All(id => BossAttackIcons.Get(id) != null), $"{BossAttackIcons.All.Count} listed");
            foreach (var id in ApprovedAttackIds)
            {
                var a = BossAttackIcons.Get(id); var t = BossAttackIcons.Icon(id);
                Check($"boss attack icon resolves: {id}",
                      a != null && t != null && t.width == 256 && t.height == 256 && !string.IsNullOrEmpty(a.name) && !string.IsNullOrEmpty(a.bossName) && id.StartsWith(a.boss + "."),
                      a == null ? "not in catalog" : t == null ? "texture missing: " + a.icon : $"{a.Label} · {t.width}x{t.height}");
            }
            Check("boss attack icon gives a sprite", BossAttackIcons.IconSprite("aurelion.hour_hands") != null);
            // Source spellings the boss branches use today (ability Name plus suffixes, sub-hazard names)
            var cases = new (string source, string boss, string id)[]
            {
                ("Starfall Mirror II (reflection)", "aurelion", "aurelion.starfall_mirror"), ("Orrery Closing rim", "aurelion", "aurelion.orrery_closing"),
                ("Meridian Lances", "aurelion", "aurelion.meridian_lances"), ("Hour Hands", "aurelion", "aurelion.hour_hands"),
                ("Bell Sweep", "bellkeeper", null), ("Leaning Bell", "bellkeeper", null), ("Meridian Lances", "sylvara", null),
            };
            var wrong = cases.Where(c => BossAttackIcons.Resolve(null, c.source, c.boss)?.id != c.id).Select(c => $"{c.boss}/{c.source}").ToList();
            Check("hazard sources resolve to the right boss attack (or none)", wrong.Count == 0, wrong.Count == 0 ? $"{cases.Length} cases" : string.Join(", ", wrong));
            Check("an explicit Hazard.AttackId wins over the source text",
                  BossAttackIcons.Resolve(new Hazard { Source = "Meridian Lances", AttackId = "aurelion.hour_hands" }, null)?.id == "aurelion.hour_hands");
        }

        // after a defeat, on the Results screen
        IEnumerator DeathRecapIconChecks(string label)
        {
            var w = d.World; int n = w.TakenLog.Count;
            Check($"{label}: death recap has an attack ID slot per hit", n > 0 && w.TakenAttackIds.Count == n, $"{w.TakenAttackIds.Count}/{n}");
            var expected = BossAttackIcons.Resolve(null, d.DefeatCause, BossAttackIcons.BossKey(d.Boss));
            var got = n > 0 ? BossAttackIcons.Get(w.TakenAttackIds[n - 1]) : null;
            if (expected != null)
                Check($"{label}: killing blow shows its boss attack icon", got == expected && BossAttackIcons.Icon(got) != null, $"{d.DefeatCause} -> {got?.Label ?? "none"}");
            else Note($"{label}: killing blow '{d.DefeatCause}' is not a catalogued v2 attack, shown as text");
            RunHud.PinRecapTooltip = true;
            yield return Shot(label.Replace(' ', '_') + "_recap_tooltip");
            RunHud.PinRecapTooltip = false;
        }
    }
}
