using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using LastBell.ClassTest;

namespace LastBell.ClassTest.Desktop
{
    // Unity-free stand-in for the class-test arena: a top-down Windows window that runs the
    // same TrainingSim/ClassKits/Healer sources. Combat units are drawn 1:1 (scaled to fit).
    // Built with the .NET Framework compiler that ships with Windows, so C# 5 only.
    public sealed class ClassTestWindow : Form
    {
        [STAThread]
        public static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            if (args.Length == 2 && args[0] == "--snapshots") { Snapshots(args[1]); return; }
            Application.Run(new ClassTestWindow());
        }

        // Scripted play through the real input-to-command and paint paths, saved as PNGs.
        static void Snapshots(string dir)
        {
            System.IO.Directory.CreateDirectory(dir);
            for (int c = 0; c < ClassKits.All.Length; c++)
            {
                var w = new ClassTestWindow();
                w.timer.Stop();
                w.CreateControl();
                w.SwitchClass(c);
                w.sim.HazardsEnabled = true;
                double t = 0;
                for (int f = 0; f < 60 * 4; f++) // four seconds at 60 fps
                {
                    w.held.Clear(); w.mouseLeft = false;
                    if (f < 30) w.held.Add(Keys.W);  // walk toward the dummy
                    if (f == 40) w.held.Add(Keys.K); // technique / Mend
                    if (f > 20 && f % 2 == 0) w.mouseLeft = true; // basic
                    if (f == 140) w.held.Add(Keys.L); // signature
                    if (f == 175) w.held.Add(Keys.Space); // guard on the first hazard resolve
                    t += 1.0 / 60;
                    w.FrameAt(t, 1.0 / 60);
                }
                using (var bmp = new Bitmap(w.ClientSize.Width, w.ClientSize.Height))
                {
                    using (var g = Graphics.FromImage(bmp))
                        w.OnPaint(new PaintEventArgs(g, new Rectangle(Point.Empty, w.ClientSize)));
                    bmp.Save(System.IO.Path.Combine(dir, ClassKits.All[c].Id + ".png"), System.Drawing.Imaging.ImageFormat.Png);
                }
                Console.WriteLine(ClassKits.All[c].Id + ": tick " + w.sim.Tick + ", damage " + w.sim.TotalDamage + ", hp " + w.sim.Player.Hp + ", events " + w.sim.EventSerial);
                w.Dispose();
            }
        }

        const int Seed = 1;
        TrainingSim sim;
        int classIndex;
        int supportTarget; // 0 auto, 1 self, 2 ally
        bool showHelp = true, showRings = true;
        readonly HashSet<Keys> held = new HashSet<Keys>();
        readonly HashSet<Keys> pressedThisFrame = new HashSet<Keys>();
        bool mouseLeft, mouseRight;
        bool latchBasic, latchTechnique, latchSignature, latchGuard;
        bool prevBasic, prevTechnique, prevSignature, prevGuard, prevDashHeld, dashLatch;
        readonly Stopwatch clock = Stopwatch.StartNew();
        double lastTime, accumulator, fxNow;
        int lastSerial;
        readonly Timer timer = new Timer();

        // Presentation-only effects.
        sealed class Floater { public PointF At; public string Text; public Color Color; public double Born; }
        sealed class Beam { public PointF From, To; public Color Color; public float Width; public double Until; }
        readonly List<Floater> floaters = new List<Floater>();
        readonly List<Beam> beams = new List<Beam>();
        double dummyFlashUntil;
        readonly Dictionary<int, double> hazardFlash = new Dictionary<int, double>();
        readonly Dictionary<int, Hazard> lastHazards = new Dictionary<int, Hazard>();

        float scale = 1f; PointF origin;

        static readonly Color[] SlotColors =
        {
            Color.FromArgb(235, 235, 220), Color.FromArgb(90, 215, 240),
            Color.FromArgb(255, 200, 64), Color.FromArgb(140, 255, 140),
        };
        static readonly Color[] ClassColors =
        {
            Color.FromArgb(77, 128, 217), Color.FromArgb(158, 97, 230), Color.FromArgb(90, 200, 150),
        };
        static readonly string[] SlotKeys = { "J / LMB", "K / RMB", "L / Q", "Space" };
        static readonly string[] TargetNames = { "Auto", "Self", "Ally" };

        readonly Font fontSmall = new Font("Segoe UI", 9.5f);
        readonly Font fontBody = new Font("Segoe UI", 11f);
        readonly Font fontBold = new Font("Segoe UI", 11f, FontStyle.Bold);
        readonly Font fontTitle = new Font("Segoe UI", 17f, FontStyle.Bold);
        readonly Font fontFloat = new Font("Segoe UI", 11f, FontStyle.Bold);

