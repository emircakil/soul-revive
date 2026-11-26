using System;
using System.Drawing;
using System.Windows.Forms;

namespace SoulsRevive
{
    sealed class PixelSlider : TrackBar
    {
        bool sliding;
        public PixelSlider() { SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true); AutoSize = false; }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.Clear(Theme.Background); Dpi.Prepare(g);
            var area = Dpi.Logical(this);
            int x = 12 + (int)((area.Width - 24) * (Value - Minimum) / (double)(Maximum - Minimum)); int y = area.Height / 2;
            using (var b = new SolidBrush(Theme.Line)) g.FillRectangle(b, 12, y - 2, area.Width - 24, 4);
            using (var b = new SolidBrush(Theme.Gold)) g.FillRectangle(b, 12, y - 2, Math.Max(0, x - 12), 4);
            using (var b = new SolidBrush(Theme.Line)) for (int i = 0; i <= 10; i++) g.FillRectangle(b, 12 + (area.Width - 24) * i / 10, y + 9, 2, 3);
            Theme.Frame(g, new Rectangle(x - 6, y - 8, 14, 17), Theme.Gold);
            if (Focused) ControlPaint.DrawFocusRectangle(g, new Rectangle(2, 2, area.Width - 4, area.Height - 4), Theme.Text, Theme.Background);
        }
        void MoveThumb(int physicalX)
        {
            // Mouse positions are physical pixels; the track is laid out in 96 DPI units.
            double x = physicalX / Dpi.Scale, width = Width / Dpi.Scale;
            Value = Math.Max(Minimum, Math.Min(Maximum, Minimum + (int)Math.Round((x - 12) * (Maximum - Minimum) / (width - 24))));
            Invalidate();
        }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { Focus(); sliding = true; Capture = true; MoveThumb(e.X); } }
        protected override void OnMouseMove(MouseEventArgs e) { if (sliding) MoveThumb(e.X); }
        protected override void OnMouseUp(MouseEventArgs e) { if (sliding) { sliding = false; Capture = false; MoveThumb(e.X); } base.OnMouseUp(e); }
        protected override void OnValueChanged(EventArgs e) { base.OnValueChanged(e); Invalidate(); }
    }
    sealed class PixelCombo : ComboBox
    {
        public PixelCombo() { DropDownStyle = ComboBoxStyle.DropDownList; DrawMode = DrawMode.OwnerDrawFixed; ItemHeight = 28; FlatStyle = FlatStyle.Flat; BackColor = Theme.Panel; ForeColor = Theme.Text; Font = PixelFont.Create(22); }
        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            using (var b = new SolidBrush((e.State & DrawItemState.Selected) != 0 ? Theme.Line : Theme.Panel)) e.Graphics.FillRectangle(b, e.Bounds);
            if (e.Index >= 0)
            {
                var state = e.Graphics.Save();
                e.Graphics.TranslateTransform(e.Bounds.X, e.Bounds.Y); Dpi.Prepare(e.Graphics);
                int width = (int)Math.Round(e.Bounds.Width / Dpi.Scale), height = (int)Math.Round(e.Bounds.Height / Dpi.Scale);
                Theme.Label(e.Graphics, Items[e.Index].ToString(), 11, Theme.Text, new Rectangle(8, 0, width - 16, height), ContentAlignment.MiddleLeft);
                e.Graphics.Restore(state);
            }
            if ((e.State & DrawItemState.Focus) != 0) e.DrawFocusRectangle();
        }
    }
}
