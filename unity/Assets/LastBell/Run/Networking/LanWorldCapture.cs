using System;
using UnityEngine;

namespace LastBell.Run.Networking
{
    // Called only by the host on the simulation thread, after a completed tick. No gameplay values are changed.
    public static class LanWorldCapture
    {
        static readonly int Progress = Shader.PropertyToID("_Progress"), Shade = Shader.PropertyToID("_Shade"), Memory = Shader.PropertyToID("_Memory"), Motif = Shader.PropertyToID("_Motif"), ClipRadius = Shader.PropertyToID("_ClipRadius"), ClipCentre = Shader.PropertyToID("_ClipCentre"), Fill = Shader.PropertyToID("_ColorFill"), Border = Shader.PropertyToID("_ColorBorder"), HoleCount = Shader.PropertyToID("_HoleCount");
        static string Clip128(string text) => text == null ? "" : text.Length <= 128 ? text : text.Substring(0, 128);
        static void Clock(LanWorldFrame.BossState b, TempoClock c)
        {
            b.Beat = c.Beat; b.BeatHaste = c.State == Tempo.Haste; b.Transitioning = c.Transitioning; b.TransitionTick = c.TransitionTick; b.StateAge = c.StateAge;
        }
        static string Clip(string text) => text == null ? "" : text.Length <= 32 ? text : text.Substring(0, 32);
        public static LanWorldFrame Capture(RunDirector director, LanHostRoster roster)
        {
            if (!director || director.World == null || roster == null || director.World.Players.Count != roster.Remote.Count + 1)
                throw new InvalidOperationException("The combat world does not match the LAN roster.");
            var world = director.World;
            BossEncounter.NetIdOf = p => { int i = world.Players.IndexOf(p); return i == 0 ? 0 : i > 0 && i - 1 < roster.Remote.Count ? roster.Remote[i - 1].Id : -1; };
            BossEncounter.NetPlayerOf = id => { if (id == 0) return world.Players.Count > 0 ? world.Players[0] : null; for (int i = 0; i < roster.Remote.Count; i++) if (roster.Remote[i].Id == id) return i + 1 < world.Players.Count ? world.Players[i + 1] : null; return null; };
            var frame = new LanWorldFrame { Tick = world.Tick, RunState = (int)director.State, StateTicks = director.StateTicks, Victory = director.Victory };
            if (director.Boss)
            {
                var b = director.Boss; var pos = SimSmoother.SimPosition(b.transform);
                frame.Boss = new LanWorldFrame.BossState { X = pos.x, Y = pos.y, Z = pos.z, Yaw = SimSmoother.SimRotation(b.transform).eulerAngles.y, Hp = b.Hp, MaxHp = b.MaxHp, Dead = b.Dead, Phase = b.PhaseName, Attack = b.Current, Extra = b.HudExtra };
                var presenter = b.GetComponentInChildren<CharacterPresenter>(true);
                if (presenter) { frame.Boss.Clip = presenter.CurrentClip; frame.Boss.Overlay = presenter.OverlayClip; frame.Boss.ClipSpeed = presenter.NetworkClipSpeed; frame.Boss.OverlaySpeed = presenter.NetworkOverlaySpeed; frame.Boss.Hidden = presenter.Hidden; }
                if (b is TempoTyrantEncounter tyrant && tyrant.Rig) { frame.Boss.PoseKind = 1; frame.Boss.Pose = (int)tyrant.Rig.Current; frame.Boss.Haste = tyrant.Rig.Haste; frame.Boss.Frenzy = tyrant.Frenzy; Clock(frame.Boss, tyrant.Clock);
                    frame.Boss.Announcement = Clip128(tyrant.Announcement);
                    foreach (var (at, n, imminent) in tyrant.Numbers) { if (frame.Boss.Numbers.Count >= LanWorldFrame.MaxNumbers) break; frame.Boss.Numbers.Add(new LanWorldFrame.Number { X = at.x, Y = at.y, Z = at.z, N = n, Imminent = imminent }); } }
                else if (b is ChimeboundOracleEncounter oracle && oracle.Rig) { frame.Boss.PoseKind = 2; frame.Boss.Pose = (int)oracle.Rig.Current; frame.Boss.Haste = oracle.Rig.Haste; frame.Boss.Frenzy = oracle.Enraged; Clock(frame.Boss, oracle.Clock); }
                b.CaptureNetProps(frame.Boss.Props);
                b.CaptureNetLook(frame.Boss);
                if (frame.Boss.Look.Count > LanWorldFrame.MaxLook) frame.Boss.Look.RemoveRange(LanWorldFrame.MaxLook, frame.Boss.Look.Count - LanWorldFrame.MaxLook);
                if (frame.Boss.Props.Count > LanWorldFrame.MaxProps) frame.Boss.Props.RemoveRange(LanWorldFrame.MaxProps, frame.Boss.Props.Count - LanWorldFrame.MaxProps);
            }
            for (int i = 0; i < world.Players.Count; i++)
            {
                var actor = world.Players[i]; var pos = SimSmoother.SimPosition(actor.transform);
                var p = new LanWorldFrame.Player { Id = i == 0 ? 0 : roster.Remote[i - 1].Id, X = pos.x, Y = pos.y, Z = pos.z, Yaw = SimSmoother.SimRotation(actor.transform).eulerAngles.y, Hp = actor.Hp, MaxHp = actor.MaxHealth, Stamina = actor.Stamina, MaxStamina = actor.MaxStaminaNow, Potions = actor.Potions, Down = actor.Down, Dead = actor.Dead, DownedTicks = actor.DownedTicks, ReviveProgress = Mathf.Clamp01(actor.ReviveProgress), ClassState = Clip128(actor.Kit.ClassState()) };
                p.Clip = actor.Presenter.CurrentClip; p.Overlay = actor.Presenter.OverlayClip; p.ClipSpeed = actor.Presenter.NetworkClipSpeed; p.OverlaySpeed = actor.Presenter.NetworkOverlaySpeed; p.Hidden = actor.Presenter.Hidden;
                for (int slot = 0; slot < LanWorldFrame.Slots; slot++) { p.Cooldowns[slot] = Mathf.Clamp01(actor.Kit.Cooldown01(slot)); p.SlotStates[slot] = slot < actor.Kit.SlotNames.Length ? Clip(actor.Kit.SlotState(slot)) : ""; }
                // for the client's own-movement prediction: which input this state includes, and how fast walking moves it now
                p.AckInput = i == 0 ? -1 : roster.LastInput(p.Id);
                p.MoveScale = actor.Down || actor.Stunned > 0 || actor.Dashing || !actor.MovementEnabled ? 0 : Mathf.Clamp(actor.Kit.MoveScale * Shop.ItemHooks.F(actor, "move.speed", 1), 0, 4);
                p.Marks.AddRange(actor.Marks); frame.Players.Add(p);
            }
            foreach (var hazard in world.Hazards.Live)
            {
                var pos = hazard.Origin;
                frame.Hazards.Add(new LanWorldFrame.HazardState { Id = hazard.Id, Shape = (int)hazard.Shape, Age = hazard.Age, LockAt = hazard.LockAt, ResolveAt = hazard.ResolveAt, ActiveTicks = hazard.ActiveTicks, ShareCount = hazard.ShareCount, X = pos.x, Y = pos.y, Z = pos.z, Yaw = hazard.Yaw, Radius = hazard.Radius, Inner = hazard.Inner, HalfAngle = hazard.HalfAngle, Length = hazard.Length, Width = hazard.Width, ClipRadius = hazard.ClipRadius, Soak = hazard.Soak, FillToLock = hazard.FillToLock, Burn = hazard.BurnPerSecond > 0, Source = hazard.Source, Mechanic = hazard.MechanicId });
            }
            foreach (var source in world.BossFx.LiveTelegraphs)
            {
                if (!source.go) continue;
                var pos = source.go.transform.position; var clip = source.mpb.GetVector(ClipCentre);
                var fill = source.mpb.GetColor(Fill); var border = source.mpb.GetColor(Border);
                var t = new LanWorldFrame.TelegraphState { Id = source.networkId, Shape = source.networkShape, Order = source.mr.sortingOrder, Motif = Mathf.RoundToInt(source.mpb.GetFloat(Motif)), Radius = source.networkRadius, Inner = source.networkInner, HalfAngle = source.networkHalf, X = pos.x, Z = pos.z, Yaw = source.go.transform.eulerAngles.y, Progress = source.mpb.GetFloat(Progress), Shade = source.mpb.GetFloat(Shade), Memory = source.mpb.GetFloat(Memory), MaxFade = source.maxFade, ClipRadius = source.mpb.GetFloat(ClipRadius), ClipX = clip.x, ClipZ = clip.y, Locked = source.locked, Tinted = source.networkTinted, Spent = source.flash >= 0, SpentFast = source.SpentFast, FillR = fill.r, FillG = fill.g, FillB = fill.b, FillA = fill.a, BorderR = border.r, BorderG = border.g, BorderB = border.b, BorderA = border.a, FlashScale = source.FlashScale, ScaleZ = source.go.transform.localScale.z };
                if (source.holeBuf != null)
                {
                    t.HoleCount = Mathf.Clamp(Mathf.RoundToInt(source.mpb.GetFloat(HoleCount)), 0, 6);
                    for (int i = 0; i < t.HoleCount; i++) { var h = source.holeBuf[i]; t.Holes[i*4] = h.x; t.Holes[i*4+1] = h.y; t.Holes[i*4+2] = h.z; t.Holes[i*4+3] = h.w; }
                }
                frame.Telegraphs.Add(t);
            }
            // a busy fight (bullet hells) can outgrow one snapshot: keep the newest hazards and telegraphs within the caps
            if (frame.Hazards.Count > HazardCap) { frame.Hazards.Sort((a, c) => a.Age.CompareTo(c.Age)); frame.Hazards.RemoveRange(HazardCap, frame.Hazards.Count - HazardCap); }
            if (frame.Telegraphs.Count > TelegraphCap) frame.Telegraphs.RemoveRange(0, frame.Telegraphs.Count - TelegraphCap);
            foreach (var projectile in world.Projectiles.Live)
                frame.Projectiles.Add(new LanWorldFrame.ProjectileState { Id = projectile.NetworkId, X = projectile.Pos.x, Y = projectile.Pos.y, Z = projectile.Pos.z, R = projectile.Color.r, G = projectile.Color.g, B = projectile.Color.b, A = projectile.Color.a });
            foreach (var bullet in world.Bullets.Live)
            {
                if (bullet.Dead) continue;
                if (frame.Bullets.Count >= BulletCap) break;
                frame.Bullets.Add(new LanWorldFrame.BulletState { X = bullet.Pos.x, Z = bullet.Pos.z, VX = bullet.Vel.x, VZ = bullet.Vel.z, Radius = bullet.Radius, Colour = (byte)Mathf.Clamp(bullet.Colour, 0, 2), Water = bullet.Source == "Waterfall Jet" });
            }
            if (frame.Projectiles.Count > LanWorldFrame.MaxProjectiles) frame.Projectiles.RemoveRange(0, frame.Projectiles.Count - LanWorldFrame.MaxProjectiles);
            return frame;
        }
        // well under the wire limits (256 each), so the whole snapshot stays inside one message
        public const int HazardCap = 160, TelegraphCap = 160, BulletCap = 600;
    }
}
