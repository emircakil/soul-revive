using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace SoulsRevive
{
    public sealed class Companion : Form
    {
        readonly TimerEngine engine;
        readonly System.Windows.Forms.Timer timer;
        readonly NotifyIcon tray;
        readonly ContextMenuStrip menu;
        readonly ToolStripMenuItem showItem, modeItem, settingsItem, quitItem;
        readonly EmberButton primary, secondary, workMode, chillMode;
        readonly EmberButton settingsButton, minimizeButton, closeButton, statsButton;
        readonly Soundscape audio = new Soundscape();
        readonly WorkRoutine workRoutine = new WorkRoutine();
        int lastTick = Environment.TickCount, frame, chillFrame, saveTicks, kneel;
        bool exiting, dragging, startHidden, showQuietly;
        Point dragOffset;
        string message = "";
        DateTime messageUntil;

        public Companion(Preferences p, bool preview) : this(p, preview, false) { }
        public Companion(Preferences p, bool preview, bool startHidden)
        {
            this.startHidden = startHidden && !preview;
            engine = new TimerEngine(p); L.Language = p.Language; Theme.Light = p.LightMode; PixelArt.Equipped = KnightEquipment.From(p);
            chillFrame = preview ? PixelArt.TavernArrivalFrames + 36 : 0;
            kneel = engine.Due ? PixelArt.KneelFrames : 0;
            Text = "Soul Revive"; AccessibleName = L.Text("Soul Revive mola sayacı", "Soul Revive break timer");
            ClientSize = new Size(384, 668); FormBorderStyle = FormBorderStyle.None; AutoScaleMode = AutoScaleMode.None;
            BackColor = Theme.Background; DoubleBuffered = true; StartPosition = FormStartPosition.Manual;
            TopMost = p.AlwaysOnTop; Icon = PixelArt.MakeIcon(); KeyPreview = true;
            // The primary action stays the first control; tests and screen readers rely on that order.
            primary = Button("", 20, 486, 168, 42, delegate { Ui.RunPrimary(engine); }, true);
            secondary = Button("", 196, 486, 168, 42, delegate { Ui.RunSecondary(engine); }, false);
            workMode = Button(Ui.WorkingMode, 20, 74, 168, 30, delegate { engine.SetChill(false); }, false);
            chillMode = Button(Ui.ChillMode, 196, 74, 168, 30, delegate { engine.SetChill(true); }, false);
            settingsButton = Button("", 272, 18, 28, 28, OpenSettings, false);
            settingsButton.GearIcon = true;
            minimizeButton = Button("_", 304, 18, 28, 28, delegate { WindowState = FormWindowState.Minimized; }, false);
            closeButton = Button("X", 336, 18, 28, 28, Hide, false);
            statsButton = Button(Ui.StatisticsButton, 20, 544, 344, 32, OpenStats, false);
            menu = new ContextMenuStrip { Font = PixelFont.Create(Dpi.S(16)), BackColor = Theme.Panel, ForeColor = Theme.Text };
            showItem = new ToolStripMenuItem("", null, delegate { Reveal(); });
            modeItem = new ToolStripMenuItem("", null, delegate { engine.SetChill(!p.Chill); });
            settingsItem = new ToolStripMenuItem("", null, delegate { Reveal(); OpenSettings(); });
            quitItem = new ToolStripMenuItem("", null, delegate { Quit(); });
            menu.Items.AddRange(new ToolStripItem[] { showItem, modeItem, settingsItem, new ToolStripSeparator(), quitItem });
            tray = new NotifyIcon { Text = "Soul Revive", Icon = Icon, ContextMenuStrip = menu, Visible = !preview };
            tray.DoubleClick += delegate { Reveal(); }; tray.BalloonTipClicked += delegate { Reveal(); };
            engine.Changed += OnChanged;
            audio.Failed += delegate (string error) {
                if (IsDisposed || !IsHandleCreated) return;
                BeginInvoke((Action)delegate {
                    message = L.Text("Ses başlatılamadı: ", "Audio could not start: ") + error;
                    messageUntil = DateTime.Now.AddSeconds(15);
                    Invalidate();
                });
            };
            timer = new System.Windows.Forms.Timer { Interval = 125 };
            timer.Tick += delegate
            {
                if (Program.QuitRequested != null && Program.QuitRequested.WaitOne(0)) { Quit(); return; }
                if (Program.ShowRequested != null && Program.ShowRequested.WaitOne(0)) Reveal();
                int now = Environment.TickCount; double delta = unchecked((uint)(now - lastTick)) / 1000.0; lastTick = now;
                if (delta > 5 && !p.Chill && !p.Paused) { p.Paused = true; Save(); }
                engine.Advance(delta); frame++; if (p.Chill) chillFrame++;
                workRoutine.Advance(engine.Working);
                kneel = PixelArt.StepKneel(kneel, engine.Due);
                if (++saveTicks >= 40)
                {
                    saveTicks = 0;
                    CheckIdle();
                    // Nothing changes while paused, expired or in chill mode, so there is nothing to write.
                    if (engine.Working || p.InBreak) Save();
                }
                UpdateControls();
                if (Visible && WindowState != FormWindowState.Minimized) Invalidate();
            };
            if (!preview) { SystemEvents.PowerModeChanged += OnPower; SystemEvents.SessionSwitch += OnSession; timer.Start(); audio.Apply(p); }
            UpdateControls();
            MouseDown += delegate (object sender, MouseEventArgs e) { if (e.Button == MouseButtons.Left) { dragging = true; dragOffset = e.Location; Capture = true; } };
            MouseMove += delegate { if (dragging) Location = new Point(Cursor.Position.X - dragOffset.X, Cursor.Position.Y - dragOffset.Y); };
            MouseUp += delegate
            {
                if (!dragging) return; dragging = false; Capture = false; var a = Screen.FromControl(this).WorkingArea;
                Location = new Point(Math.Max(a.Left, Math.Min(Left, a.Right - Width)), Math.Max(a.Top, Math.Min(Top, a.Bottom - Height)));
                p.X = Left; p.Y = Top; Save();
            };
            Dpi.Apply(this);
            var area = Screen.PrimaryScreen.WorkingArea; Location = new Point(area.Right - Width - 24, area.Bottom - Height - 24);
            foreach (var screen in Screen.AllScreens) if (screen.WorkingArea.Contains(new Rectangle(p.X, p.Y, Width, Height))) { Location = new Point(p.X, p.Y); break; }
        }
        EmberButton Button(string text, int x, int y, int w, int h, Action action, bool isPrimary)
        { var b = new EmberButton(text, new Rectangle(x, y, w, h), isPrimary); b.Click += delegate { action(); }; Controls.Add(b); return b; }

        protected override void SetVisibleCore(bool value)
        {
            if (startHidden)
            {
                // Keep the message loop, timer and tray running without showing the window.
                startHidden = false;
                if (!IsHandleCreated) CreateHandle();
                value = false;
            }
            base.SetVisibleCore(value);
        }
        protected override bool ShowWithoutActivation { get { return showQuietly; } }

        void OnPower(object s, PowerModeChangedEventArgs e) { if (e.Mode == PowerModes.Suspend) SystemPause(); else if (e.Mode == PowerModes.Resume) audio.Apply(engine.P); }
        void OnSession(object s, SessionSwitchEventArgs e) { if (e.Reason == SessionSwitchReason.SessionLock) SystemPause(); else if (e.Reason == SessionSwitchReason.SessionUnlock) audio.Apply(engine.P); }
        void SystemPause()
        { if (IsDisposed || !IsHandleCreated) return; BeginInvoke((Action)delegate { if (!engine.P.Chill) engine.P.Paused = true; lastTick = Environment.TickCount; audio.Mute(); Save(); UpdateControls(); Invalidate(); }); }

        void CheckIdle()
        {
            var p = engine.P;
            if (!p.IdlePause || !engine.Working) return;
            double idle = IdleInput.Seconds();
            if (idle < p.IdleMinutes * 60) return;
            if (engine.PauseForIdle(idle, DateTime.Now))
            {
                // Lower totals must replace the saved ones (and their backups).
                Save(true);
                message = Ui.IdleBanner; messageUntil = DateTime.Now.AddSeconds(30);
            }
        }
        void Save() { Save(false); }
        void Save(bool authoritative) { if (!Storage.Save(engine.P, authoritative)) { message = Ui.SaveFailed; messageUntil = DateTime.Now.AddSeconds(10); } }
        void Reveal() { Show(); WindowState = FormWindowState.Normal; Activate(); }
        /// <summary>Shows the window without taking keyboard focus, and flashes the taskbar button.</summary>
        void RevealQuietly()
        {
            if (!Visible) { showQuietly = true; try { Show(); } finally { showQuietly = false; } }
            else if (WindowState == FormWindowState.Minimized) NativeWindowing.ShowNoActivate(Handle);
            NativeWindowing.Flash(Handle);
        }
        internal void Quit() { exiting = true; Close(); }
        void OnChanged(TimerEvent reason)
        {
            if (reason == TimerEvent.Mode) chillFrame = 0;
            if (reason == TimerEvent.Mode || reason == TimerEvent.Due || reason == TimerEvent.Revived || reason == TimerEvent.Break || reason == TimerEvent.Cancel) workRoutine.Reset();
            if (reason == TimerEvent.Due)
            {
                Notify(Ui.DueTitle, Ui.DueText);
                if (engine.P.FocusOnDue) Reveal(); else RevealQuietly();
            }
            if (reason == TimerEvent.Revived) { message = Ui.RevivedBanner; messageUntil = DateTime.Now.AddSeconds(8); Notify(Ui.RevivedTitle, Ui.RevivedText); }
            lastTick = Environment.TickCount;
            if (reason != TimerEvent.Idle) Save();
            UpdateControls(); Invalidate();
        }
        void Notify(string title, string text) { tray.ShowBalloonTip(7000, title, text, ToolTipIcon.Info); if (engine.P.Sound) System.Media.SystemSounds.Asterisk.Play(); }
        void UpdateControls()
        {
            var p = engine.P;
            SetText(primary, Ui.PrimaryAction(engine)); primary.Enabled = !p.Chill;
            SetText(secondary, Ui.SecondaryAction(engine)); secondary.Enabled = !p.Chill;
            SetText(workMode, Ui.WorkingMode); SetText(chillMode, Ui.ChillMode);
            if (workMode.Primary == p.Chill) { workMode.Primary = !p.Chill; chillMode.Primary = p.Chill; workMode.Invalidate(); chillMode.Invalidate(); }
            SetText(statsButton, Ui.StatisticsButton);
            settingsButton.AccessibleName = Ui.Settings; minimizeButton.AccessibleName = Ui.Minimize; closeButton.AccessibleName = Ui.HideToTray;
            showItem.Text = L.Text("Şövalyeyi göster", "Show knight");
            modeItem.Text = p.Chill ? L.Text("Çalışmaya başla", "Start working") : L.Text("Çalışmıyorum", "Chill mode");
            settingsItem.Text = Ui.Settings; quitItem.Text = L.Text("Çıkış", "Quit");
            string trayText = Ui.Tray(engine);
            tray.Text = trayText.Length > 63 ? trayText.Substring(0, 63) : trayText;
        }
        static void SetText(EmberButton c, string text) { if (c.Text != text) c.Text = text; }
        internal static string TimeText(double seconds) { return Ui.TimeText(seconds); }
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            switch (keyData)
            {
                case Keys.F8: using (var panel = new DeveloperDialog()) panel.ShowDialog(this); return true;
                case Keys.Space: if (!engine.P.Chill) Ui.RunSecondary(engine); return true;
                case Keys.B: if (!engine.P.Chill) Ui.RunPrimary(engine); return true;
                case Keys.C: engine.SetChill(!engine.P.Chill); return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }
        void OpenStats()
        {
            using (var dialog = new StatsDialog(engine.P, delegate { engine.ResetStatistics(); Save(true); Invalidate(); }))
                dialog.ShowDialog(this);
        }
        void OpenSettings()
        {
            audio.Mute();
            using (var settings = new SettingsDialog(engine.P))
            {
                settings.Updating += delegate { BeginInvoke((Action)Quit); };
                if (settings.ShowDialog(this) == DialogResult.OK)
                {
                    var p = engine.P; L.Language = p.Language = settings.Language; p.LightMode = settings.LightMode; Theme.Light = p.LightMode;
                    p.ArmorStyle = settings.ArmorStyle; p.SwordStyle = settings.SwordStyle; p.ShieldStyle = settings.ShieldStyle; PixelArt.Equipped = KnightEquipment.From(p);
                    p.AlwaysOnTop = settings.OnTop; p.Sound = settings.PlaySound; p.AudioEnabled = settings.AudioEnabled;
                    p.FireVolume = settings.FireVolume; p.AmbienceVolume = settings.AmbienceVolume; p.MusicVolume = settings.MusicVolume;
                    p.DailyGoalMinutes = settings.DailyGoal; p.IdlePause = settings.IdlePauseEnabled; p.IdleMinutes = settings.IdleMinutesValue; p.FocusOnDue = settings.FocusOnDue;
                    BackColor = Theme.Background; TopMost = p.AlwaysOnTop;
                    engine.ApplyDurations(settings.Work, settings.Rest, settings.LongEvery, settings.LongMinutes);
                    menu.BackColor = Theme.Panel; menu.ForeColor = Theme.Text; foreach (Control c in Controls) c.Invalidate(); Invalidate();
                }
            }
            audio.Apply(engine.P);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var p = engine.P; Graphics g = e.Graphics; g.Clear(Theme.Background); Dpi.Prepare(g);
            Theme.Frame(g, new Rectangle(0, 0, 384, 668), Theme.Background);
            Theme.Label(g, Ui.Title, 17, Theme.Gold, new Rectangle(20, 14, 246, 32), ContentAlignment.MiddleLeft);
            Theme.Label(g, Ui.Tagline, 8, Theme.Muted, new Rectangle(20, 46, 344, 20), ContentAlignment.MiddleLeft);
            Theme.Frame(g, new Rectangle(18, 114, 348, 188), Theme.Panel);
            using (var scene = PixelArt.Scene(p.Chill ? 1 : engine.Health, p.InBreak, p.Chill ? chillFrame : frame, DayCycle.Hour(p), p.Chill, workRoutine.Action, workRoutine.Frame, kneel))
            { g.InterpolationMode = InterpolationMode.NearestNeighbor; g.PixelOffsetMode = PixelOffsetMode.Half; g.DrawImage(scene, new Rectangle(20, 116, 344, 184)); g.PixelOffsetMode = PixelOffsetMode.Default; }
            Theme.Frame(g, new Rectangle(20, 312, 344, 28), Theme.Panel); Theme.Label(g, Ui.State(engine), 8, Theme.Gold, new Rectangle(24, 312, 336, 28), ContentAlignment.MiddleCenter);
            Theme.Label(g, Ui.Clock(engine), p.Chill ? 32 : 42, Theme.Text, new Rectangle(20, 348, 344, 68), ContentAlignment.MiddleCenter);
            Theme.Label(g, Ui.Subtitle(engine), 9, Theme.Muted, new Rectangle(20, 416, 344, 26), ContentAlignment.MiddleCenter);
            double hp = p.Chill ? 1 : Math.Max(0, Math.Min(1, engine.Health));
            Theme.Label(g, Ui.Health, 8, Theme.Muted, new Rectangle(20, 452, 42, 20), ContentAlignment.MiddleLeft);
            using (var b = new SolidBrush(Theme.Panel)) g.FillRectangle(b, 58, 457, 252, 8);
            using (var b = new SolidBrush(p.InBreak || p.Chill ? Theme.Green : Theme.Gold)) g.FillRectangle(b, 58, 457, (int)(252 * hp), 8);
            using (var pen = new Pen(Theme.Background, 2)) for (int i = 1; i < 21; i++) g.DrawLine(pen, 58 + i * 12, 457, 58 + i * 12, 465);
            Theme.Label(g, ((int)(hp * 100)) + "%", 8, Theme.Muted, new Rectangle(316, 452, 48, 20), ContentAlignment.MiddleCenter);
            Theme.Label(g, Ui.Today, 8, Theme.Muted, new Rectangle(20, 588, 164, 20), ContentAlignment.MiddleCenter);
            Theme.Label(g, Ui.Total, 8, Theme.Muted, new Rectangle(200, 588, 164, 20), ContentAlignment.MiddleCenter);
            Theme.Label(g, Ui.Duration(p.TodayWorkSeconds), 12, Theme.Gold, new Rectangle(20, 608, 164, 24), ContentAlignment.MiddleCenter);
            Theme.Label(g, Ui.Duration(p.TotalWorkSeconds), 12, Theme.Gold, new Rectangle(200, 608, 164, 24), ContentAlignment.MiddleCenter);
            string footer = DateTime.Now < messageUntil ? message : Ui.Footer(p);
            Theme.Label(g, footer, 8, Theme.Muted, new Rectangle(20, 640, 344, 20), ContentAlignment.MiddleCenter);
        }
        protected override void OnFormClosing(FormClosingEventArgs e) { Save(); if (!exiting && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); return; } base.OnFormClosing(e); }
        protected override void Dispose(bool disposing)
        { if (disposing) { SystemEvents.PowerModeChanged -= OnPower; SystemEvents.SessionSwitch -= OnSession; timer.Stop(); timer.Dispose(); audio.Dispose(); tray.Visible = false; tray.Dispose(); menu.Dispose(); if (Icon != null) Icon.Dispose(); } base.Dispose(disposing); }
    }

    static class NativeWindowing
    {
        [StructLayout(LayoutKind.Sequential)]
        struct FlashInfo { public uint Size; public IntPtr Window; public uint Flags; public uint Count; public uint Timeout; }
        [DllImport("user32.dll")] static extern bool FlashWindowEx(ref FlashInfo info);
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr window, int command);
        public static void Flash(IntPtr window)
        {
            // FLASHW_TRAY | FLASHW_TIMERNOFG: flash the taskbar button until the user switches to the window.
            var info = new FlashInfo { Size = (uint)Marshal.SizeOf<FlashInfo>(), Window = window, Flags = 2 | 12, Count = 5 };
            try { FlashWindowEx(ref info); } catch (EntryPointNotFoundException) { } catch (DllNotFoundException) { }
        }
        public static void ShowNoActivate(IntPtr window) { try { ShowWindow(window, 4); } catch (EntryPointNotFoundException) { } catch (DllNotFoundException) { } }
    }

    /// <summary>Time since the last keyboard or mouse input in this session. Reads no input content.</summary>
    static class IdleInput
    {
        [StructLayout(LayoutKind.Sequential)] struct LastInputInfo { public uint Size; public uint Time; }
        [DllImport("user32.dll")] static extern bool GetLastInputInfo(ref LastInputInfo info);
        public static double Seconds()
        {
            var info = new LastInputInfo { Size = (uint)Marshal.SizeOf<LastInputInfo>() };
            try { if (!GetLastInputInfo(ref info)) return 0; } catch (EntryPointNotFoundException) { return 0; } catch (DllNotFoundException) { return 0; }
            return unchecked((uint)Environment.TickCount - info.Time) / 1000.0;
        }
    }

    sealed class PixelLabel : Control
    {
        public float TextSize = 10;
        public Color Ink;
        public PixelLabel(string text, Rectangle rect, Color color, float size) { Text = text; Bounds = rect; Ink = color; TextSize = size; SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true); BackColor = Color.Transparent; }
        protected override void OnPaint(PaintEventArgs e)
        {
            Dpi.Prepare(e.Graphics);
            var area = Dpi.Logical(this);
            using (var brush = new SolidBrush(Ink))
            using (var format = new StringFormat())
            {
                e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.SingleBitPerPixelGridFit;
                format.LineAlignment = Text.Contains("\n") || area.Height > 90 ? StringAlignment.Near : StringAlignment.Center;
                format.Trimming = StringTrimming.EllipsisCharacter;
                e.Graphics.DrawString(Text, PixelFont.Cached(PixelFont.TextPixels(TextSize)), brush, area, format);
            }
        }
    }

    /// <summary>Seven pixel bars for the last week of work, with the daily goal as a dotted line.</summary>
    sealed class WeekChart : Control
    {
        readonly Preferences p;
        public WeekChart(Preferences p, Rectangle bounds)
        {
            this.p = p; Bounds = bounds; AccessibleName = L.Text("Son 7 günün çalışma grafiği", "Work chart for the last 7 days");
            SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.Clear(Theme.Background); Dpi.Prepare(g);
            var area = Dpi.Logical(this);
            var now = DateTime.Now;
            double[] values = Ui.WeekSeconds(p, now); string[] labels = Ui.WeekLabels(now);
            double goal = p.DailyGoalMinutes * 60.0, max = Math.Max(goal, 3600);
            foreach (double v in values) max = Math.Max(max, v);
            int chartTop = 4, chartHeight = area.Height - 26, slot = area.Width / 7;
            for (int i = 0; i < 7; i++)
            {
                int height = (int)Math.Round(chartHeight * values[i] / max);
                int x = i * slot + slot / 4, width = slot / 2;
                using (var b = new SolidBrush(Theme.Panel)) g.FillRectangle(b, x, chartTop, width, chartHeight);
                using (var b = new SolidBrush(goal > 0 && values[i] >= goal ? Theme.Green : Theme.Gold)) g.FillRectangle(b, x, chartTop + chartHeight - height, width, height);
                Theme.Label(g, labels[i], 7, i == 6 ? Theme.Text : Theme.Muted, new Rectangle(i * slot, chartTop + chartHeight + 2, slot, 20), ContentAlignment.MiddleCenter);
            }
            if (goal > 0)
            {
                int y = chartTop + chartHeight - (int)Math.Round(chartHeight * goal / max);
                using (var b = new SolidBrush(Theme.Text)) for (int x = 0; x < area.Width; x += 6) g.FillRectangle(b, x, y, 3, 1);
            }
        }
    }

    sealed class StatsDialog : Form
    {
        readonly System.Windows.Forms.Timer refresh = new System.Windows.Forms.Timer { Interval = 1000 };
        public static string Hours(double seconds) { return Ui.Hours(seconds); }
        public static string Duration(double seconds) { return Ui.Duration(seconds); }
        public StatsDialog(Preferences p) : this(p, null) { }
        public StatsDialog(Preferences p, Action resetStatistics)
        {
            p.CheckDay(); Text = L.Text("Soul Revive · İstatistikler", "Soul Revive · Statistics"); ClientSize = new Size(430, 628); AutoScaleMode = AutoScaleMode.None; BackColor = Theme.Background;
            FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false; StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false; Icon = PixelArt.MakeIcon();
            Add(L.Text("ATEŞİNİN HİKÂYESİ", "YOUR BONFIRE JOURNEY"), 20, 18, 40, Theme.Gold, 16);
            Add(L.Text("TOPLAM ÇALIŞMA", "TOTAL WORK TIME"), 20, 76, 22, Theme.Muted, 9);
            var total = Add("", 20, 104, 62, Theme.Gold, 28);
            var totalTime = Add("", 20, 168, 24, Theme.Muted, 9);
            var today = Add("", 20, 206, 36, Theme.Text, 15);
            var todayTime = Add("", 20, 244, 24, Theme.Muted, 9);
            var sessions = Add("", 20, 284, 28, Theme.Text, 11);
            var movement = Add("", 20, 316, 28, Theme.Text, 11);
            var streak = Add("", 20, 348, 28, Theme.Gold, 11);
            Add(L.Text("SON 7 GÜN", "LAST 7 DAYS"), 20, 384, 24, Theme.Muted, 9);
            var chart = new WeekChart(p, new Rectangle(20, 412, 390, 96)); Controls.Add(chart);
            Add(L.Text("Yalnızca aktif çalışma süresi sayılır.\nDuraklatma, mola ve chill modu hariç.", "Counts active work time only.\nPause, break and chill time are excluded."), 20, 516, 48, Theme.Muted, 9);
            Action update = delegate {
                p.CheckDay();
                total.Text = Ui.Hours(p.TotalWorkSeconds) + L.Text(" saat", " hours");
                totalTime.Text = Ui.Duration(p.TotalWorkSeconds);
                today.Text = L.Text("BUGÜN ", "TODAY ") + Ui.Hours(p.TodayWorkSeconds) + L.Text(" saat", " hours");
                todayTime.Text = Ui.Duration(p.TodayWorkSeconds) + (p.DailyGoalMinutes > 0 ? "  ·  " + Ui.Goal(p) : "");
                sessions.Text = p.CompletedSessions + L.Text(" tamamlanan tur · bugün ", " completed rounds · today ") + p.TodaySessions;
                movement.Text = L.Text("Bugün: ", "Today: ") + p.Breaks + L.Text(" mola / ", " breaks / ") + Ui.Minutes(p.MovementSeconds) + L.Text(" dk hareket", " min movement");
                streak.Text = Ui.StreakText(p, DateTime.Now);
                Invalidate(true);
            };
            update();
            refresh.Tick += delegate { update(); };
            refresh.Start();
            var reset = new EmberButton(L.Text("Sıfırla", "Reset"), new Rectangle(20, 576, 185, 36), false);
            reset.Enabled = resetStatistics != null;
            reset.Click += delegate
            {
                if (MessageBox.Show(this, L.Text("Tüm çalışma istatistikleri ve geçmiş silinsin mi? Bu geri alınamaz.", "Delete all work statistics and history? This cannot be undone."),
                    "Soul Revive", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
                resetStatistics(); update();
            };
            Controls.Add(reset);
            var close = new EmberButton(L.Text("Ateşe dön", "Back to the fire"), new Rectangle(225, 576, 185, 36), true); close.DialogResult = DialogResult.OK; Controls.Add(close); AcceptButton = close; CancelButton = close;
            Dpi.Apply(this);
        }
        PixelLabel Add(string s, int x, int y, int height, Color color, float size)
        {
            var label = new PixelLabel(s, new Rectangle(x, y, 390, height), color, size);
            Controls.Add(label);
            return label;
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) { refresh.Dispose(); if (Icon != null) Icon.Dispose(); }
            base.Dispose(disposing);
        }
    }

    sealed class SettingsDialog : Form
    {
        readonly NumericUpDown work, rest, longEvery, longMinutes, dailyGoal, idleMinutes;
        readonly CheckBox top, sound, startup, audioEnabled, idlePause, focusOnDue;
        readonly PixelCombo language, theme, armor, sword, shield;
        readonly PixelSlider fire, ambience, music;
        readonly Soundscape preview = new Soundscape();
        readonly Panel generalPanel, timePanel, soundPanel, equipmentPanel, updatePanel;
        readonly List<Panel> panels = new List<Panel>();
        readonly List<EmberButton> tabs = new List<EmberButton>();
        readonly EmberButton save, cancel, check, install;
        UpdateInfo available;
        bool busy;
        public event Action Updating;
        public int Work { get { return (int)work.Value; } }
        public int Rest { get { return (int)rest.Value; } }
        public int LongEvery { get { return (int)longEvery.Value; } }
        public int LongMinutes { get { return (int)longMinutes.Value; } }
        public int DailyGoal { get { return (int)dailyGoal.Value; } }
        public int IdleMinutesValue { get { return (int)idleMinutes.Value; } }
        public bool IdlePauseEnabled { get { return idlePause.Checked; } }
        public bool FocusOnDue { get { return focusOnDue.Checked; } }
        public bool OnTop { get { return top.Checked; } }
        public bool PlaySound { get { return sound.Checked; } }
        public string Language { get { return language.SelectedIndex == 1 ? "en" : "tr"; } }
        public bool LightMode { get { return theme.SelectedIndex == 1; } }
        public int ArmorStyle { get { return armor.SelectedIndex; } }
        public int SwordStyle { get { return sword.SelectedIndex; } }
        public int ShieldStyle { get { return shield.SelectedIndex; } }
        public bool AudioEnabled { get { return audioEnabled.Checked; } }
        public int FireVolume { get { return fire.Value; } }
        public int AmbienceVolume { get { return ambience.Value; } }
        public int MusicVolume { get { return music.Value; } }
        public SettingsDialog(Preferences p)
        {
            Text = L.Text("Soul Revive · Ayarlar", "Soul Revive · Settings"); ClientSize = new Size(540, 626); AutoScaleMode = AutoScaleMode.None;
            BackColor = Theme.Background; ForeColor = Theme.Text; Font = PixelFont.Create(22); Icon = PixelArt.MakeIcon();
            FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false; StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false;
            Add(this, L.Text("ATEŞİNİN RİTMİ", "YOUR BONFIRE ROUTINE"), 20, 14, 500, 38, Theme.Gold, 17);
            generalPanel = Section(L.Text("GENEL", "GENERAL"));
            timePanel = Section(L.Text("ZAMAN", "TIMER"));
            soundPanel = Section(L.Text("SESLER", "SOUNDS"));
            equipmentPanel = Section(L.Text("ŞÖVALYE", "KNIGHT"));
            updatePanel = Section(L.Text("SÜRÜM", "UPDATES"));

            // Knight
            Add(equipmentPanel, L.Text("Zırh", "Armor"), 0, 4, 150, 36, Theme.Text, 12);
            armor = Combo(equipmentPanel, 180, 4, Ui.ArmorNames, Math.Max(0, Math.Min(2, p.ArmorStyle)));
            Add(equipmentPanel, L.Text("Kılıç", "Sword"), 0, 60, 150, 36, Theme.Text, 12);
            sword = Combo(equipmentPanel, 180, 60, Ui.SwordNames, Math.Max(0, Math.Min(2, p.SwordStyle)));
            Add(equipmentPanel, L.Text("Kalkan", "Shield"), 0, 116, 150, 36, Theme.Text, 12);
            shield = Combo(equipmentPanel, 180, 116, Ui.ShieldNames, Math.Max(0, Math.Min(2, p.ShieldStyle)));
            Add(equipmentPanel, L.Text("EKİPMAN ÖNİZLEMESİ", "EQUIPMENT PREVIEW"), 0, 172, 500, 30, Theme.Gold, 11);
            var equipmentPreview = new EquipmentPreview { Bounds = new Rectangle(78, 212, 344, 184), Selection = delegate { return new KnightEquipment(ArmorStyle, SwordStyle, ShieldStyle); } }; equipmentPanel.Controls.Add(equipmentPreview);
            foreach (var picker in new[] { armor, sword, shield }) picker.SelectedIndexChanged += delegate { equipmentPreview.Invalidate(); };
            Add(equipmentPanel, L.Text("Seçimlerini uygulamak için Kaydet.", "Select Save to equip your choices."), 0, 410, 500, 28, Theme.Muted, 10);

            // General
            Add(generalPanel, L.Text("Dil", "Language"), 0, 4, 150, 30, Theme.Text, 11); language = Combo(generalPanel, 180, 4, Ui.LanguageNames, p.Language == "en" ? 1 : 0);
            Add(generalPanel, L.Text("Görünüm", "Appearance"), 0, 52, 150, 30, Theme.Text, 11); theme = Combo(generalPanel, 180, 52, Ui.ThemeNames, p.LightMode ? 1 : 0);
            top = Check(generalPanel, L.Text("Her zaman üstte göster", "Always on top"), 104, p.AlwaysOnTop);
            sound = Check(generalPanel, L.Text("Mola bildirim sesi", "Break reminder sound"), 140, p.Sound);
            startup = Check(generalPanel, L.Text("Windows açılışında tepside başlat", "Start in the tray at Windows sign-in"), 176, Startup.Enabled);
            Add(generalPanel, L.Text("Ekran kilidi ve uyku çalışmayı duraklatır.\nMolada sen hareket et; şövalyen dinlensin.\nKısayollar: Boşluk duraklat · B mola · C chill · F8 panel", "Lock and sleep pause your work timer.\nMove during breaks while your knight rests.\nKeys: Space pause · B break · C chill · F8 panel"), 0, 222, 500, 96, Theme.Muted, 9);
            var developer = new EmberButton(L.Text("Geliştirici paneli / F8", "Developer panel / F8"), new Rectangle(0, 412, 500, 28), false);
            developer.Click += delegate { using (var panel = new DeveloperDialog()) panel.ShowDialog(this); }; generalPanel.Controls.Add(developer);

            // Timer
            Add(timePanel, L.Text("Çalışma süresi (dakika)", "Work time (minutes)"), 0, 4, 345, 30, Theme.Text, 11); work = Number(timePanel, 382, 4, 1, 180, p.WorkMinutes, L.Text("Çalışma süresi", "Work duration"));
            Add(timePanel, L.Text("Mola süresi (dakika)", "Break time (minutes)"), 0, 48, 345, 30, Theme.Text, 11); rest = Number(timePanel, 382, 48, 1, 30, p.BreakMinutes, L.Text("Mola süresi", "Break duration"));
            Add(timePanel, L.Text("Uzun mola: her kaç turda (0 = kapalı)", "Long break every N rounds (0 = off)"), 0, 92, 375, 30, Theme.Text, 11); longEvery = Number(timePanel, 382, 92, 0, 12, p.LongBreakEvery, L.Text("Uzun mola aralığı", "Long break interval"));
            Add(timePanel, L.Text("Uzun mola süresi (dakika)", "Long break time (minutes)"), 0, 136, 345, 30, Theme.Text, 11); longMinutes = Number(timePanel, 382, 136, 1, 60, p.LongBreakMinutes, L.Text("Uzun mola süresi", "Long break duration"));
            Add(timePanel, L.Text("Günlük hedef (dakika, 0 = yok)", "Daily goal (minutes, 0 = none)"), 0, 180, 375, 30, Theme.Text, 11); dailyGoal = Number(timePanel, 382, 180, 0, 720, p.DailyGoalMinutes, L.Text("Günlük hedef", "Daily goal"));
            idlePause = Check(timePanel, L.Text("Klavye/fare kullanılmazsa duraklat", "Pause when keyboard/mouse are idle"), 226, p.IdlePause);
            Add(timePanel, L.Text("Boşta sayılma süresi (dakika)", "Idle after (minutes)"), 0, 264, 345, 30, Theme.Text, 11); idleMinutes = Number(timePanel, 382, 264, 1, 60, p.IdleMinutes, L.Text("Boşta süresi", "Idle time"));
            focusOnDue = Check(timePanel, L.Text("Mola zamanı pencereyi öne getir", "Bring the window forward when a break is due"), 308, p.FocusOnDue);
            Add(timePanel, L.Text("Süreyi değiştirmek yeni bir tur başlatır.\nBoşta algılama yalnızca son giriş zamanını okur;\nboşta geçen süre çalışmadan düşülür.", "Changing durations starts a new round.\nIdle detection only reads the last input time;\nidle time is removed from your work."), 0, 346, 500, 90, Theme.Muted, 9);

            // Sounds
            audioEnabled = Check(soundPanel, L.Text("Müzik ve ortam seslerini çal", "Play music and ambience"), 0, p.AudioEnabled);
            fire = Volume(soundPanel, L.Text("ATEŞ / ÇITIRTI", "FIRE / CRACKLE"), 64, p.FireVolume);
            ambience = Volume(soundPanel, L.Text("AMBİYANS / RÜZGÂR", "AMBIENCE / WIND"), 154, p.AmbienceVolume);
            music = Volume(soundPanel, L.Text("MÜZİK / BONFIRE MELODİSİ", "MUSIC / BONFIRE MELODY"), 244, p.MusicVolume);
            var listen = new EmberButton(L.Text("Önizle / durdur", "Preview / stop"), new Rectangle(0, 348, 244, 36), false); bool listening = false;
            Action play = delegate { preview.Apply(new Preferences { AudioEnabled = true, FireVolume = fire.Value, AmbienceVolume = ambience.Value, MusicVolume = music.Value }); };
            listen.Click += delegate { listening = !listening; listen.Text = listening ? L.Text("Önizlemeyi durdur", "Stop preview") : L.Text("Önizle / durdur", "Preview / stop"); if (listening) play(); else preview.Mute(); }; soundPanel.Controls.Add(listen);
            foreach (var slider in new[] { fire, ambience, music }) slider.MouseUp += delegate { if (listening) play(); };
            var soundStatus = new PixelLabel(L.Text("Kaydet sonrası arka planda çalar.\nTüm sesler çevrimdışı çalışır.", "Select Save to keep audio playing in the background.\nAll sounds work offline."), new Rectangle(0, 394, 500, 48), Theme.Muted, 9);
            soundPanel.Controls.Add(soundStatus);
            preview.Failed += delegate (string error) {
                if (IsDisposed || !IsHandleCreated) return;
                BeginInvoke((Action)delegate {
                    listening = false;
                    listen.Text = L.Text("Önizle / durdur", "Preview / stop");
                    soundStatus.Text = L.Text("Ses başlatılamadı: ", "Audio could not start: ") + error;
                    soundStatus.Invalidate();
                });
            };

            // Updates
            Add(updatePanel, L.Text("YÜKLÜ SÜRÜM / ", "INSTALLED VERSION / ") + Updates.VersionText, 0, 4, 500, 32, Theme.Gold, 13);
            Add(updatePanel, L.Text("Yeni sürümleri buradan kontrol et.\nOnayladığında indirilir, doğrulanır ve yeniden açılır.\nAyarların ve istatistiklerin korunur.", "Check for new versions here.\nApproved updates download, verify and restart.\nYour settings and statistics are preserved."), 0, 52, 500, 86, Theme.Text, 10);
            var status = new PixelLabel(L.Text("Kontrol için internet bağlantısı gerekir.", "An internet connection is needed to check."), new Rectangle(0, 218, 500, 140), Theme.Muted, 10); updatePanel.Controls.Add(status);
            check = new EmberButton(L.Text("Güncellemeleri kontrol et", "Check for updates"), new Rectangle(0, 156, 500, 42), false); updatePanel.Controls.Add(check);
            install = new EmberButton(L.Text("İndir ve güncelle", "Download and update"), new Rectangle(0, 376, 500, 42), true); install.Enabled = false; updatePanel.Controls.Add(install);
            check.Click += async delegate
            {
                available = null; SetBusy(true); status.Text = L.Text("Yayınlar kontrol ediliyor...", "Checking releases..."); status.Invalidate();
                try { available = await Task.Factory.StartNew<UpdateInfo>(Updates.Check); if (IsDisposed) return; status.Text = available == null ? L.Text("Güncelsin. Ateşin yanmaya devam ediyor.", "You are up to date. Your fire keeps burning.") : L.Text("Yeni sürüm hazır: ", "New version available: ") + available.Version.ToString(3) + "\n" + L.Text("İndir ve güncelle ile devam et.", "Select Download and update to continue."); }
                catch (Exception ex) { if (!IsDisposed) status.Text = ex.Message; }
                finally { if (!IsDisposed) { SetBusy(false); status.Invalidate(); } }
            };
            install.Click += async delegate
            {
                if (available == null) return;
                if (MessageBox.Show(this, L.Text("Güncelleme indirilip doğrulanacak. Soul Revive kapanıp yeniden açılacak. Devam edilsin mi?", "The update will download and verify, then Soul Revive will close and reopen. Continue?"), "Soul Revive · " + available.Version.ToString(3), MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                SetBusy(true);
                try
                {
                    await Task.Factory.StartNew(delegate { Updates.DownloadAndStage(available, delegate (int n) { if (!IsDisposed && IsHandleCreated) BeginInvoke((Action)delegate { status.Text = L.Text("İndiriliyor: %", "Downloading: %") + n; status.Invalidate(); }); }); });
                    if (Updating != null) Updating(); DialogResult = DialogResult.Abort; Close();
                }
                catch (Exception ex) { status.Text = ex.Message; SetBusy(false); status.Invalidate(); }
            };
            save = new EmberButton(L.Text("Kaydet", "Save"), new Rectangle(278, 570, 242, 38), true); cancel = new EmberButton(L.Text("Vazgeç", "Cancel"), new Rectangle(20, 570, 242, 38), false);
            cancel.DialogResult = DialogResult.Cancel; save.Click += delegate { try { if (startup.Checked != Startup.Enabled) Startup.Set(startup.Checked); DialogResult = DialogResult.OK; Close(); } catch (Exception ex) { MessageBox.Show(this, ex.Message, "Soul Revive", MessageBoxButtons.OK, MessageBoxIcon.Warning); } };
            Controls.Add(save); Controls.Add(cancel); AcceptButton = save; CancelButton = cancel; ShowSection(0);
            Dpi.Apply(this);
        }
        void SetBusy(bool value) { busy = value; save.Enabled = cancel.Enabled = !value; check.Enabled = !value; install.Enabled = !value && available != null; }
        internal void ShowSection(int index)
        {
            for (int i = 0; i < panels.Count; i++) { panels[i].Visible = i == index; tabs[i].Primary = i == index; tabs[i].Invalidate(); }
        }
        internal int SectionCount { get { return panels.Count; } }
        protected override void OnFormClosing(FormClosingEventArgs e) { if (busy && DialogResult != DialogResult.Abort) e.Cancel = true; base.OnFormClosing(e); }
        protected override void Dispose(bool disposing) { if (disposing) { preview.Dispose(); if (Icon != null) Icon.Dispose(); } base.Dispose(disposing); }
        Panel Section(string title)
        {
            int index = panels.Count;
            var panel = new Panel { Bounds = new Rectangle(20, 116, 500, 440), BackColor = Theme.Background }; Controls.Add(panel); panels.Add(panel);
            var tab = new EmberButton(title, new Rectangle(20 + index * 101, 66, 96, 34), false);
            tab.Click += delegate { ShowSection(index); }; Controls.Add(tab); tabs.Add(tab);
            return panel;
        }
        static void Add(Control host, string s, int x, int y, int w, int h, Color color, float size) { host.Controls.Add(new PixelLabel(s, new Rectangle(x, y, w, h), color, size)); }
        static NumericUpDown Number(Control host, int x, int y, int min, int max, int value, string name)
        { var n = new NumericUpDown { Bounds = new Rectangle(x, y, 118, 36), Minimum = min, Maximum = max, Value = Math.Max(min, Math.Min(max, value)), Font = PixelFont.Create(20), BackColor = Theme.Panel, ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle, AccessibleName = name }; host.Controls.Add(n); return n; }
        static CheckBox Check(Control host, string text, int y, bool value) { var c = new CheckBox { Text = text, Checked = value, Bounds = new Rectangle(0, y, 500, 30), UseCompatibleTextRendering = true, FlatStyle = FlatStyle.Flat }; host.Controls.Add(c); return c; }
        static PixelCombo Combo(Control host, int x, int y, string[] items, int selected) { var c = new PixelCombo { Bounds = new Rectangle(x, y, 320, 36) }; c.Items.AddRange(items); c.SelectedIndex = selected; host.Controls.Add(c); return c; }
        static PixelSlider Volume(Control host, string text, int y, int value)
        {
            var label = new PixelLabel(text + " / " + value + "%", new Rectangle(0, y, 500, 28), Theme.Gold, 11); host.Controls.Add(label);
            var slider = new PixelSlider { Bounds = new Rectangle(0, y + 30, 500, 42), Minimum = 0, Maximum = 100, TickFrequency = 10, Value = value, BackColor = Theme.Background, AccessibleName = text, SmallChange = 5, LargeChange = 10 };
            slider.ValueChanged += delegate { label.Text = text + " / " + slider.Value + "%"; label.Invalidate(); }; host.Controls.Add(slider); return slider;
        }
    }
}
