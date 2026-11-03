using System;
using System.Collections.Generic;
using UnityEngine;

namespace LastBell.Run
{
    // One Crucivar phase (bosses/2-brass-foundry-crucivar/abilities-v4/). Each phase is one class in Abilities/ that spawns
    // its hazards through W.Hazards and yields ticks like an encounter script; CrucivarEncounter.Script runs them in turn.
    // The older sets v1-v3 were retired on 11-01 (Chzy18: "retire everything that isn't current"); they live in git history.
    public abstract class CrucivarAbility
    {
        public abstract string Id { get; }
        protected CrucivarEncounter Boss { get; private set; }
        protected CombatWorld W { get; private set; }
        public void Bind(CrucivarEncounter boss, CombatWorld world) { Boss = boss; W = world; }

        // Easy stretches every warning by 1.25; recoveries are not warnings and stay as authored.
        public float WarnScale => W.Difficulty == Difficulty.Easy ? 1.25f : 1f;
        protected int T(float seconds) => Sim.Ticks(seconds * WarnScale);
        protected bool Hard => W.Difficulty == Difficulty.Hard;
        protected bool Easy => W.Difficulty == Difficulty.Easy;
        protected List<PlayerController> Standing() => W.Players.FindAll(p => !p.Down);
        // players who deal damage, for HP scaling (BossEncounter.Spawn's rule): passive menu bots only do the mechanics
        protected int Fighters() { int f = 0; foreach (var p in W.Players) if (!(p.Input is AllyBot) || AllyBot.Attacks) f++; return Mathf.Max(1, f); }
        protected static Vector3 Flat(Vector3 v) => Sim.Flat(v);

        public static float Rim => CombatWorld.PlayerBound;   // 13.7 m in the Brass Foundry
        // the farthest floor point from the centre: Test Rush can put him on a square map (the key-art dais), whose corners
        // lie Rim x 1.41 out, so anything meant to reach the wall sizes to this, not Rim (no safe corners)
        public static float Far => CombatWorld.SquareBound ? Rim * 1.41422f : Rim;
        // distance along dir from o to the floor's edge plus the hit margin (circle or square floor); 0 if o is outside
        public static float WallDist(Vector3 o, Vector3 dir)
        {
            o = Sim.Flat(o); dir = Sim.Flat(dir).normalized;
            float r = Rim + Hazard.Margin;
            if (!CombatWorld.SquareBound) return RayToCircle(o, dir, r, out float t) ? t : 0;
            float tx = Mathf.Abs(dir.x) < 1e-5f ? float.MaxValue : ((dir.x > 0 ? r : -r) - o.x) / dir.x;
            float tz = Mathf.Abs(dir.z) < 1e-5f ? float.MaxValue : ((dir.z > 0 ? r : -r) - o.z) / dir.z;
            return Mathf.Max(0, Mathf.Min(tx, tz));
        }
        // farthest positive distance along dir from o to the circle |p| = r
        public static bool RayToCircle(Vector3 o, Vector3 dir, float r, out float t)
        {
            float b = Vector3.Dot(o, dir), disc = b * b - o.sqrMagnitude + r * r;
            t = disc < 0 ? 0 : -b + Mathf.Sqrt(disc);
            return disc >= 0 && t > 0;
        }
    }

    // Launch switches (practice and watch mode only): see CrucivarWatch for -crucivarWatch and its options.
    public static class CrucivarArgs
    {
        public static string Arg(string name)
        {
            var a = Environment.GetCommandLineArgs();
            int i = Array.FindIndex(a, x => x.Equals(name, StringComparison.OrdinalIgnoreCase));
            return i >= 0 && i + 1 < a.Length ? a[i + 1] : null;
        }
    }
}
