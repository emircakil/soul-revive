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


#if !PORTABLE
using System.Windows.Forms;
#endif

namespace SoulsRevive
{
    sealed class KnightEquipment
    {
        public readonly int Armor,Sword,Shield;
        public KnightEquipment(int armor,int sword,int shield)
        {Armor=Math.Max(0,Math.Min(2,armor));Sword=Math.Max(0,Math.Min(2,sword));Shield=Math.Max(0,Math.Min(2,shield));}
        public static KnightEquipment From(Preferences p) {return new KnightEquipment(p.ArmorStyle,p.SwordStyle,p.ShieldStyle);}
    }
    static partial class PixelArt
    {
        internal static KnightEquipment Equipped=new KnightEquipment(0,0,0);
        static bool armorInk;
        sealed class ArmorInk : IDisposable
        {
            readonly bool before;
            public ArmorInk(bool enabled){before=armorInk;armorInk=enabled;}
            public void Dispose(){armorInk=before;}
        }
        static Color EquipmentColor(string hex)
        {
            Color c=ColorTranslator.FromHtml(hex);
            if(!armorInk || Equipped.Armor==0 || c.R>c.G || c.G>c.B+16 || c.B-c.R>65)return c;
            // Preserve the sprite's existing light and shadow while changing metal finish.
            double brightness=(c.R+c.G+c.B)/765.0;
            if(Equipped.Armor==1)return DayCycle.Blend(Color.FromArgb(19,23,32),Color.FromArgb(139,154,176),brightness);
            return DayCycle.Blend(Color.FromArgb(51,35,28),Color.FromArgb(244,208,140),brightness);
        }
        internal static Bitmap EquipmentScene(KnightEquipment equipment,double health,bool rest,int frame,double hour,bool chill,WorkAction action,int actionFrame)
        {
            var previous=Equipped;
            try {Equipped=equipment;return Scene(health,rest,frame,hour,chill,action,actionFrame);}
            finally {Equipped=previous;}
        }
        static void DrawEquippedShield(Graphics g)
        {
            using(new ArmorInk(false))
            {
                if(Equipped.Shield==1)
                {
                    Poly(g,"#342F2A",-19,29,-15,26,-9,26,-5,30,-5,40,-9,45,-15,45,-19,40);
                    Poly(g,"#9D7750",-17,30,-14,28,-10,28,-7,31,-7,39,-10,43,-14,43,-17,39);
                    R(g,"#684C35",-13,29,1,13);R(g,"#B8C0BD",-15,33,7,6);R(g,"#E0D7B9",-13,34,3,3);
                }
                else if(Equipped.Shield==2)
                {
                    Poly(g,"#283747",-19,26,-5,26,-5,41,-12,48,-19,41);
                    Poly(g,"#A7B6BE",-18,27,-6,27,-6,40,-12,46,-18,40);
                    Poly(g,"#3B607A",-16,29,-8,29,-8,39,-12,43,-16,39);
                    Poly(g,"#DCE6CF",-12,31,-9,36,-12,40,-15,36);
                }
                else
                {
                    Poly(g,"#252A32",-19,28,-7,26,-5,41,-11,48,-19,41);
                    Poly(g,"#6E747A",-18,29,-8,28,-7,40,-11,45,-17,40);
                    Poly(g,"#354650",-16,30,-9,29,-9,39,-12,43,-15,39);
                    R(g,"#B9A16D",-13,31,2,10);R(g,"#B9A16D",-16,34,7,2);
                }
            }
        }
    }
#if !PORTABLE
    sealed class EquipmentPreview : Control
    {
        public Func<KnightEquipment> Selection;
        public EquipmentPreview(){DoubleBuffered=true;AccessibleName=L.Text("Şövalye ekipman önizlemesi","Knight equipment preview");}
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);if(Selection==null)return;
            e.Graphics.InterpolationMode=InterpolationMode.NearestNeighbor;e.Graphics.PixelOffsetMode=PixelOffsetMode.Half;
            using(var scene=PixelArt.EquipmentScene(Selection(),1,false,0,12,false,WorkAction.Idle,0))e.Graphics.DrawImage(scene,ClientRectangle);
        }
    }
#endif
}
