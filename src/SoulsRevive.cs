using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace SoulsRevive
{
    static class Startup
    {
        const string Key = @"Software\Microsoft\Windows\CurrentVersion\Run";
        public static bool Enabled
        {
            get
            {
                try { using (var key = Registry.CurrentUser.OpenSubKey(Key))
                    return key != null && string.Equals(key.GetValue("SoulsRevive") as string, Command, StringComparison.OrdinalIgnoreCase); }
                catch (System.Security.SecurityException) { return false; }
                catch (UnauthorizedAccessException) { return false; }
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

    /// <summary>
    /// The layout is authored at 96 DPI. The process is DPI aware so Windows does not blur the pixel
    /// art by stretching a bitmap; instead every form and control is scaled by <see cref="Scale"/>.
    /// </summary>
    static class Dpi
    {
        public static float Scale = 1f;
        [DllImport("user32.dll")] static extern bool SetProcessDPIAware();

        public static void Initialize()
        {
            try { SetProcessDPIAware(); } catch (EntryPointNotFoundException) { } catch (DllNotFoundException) { }
            float scale = 1f;
            try { using (var g = Graphics.FromHwnd(IntPtr.Zero)) scale = g.DpiX / 96f; } catch (Exception) { }
            string forced = Environment.GetEnvironmentVariable("SOULSREVIVE_UI_SCALE");
            float parsed;
            if (!string.IsNullOrEmpty(forced) && float.TryParse(forced, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out parsed)) scale = parsed;
            // Keep the tallest window (668 logical pixels) on screen, and use quarter steps for crisp pixels.
            var area = Screen.PrimaryScreen.WorkingArea;
            scale = Math.Min(scale, (area.Height - 16) / 668f);
            Scale = Math.Max(1f, (float)Math.Floor(scale * 4) / 4f);
        }
        public static int S(int value) { return (int)Math.Round(value * Scale); }
        public static Rectangle S(Rectangle r) { return new Rectangle(S(r.X), S(r.Y), S(r.Width), S(r.Height)); }
        /// <summary>Logical (96 DPI) size of a control's client area.</summary>
        public static Rectangle Logical(Control c) { return new Rectangle(0, 0, (int)Math.Round(c.ClientSize.Width / Scale), (int)Math.Round(c.ClientSize.Height / Scale)); }
        /// <summary>Lets paint code keep its 96 DPI coordinates.</summary>
        public static void Prepare(Graphics g) { if (Scale != 1f) g.ScaleTransform(Scale, Scale); }

        /// <summary>Scales a fully built form: client size, every control's bounds and explicit fonts.</summary>
        public static void Apply(Form form)
        {
            if (Scale == 1f) return;
            var explicitFonts = new List<Control>();
            Collect(form, explicitFonts);
            form.ClientSize = new Size(S(form.ClientSize.Width), S(form.ClientSize.Height));
            if (form.Font.Unit == GraphicsUnit.Pixel) form.Font = PixelFont.Create(form.Font.Size * Scale);
            foreach (var control in explicitFonts) control.Font = PixelFont.Create(control.Font.Size * Scale);
            ScaleChildren(form);
        }
        static void Collect(Control parent, List<Control> explicitFonts)
        {
            foreach (Control child in parent.Controls)
            {
                if (!ReferenceEquals(child.Font, parent.Font) && child.Font.Unit == GraphicsUnit.Pixel) explicitFonts.Add(child);
                Collect(child, explicitFonts);
            }
        }
        static void ScaleChildren(Control parent)
        {
            foreach (Control child in parent.Controls)
            {
                child.Bounds = S(child.Bounds);
                var combo = child as ComboBox;
                if (combo != null) combo.ItemHeight = S(combo.ItemHeight);
                ScaleChildren(child);
            }
        }
    }

    static class Theme
    {
        public static bool Light;
        static readonly Dictionary<string, Color> colors = new Dictionary<string, Color>();
        public static Color Hex(string hex)
        {
            Color c;
            lock (colors) if (!colors.TryGetValue(hex, out c)) colors[hex] = c = ColorTranslator.FromHtml(hex);
            return c;
        }
        public static Color Background { get { return Hex(Palette.Background(Light)); } }
        public static Color Panel { get { return Hex(Palette.Panel(Light)); } }
        public static Color Line { get { return Hex(Palette.Line(Light)); } }
        public static Color Gold { get { return Hex(Palette.Gold(Light)); } }
        public static Color Text { get { return Hex(Palette.Text(Light)); } }
        public static Color Muted { get { return Hex(Palette.Muted(Light)); } }
        public static Color Green { get { return Hex(Palette.Green(Light)); } }
        public static Color PrimaryFill { get { return Hex(Palette.Primary(Light)); } }
        public static Color PrimaryInk { get { return Hex(Palette.PrimaryText); } }
        public static void Frame(Graphics g, Rectangle r, Color fill)
        {
            using (var b = new SolidBrush(fill)) g.FillRectangle(b, r);
            using (var pen = new Pen(Line, 2)) g.DrawRectangle(pen, r.X + 1, r.Y + 1, r.Width - 3, r.Height - 3);
            using (var b = new SolidBrush(Gold))
                foreach (Point pt in new[] { new Point(r.Left, r.Top), new Point(r.Right - 4, r.Top), new Point(r.Left, r.Bottom - 4), new Point(r.Right - 4, r.Bottom - 4) })
                    g.FillRectangle(b, pt.X, pt.Y, 4, 4);
        }
        public static void Label(Graphics g, string s, float size, Color color, Rectangle rect, ContentAlignment align)
        {
            using (var brush = new SolidBrush(color))
            using (var format = new StringFormat())
            {
                g.TextRenderingHint = TextRenderingHint.SingleBitPerPixelGridFit;
                format.Alignment = align == ContentAlignment.MiddleCenter ? StringAlignment.Center : align == ContentAlignment.MiddleRight ? StringAlignment.Far : StringAlignment.Near;
                format.LineAlignment = StringAlignment.Center; format.Trimming = StringTrimming.EllipsisCharacter;
                format.FormatFlags = StringFormatFlags.NoWrap;
                g.DrawString(s, PixelFont.Cached(PixelFont.TextPixels(size)), brush, rect, format);
            }
        }
    }

    public sealed class EmberButton : Button
    {
        public bool Primary;
        public bool GearIcon;
        bool hover;
        public EmberButton(string text, Rectangle bounds, bool primary)
        {
            Text = text; Bounds = bounds; Primary = primary; Cursor = Cursors.Hand;
            AccessibleName = text; FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0;
            SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
        }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnTextChanged(EventArgs e) { AccessibleName = Text; base.OnTextChanged(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Dpi.Prepare(g);
            Rectangle r = Dpi.Logical(this);
            Color bg = Primary ? Theme.PrimaryFill : Theme.Panel;
            if (!Enabled) bg = Theme.Panel;
            if (hover && Enabled) bg = DayCycle.Blend(bg, Theme.Text, .12);
            Theme.Frame(g, r, bg);
            using (var pen = new Pen(DayCycle.Blend(bg, Theme.Text, .25), 2)) g.DrawLine(pen, 5, 4, r.Width - 6, 4);
            if (GearIcon)
            {
                // Integer-pixel gear: independent of font glyph coverage and scaling.
                string[] pixels = {
                    "00000111100000", "00000111100000",
                    "00111111111100", "00111111111100",
                    "00111000011100", "11110000001111",
                    "11110000001111", "11110000001111",
                    "11110000001111", "00111000011100",
                    "00111111111100", "00111111111100",
                    "00000111100000", "00000111100000"
                };
                using (var ink = new SolidBrush(Enabled ? Theme.Gold : Theme.Muted))
                    for (int y = 0; y < pixels.Length; y++)
                        for (int x = 0; x < pixels[y].Length; x++)
                            if (pixels[y][x] == '1') g.FillRectangle(ink, (r.Width - 14) / 2 + x, (r.Height - 14) / 2 + y, 1, 1);
            }
            else
            {
                int inset = r.Width < 44 ? 2 : r.Width < 100 ? 4 : 8;
                Theme.Label(g, Text, 11, !Enabled ? Theme.Muted : Primary ? Theme.PrimaryInk : Theme.Text, new Rectangle(inset, 2, r.Width - inset * 2, r.Height - 4), ContentAlignment.MiddleCenter);
            }
            if (Focused) ControlPaint.DrawFocusRectangle(g, new Rectangle(4, 4, r.Width - 8, r.Height - 8));
        }
    }

    static class Program
    {
        internal static EventWaitHandle ShowRequested;
        internal static EventWaitHandle QuitRequested;
        [STAThread]
        static void Main(string[] args)
        {
            try
            {
                string profileSuffix = "";
                if (args.Length >= 2 && (args[0] == "--profile" || args[0] == "--quit-profile"))
                {
                    Storage.DirectoryPath = Path.GetFullPath(args[1]);
                    using (var sha = System.Security.Cryptography.SHA256.Create()) profileSuffix = "." + BitConverter.ToString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(Storage.DirectoryPath.ToLowerInvariant()))).Replace("-", "").Substring(0, 16);
                    if (args[0] == "--quit-profile") { try { using (var quit = EventWaitHandle.OpenExisting(@"Local\SoulsRevive.Quit" + profileSuffix)) quit.Set(); } catch (WaitHandleCannotBeOpenedException) { } return; }
                }
                if (args.Length > 0 && args[0] == "--apply-update") { Updates.Apply(args); return; }
                if (args.Length > 0 && args[0] == "--uninstall") { Installation.Uninstall(); return; }
                if (args.Length == 2 && args[0] == "--remove-installation") { int parent; if (int.TryParse(args[1], out parent)) Installation.Remove(parent); return; }
                if (args.Length == 2 && args[0] == "--export-assets") { ArtworkExport.Save(args[1]); return; }
                if (args.Length == 2 && args[0] == "--version-file") { File.WriteAllText(args[1], Updates.VersionText); return; }
                if (args.Length == 2 && args[0] == "--check-update")
                { try { var update = Updates.Check(); File.WriteAllText(args[1], update == null ? "up-to-date" : update.Version.ToString()); } catch (Exception ex) { File.WriteAllText(args[1], ex.Message); } return; }
                if (args.Length > 0 && args[0] == "--quit") { try { using (var quit = EventWaitHandle.OpenExisting(@"Local\SoulsRevive.Quit")) quit.Set(); } catch (WaitHandleCannotBeOpenedException) { } return; }
                // DPI awareness must be declared before the first window exists.
                Dpi.Initialize();
                Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false); PixelFont.Initialize();
                if (args.Length >= 2 && args[0] == "--self-test") { Tests.Run(args[1]); return; }
                if (args.Length >= 2 && args[0] == "--audio-self-test") { Tests.RunAudio(args[1]); return; }
                if (args.Length > 0 && args[0] == "--enable-startup") { Startup.Set(true); return; }
                if (args.Length > 0 && args[0] == "--disable-startup") { Startup.Set(false); return; }
                // Started from the Windows sign-in entry: wait quietly in the tray instead of jumping on top.
                bool startHidden = args.Contains("--startup");
                bool created;
                using (var mutex = new Mutex(true, @"Local\SoulsRevive.Katana.DesktopCompanion" + profileSuffix, out created))
                {
                    using (ShowRequested = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\SoulsRevive.ShowWindow" + profileSuffix))
                    using (QuitRequested = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\SoulsRevive.Quit" + profileSuffix))
                    {
                        if (!created) { if (!startHidden) ShowRequested.Set(); return; }
                        ThreadPool.QueueUserWorkItem(delegate { Updates.CleanupTemporaryFiles(); });
                        using (var form = new Companion(Storage.Load(), false, startHidden)) Application.Run(form);
                    }
                }
            }
            catch (Exception ex)
            {
                if (args.Length >= 2 && (args[0] == "--self-test" || args[0] == "--audio-self-test"))
                { Directory.CreateDirectory(args[1]); File.WriteAllText(Path.Combine(args[1], "failure.txt"), ex.ToString()); Environment.ExitCode = 1; }
                else MessageBox.Show(L.Text("Soul Revive başlatılamadı.\n", "Could not start Soul Revive.\n") + ex.Message, "Soul Revive", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
