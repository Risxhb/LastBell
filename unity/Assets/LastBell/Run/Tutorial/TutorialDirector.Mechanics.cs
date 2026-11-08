using System.Collections.Generic;
using UnityEngine;

namespace LastBell.Run
{
    // The shared hostile mechanics other bosses use, rebuilt as plain Hazards for the tutorial (no new damage code: the
    // HazardSystem resolves them exactly as it does for Aurelion, Crucivar and the minibosses).
    //   soak tower   Hazard.Soak + ShareTotal/ShareCount (Mould Press, Meridian Towers). WipeIfEmpty is left off on purpose:
    //                an empty tower would down the party and end the tutorial, so the director judges it instead.
    //   spread       a circle that follows each player and hits everyone ELSE inside (Affects), like Ember Brand
    //   burn         standing pools, BurnPerSecond (Crucivar's slag, Aurelion's Sunspot scorch)
    //   marks        a verdict only the marked player can be hit by (Affects), like Zenith Marks' SHADE / SUN
    //   tether       HazardShape.Tether with a 6-10 m band (Gear Chain), chained to a rim post
    //   following    a circle that tracks you, locks, then lands (a delayed circle)
    public sealed partial class TutorialDirector
    {
        Hazard Spawn(Hazard h) => W.Hazards.Spawn(h);
        static float Rim => CombatWorld.PlayerBound + PlayerController.Radius;

        Hazard Tower(Vector3 at, int count, int total, int resolve) => Spawn(new Hazard
        {
            Source = "Soak Tower", MechanicId = MechanicIcons.SoakTower, Shape = HazardShape.Circle, Kind = HazardKind.Ground,
            Origin = Sim.Flat(at), Radius = 2.2f, LockAt = 0, ResolveAt = resolve, Damage = total, ShareTotal = total, ShareCount = count,
            Soak = true, Knockback = 0, Stun = 0,
        });

        Hazard SpreadCircle(PlayerController owner, bool mine, int lockAt, int resolve) => Spawn(new Hazard
        {
            Source = mine ? "Spread mark · yours" : "Spread mark", MechanicId = MechanicIcons.Spread, Shape = HazardShape.Circle, Kind = HazardKind.Ground,
            Origin = Sim.Flat(owner.transform.position), Radius = 3f, LockAt = lockAt, ResolveAt = resolve, Damage = 25, Knockback = 0.5f,
            Affects = q => q != owner, Track = hz => { if (owner) hz.Origin = Sim.Flat(owner.transform.position); },
        });

        Hazard Pool(Vector3 at, int resolve) => Spawn(new Hazard
        {
            Source = "Molten Pool", Shape = HazardShape.Circle, Kind = HazardKind.Ground, Origin = Sim.Flat(at), Radius = 3f,
            LockAt = 90, ResolveAt = resolve, BurnPerSecond = 20, Knockback = 0, Stun = 0, ClipRadius = Rim,
        });

        Hazard Verdict(PlayerController who, bool shade, Vector3 centre, int resolve) => Spawn(shade
            ? new Hazard { Source = "Shade verdict", Shape = HazardShape.Ring, Kind = HazardKind.Ground, Origin = Sim.Flat(centre), Inner = ShadeR, Radius = 30f,
                LockAt = 0, ResolveAt = resolve, Damage = 30, Knockback = 0, Stun = 15, Affects = q => q == who, ClipRadius = Rim }
            : new Hazard { Source = "Sun verdict", Shape = HazardShape.Circle, Kind = HazardKind.Ground, Origin = Sim.Flat(centre), Radius = SunR,
                LockAt = 0, ResolveAt = resolve, Damage = 30, Knockback = 0, Stun = 15, Affects = q => q == who, ClipRadius = Rim });
        public const float ShadeR = 5.5f, SunR = 8.5f;

        public const float TetherMin = 6f, TetherMax = 10f;
        public Vector3? TetherPost { get; private set; }
        public float TetherLength => TetherPost.HasValue && Player ? Sim.Flat(Player.transform.position - TetherPost.Value).magnitude : 0;
        Hazard Chain(PlayerController who, Vector3 post, int resolve) => Spawn(new Hazard
        {
            Source = "Chain snap", MechanicId = MechanicIcons.StretchTether, Shape = HazardShape.Tether, Kind = HazardKind.Ground, Origin = post,
            LockAt = 0, ResolveAt = resolve, Damage = 28, Knockback = 0, Stun = 15,
            Includes = p => { if (p != who) return false; float dd = Sim.Flat(p.transform.position - post).magnitude; return dd < TetherMin || dd > TetherMax; },
        });

        Hazard Follower(PlayerController who, int lockAt, int resolve) => Spawn(new Hazard
        {
            Source = "Following circle", MechanicId = MechanicIcons.DelayedCircle, Shape = HazardShape.Circle, Kind = HazardKind.Ground,
            Origin = Sim.Flat(who.transform.position), Radius = 2.4f, LockAt = lockAt, ResolveAt = resolve, Damage = 20, Knockback = 1f,
            Track = hz => { if (who) hz.Origin = Vector3.MoveTowards(hz.Origin, Sim.Flat(who.transform.position), 7f * Sim.Dt); },
        });

        // ---- the chain drawing: a brass post at the rim and a bar to the player, green inside the band, coral outside ----
        Transform tetherRoot, tetherBar; Renderer tetherBarR; Material barOk, barBad;
        void ShowTether(Vector3 post)
        {
            TetherPost = Sim.Flat(post);
            if (!tetherRoot)
            {
                tetherRoot = new GameObject("Tutorial tether").transform; tetherRoot.SetParent(W.Root, false);
                Prim.Make(PrimitiveKind.Cylinder, tetherRoot, new Vector3(0, 1.1f, 0), new Vector3(0.55f, 1.1f, 0.55f), new Color(0.75f, 0.58f, 0.3f), 0.4f);
                Prim.Make(PrimitiveKind.Sphere, tetherRoot, new Vector3(0, 2.4f, 0), Vector3.one * 0.6f, new Color(1f, 0.8f, 0.45f), 2f);
                tetherBar = Prim.Make(PrimitiveKind.Cube, tetherRoot.parent, Vector3.zero, Vector3.one, Color.white);
                tetherBarR = tetherBar.GetComponent<Renderer>();
                barOk = Prim.Material(new Color(0.35f, 1f, 0.55f), 2.5f); barBad = Prim.Material(new Color(1f, 0.35f, 0.25f), 2.5f);
            }
            tetherRoot.gameObject.SetActive(true); tetherBar.gameObject.SetActive(true);
            tetherRoot.position = TetherPost.Value;
        }
        void HideTether()
        {
            TetherPost = null;
            if (tetherRoot) tetherRoot.gameObject.SetActive(false);
            if (tetherBar) tetherBar.gameObject.SetActive(false);
        }
        void LateUpdate()
        {
            if (!TetherPost.HasValue || !tetherBar || !Player) return;
            var a = TetherPost.Value + Vector3.up * 1.6f; var b = Player.transform.position + Vector3.up * 1.1f;
            var mid = (a + b) / 2; float len = Vector3.Distance(a, b);
            tetherBar.position = mid; tetherBar.rotation = Quaternion.LookRotation(b - a); tetherBar.localScale = new Vector3(0.12f, 0.12f, len);
            float flat = TetherLength;
            tetherBarR.sharedMaterial = flat >= TetherMin && flat <= TetherMax ? barOk : barBad;
        }
    }
}
