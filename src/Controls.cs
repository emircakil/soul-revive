using System;
using System.Drawing;
using System.Windows.Forms;

namespace SoulsRevive
{
    sealed class PixelSlider : TrackBar
    {
        bool sliding;
        public PixelSlider() {SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);AutoSize=false;}
        protected override void OnPaint(PaintEventArgs e)
        {
            var g=e.Graphics;g.Clear(Theme.Background);int x=12+(int)((Width-24)*(Value-Minimum)/(double)(Maximum-Minimum));int y=Height/2;
            using(var b=new SolidBrush(Theme.Line))g.FillRectangle(b,12,y-2,Width-24,4);
            using(var b=new SolidBrush(Theme.Gold))g.FillRectangle(b,12,y-2,Math.Max(0,x-12),4);
            for(int i=0;i<=10;i++)using(var b=new SolidBrush(Theme.Line))g.FillRectangle(b,12+(Width-24)*i/10,y+9,2,3);
            Theme.Frame(g,new Rectangle(x-6,y-8,14,17),Theme.Gold);
            if(Focused)ControlPaint.DrawFocusRectangle(g,new Rectangle(2,2,Width-4,Height-4),Theme.Text,Theme.Background);
        }
        void MoveThumb(int x) {Value=Math.Max(Minimum,Math.Min(Maximum,Minimum+(int)Math.Round((x-12)*(Maximum-Minimum)/(double)(Width-24))));Invalidate();}
        protected override void OnMouseDown(MouseEventArgs e) {if(e.Button==MouseButtons.Left){Focus();sliding=true;Capture=true;MoveThumb(e.X);} }
        protected override void OnMouseMove(MouseEventArgs e) {if(sliding)MoveThumb(e.X);}
        protected override void OnMouseUp(MouseEventArgs e) {if(sliding){sliding=false;Capture=false;MoveThumb(e.X);}base.OnMouseUp(e);}
        protected override void OnValueChanged(EventArgs e) {base.OnValueChanged(e);Invalidate();}
    }
    sealed class PixelCombo : ComboBox
    {
        public PixelCombo() {DropDownStyle=ComboBoxStyle.DropDownList;DrawMode=DrawMode.OwnerDrawFixed;ItemHeight=28;FlatStyle=FlatStyle.Flat;BackColor=Theme.Panel;ForeColor=Theme.Text;Font=PixelFont.Create(22);}
        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            using(var b=new SolidBrush((e.State&DrawItemState.Selected)!=0?Theme.Line:Theme.Panel))e.Graphics.FillRectangle(b,e.Bounds);
            if(e.Index>=0)Theme.Label(e.Graphics,Items[e.Index].ToString(),11,Theme.Text,new Rectangle(e.Bounds.X+8,e.Bounds.Y,e.Bounds.Width-16,e.Bounds.Height),ContentAlignment.MiddleLeft,"");
            if((e.State&DrawItemState.Focus)!=0)e.DrawFocusRectangle();
        }
    }
}
