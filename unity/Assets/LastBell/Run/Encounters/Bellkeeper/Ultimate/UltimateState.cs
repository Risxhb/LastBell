using System.Collections.Generic;
using LastBell.VFX;
using UnityEngine;

namespace LastBell.Run
{
    // Shared systems of the Bellkeeper Ultimate, owned by the encounter (BellkeeperEncounter.Ult) and used by its mechanics:
    // personal notes with an ivory glyph, footprint outlines, and wipes. Reset() on start, halt, phase change and defeat.
    // (The v1 Ultimate plan's stations, fissure, Stilled, Cracked Hour, first-body lanes and ledger recorder were deleted
    // with its mechanics, Chzy18 1 Nov.)
    public sealed class UltimateState
    {
        public const int Lethal = 999;   // one-shot (ULTIMATE.md §2)

        readonly BellkeeperEncounter boss;
        CombatWorld W => boss.World;
        public UltimateState(BellkeeperEncounter b) { boss = b; }

        // ---------------- personal status ----------------
        public sealed class Status
        {
            public string Note;            // a mechanic's own marker over the head, null = none
            public string DebtNote;        // Chime Debts: the debt timer (drawn by the relay, EscapementRelay.DrawGui)
            public Transform Glyph;        // the world glyph above the head (built on demand)
        }
        readonly Dictionary<PlayerController, Status> status = new Dictionary<PlayerController, Status>();
        public Status Of(PlayerController p)
        {
            if (!status.TryGetValue(p, out var s)) status[p] = s = new Status();
            return s;
        }
        public IEnumerable<KeyValuePair<PlayerController, Status>> All => status;

        // ---------------- wipes ----------------
        public string WipeCause { get; private set; }
        public int Wipes { get; private set; }
        // Ends the attempt for the whole party (push timer, enrage, a mechanic that wipes): a lethal hit on every standing
        // player. The director then sees nobody standing and goes to Downed, as for any defeat.
        // attackId: the mechanic whose failure wiped (its icon in the death recap); null for global causes (the push timer), shown as text
        public void Wipe(string cause, string attackId = null)
        {
            WipeCause = cause; Wipes++;
            boss.PlanLog.Add((W.Tick, "wipe " + cause));
            var h = new Hazard { Source = cause, AttackId = attackId, Shape = HazardShape.Circle, Kind = HazardKind.Ground, Radius = 99, Damage = Lethal * 10, Knockback = 0, Stun = 0 };
            foreach (var p in W.Players.ToArray()) if (!p.Down) p.ReceiveHit(h);
        }

        // ---------------- footprints ----------------
        // A hazard's shape, stored as it is now; Outline() draws it as an ivory dashed memory outline (never coral).
        public sealed class Footprint
        {
            public string Source; public int Tick; public HazardShape Shape;
            public Vector3 Origin; public float Yaw, Radius, Inner, HalfAngle, Length, Width;
            public static Footprint Of(Hazard h, int tick) => new Footprint
            {
                Source = h.Source, Tick = tick, Shape = h.Shape, Origin = h.Origin, Yaw = h.Yaw,
                Radius = h.Radius, Inner = h.Inner, HalfAngle = h.HalfAngle, Length = h.Length, Width = h.Width,
            };
        }
        public BellkeeperFx.Telegraph Outline(Footprint f)
        {
            var fx = W.BossFx;
            var t = f.Shape switch
            {
                HazardShape.Cone => fx.Cone(f.Radius, f.HalfAngle),
                HazardShape.Ring => fx.Ring(f.Inner, f.Radius),
                HazardShape.Lane => fx.Lane(f.Length, f.Width),
                _ => fx.Circle(f.Radius),
            };
            t.Memory(); t.Place(f.Origin, f.Yaw);
            return t;
        }

        // ---------------- reset ----------------
        public void Reset()
        {
            foreach (var kv in status) if (kv.Value.Glyph) Object.Destroy(kv.Value.Glyph.gameObject);
            status.Clear();
        }
        public void ResetAll() { Reset(); WipeCause = null; }

        // ---------------- glyphs (stand-in: an ivory disc, the note on the HUD) ----------------
        static Material ivory;
        public static readonly string[] Roman = { "", "I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX", "X", "XI", "XII", "XIII" };
        // Called every frame by the encounter: places each player's glyph and returns the HUD text over their head.
        public string GlyphText(PlayerController p, int tick)
        {
            if (!status.TryGetValue(p, out var s)) return null;
            bool any = s.Note != null;
            if (s.Glyph) s.Glyph.gameObject.SetActive(any && !p.Down);
            if (!any || p.Down) return null;
            if (!s.Glyph) s.Glyph = BuildGlyph(p);
            s.Glyph.position = p.transform.position + Vector3.up * 2.9f; s.Glyph.rotation = Quaternion.identity;
            return s.Note;
        }
        static Transform BuildGlyph(PlayerController p)
        {
            if (ivory == null) ivory = Prim.Material(new Color(0.95f, 0.92f, 0.84f), 0.6f);
            var root = new GameObject("Ultimate glyph · " + p.name).transform;
            root.SetParent(p.World.Root, false);
            var go = new GameObject("face"); go.transform.SetParent(root, false); go.transform.localScale = new Vector3(0.9f, 0.02f, 0.9f);
            go.AddComponent<MeshFilter>().sharedMesh = Prim.MeshOf(PrimitiveKind.Cylinder);
            var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = ivory; mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return root;
        }
    }

    // Shared hazard timing for the Ultimate's mechanics.
    public static class UltShapes
    {
        // ticks for ResolveAt so a hazard spawned now resolves `ticksFromNow` ticks later (a hazard resolves on its spawn tick
        // + ResolveAt − 1, because it ages on its spawn tick)
        public static int ResolveIn(int ticksFromNow) => Mathf.Max(1, ticksFromNow + 1);
    }
}
