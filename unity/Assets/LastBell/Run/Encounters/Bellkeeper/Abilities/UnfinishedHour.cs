using System.Collections.Generic;
using LastBell.VFX;
using UnityEngine;

namespace LastBell.Run
{
    // The Unfinished Hour (`bellkeeper.unfinished_hour`), Bellkeeper ability set v2: proposal, Hard only (the one named
    // extra mechanic on Hard). Brief: bosses/4-shattered-hour-bellkeeper/abilities-v2/05-unfinished-hour.md.
    //
    // The Bellkeeper remembers the half each Leaning Bell struck. Leaning Bell hands its locked half to
    // Boss.RememberLean when it resolves; on Hard from phase 2 the encounter launches this ability on the next tick when
    // there is a previous half, and Cast() replays Boss.PreviousLean once at its original world position (Strike, 40 at
    // Medium, the same map-adaptive warning counted from the current Leaning Bell's resolve). The learned answer is
    // "cross, then cross back". Presentation: the remembered half as a dashed, unfilled outline between Leaning Bells
    // (never damaging), turning into the coral warning with a porcelain after-image of the bell shell that tilts over it
    // and swings down on the resolve. One memory at a time; the encounter forgets it on phase change, defeat, halt,
    // practice restart and retry (LeansForgotten).
    public sealed class UnfinishedHourAbility : BellkeeperAbility
    {
        public override string Id => BellkeeperAbilities.UnfinishedHour;
        public override string Name => "The Unfinished Hour";

        public const int Damage = 40;                       // Medium; Hard's ×1.3 (52) is applied when the hit lands
        public const float Knock = 1.5f;                    // away from the arena centre (the cone's origin)
        public const int StunTicks = 15;
        public const int RecoveryTicks = 30;                // the after-image swings down and fades; nothing damaging
        public const float MinWarning = 3.5f, Reaction = 0.6f, EscapeSpeed = 4.2f, DashLength = 3f, DashTime = 0.2f;
        // Worst crossing of a line through the centre = the floor's farthest point from the centre plus the player margin
        // and the detour round the boss body: 13.43 m on the Throne (circle 13.0) and 18.04 m on the test map (square
        // 12.5) in tools/encounter-escape/bellkeeper-v2-escape-check.ps1; +0.43 m reproduces both windows.
        const float CrossingExtra = 0.43f;

        // Leaning Bell's map-adaptive rule (01 brief): max(3.5 s, reaction + walk-and-dash at 4.2 m/s over the worst
        // crossing), rounded up to 0.1 s. Throne 3.5 s (210 ticks), test map 4.4 s (264 ticks).
        public static float WarningSeconds()
        {
            float far = CombatWorld.SquareBound ? CombatWorld.PlayerBound * Mathf.Sqrt(2) : CombatWorld.PlayerBound;
            float d = far + CrossingExtra;
            float need = Reaction + (d <= DashLength ? DashTime * d / DashLength : DashTime + (d - DashLength) / EscapeSpeed);
            return Mathf.Max(MinWarning, Mathf.Ceil(need * 10 - 1e-3f) / 10);
        }
        public static int WarningTicks() => Sim.Ticks(WarningSeconds());

        // tests
        public Hazard Live { get; private set; }            // the replayed half while it warns
        public Hazard MemoryShown { get; private set; }     // the half drawn as the dashed outline, if any
        public bool AfterImageShown => afterImage != null;
        public int Resolves { get; private set; }
        public int LastResolveTick { get; private set; } = -1;
        public bool PracticeHold;                           // tests: Practice() idles so a test can feed lean halves itself

        BellkeeperFx.Telegraph memoryMark;
        Vector3 lastLeanAt, prevLeanAt;                     // where the boss stood for each remembered lean
        int lastLeanPhase, prevLeanPhase;
        Transform afterImage, tilt; Renderer rim;
        readonly List<Material> mats = new List<Material>();

        const float AfterImageOffset = 3f;
        static readonly Color Porcelain = new Color(0.92f, 0.89f, 0.81f), Coral = new Color(1.0f, 0.42f, 0.32f);

