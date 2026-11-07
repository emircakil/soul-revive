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

    static class Theme
    {
        public static bool Light;
        public static Color Background { get { return ColorTranslator.FromHtml(Light ? "#EEE4CB" : "#14191E"); } }
        public static Color Panel { get { return ColorTranslator.FromHtml(Light ? "#DFD2B2" : "#20292D"); } }
        public static Color Line { get { return ColorTranslator.FromHtml(Light ? "#9C8865" : "#48504A"); } }
        public static Color Gold { get { return ColorTranslator.FromHtml(Light ? "#81501F" : "#E9B86B"); } }
        public static Color Text { get { return ColorTranslator.FromHtml(Light ? "#302D29" : "#F1E8CF"); } }
        public static Color Muted { get { return ColorTranslator.FromHtml(Light ? "#635B4A" : "#A2ADA4"); } }
        public static Color Green { get { return ColorTranslator.FromHtml(Light ? "#32634A" : "#98C99A"); } }
        public static void Frame(Graphics g, Rectangle r, Color fill)
        {
            using(var b=new SolidBrush(fill)) g.FillRectangle(b,r);
            using(var pen=new Pen(Line,2)) g.DrawRectangle(pen,r.X+1,r.Y+1,r.Width-3,r.Height-3);
            using(var b=new SolidBrush(Gold)) foreach(Point pt in new[] {new Point(r.Left,r.Top),new Point(r.Right-4,r.Top),new Point(r.Left,r.Bottom-4),new Point(r.Right-4,r.Bottom-4)}) g.FillRectangle(b,pt.X,pt.Y,4,4);
        }
        public static void Label(Graphics g, string s, float size, Color color, Rectangle rect, ContentAlignment align, string family)
        {
            using (Font f = PixelFont.Create(PixelFont.TextPixels(size)))
            using (var brush = new SolidBrush(color))
            using (var format = new StringFormat())
            {
                g.TextRenderingHint = TextRenderingHint.SingleBitPerPixelGridFit;
                format.Alignment = align == ContentAlignment.MiddleCenter ? StringAlignment.Center : align == ContentAlignment.MiddleRight ? StringAlignment.Far : StringAlignment.Near;
                format.LineAlignment = StringAlignment.Center; format.Trimming = StringTrimming.EllipsisCharacter;
                format.FormatFlags = StringFormatFlags.NoWrap;
                g.DrawString(s,f,brush,rect,format);
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
            Color bg = Primary ? ColorTranslator.FromHtml(Theme.Light ? "#81501F" : "#55432B") : Theme.Panel;
            if(!Enabled)bg=Theme.Panel;
            if(hover && Enabled) bg=DayCycle.Blend(bg,Theme.Text,.12);
            Theme.Frame(g,ClientRectangle,bg);
            using(var pen=new Pen(DayCycle.Blend(bg,Theme.Text,.25),2)) g.DrawLine(pen,5,4,Width-6,4);
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
                using(var ink=new SolidBrush(Enabled ? Theme.Gold : Theme.Muted))
                    for(int y=0;y<pixels.Length;y++)
                        for(int x=0;x<pixels[y].Length;x++)
                            if(pixels[y][x]=='1')g.FillRectangle(ink,(Width-14)/2+x,(Height-14)/2+y,1,1);
            }
            else
            {
                int inset = Width < 44 ? 2 : 8;
                Theme.Label(g, Text, 11, !Enabled ? Theme.Muted : Primary ? ColorTranslator.FromHtml("#FFF1D6") : Theme.Text, new Rectangle(inset,2,Width-inset*2,Height-4), ContentAlignment.MiddleCenter, "pixel");
            }
            if (Focused) ControlPaint.DrawFocusRectangle(g, new Rectangle(4, 4, Width - 8, Height - 8));
        }
    }

    // Original pixel artwork, rendered on a 172 x 92 canvas with integer pixels.
    static class Program
    {
        internal static EventWaitHandle ShowRequested;
        internal static EventWaitHandle QuitRequested;
        [STAThread]
        static void Main(string[] args)
        {
            try
            {
                string profileSuffix="";
                if(args.Length==2 && (args[0]=="--profile" || args[0]=="--quit-profile"))
                {
                    Storage.DirectoryPath=Path.GetFullPath(args[1]);
                    using(var sha=System.Security.Cryptography.SHA256.Create())profileSuffix="."+BitConverter.ToString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(Storage.DirectoryPath.ToLowerInvariant()))).Replace("-","").Substring(0,16);
                    if(args[0]=="--quit-profile") {try{using(var quit=EventWaitHandle.OpenExisting(@"Local\SoulsRevive.Quit"+profileSuffix))quit.Set();}catch(WaitHandleCannotBeOpenedException){}return;}
                }
                if(args.Length>0 && args[0]=="--apply-update") { Updates.Apply(args); return; }
                if(args.Length>0 && args[0]=="--uninstall") { Installation.Uninstall(); return; }
                if(args.Length==2 && args[0]=="--remove-installation") { Installation.Remove(int.Parse(args[1])); return; }
                if(args.Length==2 && args[0]=="--export-assets") { ArtworkExport.Save(args[1]); return; }
                if(args.Length==2 && args[0]=="--version-file") { File.WriteAllText(args[1],Updates.VersionText); return; }
                if(args.Length==2 && args[0]=="--check-update")
                { try {var update=Updates.Check();File.WriteAllText(args[1],update==null?"up-to-date":update.Version.ToString());}catch(Exception ex){File.WriteAllText(args[1],ex.Message);}return; }
                if(args.Length>0 && args[0]=="--quit") {try{using(var quit=EventWaitHandle.OpenExisting(@"Local\SoulsRevive.Quit"))quit.Set();}catch(WaitHandleCannotBeOpenedException){}return;}
                Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false); PixelFont.Initialize();
                if(args.Length>=2 && args[0]=="--self-test") { Tests.Run(args[1]); return; }
                if(args.Length>=2 && args[0]=="--audio-self-test") { Tests.RunAudio(args[1]); return; }
                if(args.Length>0 && args[0]=="--enable-startup") { Startup.Set(true); return; }
                if(args.Length>0 && args[0]=="--disable-startup") { Startup.Set(false); return; }
                bool created;
                using(var mutex=new Mutex(true,@"Local\SoulsRevive.Katana.DesktopCompanion"+profileSuffix,out created))
                {
                    using (ShowRequested = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\SoulsRevive.ShowWindow"+profileSuffix))
                    using (QuitRequested = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\SoulsRevive.Quit"+profileSuffix))
                    {
                        if(!created) { ShowRequested.Set(); return; }
                        using(var form=new Companion(Storage.Load(),false)) Application.Run(form);
                    }
                }
            }
            catch(Exception ex)
            {
                if(args.Length>=2 && (args[0]=="--self-test" || args[0]=="--audio-self-test"))
                { Directory.CreateDirectory(args[1]); File.WriteAllText(Path.Combine(args[1],"failure.txt"),ex.ToString()); Environment.ExitCode=1; }
                else MessageBox.Show(L.Text("Soul Revive başlatılamadı.\n","Could not start Soul Revive.\n")+ex.Message,"Soul Revive",MessageBoxButtons.OK,MessageBoxIcon.Error);
            }
        }
    }
}


