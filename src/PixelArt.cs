using System;
using System.Drawing;
#if PORTABLE
using Bitmap = SoulsRevive.Cross.PixelBitmap;
using Graphics = SoulsRevive.Cross.PixelGraphics;
using SolidBrush = SoulsRevive.Cross.PixelBrush;
using Pen = SoulsRevive.Cross.PixelPen;
#else
using System.Drawing.Drawing2D;
#endif
namespace SoulsRevive
{
    static partial class PixelArt
    {
        // Scenes repaint several times a second with hundreds of shapes: parse each hex colour and
        // create its brush once per thread (and per armour finish) instead of for every shape.
        [ThreadStatic] static System.Collections.Generic.Dictionary<string, SolidBrush> brushes;
        static SolidBrush Brush(string hex)
        {
            if (brushes == null) brushes = new System.Collections.Generic.Dictionary<string, SolidBrush>();
            string key = armorInk && Equipped.Armor != 0 ? hex + "|" + Equipped.Armor : hex;
            SolidBrush brush;
            if (!brushes.TryGetValue(key, out brush)) brushes[key] = brush = new SolidBrush(EquipmentColor(hex));
            return brush;
        }
        static void R(Graphics g, string hex, int x, int y, int w, int h) { g.FillRectangle(Brush(hex), x, y, w, h); }
        static void Poly(Graphics g, string hex, params int[] points)
        {
            Point[] p = new Point[points.Length / 2];
            for (int i = 0; i < p.Length; i++) p[i] = new Point(points[i * 2], points[i * 2 + 1]);
            g.FillPolygon(Brush(hex), p);
        }
        public static Bitmap Scene(double health, bool rest, int frame)
        { return Scene(health, rest, frame, 22, false); }
        public static Bitmap Scene(double health, bool rest, int frame, double hour, bool chill)
        { return Scene(health,rest,frame,hour,chill,WorkAction.Idle,0); }
        /// <param name="kneel">Collapse progress when a round runs out: 0 standing … <see cref="KneelFrames"/> kneeling.
        /// Negative derives it from <paramref name="health"/> (kneeling at zero), for still images and tests.</param>
        public static Bitmap Scene(double health, bool rest, int frame, double hour, bool chill, WorkAction action, int actionFrame, int kneel = -1)
        {
            if (chill) return Tavern(frame);
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
                // The moon, stars and clouds fade with the light instead of popping in and out.
                double night = Math.Max(0, Math.Min(1, (.55 - daylight) / .35));
                if (night > 0)
                {
                    Fill(g, DayCycle.Blend(sky, ColorTranslator.FromHtml("#77828A"), night), 135, 11, 13, 13);
                    Fill(g, DayCycle.Blend(sky, ColorTranslator.FromHtml("#D9DDC4"), night), 137, 12, 9, 9);
                    Fill(g, sky, 135, 9, 8, 9);
                }
                if(hour>=6 && hour<19)
                {
                    int sunX=15+(int)((hour-6)/13*140), sunY=50-(int)(Math.Sin((hour-6)/13*Math.PI)*40);
                    R(g,"#EBC27E",sunX-2,sunY+2,14,8); R(g,"#FFE4A5",sunX,sunY,10,12);
                }
                Color cloudShade = DayCycle.Blend(ColorTranslator.FromHtml("#29313F"), ColorTranslator.FromHtml("#B8CBCA"), daylight);
                Color cloudLight = DayCycle.Blend(ColorTranslator.FromHtml("#323B4A"), ColorTranslator.FromHtml("#C9D5CF"), daylight);
                for(int i=0;i<3;i++) { int cx=(i*67+frame/20)%205-25; Fill(g,cloudShade,cx,18+i*9,22,3); Fill(g,cloudLight,cx+5,16+i*9,12,2); }
                Poly(g,"#1B2330",0,62,22,43,44,59,74,29,111,61,140,37,172,56,172,86,0,86);
                R(g,"#242A35",17,26,13,43); R(g,"#242A35",13,23,5,7); R(g,"#242A35",22,20,4,8); R(g,"#242A35",29,23,5,7);
                R(g,"#1C222C",18,31,3,35); R(g,"#30333A",25,37,3,6);
                R(g,"#1C232E",34,43,15,32); R(g,"#252D38",39,35,6,12);
                R(g,"#333644",23,16,1,5); R(g,"#66504A",24,16,8,3);
                R(g,"#1A202A",144,46,17,31); R(g,"#272D37",147,39,5,10); R(g,"#272D37",156,41,4,8);
                double starlight = Math.Max(0, Math.Min(1, (.3 - daylight) / .2));
                for (int i = 0; starlight > 0 && i < 21; i++)
                {
                    int x = (i * 47 + 9) % 172, y = (i * 19 + 7) % 47;
                    Fill(g, DayCycle.Blend(sky, ColorTranslator.FromHtml(i % 5 == 0 ? "#69717B" : "#353D49"), starlight), x, y, 1, 1);
                }
                R(g,"#1E242B",0,73,172,19); R(g,"#2A2D31",0,76,172,2);
                for (int i = 0; i < 16; i++) R(g,"#363638",(i*31)%172,79+(i%4)*3,8,1);
                R(g,"#11151A",55,81,57,4);
                // Bonfire and its buried sword.
                R(g,"#392C26",111,73,32,9); R(g,"#593C2A",115,74,24,7);
                R(g,"#A36637",118,78,20,2); R(g,"#805338",115,80,14,2);
                double beat = 2 * Math.PI * (frame % 16) / 16;
                int flicker = (int)Math.Round(2 * Math.Sin(beat) + Math.Sin(3 * beat));
                // "Your fire is fading": the flames shrink as the round runs out and grow back during
                // a break. A freshly thrown log makes them flare for a moment.
                double flare = !rest && action == WorkAction.TendFire && health > 0 ? Smooth(Math.Max(0, Math.Min(1, Math.Min((actionFrame - 68) / 6.0, (100 - actionFrame) / 14.0)))) : 0;
                double flame = (health <= 0 ? .5 : .62 + .38 * Math.Max(0, Math.Min(1, health))) + .35 * flare;
                Flame(g,"#A24628",flame,119,77,116,68,121,60-flicker,124,67,128,53+flicker,132,66,136,63,139,74,134,79);
                Flame(g,"#E6853E",flame,121,76,120,69,125,64,128,58+flicker,132,70,135,72,133,77);
                Flame(g,"#FFD48B",flame,125,77,124,72,128,65+flicker,130,73,132,77);
                R(g,"#B9BBB0",128,43,2,25); R(g,"#5E646B",130,44,1,23);
                R(g,"#99794B",124,43,10,2); R(g,"#67543F",128,36,2,7); R(g,"#B6A071",127,35,4,2);
                for (int i=0;i<(flame<.7 ? 4 : flare>.2 ? 14 : 8);i++)
                {
                    int rise = (frame * 2 + i * 7) % 31;
                    int drift = (int)Math.Round(rise * .15 + 2 * Math.Sin(beat + i));
                    R(g, rise < 17 ? "#DF9250" : "#8F4D31",120+(i*11)%16+drift,67-rise,1,1);
                }
                if(rest) {using(new ArmorInk(true))DrawRestingKnight(g,frame,true);}
                else {
                using(new ArmorInk(true))DrawWorkingKnight(g,health,frame,action,actionFrame,kneel);
                }
                // Foreground tufts and stones anchor the scene.
                R(g,"#4A4740",45,82,7,3); R(g,"#636055",46,81,4,1);
                R(g,"#343B38",9,79,1,5); R(g,"#41493E",10,81,2,1);
                R(g,"#343B38",158,79,1,5); R(g,"#41493E",156,81,2,1);
            }
            return bitmap;
        }
        static void Fill(Graphics g, Color color, int x, int y, int w, int h) { using (var b = new SolidBrush(color)) g.FillRectangle(b, x, y, w, h); }
        /// <summary>Flame polygon scaled upward from its base so a smaller fire stays seated on the logs.</summary>
        static void Flame(Graphics g, string hex, double scale, params int[] points)
        {
            var scaled = new int[points.Length];
            for (int i = 0; i < points.Length; i += 2) { scaled[i] = points[i]; scaled[i + 1] = 79 - (int)Math.Round((79 - points[i + 1]) * scale); }
            Poly(g, hex, scaled);
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
            var shieldPose=g.Save();g.TranslateTransform(85,39);DrawEquippedShield(g);g.Restore(shieldPose);
            var swordPose=g.Save();g.TranslateTransform(62,66);g.ScaleTransform(.45f,.45f);HeldSword(g,0,0,180);g.Restore(swordPose);
            if(asleep)
            {
                // Two staggered Z's drift up, grow from a small z to a capital Z, and fade into the night.
                for(int k=0;k<2;k++)
                {
                    int rise=(frame/3+k*12)%24;
                    double fade=rise<3 ? rise/3.0 : Math.Max(0,(24-rise)/8.0);
                    if(fade<=0)continue;
                    Color ink=DayCycle.Blend(ColorTranslator.FromHtml("#1B2433"),ColorTranslator.FromHtml("#CFD5C3"),Math.Min(1,fade));
                    int size=rise<10 ? 4 : 5;
                    int x=84+rise/3+(int)Math.Round(Math.Sin(rise*.5)),y=58-rise;
                    Fill(g,ink,x,y,size,1);
                    for(int d=1;d<size-1;d++)Fill(g,ink,x+size-1-d,y+d,1,1);
                    Fill(g,ink,x,y+size-1,size,1);
                }
            }
        }
#if !PORTABLE
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
#endif
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
#if !PORTABLE
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr h);
#endif
    }

}
