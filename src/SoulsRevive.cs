using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace SoulsRevive
{
    static class L
    {
        public static string Language = "tr";
        public static string Text(string tr, string en) { return Language == "en" ? en : tr; }
    }
    [DataContract]
    public sealed class Preferences
    {
        [DataMember] public string Language = "tr";
        [DataMember] public double WorkLeftBeforeBreak;
        [DataMember] public int WorkMinutes = 30;
        [DataMember] public int BreakMinutes = 3;
        [DataMember] public bool AlwaysOnTop = true;
        [DataMember] public bool Sound = true;
        [DataMember] public int X = int.MinValue;
        [DataMember] public int Y = int.MinValue;
        [DataMember] public string Day = "";
        [DataMember] public int Breaks;
        [DataMember] public double MovementSeconds;
        [DataMember] public double Remaining = 1800;
        [DataMember] public bool InBreak;
        [DataMember] public bool Paused;

        public void Validate()
        {
            WorkMinutes = Math.Max(1, Math.Min(180, WorkMinutes));
            BreakMinutes = Math.Max(1, Math.Min(30, BreakMinutes));
            Remaining = double.IsNaN(Remaining) || double.IsInfinity(Remaining) ? WorkMinutes * 60 :
                Math.Max(0, Math.Min(Remaining, (InBreak ? BreakMinutes : WorkMinutes) * 60));
            Breaks = Math.Max(0, Breaks);
            if (double.IsNaN(MovementSeconds) || double.IsInfinity(MovementSeconds)) MovementSeconds = 0;
            MovementSeconds = Math.Max(0, MovementSeconds);
            CheckDay();
        }
        public void CheckDay()
        {
            string today = DateTime.Now.ToString("yyyy-MM-dd");
            if (Day != today) { Day = today; Breaks = 0; MovementSeconds = 0; }
        }
    }

    public sealed class TimerEngine
    {
        public readonly Preferences P;
        public event Action<string> Changed;
        public TimerEngine(Preferences p) { P = p; p.Validate(); }
        public bool Due { get { return !P.InBreak && P.Remaining <= 0; } }
        public double Health
        {
            get { return P.InBreak ? 1 - (1 - P.WorkLeftBeforeBreak / (P.WorkMinutes * 60.0)) * P.Remaining / (P.BreakMinutes * 60.0) : P.Remaining / (P.WorkMinutes * 60.0); }
        }
        public void Advance(double seconds)
        {
            P.CheckDay();
            if (P.Paused || seconds <= 0 || seconds > 5 || double.IsNaN(seconds)) return;
            double before = P.Remaining;
            double used = Math.Min(seconds, before);
            P.Remaining = Math.Max(0, before - seconds);
            if (P.InBreak) P.MovementSeconds += used;
            if (before > 0 && P.Remaining == 0)
            {
                if (P.InBreak)
                {
                    P.Breaks++;
                    P.InBreak = false;
                    P.Remaining = P.WorkMinutes * 60;
                    Signal("revived");
                }
                else Signal("due");
            }
        }
        public void BeginBreak()
        {
            if (P.InBreak) return;
            P.WorkLeftBeforeBreak = P.Remaining;
            P.InBreak = true; P.Paused = false; P.Remaining = P.BreakMinutes * 60; Signal("break");
        }
        public void CancelBreak()
        {
            if (!P.InBreak) return;
            // Returning to work starts a full round without counting a completed break.
            P.InBreak = false; P.Remaining = P.WorkMinutes * 60; P.Paused = false; Signal("cancel");
        }
        public void TogglePause() { P.Paused = !P.Paused; Signal("pause"); }
        public void ApplyDurations(int work, int rest)
        {
            if (P.WorkMinutes != work || P.BreakMinutes != rest)
            {
                P.WorkMinutes = work; P.BreakMinutes = rest;
                P.InBreak = false; P.Remaining = work * 60; P.Paused = false;
            }
            Signal("settings");
        }
        void Signal(string reason) { if (Changed != null) Changed(reason); }
    }

    static class Storage
    {
        public static string DirectoryPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SoulsRevive");
        public static string SettingsPath { get { return Path.Combine(DirectoryPath, "settings.json"); } }
        public static string LastError = "";
        public static Preferences Load()
        {
            foreach (string path in new[] { SettingsPath, SettingsPath + ".bak" })
            {
                try
                {
                    if (File.Exists(path))
                        using (var f = File.OpenRead(path))
                        {
                            var p = (Preferences)new DataContractJsonSerializer(typeof(Preferences)).ReadObject(f);
                            if (p != null) return p;
                        }
                }
                catch (Exception ex) { LastError = ex.Message; }
            }
            return new Preferences();
        }
        public static bool Save(Preferences p)
        {
            try
            {
                Directory.CreateDirectory(DirectoryPath);
                string tmp = SettingsPath + ".tmp";
                using (var f = File.Create(tmp)) new DataContractJsonSerializer(typeof(Preferences)).WriteObject(f, p);
                if (File.Exists(SettingsPath)) File.Replace(tmp, SettingsPath, SettingsPath + ".bak");
                else File.Move(tmp, SettingsPath);
                LastError = ""; return true;
            }
            catch (Exception ex) { LastError = ex.Message; return false; }
        }
    }

    static class Startup
    {
        const string Key = @"Software\Microsoft\Windows\CurrentVersion\Run";
        public static bool Enabled
        {
            get
            {
                using (var key = Registry.CurrentUser.OpenSubKey(Key))
                    return key != null && string.Equals(key.GetValue("SoulsRevive") as string, Command, StringComparison.OrdinalIgnoreCase);
            }
        }
        static string Command { get { return "\"" + Application.ExecutablePath + "\" --startup"; } }
        public static void Set(bool enabled)
        {
            using (var key = Registry.CurrentUser.CreateSubKey(Key))
                if (enabled) key.SetValue("SoulsRevive", Command, RegistryValueKind.String);
                else key.DeleteValue("SoulsRevive", false);
        }
    }

    static class Theme
    {
        public static readonly Color Background = Color.FromArgb(19, 21, 26);
        public static readonly Color Panel = Color.FromArgb(27, 29, 35);
        public static readonly Color Line = Color.FromArgb(59, 53, 44);
        public static readonly Color Gold = Color.FromArgb(224, 177, 103);
        public static readonly Color Text = Color.FromArgb(235, 230, 216);
        public static readonly Color Muted = Color.FromArgb(151, 151, 156);
        public static readonly Color Green = Color.FromArgb(132, 186, 154);
        public static void Label(Graphics g, string s, float size, Color color, Rectangle rect, ContentAlignment align, string family)
        {
            using (Font f = new Font(family, size, FontStyle.Regular, GraphicsUnit.Point))
            {
                TextFormatFlags flags = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis;
                if (align == ContentAlignment.MiddleCenter) flags |= TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter;
                else flags |= TextFormatFlags.Left | TextFormatFlags.VerticalCenter;
                TextRenderer.DrawText(g, s, f, rect, color, flags);
            }
        }
    }

    public sealed class EmberButton : Button
    {
        public bool Primary;
        bool hover;
        public EmberButton(string text, Rectangle bounds, bool primary)
        {
            Text = text; Bounds = bounds; Primary = primary; Cursor = Cursors.Hand;
            AccessibleName = text; FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0;
            SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
        }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Color bg = Primary ? (hover ? Color.FromArgb(229, 178, 101) : Theme.Gold) : (hover ? Color.FromArgb(45, 46, 53) : Theme.Panel);
            using (var b = new SolidBrush(bg)) g.FillRectangle(b, ClientRectangle);
            using (var p = new Pen(Primary ? Theme.Gold : Theme.Line)) g.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
            Theme.Label(g, Text, 10, Primary ? Theme.Background : Theme.Text, ClientRectangle, ContentAlignment.MiddleCenter, "Segoe UI");
            if (Focused) ControlPaint.DrawFocusRectangle(g, new Rectangle(4, 4, Width - 8, Height - 8));
        }
    }

    // Original pixel artwork, rendered on a 172 x 92 canvas with integer pixels.
    static class PixelArt
    {
        static void R(Graphics g, string hex, int x, int y, int w, int h)
        { using (var b = new SolidBrush(ColorTranslator.FromHtml(hex))) g.FillRectangle(b, x, y, w, h); }
        static void Poly(Graphics g, string hex, params int[] points)
        {
            Point[] p = new Point[points.Length / 2];
            for (int i = 0; i < p.Length; i++) p[i] = new Point(points[i * 2], points[i * 2 + 1]);
            using (var b = new SolidBrush(ColorTranslator.FromHtml(hex))) g.FillPolygon(b, p);
        }
        public static Bitmap Scene(double health, bool rest, int frame)
        {
            var bitmap = new Bitmap(172, 92);
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                g.Clear(ColorTranslator.FromHtml("#121720"));
                // Distant moon, broken keep, and mountain silhouettes.
                R(g,"#333947",135,11,13,13); R(g,"#59606A",137,12,9,9); R(g,"#A2A394",138,12,6,7);
                R(g,"#121720",135,9,8,9);
                Poly(g,"#1B2330",0,62,22,43,44,59,74,29,111,61,140,37,172,56,172,86,0,86);
                R(g,"#242A35",17,26,13,43); R(g,"#242A35",13,23,5,7); R(g,"#242A35",22,20,4,8); R(g,"#242A35",29,23,5,7);
                R(g,"#1C222C",18,31,3,35); R(g,"#30333A",25,37,3,6);
                R(g,"#1C232E",34,43,15,32); R(g,"#252D38",39,35,6,12);
                R(g,"#333644",23,16,1,5); R(g,"#66504A",24,16,8,3);
                R(g,"#1A202A",144,46,17,31); R(g,"#272D37",147,39,5,10); R(g,"#272D37",156,41,4,8);
                for (int i = 0; i < 21; i++)
                {
                    int x = (i * 47 + 9) % 172, y = (i * 19 + 7) % 47;
                    R(g, i % 5 == 0 ? "#69717B" : "#353D49", x, y, 1, 1);
                }
                R(g,"#1E242B",0,73,172,19); R(g,"#2A2D31",0,76,172,2);
                for (int i = 0; i < 16; i++) R(g,"#363638",(i*31)%172,79+(i%4)*3,8,1);
                R(g,"#11151A",55,81,57,4);
                // Bonfire and its buried sword.
                R(g,"#392C26",111,73,32,9); R(g,"#593C2A",115,74,24,7);
                R(g,"#A36637",118,78,20,2); R(g,"#805338",115,80,14,2);
                double beat = 2 * Math.PI * (frame % 16) / 16;
                int flicker = (int)Math.Round(2 * Math.Sin(beat) + Math.Sin(3 * beat));
                Poly(g,"#A24628",119,77,116,68,121,60-flicker,124,67,128,53+flicker,132,66,136,63,139,74,134,79);
                Poly(g,"#E6853E",121,76,120,69,125,64,128,58+flicker,132,70,135,72,133,77);
                Poly(g,"#FFD48B",125,77,124,72,128,65+flicker,130,73,132,77);
                R(g,"#B9BBB0",128,43,2,25); R(g,"#5E646B",130,44,1,23);
                R(g,"#99794B",124,43,10,2); R(g,"#67543F",128,36,2,7); R(g,"#B6A071",127,35,4,2);
                for (int i=0;i<8;i++)
                {
                    int rise = (frame * 2 + i * 7) % 31;
                    int drift = (int)Math.Round(rise * .15 + 2 * Math.Sin(beat + i));
                    R(g, rise < 17 ? "#DF9250" : "#8F4D31",120+(i*11)%16+drift,67-rise,1,1);
                }
                int bob = rest ? -(int)Math.Round(2 * Math.Abs(Math.Sin(beat))) : (int)Math.Round(Math.Sin(beat / 2 + (frame / 16 % 2) * Math.PI));
                int slump = health <= 0 && !rest ? 7 : 0;
                int ox = rest ? 80 : 77, oy = 26 + bob + slump;
                var saved = g.Save(); g.TranslateTransform(ox, oy);
                // Tattered rust-red cloak, steel plate, chainmail and great helm.
                int wind = (int)Math.Round(3 * Math.Sin(beat));
                Poly(g,"#292029",-10,19,-18,30,-21+wind,49,-15+wind,46,-12+wind,50,-9,46,-5,49,1,31);
                Poly(g,"#713B39",-9,20,-15,29,-17+wind,44,-13+wind,42,-11+wind,46,-9,41,-5,44,0,25);
                R(g,"#9F5546",-11,25,3,13);
                if (rest)
                {
                    DrawWalkingLeg(g, 6, beat + Math.PI, bob, "#53606C");
                    DrawWalkingLeg(g, -5, beat, bob, "#87949F");
                }
                else if (slump == 0)
                {
                    R(g,"#22272E",-7,35,8,18); R(g,"#22272E",4,35,8,18);
                    R(g,"#58626E",-6,36,6,13); R(g,"#7F8B97",-6,37,2,10);
                    R(g,"#485360",5,36,6,13); R(g,"#707D89",5,37,2,10);
                    R(g,"#A1ABB2",-7,39,7,3); R(g,"#8A97A0",4,39,8,3);
                    R(g,"#2E3540",-10,49,11,5); R(g,"#7F8A93",-9,49,9,2);
                    R(g,"#2E3540",4,49,12,5); R(g,"#7F8A93",5,49,9,2);
                }
                else
                {
                    R(g,"#46505E",-6,34,17,7); R(g,"#7B8791",-5,34,14,2);
                    R(g,"#3A424E",5,39,13,7); R(g,"#818B95",6,39,11,2);
                    R(g,"#2E3540",-9,40,8,7); R(g,"#7F8A93",-8,40,6,2);
                }
                Poly(g,"#242A32",-9,17,9,17,14,25,10,35,-8,35,-13,25);
                Poly(g,"#717F8C",-7,18,7,18,11,26,7,31,-6,31,-9,25);
                Poly(g,"#A8B4BC",-7,18,1,18,-2,29,-6,28,-8,24);
                R(g,"#D0D3CD",-7,19,11,2); R(g,"#455361",1,23,8,3);
                R(g,"#8C754F",-8,31,17,3); R(g,"#C5AD77",-1,31,3,3);
                R(g,"#424A55",-12,21,6,13); R(g,"#96A3AC",-13,20,7,5);
                R(g,"#D1D6D0",-12,20,5,2);
                R(g,"#424A55",9,21,6,14); R(g,"#96A3AC",8,20,8,5); R(g,"#D1D6D0",9,20,5,2);
                // Kite shield, with an amber cross.
                Poly(g,"#252A32",-19,28,-7,26,-5,41,-11,48,-19,41);
                Poly(g,"#6E747A",-18,29,-8,28,-7,40,-11,45,-17,40);
                Poly(g,"#354650",-16,30,-9,29,-9,39,-12,43,-15,39);
                R(g,"#B9A16D",-13,31,2,10); R(g,"#B9A16D",-16,34,7,2);
                // Long sword held by the right hand.
                R(g,"#DFDCD0",18,16,2,24); R(g,"#8B9DA7",20,17,1,23); R(g,"#E4E1CF",19,13,1,4);
                R(g,"#AD8C58",14,40,10,2); R(g,"#605449",18,42,2,7); R(g,"#89949B",14,34,5,5);
                // Tall enclosed helm and narrow glowing visor.
                if (!rest && health < .25) g.TranslateTransform(1, 2 + Math.Max(bob, 0));
                R(g,"#242A33",-6,1,14,18); R(g,"#7F8B95",-7,4,16,13);
                R(g,"#B3BEC2",-5,1,11,3); R(g,"#D1D8D2",-5,4,3,9);
                R(g,"#A9B5BB",-2,4,8,4); R(g,"#566470",6,5,3,12);
                R(g,"#272E37",-6,9,13,3); R(g, health < .25 && !rest ? "#E48F63" : "#D4BB85",-4,10,8,1);
                R(g,"#C0C8C8",-1,6,2,11); R(g,"#56616A",-5,14,3,1); R(g,"#56616A",3,14,2,1);
                R(g,"#87949B",-5,17,11,2); R(g,"#393F48",-3,19,7,2);
                g.Restore(saved);
                // Foreground tufts and stones anchor the scene.
                R(g,"#4A4740",45,82,7,3); R(g,"#636055",46,81,4,1);
                R(g,"#343B38",9,79,1,5); R(g,"#41493E",10,81,2,1);
                R(g,"#343B38",158,79,1,5); R(g,"#41493E",156,81,2,1);
            }
            return bitmap;
        }
        static void DrawWalkingLeg(Graphics g, int hip, double phase, int bob, string steel)
        {
            int stride = (int)Math.Round(6 * Math.Sin(phase));
            int lift = (int)Math.Round(4 * Math.Max(0, Math.Cos(phase)));
            int knee = hip + stride / 2 + lift;
            int foot = hip + stride;
            int sole = 53 - lift - bob;
            Poly(g,"#323C48",hip-3,34,hip+4,34,knee+4,43,foot+4,sole,foot-3,sole,knee-3,43);
            R(g,steel,knee-3,40-lift/2,7,4);
            R(g,"#394450",foot-3,sole-4,10,5);
            R(g,steel,foot-2,sole-4,7,2);
        }
        public static Icon MakeIcon()
        {
            using (var bmp = new Bitmap(32,32))
            {
                using(var g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.Transparent);
                    R(g,"#303A48",7,5,18,24); R(g,"#96A5AF",8,6,16,20);
                    R(g,"#C8D0CD",9,7,4,16); R(g,"#455363",21,8,3,17);
                    R(g,"#18212B",8,13,16,4); R(g,"#E7B569",10,14,12,2);
                    R(g,"#C8D0CD",15,9,3,16); R(g,"#64727D",10,26,12,3);
                }
                IntPtr h = bmp.GetHicon();
                try { return (Icon)Icon.FromHandle(h).Clone(); }
                finally { DestroyIcon(h); }
            }
        }
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr h);
    }

    public sealed class Companion : Form
    {
        readonly TimerEngine engine;
        readonly Action refreshLanguage;
        readonly System.Windows.Forms.Timer timer;
        readonly NotifyIcon tray;
        readonly EmberButton primary, pause;
        int lastTick = Environment.TickCount, frame, saveTicks;
        bool exiting, dragging;
        Point dragOffset;
        string message = "";
        DateTime messageUntil;
        public Companion(Preferences p, bool preview)
        {
            L.Language = p.Language == "en" ? "en" : "tr";
            engine = new TimerEngine(p);
            Text = "Soul Revive"; AccessibleName = L.Text("Soul Revive mola sayacı","Soul Revive break timer");
            ClientSize = new Size(344, 510); FormBorderStyle = FormBorderStyle.None;
            AutoScaleMode = AutoScaleMode.None; BackColor = Theme.Background;
            DoubleBuffered = true; StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = true; TopMost = p.AlwaysOnTop; Icon = PixelArt.MakeIcon();
            var area = Screen.PrimaryScreen.WorkingArea;
            Location = new Point(area.Right - Width - 24, area.Bottom - Height - 24);
            if (p.X != int.MinValue && p.Y != int.MinValue)
            {
                var bounds = new Rectangle(p.X, p.Y, Width, Height);
                foreach (var screen in Screen.AllScreens)
                    if (screen.WorkingArea.Contains(bounds)) { Location = bounds.Location; break; }
            }
            primary = new EmberButton(L.Text("Mola ver","Take a break"), new Rectangle(20, 402, 199, 42), true);
            primary.Click += delegate { if (p.InBreak) { engine.CancelBreak(); } else engine.BeginBreak(); };
            Controls.Add(primary);
            pause = new EmberButton(L.Text("Duraklat","Pause"), new Rectangle(227, 402, 97, 42), false);
            pause.Click += delegate { engine.TogglePause(); }; Controls.Add(pause);
            var settings = new EmberButton("⚙", new Rectangle(238, 15, 28, 27), false);
            settings.AccessibleName = L.Text("Ayarlar","Settings"); settings.Click += delegate { OpenSettings(); }; Controls.Add(settings);
            var hide = new EmberButton("−", new Rectangle(270, 15, 24, 27), false);
            hide.AccessibleName = L.Text("Sistem tepsisine küçült","Minimize to tray"); hide.Click += delegate { Hide(); }; Controls.Add(hide);
            var close = new EmberButton("×", new Rectangle(298, 15, 26, 27), false);
            close.AccessibleName = L.Text("Sistem tepsisine gizle","Hide in tray"); close.Click += delegate { Hide(); }; Controls.Add(close);
            var tooltip = new ToolTip();
            tooltip.SetToolTip(settings, L.Text("Ayarlar","Settings")); tooltip.SetToolTip(hide, L.Text("Sistem tepsisine gizle (sayaç çalışır)","Hide in tray (timer continues)"));
            tooltip.SetToolTip(close, L.Text("Sistem tepsisine gizle • Çıkış için tepsi menüsü","Hide in tray · Use the tray menu to quit"));
            var menu = new ContextMenuStrip();
            menu.Items.Add(L.Text("Şövalyeyi göster","Show knight"), null, delegate { Reveal(); });
            menu.Items.Add(L.Text("Mola ver","Take a break"), null, delegate { engine.BeginBreak(); Reveal(); });
            menu.Items.Add(L.Text("Duraklat / devam et","Pause / resume"), null, delegate { engine.TogglePause(); });
            menu.Items.Add(L.Text("Ayarlar","Settings"), null, delegate { Reveal(); OpenSettings(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(L.Text("Çıkış","Quit"), null, delegate { exiting = true; Close(); });
            tray = new NotifyIcon { Text = "Soul Revive", Icon = Icon, ContextMenuStrip = menu, Visible = !preview };
            refreshLanguage = delegate {
                settings.AccessibleName = L.Text("Ayarlar","Settings");
                hide.AccessibleName = close.AccessibleName = L.Text("Tepsiye gizle","Hide in tray");
                tooltip.SetToolTip(settings, settings.AccessibleName);
                tooltip.SetToolTip(hide, hide.AccessibleName); tooltip.SetToolTip(close, close.AccessibleName);
                menu.Items[0].Text = L.Text("Şövalyeyi göster","Show knight");
                menu.Items[1].Text = L.Text("Mola ver","Take a break");
                menu.Items[2].Text = L.Text("Duraklat / devam et","Pause / resume");
                menu.Items[3].Text = settings.AccessibleName; menu.Items[5].Text = L.Text("Çıkış","Quit");
                AccessibleName = L.Text("Soul Revive mola sayacı","Soul Revive break timer");
            };
            tray.DoubleClick += delegate { Reveal(); }; tray.BalloonTipClicked += delegate { Reveal(); };
            engine.Changed += OnChanged;
            timer = new System.Windows.Forms.Timer { Interval = 125 };
            timer.Tick += delegate
            {
                if (Program.ShowRequested != null && Program.ShowRequested.WaitOne(0)) Reveal();
                int now = Environment.TickCount;
                double delta = unchecked((uint)(now - lastTick)) / 1000.0; lastTick = now;
                engine.Advance(delta); frame++;
                if (++saveTicks >= 240) { saveTicks = 0; Save(); }
                UpdateControls(); if (Visible) Invalidate();
            };
            if (!preview)
            {
                SystemEvents.PowerModeChanged += OnPower;
                SystemEvents.SessionSwitch += OnSession;
                timer.Start();
            }
            UpdateControls();
            MouseDown += delegate(object sender, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left) { dragging = true; dragOffset = e.Location; Capture = true; }
            };
            MouseMove += delegate(object sender, MouseEventArgs e)
            { if (dragging) Location = new Point(Cursor.Position.X - dragOffset.X, Cursor.Position.Y - dragOffset.Y); };
            MouseUp += delegate
            {
                if (!dragging) return;
                dragging = false; Capture = false;
                var working = Screen.FromControl(this).WorkingArea;
                Location = new Point(Math.Max(working.Left, Math.Min(Left, working.Right-Width)), Math.Max(working.Top, Math.Min(Top, working.Bottom-Height)));
                p.X = Left; p.Y = Top; Save();
            };
        }
        void OnPower(object s, PowerModeChangedEventArgs e)
        { if (e.Mode == PowerModes.Suspend) SystemPause(); }
        void OnSession(object s, SessionSwitchEventArgs e)
        { if (e.Reason == SessionSwitchReason.SessionLock) SystemPause(); }
        void SystemPause()
        {
            if (IsDisposed || !IsHandleCreated) return;
            BeginInvoke((Action)delegate { engine.P.Paused = true; lastTick = Environment.TickCount; Save(); UpdateControls(); Invalidate(); });
        }
        void Save()
        {
            if (!Storage.Save(engine.P)) { message = L.Text("Ayarlar kaydedilemedi. Klasör iznini kontrol et.","Unable to save settings. Check folder permissions."); messageUntil = DateTime.Now.AddSeconds(10); }
        }
        void Reveal() { Show(); WindowState = FormWindowState.Normal; Activate(); }
        void OnChanged(string reason)
        {
            if (reason == "due")
            {
                Notify(L.Text("Ateş sönüyor, şövalye.","Your fire is fading."), L.Text("Ayağa kalk ve kısa bir yürüyüş yap. Hazırsan Mola ver'e bas.","Stand up and take a short walk. Select Take a break when ready."));
                Reveal();
            }
            if (reason == "revived")
            {
                message = "HUMANITY RESTORED"; messageUntil = DateTime.Now.AddSeconds(8);
                Notify(L.Text("Ateş yeniden yandı.","Your fire is rekindled."), L.Text("Mola tamamlandı. Yeni çalışma süren başladı.","Break complete. Your next work period has started."));
            }
            lastTick = Environment.TickCount; Save(); UpdateControls(); Invalidate();
        }
        void Notify(string title, string text)
        {
            tray.ShowBalloonTip(7000, title, text, ToolTipIcon.Info);
            if (engine.P.Sound) System.Media.SystemSounds.Asterisk.Play();
        }
        void UpdateControls()
        {
            primary.Text = engine.P.InBreak ? L.Text("Molayı bitirmeden dön","Return early") : L.Text("Mola ver  →","Take a break  →");
            pause.Text = engine.P.Paused ? L.Text("Devam et","Resume") : L.Text("Duraklat","Pause");
            string time = TimeText(engine.P.Remaining);
            tray.Text = "Soul Revive · " + (engine.P.Paused ? L.Text("Duraklatıldı","Paused") : engine.P.InBreak ? L.Text("Mola ","Break ") + time : L.Text("Mola için ","Next break in ") + time);
            primary.AccessibleName = primary.Text; pause.AccessibleName = pause.Text;
        }
        internal static string TimeText(double seconds)
        { int n = (int)Math.Ceiling(seconds); return (n/60).ToString("00") + ":" + (n%60).ToString("00"); }
        void OpenSettings()
        {
            using (var settings = new SettingsDialog(engine.P))
            {
                if (settings.ShowDialog(this) == DialogResult.OK)
                {
                    L.Language = engine.P.Language = settings.Language;
                    refreshLanguage();
                    engine.P.AlwaysOnTop = settings.OnTop; engine.P.Sound = settings.PlaySound;
                    TopMost = settings.OnTop; engine.ApplyDurations(settings.Work, settings.Rest);
                }
            }
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics; g.Clear(Theme.Background);
            using (var pen = new Pen(Theme.Line)) g.DrawRectangle(pen,0,0,Width-1,Height-1);
            Theme.Label(g,"SOUL / REVIVE",12,Theme.Gold,new Rectangle(20,14,210,25),ContentAlignment.MiddleLeft,"Georgia");
            Theme.Label(g,L.Text("MASAÜSTÜ MOLA ARKADAŞIN","YOUR DESKTOP BREAK COMPANION"),7,Theme.Muted,new Rectangle(20,37,210,16),ContentAlignment.MiddleLeft,"Segoe UI");
            using (var scene = PixelArt.Scene(engine.Health, engine.P.InBreak, frame))
            {
                g.InterpolationMode = InterpolationMode.NearestNeighbor; g.PixelOffsetMode = PixelOffsetMode.Half;
                g.DrawImage(scene,new Rectangle(0,64,344,184)); g.PixelOffsetMode = PixelOffsetMode.Default;
            }
            using (var b = new SolidBrush(Theme.Panel)) g.FillRectangle(b,20,254,304,27);
            string state = engine.P.Paused ? L.Text("ATEŞİN SENİ BEKLİYOR","YOUR FIRE AWAITS") : engine.P.InBreak ? L.Text("AYAĞA KALK. YENİDEN DOĞ.","STAND UP. RISE AGAIN.") : engine.Due ? L.Text("KÜLLERİNDEN DOĞ, ŞÖVALYE","RISE FROM THE ASHES") : L.Text("ATEŞİ SÖNDÜRME","KEEP THE FIRE ALIVE");
            Theme.Label(g,state,8,engine.Due ? Color.FromArgb(224,131,103) : Theme.Gold,new Rectangle(20,254,304,27),ContentAlignment.MiddleCenter,"Segoe UI");
            Theme.Label(g,TimeText(engine.P.Remaining),39,Theme.Text,new Rectangle(20,282,304,62),ContentAlignment.MiddleCenter,"Consolas");
            string subtitle = engine.P.Paused ? L.Text("Duraklatıldı · Devam et ile sürdür","Paused · Select Resume to continue") : engine.P.InBreak ? L.Text("Biraz yürü, esne; ateş yeniden canlansın.","Walk or stretch while the fire recovers.") : engine.Due ? L.Text("Mola ver'e bas ve kısa bir yürüyüşe çık.","Select Take a break and go for a walk.") : L.Text("Bir sonraki hareket molasına kalan süre","Time until your next break");
            Theme.Label(g,subtitle,9,Theme.Muted,new Rectangle(12,341,320,23),ContentAlignment.MiddleCenter,"Segoe UI");
            Theme.Label(g,L.Text("CAN","HP"),7,Theme.Muted,new Rectangle(20,371,30,16),ContentAlignment.MiddleLeft,"Segoe UI");
            using (var b = new SolidBrush(Color.FromArgb(48,38,38))) g.FillRectangle(b,54,375,223,7);
            double hp = Math.Max(0,Math.Min(1,engine.Health));
            using (var b = new SolidBrush(engine.P.InBreak ? Theme.Green : Color.FromArgb(177,83,66))) g.FillRectangle(b,54,375,(int)(223*hp),7);
            Theme.Label(g,((int)(hp*100))+"%",7,Theme.Muted,new Rectangle(282,370,43,17),ContentAlignment.MiddleCenter,"Consolas");
            Theme.Label(g,engine.P.WorkMinutes + L.Text(" dk çalışma  /  "," min work  /  ") + engine.P.BreakMinutes + L.Text(" dk hareket"," min movement"),8,Theme.Muted,new Rectangle(20,451,304,20),ContentAlignment.MiddleCenter,"Segoe UI");
            string footer = DateTime.Now < messageUntil ? message : L.Text("BUGÜN  ·  ","TODAY  ·  ") + engine.P.Breaks + L.Text(" mola  ·  "," breaks  ·  ") + (int)(engine.P.MovementSeconds/60) + L.Text(" dk hareket"," min movement");
            Theme.Label(g,footer,8,DateTime.Now < messageUntil ? Theme.Gold : Theme.Muted,new Rectangle(12,478,320,22),ContentAlignment.MiddleCenter,"Segoe UI");
        }
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!exiting && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); return; }
            Save(); base.OnFormClosing(e);
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                SystemEvents.PowerModeChanged -= OnPower; SystemEvents.SessionSwitch -= OnSession;
                timer.Stop(); timer.Dispose(); tray.Visible = false; tray.Dispose();
                if (Icon != null) Icon.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    sealed class SettingsDialog : Form
    {
        readonly NumericUpDown work, rest;
        readonly CheckBox top, sound, startup;
        public int Work { get { return (int)work.Value; } }
        public int Rest { get { return (int)rest.Value; } }
        public bool OnTop { get { return top.Checked; } }
        public bool PlaySound { get { return sound.Checked; } }
        readonly ComboBox language = new ComboBox();
        public string Language { get { return language.SelectedIndex == 1 ? "en" : "tr"; } }
        public SettingsDialog(Preferences p)
        {
            Text = L.Text("Soul Revive · Ayarlar","Soul Revive · Settings"); ClientSize = new Size(380,478);
            BackColor = Theme.Background; ForeColor = Theme.Text; Font = new Font("Segoe UI",10);
            FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false;
            AddLabel(L.Text("ATEŞİNİN RİTMİ","YOUR BREAK ROUTINE"),20,18,340,28,Theme.Gold,14);
            AddLabel(L.Text("Çalışma süresi (dakika)","Work time (minutes)"),20,68,240,28,Theme.Text,10);
            work = Number(268,68,1,180,p.WorkMinutes);
            AddLabel(L.Text("Hareket molası (dakika)","Movement break (minutes)"),20,110,240,28,Theme.Text,10);
            rest = Number(268,110,1,30,p.BreakMinutes);
            top = Check(L.Text("Her zaman üstte göster","Always on top"),164,p.AlwaysOnTop);
            sound = Check(L.Text("Mola başında ve sonunda ses çal","Play reminder sounds"),199,p.Sound);
            startup = Check(L.Text("Windows açılışında başlat","Start at Windows sign-in"),234,Startup.Enabled);
            AddLabel(L.Text("Mola, Mola ver'e bastığında başlar.\nUygulama hareketini veya duruşunu ölçmez.\nEkran kilitlenince ve uykuda sayaç duraklar.\nSüreleri değiştirmek yeni bir tur başlatır.","Select Take a break to start a movement break.\nThe timer pauses on lock or sleep.\nChanging durations starts a new round."),20,280,340,85,Theme.Muted,9);
            AddLabel(L.Text("Dil","Language"),20,367,140,27,Theme.Text,10);
            language.Bounds = new Rectangle(178,365,182,30); language.DropDownStyle = ComboBoxStyle.DropDownList;
            language.Items.AddRange(new string[] { "Türkçe", "English" }); language.SelectedIndex = L.Language == "en" ? 1 : 0;
            Controls.Add(language);
            var save = new EmberButton(L.Text("Kaydet","Save"),new Rectangle(190,426,170,36),true);
            save.Click += delegate
            {
                try
                {
                    if (startup.Checked != Startup.Enabled) Startup.Set(startup.Checked);
                    DialogResult = DialogResult.OK; Close();
                }
                catch(Exception ex) { MessageBox.Show(this,L.Text("Başlangıç ayarı kaydedilemedi:\n","Could not save startup settings:\n")+ex.Message,"Soul Revive",MessageBoxButtons.OK,MessageBoxIcon.Warning); }
            };
            Controls.Add(save); AcceptButton = save;
            var cancel = new EmberButton(L.Text("Vazgeç","Cancel"),new Rectangle(20,426,155,36),false);
            cancel.DialogResult = DialogResult.Cancel; Controls.Add(cancel); CancelButton = cancel;
        }
        void AddLabel(string text,int x,int y,int w,int h,Color color,float size)
        { Controls.Add(new Label { Text=text, Bounds=new Rectangle(x,y,w,h),ForeColor=color,Font=new Font("Segoe UI",size),AutoSize=false }); }
        NumericUpDown Number(int x,int y,int min,int max,int value)
        {
            var n = new NumericUpDown { Bounds=new Rectangle(x,y,92,30),Minimum=min,Maximum=max,Value=value,BackColor=Theme.Panel,ForeColor=Theme.Text };
            Controls.Add(n); return n;
        }
        CheckBox Check(string text,int y,bool value)
        {
            var c = new CheckBox { Text=text,Checked=value,Bounds=new Rectangle(20,y,340,28),FlatStyle=FlatStyle.Standard };
            Controls.Add(c); return c;
        }
    }

    static class Tests
    {
        static int count;
        static void Check(bool value,string name) { if (!value) throw new Exception(name); count++; }
        public static void Run(string output)
        {
            var p = new Preferences(); var e = new TimerEngine(p); int due=0,revived=0;
            e.Changed += delegate(string s) { if(s=="due")due++; if(s=="revived")revived++; };
            for(int i=0;i<1800;i++) e.Advance(1);
            Check(e.Due && due==1 && e.Health==0,"work reaches zero and notifies once");
            e.Advance(1); Check(due==1,"no repeated due notification");
            e.BeginBreak(); e.Advance(5); e.TogglePause(); double before=p.Remaining;
            e.Advance(2); Check(p.Remaining==before,"pause freezes time");
            e.TogglePause(); e.Advance(3600); Check(p.Remaining==before,"sleep gap excluded");
            for(int i=0;i<175;i++)e.Advance(1);
            Check(!p.InBreak && p.Breaks==1 && revived==1 && p.Remaining==1800,"completed break revives and starts next round");
            Check(p.MovementSeconds==180,"counts actual break seconds");
            p.Remaining = 0; e.BeginBreak(); e.Advance(2); e.CancelBreak();
            Check(!e.Due && p.Remaining==1800 && p.Breaks==1,"early return after expiry starts a full work round without awarding a break");
            e.Advance(1); Check(p.Remaining==1799,"new work round counts down");
            e.ApplyDurations(45,5); Check(p.Remaining==2700 && !p.InBreak,"duration change resets cycle");
            p.Day="2000-01-01"; p.CheckDay(); Check(p.Breaks==0 && p.MovementSeconds==0,"daily rollover");
            p.WorkMinutes=0; p.BreakMinutes=999; p.Remaining=double.NaN; p.Validate();
            Check(p.WorkMinutes==1 && p.BreakMinutes==30 && p.Remaining==60,"invalid settings constrained");
            Storage.DirectoryPath=output; p.X=150; p.Y=160;
            p.Language = "en";
            Check(Storage.Save(p),"settings saved"); var loaded=Storage.Load();
            using (var localized = new Companion(loaded, true))
            using (var dialog = new SettingsDialog(loaded))
            {
                Check(dialog.Language == "en" && dialog.Text.Contains("Settings"), "English settings survive restart");
                Check(localized.Controls[0].Text.Contains("Take a break"), "English action button");
            }
            Check(loaded.X==150 && loaded.Y==160 && loaded.Remaining==60,"settings round trip");
            Check(Companion.TimeText(.1)=="00:01" && Companion.TimeText(3600)=="60:00","timer formatting");
            Directory.CreateDirectory(output);
            Application.EnableVisualStyles();
            var healthy=new Preferences();
            using(var form=new Companion(healthy,true))
            using(var b=new Bitmap(344,510))
            {
                form.CreateControl();
                foreach(Control child in form.Controls) child.CreateControl();
                form.DrawToBitmap(b,new Rectangle(0,0,344,510)); b.Save(Path.Combine(output,"preview.png"));
            }
            using(var icon=PixelArt.MakeIcon())
            using(var file=File.Create(Path.Combine(output,"app.ico"))) icon.Save(file);
            using(var b=PixelArt.Scene(0,false,0)) b.Save(Path.Combine(output,"knight-tired.png"));
            using(var b=PixelArt.Scene(.5,true,3)) b.Save(Path.Combine(output,"knight-break.png"));
            File.WriteAllText(Path.Combine(output,"results.txt"),count+" checks passed.\r\n");
        }
    }

    static class Program
    {
        internal static EventWaitHandle ShowRequested;
        [STAThread]
        static void Main(string[] args)
        {
            try
            {
                if(args.Length>=2 && args[0]=="--self-test") { Tests.Run(args[1]); return; }
                if(args.Length>0 && args[0]=="--enable-startup") { Startup.Set(true); return; }
                if(args.Length>0 && args[0]=="--disable-startup") { Startup.Set(false); return; }
                bool created;
                using(var mutex=new Mutex(true,@"Local\SoulsRevive.Katana.DesktopCompanion",out created))
                {
                    using (ShowRequested = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\SoulsRevive.ShowWindow"))
                    {
                        if(!created) { ShowRequested.Set(); return; }
                        Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
                        using(var form=new Companion(Storage.Load(),false)) Application.Run(form);
                    }
                }
            }
            catch(Exception ex)
            {
                if(args.Length>=2 && args[0]=="--self-test")
                { Directory.CreateDirectory(args[1]); File.WriteAllText(Path.Combine(args[1],"failure.txt"),ex.ToString()); Environment.ExitCode=1; }
                else MessageBox.Show(L.Text("Soul Revive başlatılamadı.\n","Could not start Soul Revive.\n")+ex.Message,"Soul Revive",MessageBoxButtons.OK,MessageBoxIcon.Error);
            }
        }
    }
}


