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
        [DataMember] public bool Chill;
        [DataMember] public bool LightMode;
        [DataMember] public int FireVolume = 35;
        [DataMember] public int AmbienceVolume = 25;
        [DataMember] public int MusicVolume = 20;
        [DataMember] public bool AudioEnabled;
        [DataMember] public double TotalWorkSeconds;
        [DataMember] public double TodayWorkSeconds;
        [DataMember] public int CompletedSessions;
        [DataMember] public int SchemaVersion = 2;

        [OnDeserializing] void Defaults(StreamingContext context)
        { WorkMinutes = 30; BreakMinutes = 3; Remaining = 1800; AlwaysOnTop = true; Sound = true; X = Y = int.MinValue; Language = "tr"; FireVolume = 35; AmbienceVolume = 25; MusicVolume = 20; }

        public void Validate()
        {
            WorkMinutes = Math.Max(1, Math.Min(180, WorkMinutes));
            BreakMinutes = Math.Max(1, Math.Min(30, BreakMinutes));
            if (Chill) { InBreak = false; Paused = false; }
            WorkLeftBeforeBreak = Math.Min(WorkMinutes * 60.0, ValidSeconds(WorkLeftBeforeBreak));
            Remaining = double.IsNaN(Remaining) || double.IsInfinity(Remaining) ? WorkMinutes * 60 :
                Math.Max(0, Math.Min(Remaining, (InBreak ? BreakMinutes : WorkMinutes) * 60));
            Breaks = Math.Max(0, Breaks);
            if (double.IsNaN(MovementSeconds) || double.IsInfinity(MovementSeconds)) MovementSeconds = 0;
            MovementSeconds = Math.Max(0, MovementSeconds);
            TotalWorkSeconds = ValidSeconds(TotalWorkSeconds); TodayWorkSeconds = ValidSeconds(TodayWorkSeconds);
            CompletedSessions = Math.Max(0, CompletedSessions);
            FireVolume = Math.Max(0, Math.Min(100, FireVolume)); AmbienceVolume = Math.Max(0, Math.Min(100, AmbienceVolume)); MusicVolume = Math.Max(0, Math.Min(100, MusicVolume));
            Language = Language == "en" ? "en" : "tr"; SchemaVersion = 2;
            CheckDay();
        }
        static double ValidSeconds(double n) { return double.IsNaN(n) || double.IsInfinity(n) ? 0 : Math.Max(0, n); }
        public void CheckDay()
        { CheckDay(DateTime.Now); }
        public void CheckDay(DateTime now)
        {
            string today = now.ToString("yyyy-MM-dd");
            if (Day != today) { Day = today; Breaks = 0; MovementSeconds = 0; TodayWorkSeconds = 0; }
        }
    }

    public sealed class TimerEngine
    {
        public readonly Preferences P;
        public event Action<string> Changed;
        public TimerEngine(Preferences p) { P = p; p.Validate(); }
        public bool Due { get { return !P.Chill && !P.InBreak && P.Remaining <= 0; } }
        public double Health
        {
            get { return P.InBreak ? 1 - (1 - P.WorkLeftBeforeBreak / (P.WorkMinutes * 60.0)) * P.Remaining / (P.BreakMinutes * 60.0) : P.Remaining / (P.WorkMinutes * 60.0); }
        }
        public void Advance(double seconds)
        { Advance(seconds, DateTime.Now); }
        public void Advance(double seconds, DateTime now)
        {
            P.CheckDay(now);
            if (P.Chill || P.Paused || seconds <= 0 || seconds > 5 || double.IsNaN(seconds)) return;
            double before = P.Remaining;
            double used = Math.Min(seconds, before);
            P.Remaining = Math.Max(0, before - seconds);
            if (P.InBreak) P.MovementSeconds += used;
            else
            {
                P.TotalWorkSeconds += used;
                // Allocate a tick crossing midnight only to its current-day portion.
                P.TodayWorkSeconds += Math.Min(used, now.TimeOfDay.TotalSeconds);
            }
            if (before > 0 && P.Remaining == 0)
            {
                if (P.InBreak)
                {
                    P.Breaks++;
                    P.InBreak = false;
                    P.Remaining = P.WorkMinutes * 60;
                    Signal("revived");
                }
                else { P.CompletedSessions++; Signal("due"); }
            }
        }
        public void BeginBreak()
        {
            if (P.InBreak || P.Chill) return;
            P.WorkLeftBeforeBreak = P.Remaining;
            P.InBreak = true; P.Paused = false; P.Remaining = P.BreakMinutes * 60; Signal("break");
        }
        public void CancelBreak()
        {
            if (!P.InBreak) return;
            // Returning to work starts a full round without counting a completed break.
            P.InBreak = false; P.Remaining = P.WorkMinutes * 60; P.Paused = false; Signal("cancel");
        }
        public void TogglePause() { if (P.Chill) return; P.Paused = !P.Paused; Signal("pause"); }
        public void SetChill(bool chill)
        {
            if (P.Chill == chill) return;
            P.Chill = chill; P.InBreak = false; P.Paused = false;
            P.Remaining = P.WorkMinutes * 60; Signal("mode");
        }
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
            using (Font f = PixelFont.Create(size * 1.65f))
            using (var brush = new SolidBrush(color))
            using (var format = new StringFormat())
            {
                g.TextRenderingHint = TextRenderingHint.SingleBitPerPixelGridFit;
                format.Alignment = align == ContentAlignment.MiddleCenter ? StringAlignment.Center : StringAlignment.Near;
                format.LineAlignment = StringAlignment.Center; format.Trimming = StringTrimming.EllipsisCharacter;
                format.FormatFlags = StringFormatFlags.NoWrap;
                g.DrawString(s,f,brush,rect,format);
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
        protected override void OnTextChanged(EventArgs e) { AccessibleName = Text; base.OnTextChanged(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Color bg = Primary ? Theme.Gold : Theme.Panel;
            if(!Enabled)bg=Theme.Panel;
            if(hover && Enabled) bg=DayCycle.Blend(bg,Theme.Text,.12);
            Theme.Frame(g,ClientRectangle,bg);
            using(var pen=new Pen(DayCycle.Blend(bg,Theme.Text,.25),2)) g.DrawLine(pen,5,4,Width-6,4);
            Theme.Label(g, Text, 10, !Enabled ? Theme.Muted : Primary ? Theme.Background : Theme.Text, ClientRectangle, ContentAlignment.MiddleCenter, "pixel");
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
        { return Scene(health, rest, frame, 22, false); }
        public static Bitmap Scene(double health, bool rest, int frame, double hour, bool chill)
        {
            var bitmap = new Bitmap(172, 92);
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                double daylight = DayCycle.Sunlight(hour);
                Color sky = DayCycle.Blend(ColorTranslator.FromHtml("#121C30"), ColorTranslator.FromHtml("#83B6BE"),daylight);
                double dawn = Math.Max(0,1-Math.Abs(hour-6)/2), dusk = Math.Max(0,1-Math.Abs(hour-18.5)/2);
                sky = DayCycle.Blend(sky,ColorTranslator.FromHtml("#B87978"),Math.Max(dawn,dusk)*.7);
                g.Clear(sky);
                for(int y=32;y<74;y+=4)
                    using(var b=new SolidBrush(DayCycle.Blend(sky,ColorTranslator.FromHtml("#D6AC7D"),(y-32)/90.0*(.25+daylight*.5)))) g.FillRectangle(b,0,y,172,4);
                // Distant moon, broken keep, and mountain silhouettes.
                if(hour>=6 && hour<19)
                {
                    int sunX=15+(int)((hour-6)/13*140), sunY=50-(int)(Math.Sin((hour-6)/13*Math.PI)*40);
                    R(g,"#EBC27E",sunX-2,sunY+2,14,8); R(g,"#FFE4A5",sunX,sunY,10,12);
                    for(int i=0;i<3;i++) { int cx=(i*67+frame/20)%205-25; R(g,"#B8CBCA",cx,18+i*9,22,3); R(g,"#C9D5CF",cx+5,16+i*9,12,2); }
                }
                else { R(g,"#77828A",135,11,13,13); R(g,"#D9DDC4",137,12,9,9); using(var b=new SolidBrush(sky))g.FillRectangle(b,135,9,8,9); }
                Poly(g,"#1B2330",0,62,22,43,44,59,74,29,111,61,140,37,172,56,172,86,0,86);
                R(g,"#242A35",17,26,13,43); R(g,"#242A35",13,23,5,7); R(g,"#242A35",22,20,4,8); R(g,"#242A35",29,23,5,7);
                R(g,"#1C222C",18,31,3,35); R(g,"#30333A",25,37,3,6);
                R(g,"#1C232E",34,43,15,32); R(g,"#252D38",39,35,6,12);
                R(g,"#333644",23,16,1,5); R(g,"#66504A",24,16,8,3);
                R(g,"#1A202A",144,46,17,31); R(g,"#272D37",147,39,5,10); R(g,"#272D37",156,41,4,8);
                for (int i = 0; i < (daylight<.15 ? 21 : 0); i++)
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
                bool wander = chill && frame % 240 < 144;
                if(rest || (chill && !wander)) DrawRestingKnight(g,frame,rest);
                else {
                int bob = wander ? -(int)Math.Round(2 * Math.Abs(Math.Sin(beat))) : (int)Math.Round(Math.Sin(beat / 2 + (frame / 16 % 2) * Math.PI));
                int slump = health <= 0 && !rest ? 7 : 0;
                int ox = wander ? 58+(int)(18*Math.Sin(frame*Math.PI/72)) : 65, oy = 26 + bob + slump;
                var saved = g.Save(); g.TranslateTransform(ox, oy);
                // Tattered rust-red cloak, steel plate, chainmail and great helm.
                int wind = (int)Math.Round(3 * Math.Sin(beat));
                Poly(g,"#292029",-10,19,-18,30,-21+wind,49,-15+wind,46,-12+wind,50,-9,46,-5,49,1,31);
                Poly(g,"#713B39",-9,20,-15,29,-17+wind,44,-13+wind,42,-11+wind,46,-9,41,-5,44,0,25);
                R(g,"#9F5546",-11,25,3,13);
                if (wander)
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
                }
                // Foreground tufts and stones anchor the scene.
                R(g,"#4A4740",45,82,7,3); R(g,"#636055",46,81,4,1);
                R(g,"#343B38",9,79,1,5); R(g,"#41493E",10,81,2,1);
                R(g,"#343B38",158,79,1,5); R(g,"#41493E",156,81,2,1);
            }
            return bitmap;
        }
        static void DrawRestingKnight(Graphics g,int frame,bool asleep)
        {
            int breath=(frame/12)%2;
            // Resting pose: cape on the ground, bent boots, helm against the shield.
            R(g,"#29252B",73,76,39,5); Poly(g,"#713B39",79,67,99,66,111,78,73,79);
            R(g,"#A65C48",79,74,26,3);
            R(g,"#394551",87,68+breath,16,7); R(g,"#91A0AA",88,68+breath,13,3);
            R(g,"#596975",101,72,7,7); R(g,"#B2BAB9",101,72,5,2); R(g,"#34404A",105,77,9,3);
            R(g,"#28323C",73,61+breath,14,14); R(g,"#98A7AF",74,61+breath,12,12);
            R(g,"#D0D7CE",75,62+breath,3,8); R(g,"#394753",75,66+breath,10,2);
            R(g,"#C5B184",78,67+breath,4,1); R(g,"#667782",81,61+breath,4,11);
            Poly(g,"#26323A",66,67,76,66,78,77,72,81,66,77); R(g,"#8A967F",68,69,6,2); R(g,"#B49A64",71,69,1,8);
            R(g,"#B2BEC0",62,67,1,11); R(g,"#A78C5C",59,66,7,1);
            if(asleep)
            {
                int rise=(frame/4)%16; int x=83+rise/4,y=57-rise;
                R(g,"#CFD5C3",x,y,4,1); R(g,"#CFD5C3",x+2,y+1,1,1); R(g,"#CFD5C3",x+1,y+2,1,1); R(g,"#CFD5C3",x,y+3,4,1);
            }
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
                    DrawBonfireIcon(g);
                }
                IntPtr h = bmp.GetHicon();
                try { return (Icon)Icon.FromHandle(h).Clone(); }
                finally { DestroyIcon(h); }
            }
        }
        public static Bitmap IconBitmap(int size)
        {
            var bmp=new Bitmap(size,size);
            using(var g=Graphics.FromImage(bmp)) { g.Clear(Color.Transparent); g.ScaleTransform(size/32f,size/32f); DrawBonfireIcon(g); }
            return bmp;
        }
        static void DrawBonfireIcon(Graphics g)
        {
            R(g,"#222D36",5,27,23,3); R(g,"#586061",3,25,7,3); R(g,"#79817A",5,24,5,2); R(g,"#586061",24,25,5,3);
            Poly(g,"#88452C",8,26,6,18,11,10,13,17,20,4,23,15,26,19,25,26);
            Poly(g,"#F08A36",10,26,9,20,13,14,15,20,20,9,21,20,24,22,22,27);
            Poly(g,"#FFE4A0",13,26,13,22,17,16,18,23,21,26);
            R(g,"#8A5A38",7,27,18,3); R(g,"#C18D52",9,27,14,1);
            R(g,"#CFD9CF",15,7,2,15); R(g,"#738C96",17,8,1,14); R(g,"#E9C478",12,6,9,2); R(g,"#76513C",15,2,2,4); R(g,"#E2BC75",14,1,4,2);
            R(g,"#F5BC65",7,10,2,2); R(g,"#D8793B",25,8,1,2); R(g,"#F5BC65",4,19,1,1);
        }
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr h);
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
                if(args.Length>=2 && args[0]=="--self-test")
                { Directory.CreateDirectory(args[1]); File.WriteAllText(Path.Combine(args[1],"failure.txt"),ex.ToString()); Environment.ExitCode=1; }
                else MessageBox.Show(L.Text("Soul Revive başlatılamadı.\n","Could not start Soul Revive.\n")+ex.Message,"Soul Revive",MessageBoxButtons.OK,MessageBoxIcon.Error);
            }
        }
    }
}