        public ClassTestWindow()
        {
            Text = "Last Bell — Class Test Arena";
            ClientSize = new Size(1440, 860);
            MinimumSize = new Size(1000, 640);
            BackColor = Color.FromArgb(18, 18, 23);
            DoubleBuffered = true;
            KeyPreview = true;
            StartPosition = FormStartPosition.CenterScreen;
            sim = new TrainingSim(ClassKits.All[0], Seed);
            lastSerial = sim.EventSerial;
            timer.Interval = 5;
            timer.Tick += delegate { Frame(); };
            timer.Start();
            Deactivate += delegate { held.Clear(); mouseLeft = mouseRight = false; };
        }

        // ---------- Input ----------

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            const int WM_KEYDOWN = 0x100, WM_SYSKEYDOWN = 0x104;
            if (msg.Msg == WM_KEYDOWN || msg.Msg == WM_SYSKEYDOWN)
            {
                Keys k = keyData & Keys.KeyCode;
                if (!held.Contains(k)) pressedThisFrame.Add(k);
                held.Add(k);
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }
        protected override void OnKeyUp(KeyEventArgs e) { held.Remove(e.KeyCode); if (e.KeyCode == Keys.ShiftKey) { held.Remove(Keys.LShiftKey); held.Remove(Keys.RShiftKey); } }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) mouseLeft = true; if (e.Button == MouseButtons.Right) mouseRight = true; }
        protected override void OnMouseUp(MouseEventArgs e) { if (e.Button == MouseButtons.Left) mouseLeft = false; if (e.Button == MouseButtons.Right) mouseRight = false; }

        bool H(Keys k) { return held.Contains(k); }
        bool Pressed(Keys k) { return pressedThisFrame.Contains(k); }

        [StructLayout(LayoutKind.Sequential)]
        struct XInputGamepad { public ushort Buttons; public byte LeftTrigger, RightTrigger; public short LX, LY, RX, RY; }
        [StructLayout(LayoutKind.Sequential)]
        struct XInputState { public uint Packet; public XInputGamepad Pad; }
        [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")]
        static extern uint XInputGetState(uint index, out XInputState state);
        bool xinputMissing;
        XInputGamepad pad;
        bool padConnected;

        void PollPad()
        {
            padConnected = false;
            if (xinputMissing) return;
            try
            {
                XInputState s;
                if (XInputGetState(0, out s) == 0) { pad = s.Pad; padConnected = true; }
            }
            catch (DllNotFoundException) { xinputMissing = true; }
            catch (EntryPointNotFoundException) { xinputMissing = true; }
        }
        bool PadButton(ushort mask) { return padConnected && (pad.Buttons & mask) != 0; }

        PointF MoveInput()
        {
            float x = 0, y = 0;
            if (H(Keys.A) || H(Keys.Left)) x -= 1;
            if (H(Keys.D) || H(Keys.Right)) x += 1;
            if (H(Keys.W) || H(Keys.Up)) y -= 1;
            if (H(Keys.S) || H(Keys.Down)) y += 1;
            if (padConnected)
            {
                float sx = pad.LX / 32767f, sy = -pad.LY / 32767f;
                if (Math.Sqrt(sx * sx + sy * sy) > 0.25 && sx * sx + sy * sy > x * x + y * y) { x = sx; y = sy; }
            }
            float len = (float)Math.Sqrt(x * x + y * y);
            if (len > 1f) { x /= len; y /= len; }
            return new PointF(x, y);
        }

        // ---------- Loop ----------

        void Frame()
        {
            double now = clock.Elapsed.TotalSeconds;
            double dt = Math.Min(0.1, now - lastTime);
            lastTime = now;
            PollPad();
            FrameAt(now, dt);
        }

        void FrameAt(double now, double dt)
        {
            fxNow = now;
            HandleToggles();

            bool basic = H(Keys.J) || mouseLeft || PadButton(0x1000);
            bool technique = H(Keys.K) || mouseRight || PadButton(0x4000);
            bool signature = H(Keys.L) || H(Keys.Q) || PadButton(0x8000);
            bool guard = H(Keys.Space) || PadButton(0x0200);
            bool dash = H(Keys.E) || H(Keys.D5) || PadButton(0x2000);
            if (dash && !prevDashHeld) dashLatch = true; prevDashHeld = dash;
            bool forceSelf = H(Keys.ShiftKey) || H(Keys.LShiftKey) || PadButton(0x0100);
            latchBasic |= basic; latchTechnique |= technique; latchSignature |= signature; latchGuard |= guard;

            accumulator += dt;
            const double tick = 1.0 / TrainingSim.TickRate;
            while (accumulator >= tick)
            {
                accumulator -= tick;
                PointF m = MoveInput();
                var cmd = new TrainingCommand
                {
                    Move = new Vec2(m.X, m.Y),
                    SupportTarget = forceSelf ? 1 : supportTarget,
                    Basic = basic || latchBasic,
                    Technique = technique || latchTechnique,
                    Signature = signature || latchSignature,
                    Guard = guard || latchGuard,
                };
                cmd.BasicPressed = cmd.Basic && !prevBasic;
                cmd.TechniquePressed = cmd.Technique && !prevTechnique;
                cmd.SignaturePressed = cmd.Signature && !prevSignature;
                cmd.GuardPressed = cmd.Guard && !prevGuard;
                cmd.Dash = dash || dashLatch; cmd.DashPressed = dashLatch; dashLatch = false;
                prevBasic = cmd.Basic; prevTechnique = cmd.Technique; prevSignature = cmd.Signature; prevGuard = cmd.Guard;
                latchBasic = latchTechnique = latchSignature = latchGuard = false;
                RememberHazards();
                sim.Step(cmd);
                FlashResolvedHazards(now);
            }
            pressedThisFrame.Clear();
            ConsumeEvents(now);
            Invalidate();
        }

        void HandleToggles()
        {
            Keys[] classKeys = { Keys.D1, Keys.D2, Keys.D3, Keys.D4, Keys.D5 };
            for (int i = 0; i < ClassKits.All.Length && i < classKeys.Length; i++)
                if (Pressed(classKeys[i]) || Pressed(Keys.NumPad1 + i)) SwitchClass(i);
            if (Pressed(Keys.Tab)) SwitchClass((classIndex + 1) % ClassKits.All.Length);
            if (Pressed(Keys.R)) Restart();
            if (Pressed(Keys.M)) sim.ResetMeter();
            if (Pressed(Keys.H)) sim.HazardsEnabled = !sim.HazardsEnabled;
            if (Pressed(Keys.I)) sim.Invulnerable = !sim.Invulnerable;
            if (Pressed(Keys.U)) { sim.DummyInfinite = !sim.DummyInfinite; Restart(); }
            if (Pressed(Keys.G)) showRings = !showRings;
            if (Pressed(Keys.T)) supportTarget = (supportTarget + 1) % 3;
            if (Pressed(Keys.F1)) showHelp = !showHelp;
            if (Pressed(Keys.Escape)) Close();
        }

        void SwitchClass(int i) { classIndex = i; sim.SetClass(ClassKits.All[i]); ClearFx(); }
        void Restart() { sim.Reset(Seed); ClearFx(); }
        void ClearFx() { lastSerial = sim.EventSerial; floaters.Clear(); beams.Clear(); hazardFlash.Clear(); lastHazards.Clear(); }

        void RememberHazards()
        {
            lastHazards.Clear();
            foreach (var h in sim.Hazards) lastHazards[h.Id] = h;
        }

        readonly List<Hazard> flashing = new List<Hazard>();
        void FlashResolvedHazards(double now)
        {
            foreach (var kv in lastHazards)
            {
                bool still = false;
                foreach (var h in sim.Hazards) if (h.Id == kv.Key) { still = true; break; }
                if (!still && sim.Tick >= kv.Value.ResolveTick) { hazardFlash[kv.Key] = now + 0.18; flashing.Add(kv.Value); }
            }
        }

        TrainingPlayer ActorById(int id) { return id == 2 ? sim.Ally : sim.Player; }

        void ConsumeEvents(double now)
        {
            int first = sim.EventSerial - sim.Events.Count + 1;
            for (int i = 0; i < sim.Events.Count; i++)
            {
                if (first + i <= lastSerial) continue;
                var e = sim.Events[i];
                PointF playerAt = new PointF(sim.Player.Position.X, sim.Player.Position.Y - 34);
                switch (e.Kind)
                {
                    case TrainingEventKind.ActionUsed:
                        float w = e.Slot == ActionSlot.Signature ? 9 : e.Slot == ActionSlot.Technique ? 6 : 3;
                        beams.Add(new Beam { From = P(sim.Player.Position), To = P(TrainingSim.BossPosition), Color = SlotColors[(int)e.Slot], Width = w, Until = now + 0.12 });
                        dummyFlashUntil = now + 0.08;
                        Float(new PointF(TrainingSim.BossPosition.X + (float)(rng.NextDouble() * 30 - 15), TrainingSim.BossPosition.Y - 50), e.Damage.ToString(), SlotColors[(int)e.Slot], now);
                        break;
                    case TrainingEventKind.Healed:
                    {
                        var t = ActorById(e.TargetId);
                        if (e.TargetId != 1) beams.Add(new Beam { From = P(sim.Player.Position), To = P(t.Position), Color = Color.FromArgb(120, 255, 160), Width = 4, Until = now + 0.15 });
                        Float(new PointF(t.Position.X, t.Position.Y - 34), "+" + e.Amount + (e.Overheal > 0 ? " (" + e.Overheal + " over)" : ""), Color.FromArgb(120, 255, 160), now);
                        break;
                    }
                    case TrainingEventKind.ShieldApplied:
                    case TrainingEventKind.ShieldAbsorbed:
                    case TrainingEventKind.Cleansed:
                    {
                        var t = ActorById(e.TargetId);
                        string label = e.Kind == TrainingEventKind.ShieldApplied ? "Ward" : e.Kind == TrainingEventKind.ShieldAbsorbed ? "absorbed" : "cleansed Chill";
                        Float(new PointF(t.Position.X, t.Position.Y - 34), label, Color.FromArgb(170, 220, 255), now);
                        break;
                    }
                    case TrainingEventKind.HazardResolved:
                    {
                        PointF at = e.TargetId == 2 ? new PointF(sim.Ally.Position.X, sim.Ally.Position.Y - 34) : playerAt;
                        bool hit = e.Text.EndsWith("hit") || e.Text.Contains("Hit");
                        Float(at, e.Effect ?? e.Text, hit ? Color.FromArgb(255, 90, 80) : SlotColors[3], now);
                        break;
                    }
                    case TrainingEventKind.ActionBlocked:
                        if (IsNoise(e)) break;
                        Float(playerAt, e.Text, Color.FromArgb(255, 180, 120), now);
                        break;
                    case TrainingEventKind.CastCancelled:
                    case TrainingEventKind.DummyDefeated:
                    case TrainingEventKind.KnockedOut:
                        Float(playerAt, e.Text, Color.White, now);
                        break;
                }
            }
            lastSerial = sim.EventSerial;
        }

        readonly Random rng = new Random();
        void Float(PointF at, string text, Color c, double now)
        {
            floaters.Add(new Floater { At = at, Text = text, Color = c, Born = now });
            if (floaters.Count > 30) floaters.RemoveAt(0);
        }

        // ---------- Drawing ----------

        PointF P(Vec2 v) { return new PointF(origin.X + (v.X - TrainingSim.ArenaMin.X) * scale, origin.Y + (v.Y - TrainingSim.ArenaMin.Y) * scale); }
        PointF P(PointF v) { return new PointF(origin.X + (v.X - TrainingSim.ArenaMin.X) * scale, origin.Y + (v.Y - TrainingSim.ArenaMin.Y) * scale); }
        RectangleF Circle(Vec2 c, float r) { var p = P(c); return new RectangleF(p.X - r * scale, p.Y - r * scale, 2 * r * scale, 2 * r * scale); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            double now = fxNow;

            const float panel = 380;
            float aw = TrainingSim.ArenaMax.X - TrainingSim.ArenaMin.X, ah = TrainingSim.ArenaMax.Y - TrainingSim.ArenaMin.Y;
            scale = Math.Min((ClientSize.Width - panel - 40) / aw, (ClientSize.Height - 40) / ah);
            origin = new PointF(panel + 20 + (ClientSize.Width - panel - 40 - aw * scale) / 2, 20 + (ClientSize.Height - 40 - ah * scale) / 2);

            DrawArena(g);
            var tlc = P(TrainingSim.ArenaMin); var brc = P(TrainingSim.ArenaMax);
            g.SetClip(RectangleF.FromLTRB(tlc.X, tlc.Y, brc.X, brc.Y));
            DrawHazards(g, now);
            DrawRings(g);
            g.ResetClip();
            DrawDummy(g, now);
            if (sim.IsHealer) DrawActor(g, sim.Ally, Color.FromArgb(200, 170, 110), "Ally", now);
            DrawActor(g, sim.Player, ClassColors[classIndex % ClassColors.Length], null, now);
            DrawBeams(g, now);
            DrawFloaters(g, now);
            DrawHud(g, panel);
        }

        void DrawArena(Graphics g)
        {
            var tl = P(TrainingSim.ArenaMin); var br = P(TrainingSim.ArenaMax);
            var rect = RectangleF.FromLTRB(tl.X, tl.Y, br.X, br.Y);
            using (var b = new SolidBrush(Color.FromArgb(40, 42, 50))) g.FillRectangle(b, rect);
            using (var pen = new Pen(Color.FromArgb(22, 255, 255, 255), 1))
            {
                for (float x = TrainingSim.ArenaMin.X + 10; x < TrainingSim.ArenaMax.X; x += 100)
                    g.DrawLine(pen, P(new Vec2(x, TrainingSim.ArenaMin.Y)), P(new Vec2(x, TrainingSim.ArenaMax.Y)));
                for (float y = TrainingSim.ArenaMin.Y + 20; y < TrainingSim.ArenaMax.Y; y += 100)
                    g.DrawLine(pen, P(new Vec2(TrainingSim.ArenaMin.X, y)), P(new Vec2(TrainingSim.ArenaMax.X, y)));
            }
            using (var pen = new Pen(Color.FromArgb(220, 90, 65), 3)) g.DrawRectangle(pen, rect.X, rect.Y, rect.Width, rect.Height);
        }

        void HazardPath(GraphicsPath path, Hazard h)
        {
            var r = Circle(h.Origin, h.Radius);
            if (h.Kind == HazardKind.Cone)
            {
                float start = (float)((h.Direction - h.HalfAngle) * 180 / Math.PI);
                path.AddPie(r.X, r.Y, r.Width, r.Height, start, (float)(h.HalfAngle * 2 * 180 / Math.PI));
            }
            else
            {
                path.FillMode = FillMode.Alternate;
                path.AddEllipse(r);
                path.AddEllipse(Circle(h.Origin, h.InnerRadius));
            }
        }

        void DrawHazards(Graphics g, double now)
        {
            foreach (var h in sim.Hazards)
            {
                float t = Math.Max(0, Math.Min(1, (sim.Tick - h.WarningTick) / (float)(h.ResolveTick - h.WarningTick)));
                using (var path = new GraphicsPath())
                {
                    HazardPath(path, h);
                    using (var b = new SolidBrush(Color.FromArgb((int)(40 + 110 * t * t), 255, (int)(110 - 70 * t), 40))) g.FillPath(b, path);
                    using (var pen = new Pen(Color.FromArgb(255, 95, 70), 2.5f)) g.DrawPath(pen, path);
                }
                var c = P(h.Origin);
                string label = h.Name + "  " + ((h.ResolveTick - sim.Tick) / 60f).ToString("0.0") + " s";
                var sz = g.MeasureString(label, fontBold);
                g.DrawString(label, fontBold, Brushes.White, c.X - sz.Width / 2, c.Y + 34 * scale);
            }
            for (int i = flashing.Count - 1; i >= 0; i--)
            {
                double until;
                if (!hazardFlash.TryGetValue(flashing[i].Id, out until) || now > until) { hazardFlash.Remove(flashing[i].Id); flashing.RemoveAt(i); continue; }
                using (var path = new GraphicsPath())
                {
                    HazardPath(path, flashing[i]);
                    using (var b = new SolidBrush(Color.FromArgb(150, 255, 240, 220))) g.FillPath(b, path);
                }
            }
        }

        void DrawRings(Graphics g)
        {
            if (!showRings) return;
            var kit = sim.Kit;
            for (int i = 0; i < 3; i++)
            {
                var def = kit.Offensive((ActionSlot)i);
                if (def.Range <= 0) continue;
                bool support = sim.IsHealer && i > 0; // Mend/Sanctuary reach is around the caster
                Vec2 centre = support ? sim.Player.Position : TrainingSim.BossPosition;
                bool lit = support || sim.DistanceToDummy <= def.Range;
                var c = SlotColors[i];
                using (var pen = new Pen(Color.FromArgb(lit ? 210 : 60, c), lit ? 2f : 1.5f))
                {
                    if (support) pen.DashStyle = DashStyle.Dash;
                    g.DrawEllipse(pen, Circle(centre, def.Range));
                }
            }
        }

        void DrawDummy(Graphics g, double now)
        {
            if (!sim.DummyAlive) return;
            var r = Circle(TrainingSim.BossPosition, now < dummyFlashUntil ? 30 : 26);
            using (var b = new SolidBrush(now < dummyFlashUntil ? Color.FromArgb(255, 225, 160) : Color.FromArgb(170, 125, 60))) g.FillEllipse(b, r);
            using (var pen = new Pen(Color.FromArgb(220, 180, 100), 2)) g.DrawEllipse(pen, r);
            var c = P(TrainingSim.BossPosition);
            if (!sim.DummyInfinite)
            {
                float w = 90 * scale, frac = sim.DummyHp / (float)sim.DummyMaxHp;
                g.FillRectangle(Brushes.Black, c.X - w / 2, c.Y - 44 * scale, w, 7);
                using (var b = new SolidBrush(Color.FromArgb(220, 80, 70))) g.FillRectangle(b, c.X - w / 2, c.Y - 44 * scale, w * frac, 7);
            }
        }

        void DrawActor(Graphics g, TrainingPlayer a, Color color, string name, double now)
        {
            if (!a.Alive && a.Hp <= 0)
            {
                using (var pen = new Pen(Color.FromArgb(120, color), 2) { DashStyle = DashStyle.Dot }) g.DrawEllipse(pen, Circle(a.Position, 14));
                return;
            }
            bool immuneFlash = sim.Tick < a.ImmuneUntil && ((int)(now * 15) % 2 == 0);
            bool chilled = sim.Tick < a.ChillUntil;
            Color body = chilled ? Blend(color, Color.FromArgb(170, 220, 255), 0.5f) : color;
            if (immuneFlash) body = Blend(body, Color.White, 0.6f);
            using (var b = new SolidBrush(body)) g.FillEllipse(b, Circle(a.Position, 14));
            using (var pen = new Pen(Color.FromArgb(230, 230, 230), 1.5f)) g.DrawEllipse(pen, Circle(a.Position, 14));

            // Facing toward the dummy.
            Vec2 d = TrainingSim.BossPosition - a.Position;
            if (d.Length > 1)
            {
                Vec2 tip = a.Position + d * (22f / d.Length);
                using (var pen = new Pen(Color.White, 3)) g.DrawLine(pen, P(a.Position), P(tip));
            }

            if (a == sim.Player && sim.GuardActive)
                using (var pen = new Pen(SlotColors[3], 3)) g.DrawEllipse(pen, Circle(a.Position, 22));
            if (sim.Tick < a.ShieldUntil && a.Shield > 0)
                using (var pen = new Pen(Color.FromArgb(170, 220, 255), 3) { DashStyle = DashStyle.Dash }) g.DrawEllipse(pen, Circle(a.Position, 25));
            if (a.HotUntil > sim.Tick)
                using (var pen = new Pen(Color.FromArgb(120, 255, 160), 2) { DashStyle = DashStyle.Dot }) g.DrawEllipse(pen, Circle(a.Position, 29));

            if (a == sim.Player && a.Casting)
            {
                var def = sim.Kit.Offensive(a.CastSlot);
                float t = 1f - (a.CastUntil - sim.Tick) / (float)Math.Max(1, def.CastTicks);
                using (var pen = new Pen(SlotColors[(int)a.CastSlot], 4)) g.DrawArc(pen, Circle(a.Position, 32), -90, 360 * t);
                if (sim.IsHealer && sim.CurrentCastTarget == 2)
                    using (var pen = new Pen(Color.FromArgb(160, 120, 255, 160), 2) { DashStyle = DashStyle.Dash }) g.DrawLine(pen, P(a.Position), P(sim.Ally.Position));
            }

            // HP pips above.
            var c = P(a.Position);
            float pip = 7, gap = 3, total = TrainingSim.MaxHp * pip + (TrainingSim.MaxHp - 1) * gap;
            for (int i = 0; i < TrainingSim.MaxHp; i++)
            {
                var r = new RectangleF(c.X - total / 2 + i * (pip + gap), c.Y - 18 * scale - 12, pip, pip);
                g.FillRectangle(Brushes.Black, r);
                if (i < a.Hp) using (var b = new SolidBrush(Color.FromArgb(235, 85, 85))) g.FillRectangle(b, r.X + 1, r.Y + 1, r.Width - 2, r.Height - 2);
            }
            if (name != null)
            {
                var sz = g.MeasureString(name, fontSmall);
                g.DrawString(name, fontSmall, Brushes.Gainsboro, c.X - sz.Width / 2, c.Y + 16 * scale);
            }

            // Cooldowns under the player, so eyes can stay on the arena.
            if (a == sim.Player)
            {
                float bw = 18, bh = 5, x0 = c.X - (bw * 4 + 6) / 2, y0 = c.Y + 18 * scale + 2;
                for (int i = 0; i < 4; i++)
                {
                    var slot = (ActionSlot)i;
                    int totalCd = slot == ActionSlot.Guard ? sim.Kit.Guard.Cooldown : sim.Kit.Offensive(slot).Cooldown;
                    float ready = 1f - sim.CooldownRemaining(slot) / (float)Math.Max(1, totalCd);
                    var r = new RectangleF(x0 + i * (bw + 2), y0, bw, bh);
                    g.FillRectangle(Brushes.Black, r);
                    using (var b = new SolidBrush(ready < 1 ? Blend(SlotColors[i], Color.Black, 0.45f) : SlotColors[i])) g.FillRectangle(b, r.X, r.Y, r.Width * ready, r.Height);
                }
            }
        }

        void DrawBeams(Graphics g, double now)
        {
            for (int i = beams.Count - 1; i >= 0; i--)
            {
                if (now > beams[i].Until) { beams.RemoveAt(i); continue; }
                using (var pen = new Pen(beams[i].Color, beams[i].Width)) g.DrawLine(pen, beams[i].From, beams[i].To);
            }
        }

        void DrawFloaters(Graphics g, double now)
        {
            for (int i = floaters.Count - 1; i >= 0; i--)
            {
                var f = floaters[i];
                double age = now - f.Born;
                if (age > 0.9) { floaters.RemoveAt(i); continue; }
                var p = P(f.At);
                int alpha = (int)(255 * (1 - age / 0.9));
                var sz = g.MeasureString(f.Text, fontFloat);
                using (var b = new SolidBrush(Color.FromArgb(alpha, f.Color)))
                    g.DrawString(f.Text, fontFloat, b, p.X - sz.Width / 2, p.Y - (float)(age * 40));
            }
        }

        void DrawHud(Graphics g, float width)
        {
            using (var b = new SolidBrush(Color.FromArgb(28, 28, 35))) g.FillRectangle(b, 0, 0, width, ClientSize.Height);
            var kit = sim.Kit; var p = sim.Player;
            float x = 18, y = 14, w = width - 36;

            g.DrawString(kit.Name.ToUpperInvariant(), fontTitle, Brushes.White, x, y); y += 34;
            g.DrawString("[1] Vanguard  [2] Arcanist  [3] Healer  [Tab] cycle", fontSmall, Brushes.Gray, x, y); y += 22;
            y += DrawWrapped(g, kit.Identity, fontSmall, Brushes.Silver, x, y, w) + 8;

            Pips(g, x, y, "HP", p.Hp, TrainingSim.MaxHp, Color.FromArgb(235, 85, 85)); y += 24;
            Pips(g, x, y, kit.ResourceName, p.Resource, kit.ResourceCap, SlotColors[1]); y += 30;

            for (int i = 0; i < 4; i++)
            {
                var slot = (ActionSlot)i;
                string name, detail;
                if (slot == ActionSlot.Guard)
                {
                    name = kit.Guard.Name;
                    detail = kit.Guard.Window > 0 ? (kit.Guard.Window / 60f).ToString("0.0#") + " s block" : "";
                    detail += (detail.Length > 0 ? " · " : "") + "cd " + (kit.Guard.Cooldown / 60f).ToString("0.#") + " s";
                }
                else
                {
                    var def = kit.Offensive(slot);
                    name = def.Name;
                    detail = "range " + def.Range + (def.Damage > 0 ? " · dmg " + def.Damage : "") +
                        (def.ResourceDelta != 0 ? " · " + (def.ResourceDelta > 0 ? "+" : "") + def.ResourceDelta : "") +
                        (def.CastTicks > 0 ? " · cast " + (def.CastTicks / 60f).ToString("0.0") + " s" : "") +
                        " · cd " + (def.Cooldown / 60f).ToString("0.#") + " s";
                }
                int cd = sim.CooldownRemaining(slot);
                string state; Brush stateBrush;
                bool dummySlot = slot != ActionSlot.Guard && !(sim.IsHealer && slot != ActionSlot.Basic);
                if (cd > 0) { state = (cd / 60f).ToString("0.0") + " s"; stateBrush = Brushes.LightCoral; }
                else if (dummySlot && sim.DistanceToDummy > kit.Offensive(slot).Range) { state = "out of range"; stateBrush = Brushes.Orange; }
                else { state = "ready"; stateBrush = Brushes.LightGreen; }

                using (var kb = new SolidBrush(SlotColors[i])) g.DrawString("[" + SlotKeys[i] + "]", fontSmall, kb, x, y + 2);
                g.DrawString(name, fontBold, Brushes.White, x + 74, y);
                var nsz = g.MeasureString(name, fontBold);
                g.DrawString(state, fontSmall, stateBrush, x + 78 + nsz.Width, y + 2);
                y += 20;
                string desc = slot == ActionSlot.Guard ? kit.Guard.Description : kit.Offensive(slot).Description;
                y += DrawWrapped(g, detail + (string.IsNullOrEmpty(desc) ? "" : "\n" + desc), fontSmall, Brushes.DarkGray, x + 74, y, w - 74) + 6;
            }

            if (sim.IsHealer)
            {
                g.DrawString("Support target [T]: " + TargetNames[supportTarget] + "   (hold Shift = Self)", fontSmall, Brushes.LightGreen, x, y); y += 20;
                g.DrawString("Ally HP " + sim.Ally.Hp + "/" + TrainingSim.MaxHp + (sim.Ally.Alive ? "" : " — down until reset [R]"), fontSmall, Brushes.Silver, x, y); y += 20;
            }
            g.DrawString("Distance to dummy: " + Math.Round(sim.DistanceToDummy), fontSmall, Brushes.Silver, x, y); y += 30;

            g.DrawString("TRAINING DUMMY", fontBold, Brushes.White, x, y); y += 22;
            g.DrawString("HP " + (sim.DummyInfinite ? "infinite" : sim.DummyHp + " / " + sim.DummyMaxHp) +
                (sim.LastKillTicks > 0 ? "    last kill " + (sim.LastKillTicks / 60f).ToString("0.0") + " s" : ""), fontSmall, Brushes.Silver, x, y); y += 20;
            g.DrawString("Damage " + sim.TotalDamage + "    DPS (10 s) " + sim.Dps.ToString("0.0"), fontBody, Brushes.White, x, y); y += 24;
            for (int i = 0; i < 3; i++)
            {
                if (sim.UsesBySlot[i] == 0 && kit.Offensive((ActionSlot)i).Damage == 0) continue;
                using (var kb = new SolidBrush(SlotColors[i]))
                    g.DrawString(kit.Offensive((ActionSlot)i).Name + "  " + sim.DamageBySlot[i] + " dmg  ×" + sim.UsesBySlot[i], fontSmall, kb, x, y);
                y += 18;
            }
            y += 10;
            g.DrawString("[H] Hazards " + OnOff(sim.HazardsEnabled) + "    [I] Invulnerable " + OnOff(sim.Invulnerable), fontSmall, Brushes.Gainsboro, x, y); y += 18;
            g.DrawString("[U] Dummy " + (sim.DummyInfinite ? "infinite" : sim.DummyMaxHp + " HP") + "    [G] Rings " + OnOff(showRings), fontSmall, Brushes.Gainsboro, x, y); y += 18;
            g.DrawString("[M] Reset meter   [R] Reset all   [F1] Help   [Esc] Quit", fontSmall, Brushes.Gainsboro, x, y); y += 26;

            // Recent log fills the rest.
            int lines = Math.Max(0, (int)((ClientSize.Height - y - 10) / 17));
            var shown = new List<TrainingEvent>();
            for (int i = sim.Events.Count - 1; i >= 0 && shown.Count < lines; i--)
                if (!IsNoise(sim.Events[i])) shown.Insert(0, sim.Events[i]);
            foreach (var e in shown)
            {
                g.DrawString((e.Tick / 60f).ToString("0.00"), fontSmall, Brushes.DimGray, x, y);
                g.DrawString(e.Text + (e.Damage > 0 ? "  " + e.Damage : ""), fontSmall, Brushes.Silver, x + 48, y);
                y += 17;
            }

            if (!p.Alive)
            {
                string ko = "KNOCKED OUT — respawning";
                var sz = g.MeasureString(ko, fontTitle);
                g.DrawString(ko, fontTitle, Brushes.White, width + (ClientSize.Width - width - sz.Width) / 2, ClientSize.Height * 0.42f);
            }
            if (showHelp)
            {
                string help = "Move: WASD / arrows / left stick\nBasic: J / left mouse / A (hold to repeat)\nTechnique: K / right mouse / X\nSignature: L or Q / Y      Guard: Space / RB\nSolid rings: reach to the dummy.  Dashed: Healer reach.\nF1 hides this.";
                var sz = g.MeasureString(help, fontSmall);
                var r = new RectangleF(ClientSize.Width - sz.Width - 36, ClientSize.Height - sz.Height - 30, sz.Width + 20, sz.Height + 14);
                using (var b = new SolidBrush(Color.FromArgb(210, 20, 20, 26))) g.FillRectangle(b, r);
                g.DrawString(help, fontSmall, Brushes.Gainsboro, r.X + 10, r.Y + 7);
            }
        }

        float DrawWrapped(Graphics g, string text, Font f, Brush b, float x, float y, float w)
        {
            var sz = g.MeasureString(text, f, (int)w);
            g.DrawString(text, f, b, new RectangleF(x, y, w, sz.Height + 2));
            return sz.Height;
        }

        void Pips(Graphics g, float x, float y, string label, int value, int max, Color color)
        {
            g.DrawString(label, fontSmall, Brushes.Silver, x, y);
            for (int i = 0; i < max; i++)
            {
                var r = new RectangleF(x + 74 + i * 22, y + 2, 17, 15);
                using (var bg = new SolidBrush(Color.FromArgb(10, 10, 12))) g.FillRectangle(bg, r);
                if (i < value) using (var b = new SolidBrush(color)) g.FillRectangle(b, r.X + 2, r.Y + 2, r.Width - 4, r.Height - 4);
            }
        }

        static string OnOff(bool v) { return v ? "on" : "off"; }

        // Re-pressing during a cooldown is normal play; only show blocks that teach something.
        static bool IsNoise(TrainingEvent e)
        {
            return e.Kind == TrainingEventKind.ActionBlocked && e.Text != null &&
                (e.Text.EndsWith("cooling down") || e.Text.EndsWith("attack cadence") || e.Text.EndsWith("casting"));
        }

        static Color Blend(Color a, Color b, float t)
        {
            return Color.FromArgb((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
        }
    }
}
