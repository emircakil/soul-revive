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
        public const int TavernArrivalFrames = 32;
        public const int TavernCycleFrames = 128;

        // Original pixel sprites, using the same 172 x 92 grid as the bonfire scene.
        // No clock or theme input: the inn always has warm, firelit evening lighting.
        public static Bitmap Tavern(int frame)
        {
            frame=Math.Max(0,frame);
            var bitmap=new Bitmap(172,92);
            using(var g=Graphics.FromImage(bitmap))
            {
                g.Clear(ColorTranslator.FromHtml("#322B2A"));
                DrawTavernRoom(g,frame);
                DrawTavernChair(g,47); DrawTavernChair(g,126);
                // The table and chairs sit behind the characters and their mugs.
                R(g,"#211C1D",77,81,40,3);
                R(g,"#39261E",82,67,5,15);R(g,"#A16E43",83,68,2,13);
                R(g,"#39261E",107,67,5,15);R(g,"#795032",108,68,2,13);
                R(g,"#513424",79,66,36,4);R(g,"#B9824B",78,64,38,2);
                R(g,"#D09A5A",80,64,33,1);R(g,"#71462D",79,68,36,1);

                DrawTavernKnight(g,124,frame,true,false);
                if(frame<TavernArrivalFrames)
                {
                    int x=-15+(79*frame)/(TavernArrivalFrames-1);
                    DrawTavernKnight(g,x,frame,false,true);
                    DrawTankard(g,83,57,false,0);DrawTankard(g,106,57,true,0);
                }
                else
                {
                    DrawTavernKnight(g,64,frame,false,false);
                    DrawTavernDrinks(g,(frame-TavernArrivalFrames)%128,frame);
                }
                // Foreground planks, spilled light, and a little brass inn token.
                R(g,"#9D683D",4,88,34,1);R(g,"#9D683D",101,89,42,1);
                R(g,"#DBAD68",95,66,2,1);
            }
            return bitmap;
        }

        static void DrawTavernRoom(Graphics g,int frame)
        {
            // Uneven masonry and heavy timber keep the background visibly pixel drawn.
            for(int row=0;row<5;row++)for(int col=0;col<9;col++)
            {
                int x=col*22-(row%2)*11,y=10+row*13;
                R(g,(row+col)%3==0?"#493B32":"#40352F",x+1,y+1,20,11);
                R(g,"#554235",x+3,y+2,15,1);
            }
            R(g,"#201E20",0,0,172,8);R(g,"#765036",0,8,172,3);R(g,"#A47343",0,8,172,1);
            foreach(int x in new[]{0,43,165})
            {R(g,"#251E1D",x,10,7,68);R(g,"#6A452D",x+1,11,4,66);R(g,"#96653E",x+2,13,1,62);}
            R(g,"#261E1A",0,76,172,16);R(g,"#70482D",0,78,172,14);
            for(int y=79;y<92;y+=5)
            {
                R(g,"#39291F",0,y,172,1);
                for(int x=(y%3)*13;x<172;x+=43) {R(g,"#453023",x,y+1,1,4);R(g,"#98643B",x+3,y+2,18,1);}
            }
            // Hearth: bright coals, shifting flame shapes and reflected amber on stone.
            R(g,"#201C1D",5,33,36,43);R(g,"#7F6248",6,30,34,5);R(g,"#B59061",8,30,29,1);
            R(g,"#5C4939",7,35,5,38);R(g,"#9A7850",8,37,2,31);
            R(g,"#5C4939",34,35,5,38);R(g,"#9A7850",35,38,2,30);
            R(g,"#17191C",12,36,22,36);R(g,"#542D22",13,53,20,18);
            int flicker=(frame/2)%4;
            Poly(g,"#BD5629",14,70,13,59,18,53-flicker,20,60,25,47+flicker,28,59,32,56,32,70);
            Poly(g,"#F6A343",16,69,17,61,21,57,24,53+flicker,27,63,30,66,28,70);
            Poly(g,"#FFE3A0",21,69,21,64,24,59+flicker,27,69);
            R(g,"#855134",13,70,19,3);R(g,"#C68A48",15,70,15,1);
            R(g,"#77604B",5,73,35,4);R(g,"#B48A54",7,73,31,1);
            for(int i=0;i<3;i++)R(g,"#EDB468",17+i*5,53-((frame+i*11)%17),1,1);

            // Bottle shelf, a barrel, and the inn's sun-and-tankard sign.
            R(g,"#211E1C",132,29,27,3);R(g,"#A37548",131,28,29,2);
            DrawBottle(g,134,17,"#647757");DrawBottle(g,143,19,"#AA6F43");DrawBottle(g,152,16,"#748376");
            R(g,"#30231E",145,61,17,18);R(g,"#91613A",146,62,15,15);R(g,"#BB8650",148,63,3,13);
            R(g,"#483B33",145,65,17,3);R(g,"#AC936D",146,65,15,1);R(g,"#483B33",145,74,17,3);
            R(g,"#281F1D",70,14,49,17);R(g,"#765035",71,15,47,15);R(g,"#D3A36B",72,15,45,1);
            R(g,"#362821",72,28,45,2);DrawSunCrest(g,82,22);
            DrawTankard(g,103,19,false,0);
            R(g,"#9A754B",74,10,1,5);R(g,"#9A754B",114,10,1,5);
            // A candle behind Solaire, with a slow warm flicker.
            R(g,"#806343",132,47,8,2);R(g,"#CBB790",135,41,2,6);
            R(g,frame%12<6?"#FFE1A0":"#F3BD68",135,37,2,4);R(g,"#E58A43",134,39,1,2);
        }

        static void DrawBottle(Graphics g,int x,int y,string glass)
        {R(g,"#221F20",x,y,5,10);R(g,glass,x,y+3,5,7);R(g,glass,x+1,y,3,5);R(g,"#D4B886",x+1,y,3,1);R(g,"#C7BA92",x+1,y+6,3,2);}
        static void DrawTavernChair(Graphics g,int x)
        {R(g,"#281F1C",x,54,5,29);R(g,"#8F5F38",x+1,55,3,26);R(g,"#AD7D46",x,55,5,2);R(g,"#69422A",x,72,17,4);R(g,"#B3844E",x,72,17,1);R(g,"#3A291F",x+12,76,3,7);}

        static void DrawTavernKnight(Graphics g,int x,int frame,bool solaire,bool walking)
        {using(new ArmorInk(!solaire))DrawTavernKnightCore(g,x,frame,solaire,walking);}
        static void DrawTavernKnightCore(Graphics g,int x,int frame,bool solaire,bool walking)
        {
            int breath=walking?-(int)Math.Round(Math.Abs(Math.Sin(frame*Math.PI/4))):((frame/12+(solaire?1:0))%2);
            var saved=g.Save();g.TranslateTransform(x,breath);
            // Rust-red cape for the player; green sleeves and a white sun surcoat for Solaire.
            if(!solaire)
            {Poly(g,"#35252B",-10,51,-13,70,-6,77,4,72,6,53);Poly(g,"#874A3C",-9,52,-11,71,-5,74,2,70,3,54);R(g,"#BA7251",-9,55,2,13);}
            if(walking)
            {
                int stride=(int)Math.Round(3*Math.Sin(frame*Math.PI/4));
                R(g,"#384550",-6+stride,68,5,12);R(g,"#9AA5A6",-6+stride,69,3,6);R(g,"#27303A",-8+stride,79,8,3);
                R(g,"#495563",3-stride,68,5,12);R(g,"#B1BAB7",3-stride,69,2,6);R(g,"#27303A",2-stride,79,9,3);
            }
            else
            {
                int knee=solaire?-12:6;
                R(g,"#35404A",-6,68,13,6);R(g,"#65717A",-5,68,11,3);
                R(g,"#47525C",knee,72,6,8);R(g,"#98A3A5",knee,73,3,5);R(g,"#263039",knee-1,79,10,3);
            }
            R(g,"#293039",-9,51,18,18);
            if(solaire)
            {
                R(g,"#69805B",-11,51,7,10);R(g,"#92A078",-10,51,5,3);
                R(g,"#597151",5,51,7,11);R(g,"#88996B",6,52,4,2);
                Poly(g,"#C9C7AD",-6,51,5,51,8,69,-8,69);R(g,"#F1E8C9",-5,52,8,15);R(g,"#A1A18A",5,55,2,11);
                DrawSunCrest(g,0,60);R(g,"#6A4C33",-8,68,16,3);R(g,"#CCA569",-2,68,3,3);
                FollowCupWithHead(g, frame, true);
                // Bucket helm, chainmail collar and the small red plume.
                R(g,"#39444A",-7,35,14,17);R(g,"#AEB9B3",-6,35,12,14);R(g,"#DCE0CB",-5,35,4,12);
                R(g,"#7C8A8B",3,37,3,12);R(g,"#364047",-6,41,12,2);R(g,"#E4C98E",-5,42,9,1);
                R(g,"#D5DBC6",-1,37,2,12);R(g,"#646F70",-5,47,3,1);R(g,"#646F70",3,47,2,1);
                R(g,"#75807A",-5,50,10,3);R(g,"#AFB3A0",-3,51,1,2);R(g,"#AFB3A0",1,51,1,2);
                R(g,"#713B37",-2,32,4,3);R(g,"#B85B41",-1,30,4,3);R(g,"#D78B55",1,30,2,1);
            }
            else
            {
                // Three-quarter rear pose: the cape and back face us, the visor faces Solaire.
                Poly(g,"#465663",-7,52,3,51,8,54,8,66,3,69,-6,67,-9,59);
                Poly(g,"#713E36",-9,52,-2,52,2,56,1,68,-5,73,-10,68,-11,59);
                Poly(g,"#A05D45",-8,53,-4,53,-2,58,-3,69,-6,70,-8,65);
                R(g,"#C38358",-7,55,1,10);R(g,"#55332F",-2,58,2,10);
                Poly(g,"#8C9FA5",2,53,6,53,8,59,7,65,3,66,1,61);
                R(g,"#C9D1C7",3,54,3,2);R(g,"#5C7380",5,59,3,5);
                R(g,"#927448",-5,68,12,3);R(g,"#D4B578",3,68,3,2);
                // The free arm rests on the lap rather than duplicating the raised arm.
                Poly(g,"#2D3A45",-9,55,-5,54,-3,63,1,66,-1,70,-7,66);
                R(g,"#82969F",-8,55,3,7);R(g,"#B8C5C1",-8,55,2,2);
                R(g,"#8D9F9F",-3,66,5,3);R(g,"#C6CEC0",-2,66,3,1);
                FollowCupWithHead(g, frame, false);
                // An asymmetric profile makes the direction of the helmet unmistakable.
                Poly(g,"#29343D",-5,35,3,35,7,38,9,41,9,48,5,51,-5,51,-7,47,-7,38);
                R(g,"#7F949F",-5,37,9,13);R(g,"#5E7482",-6,39,3,10);
                R(g,"#C1CFCA",-4,36,7,2);R(g,"#9FB4B6",-3,39,5,9);
                R(g,"#D6DDD0",3,39,3,3);R(g,"#A7BBBA",6,40,2,8);
                R(g,"#26333B",2,42,7,3);R(g,"#E9CB8C",5,43,4,1);
                R(g,"#BFCCC6",7,45,2,4);R(g,"#788E93",-3,50,10,2);
                R(g,"#B5C3BD",1,50,5,1);
                if(walking) {R(g,"#91A6AD",5,54,5,11);R(g,"#C7D0C4",6,54,3,2);R(g,"#869B9F",6,64,4,4);}
            }
            g.Restore(saved);
        }

        static void DrawSunCrest(Graphics g,int x,int y)
        {
            R(g,"#B67536",x-4,y-1,9,3);R(g,"#B67536",x-1,y-4,3,9);
            foreach(int direction in new[]{-1,1}) {R(g,"#B67536",x+direction*3,y-3,1,1);R(g,"#B67536",x+direction*3,y+3,1,1);}
            R(g,"#E3AF4F",x-2,y-2,5,5);R(g,"#A55C35",x-1,y-1,1,1);R(g,"#A55C35",x+1,y-1,1,1);R(g,"#A55C35",x-1,y+1,3,1);
        }

        static double Gesture(int frame,int begin,int peak,int endHold,int end)
        {if(frame<begin||frame>=end)return 0;if(frame<peak)return(frame-begin)/(double)(peak-begin);if(frame<endHold)return 1;return(end-frame)/(double)(end-endHold);}
        // Both the helm and the tankard use this pose so the rim follows the mouth.
        struct SipPose
        {
            public Point Head, Cup, Shoulder, Elbow;
            public double Angle;
            public double WidthScale;
        }

        static SipPose DrinkingPose(int frame, bool solaire)
        {
            int cycle = (frame - TavernArrivalFrames) % 128;
            int local = cycle - (solaire ? 91 : 60);
            int side = solaire ? -1 : 1;
            double reach = solaire ? Smooth(Gesture(local, 11, 18, 28, 35))
                                  : Smooth(Gesture(local, 0, 10, 24, 34));
            double tip = solaire ? Smooth(Gesture(local, 18, 22, 24, 28))
                                : Smooth(Gesture(local, 10, 16, 20, 24));
            double lift = Smooth(Gesture(cycle, solaire ? 23 : 18, solaire ? 33 : 29,
                                        solaire ? 40 : 42, solaire ? 53 : 57));
            double extend = Smooth(Gesture(cycle, solaire ? 29 : 26, 36, 37,
                                          solaire ? 48 : 51));
            int breath = (frame / 12 + (solaire ? 1 : 0)) % 2;
            var pose = new SipPose();
            // Switch silhouettes on the wrist beat, then hold the mirrored mug through contact.
            pose.WidthScale = solaire && local >= 10 && local < 31 ? -1 : 1;
            pose.Head = new Point((int)Math.Round(side * (2 * reach - tip + extend)),
                                  (int)Math.Round(reach - 2 * tip));
            pose.Angle = -side * (solaire ? 51 : 57) * tip;
            // Solaire leads with the elbow, then turns his wrist inward before contact.
            double fold = 0;
            if (solaire)
            {
                fold = Smooth(Gesture(local, 0, 9, 27, 35));
                pose.Angle = 60 * tip + 12 * lift * (1 - extend);
            }
            double angle = pose.Angle * Math.PI / 180;
            // The near lip of the rim is one pixel across and five above the grip.
            double lip = (solaire && pose.WidthScale < 0 ? -9 : side) * pose.WidthScale;
            double rimX = lip * Math.Cos(angle) + 5 * Math.Sin(angle);
            double rimY = lip * Math.Sin(angle) - 5 * Math.Cos(angle);
            int gripOffset = solaire ? 8 : -2;
            double contactX = (solaire ? 118 : 73) + pose.Head.X - rimX - gripOffset;
            double contactY = 47 + breath + pose.Head.Y - rimY - 5;
            int restX = solaire ? 106 : 83;
            // The wrist clears the tabletop on an arc, then the glass turns at the lip.
            double arc = Math.Sin(Math.PI * reach);
            pose.Cup = new Point(
                (int)Math.Round(restX + (contactX - restX) * reach + side * 3 * arc + (solaire ? -5 : 10) * extend),
                (int)Math.Round(57 + (contactY - 57) * reach - 3 * arc - 9 * lift - extend
                                - 10 * fold * (1 - reach)));
            // Lead the offer with the shoulder; unfold the elbow as the cups meet.
            // During a sip the elbow stays below the wrist, alongside the knight's ribs.
            int shoulderX = solaire ? 115 : 71;
            pose.Shoulder = new Point(shoulderX + (int)Math.Round(side * (2 * extend + reach)),
                                      55 + breath - (int)Math.Round(lift));
            double elbowForward = solaire
                ? 1 + 4 * extend + 7 * fold
                : 1 + 8 * extend + 5 * reach;
            double elbowRise = 3 * lift + 2 * extend + (solaire ? 5 * fold : 5 * reach) + tip;
            pose.Elbow = new Point(shoulderX + (int)Math.Round(side * elbowForward),
                                   64 + breath - (int)Math.Round(elbowRise));
            return pose;
        }

        static void FollowCupWithHead(Graphics g, int frame, bool solaire)
        {
            Point offset = DrinkingPose(frame, solaire).Head;
            g.TranslateTransform(offset.X, offset.Y);
        }

        static void DrawTavernDrinks(Graphics g,int cycle,int frame)
        {
            foreach (bool solaire in new[] { false, true })
            {
                SipPose pose = DrinkingPose(frame, solaire);
                Point grip = new Point(pose.Cup.X + (solaire ? 8 : -2), pose.Cup.Y + 5);
                using(new ArmorInk(!solaire))DrawDrinkingArm(g, pose.Shoulder, pose.Elbow, grip, solaire);
                DrawTankard(g, pose.Cup.X, pose.Cup.Y, solaire, pose.Angle, pose.WidthScale);
                using(new ArmorInk(!solaire))
                {
                    R(g,solaire ? "#869184" : "#778F99",grip.X-1,grip.Y,3,3);
                    R(g,"#C2CFC7",grip.X-1,grip.Y,2,1);
                }
            }
            if(cycle>=36&&cycle<39)
            {
                int sparkle=(cycle/2)%2;
                R(g,"#FFE8AA",98,43-sparkle,1,3);R(g,"#FFE8AA",94,43,2,1);R(g,"#FFE8AA",101,43,2,1);
            }
        }
        static double Smooth(double value) {return value*value*(3-2*value);}

        static void DrawDrinkingArm(Graphics g,Point shoulder,Point elbow,Point hand,bool solaire)
        {
            PixelLimb(g,shoulder,elbow,"#28363F",5);PixelLimb(g,elbow,hand,"#28363F",5);
            PixelLimb(g,shoulder,elbow,solaire ? "#768765" : "#78909C",3);PixelLimb(g,elbow,hand,"#97AEB1",3);
            PixelLimb(g,new Point(shoulder.X-1,shoulder.Y-1),new Point(elbow.X-1,elbow.Y-1),"#AEBFC0",1);
            PixelLimb(g,new Point(elbow.X-1,elbow.Y-1),new Point(hand.X-1,hand.Y-1),"#D0D8C9",1);
            R(g,"#4C6472",elbow.X-2,elbow.Y-1,5,4);R(g,"#A6B9BB",elbow.X-1,elbow.Y-1,3,1);
            // The pauldron overlaps both the torso and upper arm; there is no floating shoulder.
            Poly(g,"#293740",shoulder.X-4,shoulder.Y-3,shoulder.X+2,shoulder.Y-3,shoulder.X+4,shoulder.Y,shoulder.X+2,shoulder.Y+4,shoulder.X-3,shoulder.Y+3);
            R(g,solaire ? "#879777" : "#9BAFB5",shoulder.X-3,shoulder.Y-2,6,4);R(g,"#D0D8CC",shoulder.X-2,shoulder.Y-2,4,1);
            R(g,"#8BA0A4",hand.X-2,hand.Y-1,5,4);R(g,"#D0D8C9",hand.X-1,hand.Y-1,3,1);
        }
        static void PixelLimb(Graphics g,Point a,Point b,string color,int width)
        {
            int steps=Math.Max(Math.Abs(b.X-a.X),Math.Abs(b.Y-a.Y));
            using(var brush=new SolidBrush(ColorTranslator.FromHtml(color)))
                for(int i=0;i<=steps;i++)
                {
                    double t=steps==0?0:i/(double)steps;
                    int x=(int)Math.Round(a.X+(b.X-a.X)*t),y=(int)Math.Round(a.Y+(b.Y-a.Y)*t);
                    g.FillRectangle(brush,x-width/2,y-width/2,width,width);
                }
        }
        static void DrawTankard(Graphics g,int x,int y,bool handleRight,double angle,double widthScale=1)
        {
            using(var mug=new Bitmap(14,12))
            {
                using(var sprite=Graphics.FromImage(mug))
                {
                    int hx=handleRight?10:0;
                    R(sprite,"#382B22",hx,4,4,5);R(sprite,"#B6A17A",hx+1,4,2,4);R(sprite,"#4F3B28",hx+1,5,1,2);
                    R(sprite,"#DDC68F",3,2,7,8);R(sprite,"#A87032",4,3,5,6);R(sprite,"#E5AE4D",4,3,2,5);
                    R(sprite,"#FFF0C6",2,1,9,2);R(sprite,"#F5DEAA",5,0,4,1);R(sprite,"#E5D3A2",3,9,7,1);
                }
                if(Math.Abs(angle)<.01 && widthScale==1) {g.DrawImageUnscaled(mug,x-3,y-1);return;}
                int pivotX=handleRight?11:1,pivotY=6;
                double radians=angle*Math.PI/180,cos=Math.Cos(radians),sin=Math.Sin(radians);
                // Inverse nearest-neighbor rotation keeps the handle on the hand and every pixel crisp.
                for(int dy=-18;dy<=18;dy++)for(int dx=-18;dx<=18;dx++)
                {
                    int sx=(int)Math.Round(pivotX+(cos*dx+sin*dy)/widthScale),sy=(int)Math.Round(pivotY-sin*dx+cos*dy);
                    if(sx<0||sx>=mug.Width||sy<0||sy>=mug.Height)continue;
                    Color color=mug.GetPixel(sx,sy);if(color.A==0)continue;
                    using(var brush=new SolidBrush(color))g.FillRectangle(brush,x-3+pivotX+dx,y-1+pivotY+dy,1,1);
                }
            }
        }
    }
}
