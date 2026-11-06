using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace LastBell.Run.Networking
{
    // A presentation snapshot of host decisions, never a client-authoritative combat simulation. This schema covers
    // common actors and hazard geometry; encounter-specific presentation still needs its own replication.
    public sealed class LanWorldFrame
    {
        public const int MaxHazards = 256, MaxTelegraphs = 256, MaxProjectiles = 256, MaxMarks = 16, Slots = 5;
        public sealed class Player
        {
            public int Id, Hp, MaxHp, Potions; public float X, Y, Z, Yaw, Height, Stamina, MaxStamina;
            public int AckInput = -1; public float MoveScale = 1;   // newest input the host applied; walk-speed factor now (0 = stunned, dashing or rooted)
            public bool Down, Dead, Hidden; public int DownedTicks; public float ReviveProgress;   // revives: bled out, ticks since the fall, channel 0..1
            public string ClassState = "", Clip = "", Overlay = ""; public float ClipSpeed = 1, OverlaySpeed = 1; public readonly float[] Cooldowns = new float[Slots]; public readonly string[] SlotStates = new string[Slots];
            public readonly List<string> Marks = new List<string>();
        }
        public sealed class BossState
        {
            public float X, Y, Z, Yaw; public int Hp, MaxHp; public bool Dead;
            public string Phase = "", Attack = "", Extra = "", Clip = "", Overlay = ""; public float ClipSpeed = 1, OverlaySpeed = 1;
            public int PoseKind, Pose; public bool Haste, Frenzy, Hidden;
            // TempoClock (Tempo Tyrant, Chimebound Oracle): beat, tempo and shift state, so the client's music, HUD and the
            // Metronome Court follow the host's
            public double Beat; public bool BeatHaste, Transitioning; public int TransitionTick, StateAge;
            public string Announcement = "";   // Tempo Tyrant HUD banner
            public readonly List<Number> Numbers = new List<Number>();   // Tempo Tyrant strike numbers on the floor
            public readonly List<Prop> Props = new List<Prop>();         // boss-owned moving objects (orbs, thrown weights)
            // presentation-only boss state (BossEncounter.CaptureNetLook / ReplicaPresent): body glow and up to MaxLook
            // numbers whose meaning each boss defines (phase look, pose flags, counters)
            public float Glow = 1; public readonly List<float> Look = new List<float>();
        }
        public struct Number { public float X, Y, Z; public int N; public bool Imminent; }
        // Id: stable while the object lives; Kind: which look the boss's MakeNetProp gives it
        public struct Prop { public int Id; public byte Kind; public float X, Y, Z; }
        public const int MaxNumbers = 64, MaxProps = 256, MaxLook = 64;
        public sealed class HazardState
        {
            public int Id, Shape, Age, LockAt, ResolveAt, ActiveTicks, ShareCount;
            public float X, Y, Z, Yaw, Radius, Inner, HalfAngle, Length, Width, ClipRadius;
            public bool JumpClearable, Soak, FillToLock, Burn;
            public string Source = "", Mechanic = "";
        }
        public sealed class TelegraphState
        {
            public int Id, Shape, Order, Motif, HoleCount;
            public float Radius, Inner, HalfAngle, X, Z, Yaw, Progress, Shade, Memory, MaxFade, ClipRadius, ClipX, ClipZ;
            public float FillR, FillG, FillB, FillA, BorderR, BorderG, BorderB, BorderA;
            public float FlashScale = 1, ScaleZ = 1;   // resolve-flash strength; lanes some bosses stretch while they live
            public bool Locked, Tinted, Spent, SpentFast;
            public readonly float[] Holes = new float[24];
        }
        public sealed class ProjectileState { public int Id; public float X, Y, Z, R, G, B, A; }
        // hostile bullets (BulletSystem: Sylvara's petals, seeds and water): place and velocity, so a client draws them
        // moving between snapshots; look = hit radius, colour and water
        public struct BulletState { public float X, Z, VX, VZ, Radius; public byte Colour; public bool Water; }
        public const int MaxBullets = 640;
        public readonly List<BulletState> Bullets = new List<BulletState>();
        // One recorded presentation call on the host (LanFx): which effect method ran, on which tick, with which arguments.
        // Players, the boss and projectiles travel as references the client maps to its replica objects.
        public const int MaxEffects = 512, MaxEffectArgs = 12, MaxEffectTargets = 7;
        public struct EffectArg
        {
            public const byte None = 0, PlayerRef = 1, BossRef = 2, PlayerTransform = 3, BossTransform = 4, Vector = 5, Number = 6, Integer = 7, Flag = 8, Tint = 9, Words = 10, ProjectileRef = 11;
            public byte Kind; public int I; public float X, Y, Z, W, VX, VY, VZ; public string S;
        }
        public sealed class Effect { public int Tick; public byte Target; public string Method = ""; public EffectArg[] Args = new EffectArg[0]; }
        public readonly List<Effect> Effects = new List<Effect>();
        public int Tick, RunState, StateTicks;
        public bool Victory;
        public BossState Boss;
        public readonly List<Player> Players = new List<Player>();
        public readonly List<HazardState> Hazards = new List<HazardState>();
        public readonly List<TelegraphState> Telegraphs = new List<TelegraphState>();
        public readonly List<ProjectileState> Projectiles = new List<ProjectileState>();
        static bool Finite(float f) => !float.IsNaN(f) && !float.IsInfinity(f) && Math.Abs(f) <= 10000;
        [ThreadStatic] static string section; // names the part being encoded so a rejected snapshot says what failed
        static void Need(bool ok) { if (!ok) throw new InvalidDataException("Invalid world snapshot" + (section == null ? "." : " (" + section + ").")); }
        static void Text(BinaryWriter w, string text) { text = text ?? ""; Need(text.Length <= 128); w.Write(text); }
        static string Text(BinaryReader r) { string s = r.ReadString(); Need(s.Length <= 128); return s; }
        static void Floats(BinaryWriter w, params float[] values) { for (int i = 0; i < values.Length; i++) { if (!Finite(values[i])) Need(false); w.Write(values[i]); } }
        static float Float(BinaryReader r) { float f = r.ReadSingle(); Need(Finite(f)); return f; }
        public byte[] Encode()
        {
            section = "header";
            Need(Tick >= 0 && RunState >= 0 && RunState <= 6 && StateTicks >= 0 && Players.Count >= 1 && Players.Count <= LanLobby.Capacity && Hazards.Count <= MaxHazards && Telegraphs.Count <= MaxTelegraphs && Projectiles.Count <= MaxProjectiles);
            using (var stream = new MemoryStream())
            using (var w = new BinaryWriter(stream, Encoding.UTF8))
            {
                w.Write(Tick); w.Write(RunState); w.Write(StateTicks); w.Write(Victory); w.Write(Boss != null);
                section = "boss";
                if (Boss != null) { Need(Boss.MaxHp > 0 && Boss.Hp >= 0 && Boss.Hp <= Boss.MaxHp && Boss.PoseKind >= 0 && Boss.PoseKind <= 2 && Boss.Pose >= 0 && Boss.Pose <= 64); Floats(w, Boss.X, Boss.Y, Boss.Z, Boss.Yaw); w.Write(Boss.Hp); w.Write(Boss.MaxHp); w.Write(Boss.Dead); Text(w, Boss.Phase); Text(w, Boss.Attack); Text(w, Boss.Extra); Text(w, Boss.Clip); Text(w, Boss.Overlay); Floats(w, Boss.ClipSpeed, Boss.OverlaySpeed); w.Write(Boss.PoseKind); w.Write(Boss.Pose); w.Write(Boss.Haste); w.Write(Boss.Frenzy); w.Write(Boss.Hidden); w.Write(Boss.Beat); w.Write(Boss.BeatHaste); w.Write(Boss.Transitioning); w.Write(Boss.TransitionTick); w.Write(Boss.StateAge); Text(w, Boss.Announcement);
                    Need(Boss.Numbers.Count <= MaxNumbers && Boss.Props.Count <= MaxProps);
                    w.Write(Boss.Numbers.Count); foreach (var n in Boss.Numbers) { Floats(w, n.X, n.Y, n.Z); w.Write(n.N); w.Write(n.Imminent); }
                    w.Write(Boss.Props.Count); foreach (var p in Boss.Props) { w.Write(p.Id); w.Write(p.Kind); Floats(w, p.X, p.Y, p.Z); }
                    Need(Boss.Look.Count <= MaxLook); Floats(w, Boss.Glow); w.Write((byte)Boss.Look.Count); foreach (float v in Boss.Look) Floats(w, v); }
                section = "player"; w.Write(Players.Count); var ids = new HashSet<int>();
                foreach (var p in Players)
                {
                    Need(p != null && p.Id >= 0 && p.Id < LanLobby.Capacity && ids.Add(p.Id) && p.MaxHp > 0 && p.Hp >= 0 && p.Hp <= p.MaxHp && p.Potions >= 0 && p.Marks.Count <= MaxMarks);
                    w.Write(p.Id); Floats(w, p.X, p.Y, p.Z, p.Yaw, p.Height, p.Stamina, p.MaxStamina); w.Write(p.Hp); w.Write(p.MaxHp); w.Write(p.Potions); w.Write(p.Down); Need(p.DownedTicks >= 0 && p.ReviveProgress >= 0 && p.ReviveProgress <= 1); w.Write(p.Dead); w.Write(p.DownedTicks); Floats(w, p.ReviveProgress); Text(w, p.ClassState); Text(w, p.Clip); Text(w, p.Overlay); Floats(w, p.ClipSpeed, p.OverlaySpeed); w.Write(p.Hidden); Need(p.AckInput >= -1 && p.MoveScale >= 0 && p.MoveScale <= 4); w.Write(p.AckInput); Floats(w, p.MoveScale);
                    foreach (var cooldown in p.Cooldowns) { Need(cooldown >= 0 && cooldown <= 1 && Finite(cooldown)); w.Write(cooldown); }
                    foreach (var slotState in p.SlotStates) Text(w, slotState);
                    w.Write(p.Marks.Count); foreach (string mark in p.Marks) Text(w, mark);
                }
                section = "hazard"; w.Write(Hazards.Count); ids.Clear();
                foreach (var h in Hazards)
                {
                    Need(h != null && h.Id > 0 && ids.Add(h.Id) && h.Shape >= 0 && h.Shape <= 4 && h.Age >= 0 && h.LockAt >= 0 && h.ResolveAt >= 0 && h.ActiveTicks >= 0 && h.ShareCount >= 0 && h.Radius >= 0 && h.Inner >= 0 && h.HalfAngle >= 0 && h.Length >= 0 && h.Width >= 0 && h.ClipRadius >= 0);
                    w.Write(h.Id); w.Write(h.Shape); w.Write(h.Age); w.Write(h.LockAt); w.Write(h.ResolveAt); w.Write(h.ActiveTicks); w.Write(h.ShareCount);
                    Floats(w, h.X, h.Y, h.Z, h.Yaw, h.Radius, h.Inner, h.HalfAngle, h.Length, h.Width, h.ClipRadius);
                    w.Write(h.JumpClearable); w.Write(h.Soak); w.Write(h.FillToLock); w.Write(h.Burn); Text(w, h.Source); Text(w, h.Mechanic);
                }
                section = "telegraph"; w.Write(Telegraphs.Count); ids.Clear();
                foreach (var t in Telegraphs)
                {
                    Need(t != null && t.Id > 0 && ids.Add(t.Id) && t.Shape >= 0 && t.Shape <= 3 && t.HoleCount >= 0 && t.HoleCount <= 6 && t.Radius >= 0 && t.Inner >= 0 && t.HalfAngle >= 0 && t.ClipRadius >= 0);
                    w.Write(t.Id); w.Write(t.Shape); w.Write(t.Order); w.Write(t.Motif); w.Write(t.HoleCount);
                    Floats(w, t.Radius, t.Inner, t.HalfAngle, t.X, t.Z, t.Yaw, t.Progress, t.Shade, t.Memory, t.MaxFade, t.ClipRadius, t.ClipX, t.ClipZ, t.FillR, t.FillG, t.FillB, t.FillA, t.BorderR, t.BorderG, t.BorderB, t.BorderA, t.FlashScale, t.ScaleZ);
                    w.Write(t.Locked); w.Write(t.Tinted); w.Write(t.Spent); w.Write(t.SpentFast);
                    for (int i = 0; i < t.HoleCount * 4; i++) { Need(Finite(t.Holes[i])); w.Write(t.Holes[i]); }
                }
                section = "projectile"; w.Write(Projectiles.Count); ids.Clear();
                foreach (var p in Projectiles) { Need(p != null && p.Id > 0 && ids.Add(p.Id)); w.Write(p.Id); Floats(w, p.X, p.Y, p.Z, p.R, p.G, p.B, p.A); }
                section = "bullet"; Need(Bullets.Count <= MaxBullets); w.Write((short)Bullets.Count);
                foreach (var b in Bullets) { Need(b.Radius >= 0 && b.Colour <= 2); Floats(w, b.X, b.Z, b.VX, b.VZ, b.Radius); w.Write(b.Colour); w.Write(b.Water); }
                section = "effect"; Need(Effects.Count <= MaxEffects); w.Write(Effects.Count);
                foreach (var e in Effects)
                {
                    Need(e != null && e.Tick >= 0 && e.Target < MaxEffectTargets && e.Args != null && e.Args.Length <= MaxEffectArgs && !string.IsNullOrEmpty(e.Method) && e.Method.Length <= 40);
                    w.Write(e.Tick); w.Write(e.Target); Text(w, e.Method); w.Write((byte)e.Args.Length);
                    foreach (var a in e.Args) WriteArg(w, a);
                }
                section = "size"; Need(stream.Length <= LanConnection.MaxMessageBytes - 16); section = null; return stream.ToArray();
            }
        }
        static void WriteArg(BinaryWriter w, EffectArg a)
        {
            w.Write(a.Kind);
            switch (a.Kind)
            {
                case EffectArg.None: break;
                case EffectArg.PlayerRef: case EffectArg.PlayerTransform: Need(a.I >= 0 && a.I < LanLobby.Capacity); w.Write((byte)a.I); break;
                case EffectArg.BossRef: case EffectArg.BossTransform: break;
                case EffectArg.Vector: Floats(w, a.X, a.Y, a.Z); break;
                case EffectArg.Number: Floats(w, a.X); break;
                case EffectArg.Integer: w.Write(a.I); break;
                case EffectArg.Flag: w.Write(a.I != 0); break;
                case EffectArg.Tint: Floats(w, a.X, a.Y, a.Z, a.W); break;
                case EffectArg.Words: Text(w, a.S); break;
                case EffectArg.ProjectileRef: Need(a.I > 0); w.Write(a.I); Floats(w, a.X, a.Y, a.Z, a.VX, a.VY, a.VZ); break;
                default: Need(false); break;
            }
        }
        static EffectArg ReadArg(BinaryReader r)
        {
            var a = new EffectArg { Kind = r.ReadByte() };
            switch (a.Kind)
            {
                case EffectArg.None: break;
                case EffectArg.PlayerRef: case EffectArg.PlayerTransform: a.I = r.ReadByte(); Need(a.I < LanLobby.Capacity); break;
                case EffectArg.BossRef: case EffectArg.BossTransform: break;
                case EffectArg.Vector: a.X = Float(r); a.Y = Float(r); a.Z = Float(r); break;
                case EffectArg.Number: a.X = Float(r); break;
                case EffectArg.Integer: a.I = r.ReadInt32(); break;
                case EffectArg.Flag: a.I = r.ReadBoolean() ? 1 : 0; break;
                case EffectArg.Tint: a.X = Float(r); a.Y = Float(r); a.Z = Float(r); a.W = Float(r); break;
                case EffectArg.Words: a.S = Text(r); break;
                case EffectArg.ProjectileRef: a.I = r.ReadInt32(); Need(a.I > 0); a.X = Float(r); a.Y = Float(r); a.Z = Float(r); a.VX = Float(r); a.VY = Float(r); a.VZ = Float(r); break;
                default: Need(false); break;
            }
            return a;
        }
        public static bool TryDecode(byte[] bytes, out LanWorldFrame frame)
        {
            section = null; frame = null; if (bytes == null || bytes.Length > LanConnection.MaxMessageBytes - 16) return false;
            try
            {
                using (var r = new BinaryReader(new MemoryStream(bytes), Encoding.UTF8))
                {
                    var result = new LanWorldFrame { Tick = r.ReadInt32(), RunState = r.ReadInt32(), StateTicks = r.ReadInt32(), Victory = r.ReadBoolean() };
                    Need(result.Tick >= 0 && result.RunState >= 0 && result.RunState <= 6 && result.StateTicks >= 0);
                    if (r.ReadBoolean())
                    {
                        var b = new BossState { X = Float(r), Y = Float(r), Z = Float(r), Yaw = Float(r), Hp = r.ReadInt32(), MaxHp = r.ReadInt32(), Dead = r.ReadBoolean(), Phase = Text(r), Attack = Text(r), Extra = Text(r), Clip = Text(r), Overlay = Text(r), ClipSpeed = Float(r), OverlaySpeed = Float(r), PoseKind = r.ReadInt32(), Pose = r.ReadInt32(), Haste = r.ReadBoolean(), Frenzy = r.ReadBoolean(), Hidden = r.ReadBoolean(), Beat = r.ReadDouble(), BeatHaste = r.ReadBoolean(), Transitioning = r.ReadBoolean(), TransitionTick = r.ReadInt32(), StateAge = r.ReadInt32(), Announcement = Text(r) };
                        int numbers = r.ReadInt32(); Need(numbers >= 0 && numbers <= MaxNumbers);
                        for (int i = 0; i < numbers; i++) b.Numbers.Add(new Number { X = Float(r), Y = Float(r), Z = Float(r), N = r.ReadInt32(), Imminent = r.ReadBoolean() });
                        int props = r.ReadInt32(); Need(props >= 0 && props <= MaxProps);
                        for (int i = 0; i < props; i++) b.Props.Add(new Prop { Id = r.ReadInt32(), Kind = r.ReadByte(), X = Float(r), Y = Float(r), Z = Float(r) });
                        b.Glow = Float(r); int look = r.ReadByte(); Need(look <= MaxLook); for (int i = 0; i < look; i++) b.Look.Add(Float(r));
                        Need(b.MaxHp > 0 && b.Hp >= 0 && b.Hp <= b.MaxHp && b.PoseKind >= 0 && b.PoseKind <= 2 && b.Pose >= 0 && b.Pose <= 64); result.Boss = b;
                    }
                    int count = r.ReadInt32(); Need(count >= 1 && count <= LanLobby.Capacity); var ids = new HashSet<int>();
                    for (int i = 0; i < count; i++)
                    {
                        var p = new Player { Id = r.ReadInt32(), X = Float(r), Y = Float(r), Z = Float(r), Yaw = Float(r), Height = Float(r), Stamina = Float(r), MaxStamina = Float(r), Hp = r.ReadInt32(), MaxHp = r.ReadInt32(), Potions = r.ReadInt32(), Down = r.ReadBoolean(), Dead = r.ReadBoolean(), DownedTicks = r.ReadInt32(), ReviveProgress = Float(r), ClassState = Text(r), Clip = Text(r), Overlay = Text(r), ClipSpeed = Float(r), OverlaySpeed = Float(r), Hidden = r.ReadBoolean(), AckInput = r.ReadInt32(), MoveScale = Float(r) };
                        Need(p.AckInput >= -1 && p.MoveScale >= 0 && p.MoveScale <= 4 && p.Id >= 0 && p.Id < LanLobby.Capacity && ids.Add(p.Id) && p.MaxHp > 0 && p.Hp >= 0 && p.Hp <= p.MaxHp && p.Potions >= 0);
                        for (int j = 0; j < Slots; j++) { p.Cooldowns[j] = Float(r); Need(p.Cooldowns[j] >= 0 && p.Cooldowns[j] <= 1); }
                        for (int j = 0; j < Slots; j++) p.SlotStates[j] = Text(r);
                        int marks = r.ReadInt32(); Need(marks >= 0 && marks <= MaxMarks); for (int j = 0; j < marks; j++) p.Marks.Add(Text(r)); result.Players.Add(p);
                    }
                    count = r.ReadInt32(); Need(count >= 0 && count <= MaxHazards); ids.Clear();
                    for (int i = 0; i < count; i++)
                    {
                        var h = new HazardState { Id = r.ReadInt32(), Shape = r.ReadInt32(), Age = r.ReadInt32(), LockAt = r.ReadInt32(), ResolveAt = r.ReadInt32(), ActiveTicks = r.ReadInt32(), ShareCount = r.ReadInt32(), X = Float(r), Y = Float(r), Z = Float(r), Yaw = Float(r), Radius = Float(r), Inner = Float(r), HalfAngle = Float(r), Length = Float(r), Width = Float(r), ClipRadius = Float(r), JumpClearable = r.ReadBoolean(), Soak = r.ReadBoolean(), FillToLock = r.ReadBoolean(), Burn = r.ReadBoolean(), Source = Text(r), Mechanic = Text(r) };
                        Need(h.Id > 0 && ids.Add(h.Id) && h.Shape >= 0 && h.Shape <= 4 && h.Age >= 0 && h.LockAt >= 0 && h.ResolveAt >= 0 && h.ActiveTicks >= 0 && h.ShareCount >= 0 && h.Radius >= 0 && h.Inner >= 0 && h.HalfAngle >= 0 && h.Length >= 0 && h.Width >= 0 && h.ClipRadius >= 0); result.Hazards.Add(h);
                    }
                    count = r.ReadInt32(); Need(count >= 0 && count <= MaxTelegraphs); ids.Clear();
                    for (int i = 0; i < count; i++)
                    {
                        var t = new TelegraphState { Id = r.ReadInt32(), Shape = r.ReadInt32(), Order = r.ReadInt32(), Motif = r.ReadInt32(), HoleCount = r.ReadInt32(), Radius = Float(r), Inner = Float(r), HalfAngle = Float(r), X = Float(r), Z = Float(r), Yaw = Float(r), Progress = Float(r), Shade = Float(r), Memory = Float(r), MaxFade = Float(r), ClipRadius = Float(r), ClipX = Float(r), ClipZ = Float(r), FillR = Float(r), FillG = Float(r), FillB = Float(r), FillA = Float(r), BorderR = Float(r), BorderG = Float(r), BorderB = Float(r), BorderA = Float(r), FlashScale = Float(r), ScaleZ = Float(r), Locked = r.ReadBoolean(), Tinted = r.ReadBoolean(), Spent = r.ReadBoolean(), SpentFast = r.ReadBoolean() };
                        Need(t.Id > 0 && ids.Add(t.Id) && t.Shape >= 0 && t.Shape <= 3 && t.HoleCount >= 0 && t.HoleCount <= 6 && t.Radius >= 0 && t.Inner >= 0 && t.HalfAngle >= 0 && t.ClipRadius >= 0);
                        for (int j = 0; j < t.HoleCount * 4; j++) t.Holes[j] = Float(r);
                        result.Telegraphs.Add(t);
                    }
                    count = r.ReadInt32(); Need(count >= 0 && count <= MaxProjectiles); ids.Clear();
                    for (int i = 0; i < count; i++) { var p = new ProjectileState { Id = r.ReadInt32(), X = Float(r), Y = Float(r), Z = Float(r), R = Float(r), G = Float(r), B = Float(r), A = Float(r) }; Need(p.Id > 0 && ids.Add(p.Id)); result.Projectiles.Add(p); }
                    count = r.ReadInt16(); Need(count >= 0 && count <= MaxBullets);
                    for (int i = 0; i < count; i++) { var b = new BulletState { X = Float(r), Z = Float(r), VX = Float(r), VZ = Float(r), Radius = Float(r), Colour = r.ReadByte(), Water = r.ReadBoolean() }; Need(b.Radius >= 0 && b.Colour <= 2); result.Bullets.Add(b); }
                    count = r.ReadInt32(); Need(count >= 0 && count <= MaxEffects);
                    for (int i = 0; i < count; i++)
                    {
                        var e = new Effect { Tick = r.ReadInt32(), Target = r.ReadByte(), Method = Text(r) };
                        int args = r.ReadByte(); Need(e.Tick >= 0 && e.Target < MaxEffectTargets && e.Method.Length > 0 && e.Method.Length <= 40 && args <= MaxEffectArgs);
                        e.Args = new EffectArg[args]; for (int j = 0; j < args; j++) e.Args[j] = ReadArg(r);
                        result.Effects.Add(e);
                    }
                    Need(r.BaseStream.Position == r.BaseStream.Length); frame = result; return true;
                }
            }
            catch (Exception e) when (e is IOException || e is InvalidDataException || e is FormatException || e is ArgumentException || e is OverflowException) { return false; }
        }
    }
}