        protected override void OnBind()
        {
            Boss.LeanRemembered += OnLeanRemembered;
            Boss.LeansForgotten += HideMemory;
        }

        // Only Hard replays, and only from phase 2 (phase 1 learns nothing); practice of this move replays at once.
        bool WillReplay => Level == Difficulty.Hard && (Boss.Phase >= 2 || Boss.PracticeAbility == this);

        void OnLeanRemembered(Hazard half)
        {
            prevLeanAt = lastLeanAt; prevLeanPhase = lastLeanPhase;
            lastLeanAt = Boss.transform.position; lastLeanPhase = Boss.Phase;
            // an echo of the previous half launches next tick: its outline turns into the live warning there, and the
            // half just struck is drawn once the echo is over
            if (Boss.EchoArmed && Boss.PreviousLean != null) return;
            ShowMemory(half);
        }

        void ShowMemory(Hazard half)
        {
            HideMemory();
            if (half == null || !WillReplay) return;
            var fx = W.BossFx;
            memoryMark = half.Shape switch
            {
                HazardShape.Cone => fx.Cone(half.Radius, half.HalfAngle),
                HazardShape.Ring => fx.Ring(half.Inner, half.Radius),
                HazardShape.Lane => fx.Lane(half.Length, half.Width),
                _ => fx.Circle(half.Radius),
            };
            memoryMark.Memory(); memoryMark.Place(half.Origin, half.Yaw);
            MemoryShown = half;
        }
        void HideMemory() { memoryMark?.Dismiss(); memoryMark = null; MemoryShown = null; }

        public override IEnumerable<int> Cast()
        {
            var src = Boss.PreviousLean;
            // Hard only; nothing remembered, or a memory from another phase (the encounter forgets those): no echo
            if (Level != Difficulty.Hard || src == null || prevLeanPhase != Boss.Phase)
            {
                ShowMemory(Boss.LastLean);
                yield break;
            }
            Boss.ShowAttack(Name);
            HideMemory();
            int warn = WarningTicks();
            // a new instance at the remembered world position and orientation (the boss may have turned or moved since)
            var h = Spawn(new Hazard
            {
                Source = Name, Shape = src.Shape, Kind = HazardKind.Strike, Origin = src.Origin, Yaw = src.Yaw,
                Radius = src.Radius, Inner = src.Inner, HalfAngle = src.HalfAngle, Length = src.Length, Width = src.Width,
                LockAt = 0, ResolveAt = warn, Damage = Damage, Knockback = Knock, Stun = StunTicks,
            });
            h.OnResolve = hz =>
            {
                if (hz.Shape == HazardShape.Cone) W.BossFx.SweepResolve(hz.Origin, hz.Yaw, hz.Radius, hz.HalfAngle);
                Resolves++; LastResolveTick = W.Tick;
            };
            Live = h;
            MakeAfterImage(prevLeanAt, src.Yaw);

            // the hazard resolves in the hazard step of tick warn - 1 (the boss steps first in each tick)
            for (int i = 0; i < warn - 1; i++) { PoseWarning((i + 1f) / warn); yield return 1; }
            for (int i = 0; i < RecoveryTicks; i++) { PoseSwing(i / (float)RecoveryTicks); yield return 1; }

            Live = null;
            DestroyAfterImage();
            ShowMemory(Boss.LastLean);   // the half the current Leaning Bell struck is the next memory
        }

