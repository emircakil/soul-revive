using System;
using System.Drawing;
namespace SoulsRevive
{
    enum WorkAction { Idle, Sharpen, Map, TendFire }
    sealed class WorkRoutine
    {
        readonly Random random;
        int wait=24;
        public WorkAction Action { get; private set; }
        public int Frame { get; private set; }
        public const int Duration=112;
        public WorkRoutine() : this(new Random()) { }
        internal WorkRoutine(Random random) { this.random=random; }
        public void Reset() { Action=WorkAction.Idle;Frame=0;wait=24; }
        public void Advance(bool working)
        {
            if(!working)return;
            if(Action==WorkAction.Idle)
            {
                if(--wait<=0) {Action=(WorkAction)random.Next(1,4);Frame=0;}
            }
            else if(++Frame>=Duration)
            {Action=WorkAction.Idle;Frame=0;wait=random.Next(80,161);}
        }
    }
    static partial class PixelArt
    {
        static double WorkEnvelope(int frame)
        {return Math.Max(0,Math.Min(1,Math.Min((frame-8)/16.0,(WorkRoutine.Duration-9-frame)/16.0)));}
        static void DrawWorkingKnight(Graphics g,double health,int frame,WorkAction action,int actionFrame)
        {
            double beat=2*Math.PI*(frame%16)/16;
                int bob = (int)Math.Round(Math.Sin(beat / 2 + (frame / 16 % 2) * Math.PI));
                int slump = health <= 0 ? 7 : 0;
                double activity = WorkEnvelope(actionFrame);
                if(action==WorkAction.Idle || health<=0)activity=0;
                double shieldStow=action==WorkAction.Idle || health<=0 ? 0 : Math.Max(0,Math.Min(1,Math.Min(actionFrame/8.0,(111-actionFrame)/8.0)));
                int travel=action==WorkAction.TendFire?(int)Math.Round(24*activity):0;
                int ox = 65+travel, oy = 26 + (activity>0 ? 0 : bob) + slump;
                var saved = g.Save(); g.TranslateTransform(ox, oy);
                // During the turn the blade passes behind the entire upper body,
                // so it cannot emerge through the neck beneath the helmet.
                if(action==WorkAction.Sharpen && activity>0 && activity<1)
                    HeldSword(g,17,35,-90*activity);
                if((action==WorkAction.Map || action==WorkAction.TendFire) && activity>=.35)HeldSword(g,5,17,135);
                // A broad, clean cape silhouette with two folds and a restrained breeze.
                int wind=(int)Math.Round(Math.Sin(frame*Math.PI/32));
                Poly(g,"#30232B",-9,18,7,18,13,29,18+wind,47,10,49,-3,46,-15,50,-25+wind,47,-17,31);
                Poly(g,"#884A48",-9,20,-14,32,-22+wind,46,-15,47,-8,43,-3,24);
                Poly(g,"#B06B57",-10,23,-12,34,-17+wind,44,-14,44,-7,31,-5,21);
                Poly(g,"#60343D",-4,22,7,20,11,31,15+wind,46,9,46,3,41);
                Poly(g,"#9B584E",5,22,8,30,11+wind,44,8,43,3,29);
                var legs=g.Save();
                g.TranslateTransform(0,activity>0 ? 0 : -bob);
                if (slump == 0)
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
                if(action==WorkAction.TendFire && activity>0 && activity<1)
                { int step=(actionFrame/3)%2; R(g,"#2E3540",-10+step*3,49,11,5);R(g,"#2E3540",4-step*3,49,12,5); }
                g.Restore(legs);
                Poly(g,"#242A32",-9,17,9,17,14,25,10,35,-8,35,-13,25);
                Poly(g,"#717F8C",-7,18,7,18,11,26,7,31,-6,31,-9,25);
                Poly(g,"#A8B4BC",-7,18,1,18,-2,29,-6,28,-8,24);
                R(g,"#D0D3CD",-7,19,11,2); R(g,"#455361",1,23,8,3);
                R(g,"#8C754F",-8,31,17,3); R(g,"#C5AD77",-1,31,3,3);
                R(g,"#424A55",-12,21,6,13); R(g,"#96A3AC",-13,20,7,5);
                R(g,"#D1D6D0",-12,20,5,2);
                R(g,"#424A55",9,21,6,14); R(g,"#96A3AC",8,20,8,5); R(g,"#D1D6D0",9,20,5,2);
                // Stow the shield completely before either hand starts a task.
                if(shieldStow<1)
                {
                    var shieldState=g.Save();
                    g.TranslateTransform((float)(12*shieldStow),(float)(-10*shieldStow));
                    g.ScaleTransform((float)(1-.7*shieldStow),1);
                    DrawEquippedShield(g);
                    g.Restore(shieldState);
                }
                DrawWorkEquipment(g,action,actionFrame,activity);
                if(shieldStow>0 && shieldStow<1)
                {int handX=(int)(-12+20*shieldStow),handY=(int)(35-10*shieldStow);Arm(g,-10,handX,handY);Glove(g,handX,handY);}
                // Hands hold the paper still while the helm slowly scans left and right.
                int look=action==WorkAction.Map ? (int)Math.Round(2*activity*Math.Sin((actionFrame-24)*Math.PI/32)) : 0;
                if(action==WorkAction.Map)g.TranslateTransform((float)Math.Round(activity)+look,(float)Math.Round(2*activity));
                // Tall enclosed helm and narrow glowing visor.
                if (health < .25) g.TranslateTransform(1, 2 + Math.Max(bob, 0));
                R(g,"#242A33",-6,1,14,18); R(g,"#7F8B95",-7,4,16,13);
                R(g,"#B3BEC2",-5,1,11,3); R(g,"#D1D8D2",-5,4,3,9);
                R(g,"#A9B5BB",-2,4,8,4); R(g,"#566470",6,5,3,12);
                R(g,"#272E37",-6,9,13,3); R(g, health < .25 ? "#E48F63" : "#D4BB85",-4,10,8,1);
                R(g,"#C0C8C8",-1+look,6,2,11); R(g,"#56616A",-5,14,3,1); R(g,"#56616A",3,14,2,1);
                R(g,"#87949B",-5,17,11,2); R(g,"#393F48",-3,19,7,2);
                g.Restore(saved);
        }
        static void Arm(Graphics g,int shoulderX,int handX,int handY)
        {
            int elbowX=(shoulderX+handX)/2,elbowY=30;
            using(var outline=new Pen(ColorTranslator.FromHtml("#28313B"),6))
            using(var steel=new Pen(EquipmentColor("#82939F"),3))
            {
                Point[] points={new Point(shoulderX,24),new Point(elbowX,elbowY),new Point(handX,handY)};
                g.DrawLines(outline,points);g.DrawLines(steel,points);
            }
        }
        static void Glove(Graphics g,int x,int y)
        {R(g,"#34424C",x-3,y-2,6,6);R(g,"#AAB7BD",x-2,y-2,4,3);R(g,"#D0D7D0",x-2,y-2,3,1);}
        static void HeldSword(Graphics g,int x,int y,double angle)
        {
            using(new ArmorInk(false))
            {
            var state=g.Save();g.TranslateTransform(x,y);g.RotateTransform((float)angle);
            R(g,"#605449",-1,-2,3,9);R(g,"#BDA16D",-2,6,5,2);
            R(g,"#AD8C58",-5,-4,11,2);
            if(Equipped.Sword==1) {Poly(g,"#CCDDE1",-2,-4,-3,-25,0,-31,3,-25,3,-4);R(g,"#7995A4",1,-24,2,20);R(g,"#F1F0D9",-1,-26,1,22);}
            else if(Equipped.Sword==2) {R(g,"#ABC7CC",0,-31,1,27);R(g,"#ECF4DD",0,-32,1,2);R(g,"#D2A85D",-6,-4,13,2);}
            else {R(g,"#DFDCD0",-1,-28,2,24);R(g,"#8B9DA7",1,-27,1,23);R(g,"#E4E1CF",0,-31,1,4);}
            g.Restore(state);
            }
        }
        static void DrawWorkEquipment(Graphics g,WorkAction action,int frame,double blend)
        {
            int rightX=17,rightY=35;
            if(action==WorkAction.Sharpen || blend==0)
            {
                // The same grip anchors sword, wrist and forearm throughout the rotation.
                bool honing=action==WorkAction.Sharpen && frame>=24 && frame<80;
                double handBlend=frame>=80 ? Math.Max(0,(88-frame)/8.0) : blend;
                double stroke=honing ? Math.Sin(frame*.45) : Math.Sin(79*.45);
                int leftX=(int)Math.Round(-12+handBlend*(13+5*stroke)),leftY=(int)Math.Round(34-3*handBlend);
                Arm(g,12,rightX,rightY);
                if(handBlend>0)Arm(g,-10,leftX,leftY);
                if(action!=WorkAction.Sharpen || blend<=0 || blend>=1)
                    HeldSword(g,rightX,rightY,-90*blend);
                Glove(g,rightX,rightY);
                if(handBlend>0)
                {
                    Glove(g,leftX,leftY);
                    if(honing){R(g,"#707775",leftX-2,leftY+2,6,3);R(g,"#CED1B8",leftX-1,leftY+2,4,1);}
                    if(honing)DrawHoningSparks(g,frame);
                }
                return;
            }
            // Stow the full-length sword behind the shoulder; keep its tip above the ground.
            if(blend<.35)
            {
                double stow=blend/.35;
                int gripX=rightX-(int)Math.Round(12*stow),gripY=rightY-(int)Math.Round(18*stow);
                HeldSword(g,gripX,gripY,135*stow);Arm(g,12,gripX,gripY);Glove(g,gripX,gripY);return;
            }

            if(action==WorkAction.Map)
            {
                int width=(int)Math.Round(26*Math.Min(1,(blend-.35)/.65));
                int left=4-width/2,right=4+width/2;
                int fingerX=right,fingerY=35;
                Arm(g,-10,left,35);Arm(g,12,fingerX,fingerY);
                R(g,"#756041",left-1,27,width+3,17);R(g,"#CEB77F",left,28,width+1,14);
                if(width>16)
                {
                    R(g,"#AB915F",4,28,1,14);R(g,"#866F4B",left+3,32,6,1);R(g,"#866F4B",left+7,33,1,5);
                    R(g,"#866F4B",left+7,37,9,1);R(g,"#9D5544",right-5,31,3,3);
                }
                Glove(g,left,35);Glove(g,fingerX,fingerY);
            }
            else
            {
                // Compact wind-up and release. The log travels independently after frame 52.
                double toss=frame<40?0:frame<48?(frame-40)/8.0:frame<52?1-(frame-48)/4.0:0;
                int handX=17-(int)Math.Round(4*toss),handY=35-(int)Math.Round(9*toss);
                int leftX=handX-18,leftY=handY+1;
                // Settle both empty hands at the sides by frame 64, then hold
                // that relaxed pose until the return walk begins at frame 88.
                if(frame>=52)
                {
                    double settle=Math.Min(1,(frame-52)/12.0);
                    handX=17-(int)Math.Round(4*settle);
                    handY=35+(int)Math.Round(2*settle);
                    leftX=-1-(int)Math.Round(10*settle);
                    leftY=36+(int)Math.Round(settle);
                }
                Arm(g,12,handX,handY);Arm(g,-10,leftX,leftY);
                if(frame<52)
                    DrawWorkLog(g,handX-5,handY,0);
                Glove(g,handX,handY);Glove(g,leftX,leftY);
                if(frame>=52 && frame<72)
                {
                    double flight=(frame-52)/19.0;
                    int logX=(int)Math.Round(12+27*flight);
                    int logY=(int)Math.Round(35+17*flight-19*4*flight*(1-flight));
                    DrawWorkLog(g,logX,logY,flight*180);
                }
                else if(frame>=72 && frame<88)
                    DrawWorkLog(g,39,52,0);

            }
        }
        static void DrawHoningSparks(Graphics g,int frame)
        {
            // Three single-pixel motes per stroke, anchored to where contact occurred.
            int age=(frame-24)%8;
            if(age>=5)return;
            int born=frame-age;
            int originX=(int)Math.Round(1+5*Math.Sin(born*.45))+1;
            for(int i=0;i<3;i++)
            {
                int x=originX+(int)Math.Round((i-1)*age*.9);
                int y=34+(int)Math.Round(age*(.5+i*.3)+age*age*.08);
                R(g,age<2?"#FFE7AC":age<4?"#DFA052":"#9B633C",x,y,1,1);
            }
        }
        static void DrawWorkLog(Graphics g,int x,int y,double angle)
        {
            var saved=g.Save();g.TranslateTransform(x,y);g.RotateTransform((float)angle);
            R(g,"#4B3025",-11,-3,23,7);R(g,"#96613A",-10,-2,21,4);
            R(g,"#C28A50",9,-2,3,4);R(g,"#BC8550",-8,-2,12,1);
            g.Restore(saved);
        }
    }
}
