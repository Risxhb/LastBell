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
        // the 5 original Aurelion IDs among the 20 Chzy18 approved on 2026-10-23 (ability-icons 0d0555a), the 3 Aurelion IDs added on 2026-11-07, the 11 Sylvara v3 IDs approved on
        // 2026-11-02 and the Crucivar v4 icons accepted on 2026-11-02 (both work/attack-icons-20261102), written out here so the
        // catalog can't drift unnoticed. Normal Bellkeeper v2 is retired.
        static readonly string[] ApprovedAttackIds =
        {
            "aurelion.meridian_lances", "aurelion.starfall_mirror", "aurelion.noon_shadow", "aurelion.orrery_closing", "aurelion.hour_hands",
            "aurelion.errant_planets", "aurelion.falling_meridians", "aurelion.sunspots",   // 7 Nov (Codex attack-icons-20261106)
            "sylvara.spiral_bloom", "sylvara.seed_pods", "sylvara.waterfall_jets", "sylvara.pruning_arc", "sylvara.two_colour_choir",
            "sylvara.toll_burst", "sylvara.root_ring", "sylvara.hollow_bloom", "sylvara.bud_bloom", "sylvara.seed_storm", "sylvara.last_storm",
            "crucivar.ember_brand", "crucivar.furnace_spin", "crucivar.backdraft", "crucivar.casting_order", "crucivar.anvil_quake",
            "crucivar.quake_echo", "crucivar.foundry_uprising", "crucivar.lane_stream", "crucivar.ring_wave", "crucivar.spiral_arm",
            "crucivar.pour_flood", "crucivar.mould_towers", "crucivar.tower_slag", "crucivar.molten_crown", "crucivar.furnace_eruption",
            "crucivar.molten_overflow", "crucivar.second_press",
            "aurelion.zenith_collapse", "aurelion.meridian_towers", "aurelion.total_eclipse", "aurelion.severance", "aurelion.last_light",
            "aurelion.ecliptic_order", "aurelion.zenith_marks", "aurelion.meridian_recall",   // 8 Nov (Codex attack-icons-20261108)
        };

        void BossAttackIconChecks()
        {
            Check($"boss attack icon catalog lists exactly the {ApprovedAttackIds.Length} approved IDs",
                  BossAttackIcons.All.Count == ApprovedAttackIds.Length && ApprovedAttackIds.All(id => BossAttackIcons.Get(id) != null), $"{BossAttackIcons.All.Count} listed");
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
                ("Pour III", "crucivar", "crucivar.casting_order"), ("Ring Wave bullet", "crucivar", "crucivar.ring_wave"),
                ("Brand Pool", "crucivar", "crucivar.ember_brand"), ("Crown Rise", "crucivar", "crucivar.molten_crown"),
                ("Pour Flood", "crucivar", "crucivar.pour_flood"), ("Furnace Spin", "aurelion", null),
                ("Reunion", "aurelion", "aurelion.meridian_towers"), ("Umbra", "aurelion", "aurelion.total_eclipse"), ("Meridian Recall", "aurelion", "aurelion.meridian_recall"),
                ("Bell Sweep", "bellkeeper", null), ("Leaning Bell", "bellkeeper", null), ("Meridian Lances", "sylvara", null),
                ("Seed Pod", "sylvara", "sylvara.seed_pods"), ("Seed Burst", "sylvara", "sylvara.seed_storm"), ("Pruning Arc (back)", "sylvara", "sylvara.pruning_arc"),
                ("Cobalt Lane", "sylvara", "sylvara.two_colour_choir"), ("Choir Bud", "sylvara", null),
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