        // -lastbellAbility unfinished_hour: two Leaning Bells, the second armed, so the encounter launches the echo on the
        // tick after it resolves, as in the phase plan. Needs -lastbellDifficulty hard (on Easy and Medium the leans run
        // and nothing replays, as in the fight).
        public override IEnumerable<int> Practice()
        {
            if (PracticeHold) { while (PracticeHold) yield return 1; yield break; }
            if (Level != Difficulty.Hard)
                Debug.LogWarning("Bellkeeper: The Unfinished Hour is Hard only; practise it with -lastbellDifficulty hard (Leaning Bells only on " + Level + ")");
            var lean = Boss.Ability(BellkeeperAbilities.LeaningBell);
            foreach (var w in lean.Setup()) yield return w;
            Boss.Launch(lean);
            while (lean.Running) yield return 1;
            foreach (var w in lean.Setup()) yield return w;
            Boss.EchoArmed = true;
            Boss.Launch(lean);
            while (lean.Running || Running) yield return 1;
            yield return 1;                               // in case the lean ended on its resolve tick: the echo starts now
            while (Running) yield return 1;
            Boss.EchoArmed = false;
            if (Level != Difficulty.Hard) Boss.ShowAttack(Name + ": Hard only");
            yield return 120;
        }

        public override void Cancel()
        {
            base.Cancel();
            Live = null;
            HideMemory();
            DestroyAfterImage();
        }

        // ---------------- porcelain after-image (stand-in: code-built bell shell, crown and coral rim) ----------------

        void MakeAfterImage(Vector3 at, float yaw)
        {
            DestroyAfterImage();
            afterImage = new GameObject("Unfinished Hour after-image").transform;
            at = Sim.Flat(at) + Sim.Dir(yaw) * AfterImageOffset;   // over the remembered half, clear of the boss's own body
            afterImage.SetPositionAndRotation(at, Quaternion.Euler(0, yaw, 0));
            tilt = new GameObject("tilt").transform; tilt.SetParent(afterImage, false);
            Keep(Prim.Make(PrimitiveKind.Cone, tilt, new Vector3(0, 1.7f, 0), new Vector3(3.4f, 3.4f, 3.4f), Porcelain, 0.35f));
            Keep(Prim.Make(PrimitiveKind.Sphere, tilt, new Vector3(0, 3.35f, 0), new Vector3(1.1f, 0.85f, 1.1f), Porcelain, 0.35f));
            rim = Keep(Prim.Make(PrimitiveKind.Cylinder, tilt, new Vector3(0, 0.1f, 0), new Vector3(3.5f, 0.09f, 3.5f), Coral, 1.2f));
            PoseWarning(0);
        }
        Renderer Keep(Transform t)
        {
            var r = t.GetComponent<Renderer>(); r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mats.Add(r.sharedMaterial); return r;
        }
        static float Ease(float u) => 1 - (1 - u) * (1 - u);

        // heaved over toward the remembered half (the shell tilts the way the old lean did), the rim brightening
        void PoseWarning(float u)
        {
            if (!tilt) return;
            float wobble = Mathf.Sin(u * Mathf.PI * 6) * 1.5f * (1 - u);
            tilt.localRotation = Quaternion.Euler(8 + 27 * Ease(u) + wobble, 0, 0);
            float s = Mathf.Lerp(0.85f, 1f, Ease(Mathf.Clamp01(u * 4)));
            afterImage.localScale = new Vector3(s, s, s);
            if (rim) rim.sharedMaterial.SetColor("_EmissionColor", Coral * (1.2f + 2.4f * u));
        }
        // on the resolve: swings down onto the half, then sinks and shrinks away
        void PoseSwing(float u)
        {
            if (!tilt) return;
            tilt.localRotation = Quaternion.Euler(Mathf.Lerp(35, 78, Ease(Mathf.Clamp01(u * 4))), 0, 0);
            float s = Mathf.Lerp(1f, 0.2f, Mathf.Clamp01((u - 0.3f) / 0.7f));
            afterImage.localScale = new Vector3(s, s, s);
            afterImage.position = new Vector3(afterImage.position.x, -0.6f * u, afterImage.position.z);
            if (rim) rim.sharedMaterial.SetColor("_EmissionColor", Coral * Mathf.Lerp(3.6f, 0.5f, u));
        }
        void DestroyAfterImage()
        {
            if (afterImage) Object.Destroy(afterImage.gameObject);
            foreach (var m in mats) if (m) Object.Destroy(m);
            mats.Clear(); afterImage = null; tilt = null; rim = null;
        }
    }
}
