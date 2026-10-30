using System.Collections;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace LastBell.Run
{
    // Mechanic icons (MechanicIcons): every approved friendly and hostile mechanic ID loads its 256 px icon (mipmapped)
    // with a name; the code constants match the catalog; in a live fight, player marks and a tagged hazard warning draw
    // the icon with its name beside it (screenshots, plus the 28-icon gallery at HUD size).
    public sealed partial class RunAutoTest
    {
        // the 28 IDs Chzy18 approved on 2026-10-24 (ability-icons 54f7d22, APPROVAL.md), written out here so the catalog
        // can't drift unnoticed
        static readonly string[] ApprovedFriendlyMechanicIds =
        {
            "order_i", "order_ii", "order_iii", "order_iv", "order_v", "order_vi", "order_vii", "order_viii",
            "team_brass", "team_cobalt", "carried_mark", "tether_partner",
        };
        static readonly string[] ApprovedHostileMechanicIds =
        {
            "spread", "soak_tower", "line_stack", "stretch_tether", "pass_the_mark", "movement_stop", "movement_move", "one_side_cleave",
            "player_mirror", "pull", "shrinking_edge", "cone", "ring", "lane", "delayed_circle", "low_bar",
        };

        void MechanicIconChecks()
        {
            var approved = ApprovedFriendlyMechanicIds.Concat(ApprovedHostileMechanicIds).ToArray();
            Check("mechanic icon catalog lists exactly the 28 approved IDs",
                  MechanicIcons.All.Count == 28 && approved.All(id => MechanicIcons.Get(id) != null), $"{MechanicIcons.All.Count} listed");
            int ok = 0; var bad = new System.Collections.Generic.List<string>();
            foreach (var id in approved)
            {
                var m = MechanicIcons.Get(id); var t = MechanicIcons.Icon(id);
                bool friendly = ApprovedFriendlyMechanicIds.Contains(id);
                bool good = m != null && t != null && t.width == 256 && t.height == 256 && t.mipmapCount > 1 && !string.IsNullOrEmpty(m.name) && m.Friendly == friendly;
                if (good) ok++; else bad.Add(m == null ? id + " (not in catalog)" : t == null ? id + " (texture missing: " + m.icon + ")" : $"{id} ({t.width}x{t.height}, mips {t.mipmapCount}, {m.set})");
            }
            Check("all 28 mechanic icons load at 256 px with mipmaps, a name and the right set", ok == 28, bad.Count == 0 ? "28/28" : string.Join(", ", bad));
            var consts = typeof(MechanicIcons).GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(f => f.IsLiteral && f.FieldType == typeof(string) && f.Name != "ResourceDir").Select(f => (string)f.GetRawConstantValue()).ToList();
            Check("mechanic ID constants in code match the approved IDs", consts.Count == 28 && approved.All(consts.Contains), $"{consts.Count} constants");
            Check("order numerals map 1-8 to order_i..order_viii",
                  Enumerable.Range(1, 8).All(n => MechanicIcons.Order(n) == ApprovedFriendlyMechanicIds[n - 1]) && MechanicIcons.Order(0) == null && MechanicIcons.Order(9) == null);
            Check("each mechanic has its own icon file", MechanicIcons.All.Select(m => m.icon).Distinct().Count() == MechanicIcons.All.Count);
            Check("mechanic icon gives a sprite", MechanicIcons.IconSprite(MechanicIcons.TetherPartner) != null);
        }

        // in a live practice fight: marks over the player's head, a hazard warning tagged with a mechanic, and the gallery
        IEnumerator MechanicIconInGameChecks()
        {
            var p = d.Player;
            d.Boss.Halt(); d.World.Hazards.ClearAll();
            p.Marks.Add(MechanicIcons.OrderII); p.Marks.Add(MechanicIcons.TeamCobalt);
            var pos = p.transform.position;
            // harmless test hazards (no hit on resolve, long warning) so the boss panel shows the tagged warnings
            d.World.Hazards.Spawn(new Hazard { Source = "Test hazard", MechanicId = MechanicIcons.Spread, Shape = HazardShape.Circle, Origin = pos, Radius = 2f, LockAt = 0, ResolveAt = 900, HitOnResolve = false, Kind = HazardKind.Ground });
            d.World.Hazards.Spawn(new Hazard { Source = "Test hazard", MechanicId = MechanicIcons.SoakTower, Shape = HazardShape.Circle, Origin = pos + new Vector3(4f, 0, -2f), Radius = 1.6f, LockAt = 0, ResolveAt = 900, HitOnResolve = false, Kind = HazardKind.Ground });
            yield return WaitAlive(0.4f);
            yield return Shot("mechanic_icons_marks_and_warnings");
            RunHud.ShowMechanicGallery = true;
            yield return Shot("mechanic_icons_gallery");
            RunHud.ShowMechanicGallery = false;
            Check("mechanic marks and tagged hazards are live for the screenshot",
                  p.Marks.Count == 2 && d.World.Hazards.Live.Count(h => MechanicIcons.Get(h.MechanicId) != null) == 2);
            d.World.Hazards.ClearAll();
            p.PrepareForEncounter(p.transform.position, 180);
            Check("player mechanic marks clear before an encounter", p.Marks.Count == 0);
            d.Boss.BeginCombat();
        }
    }
}
