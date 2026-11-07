using System;
using System.Drawing;

namespace SoulsRevive.Cross
{
    // Small managed raster backend for the shared pixel artwork. System.Drawing's
    // Color/Point are portable value types; no GDI+, libgdiplus or WinForms is used.
    sealed class PixelBitmap : IDisposable
    {
        internal readonly CrossCanvas Canvas;
        public int Width => Canvas.Width;
        public int Height => Canvas.Height;
        public PixelBitmap(int width, int height) { Canvas = new CrossCanvas(width, height); }
        public Color GetPixel(int x, int y)
        {
            uint c = Canvas.Pixels[y * Width + x];
            return Color.FromArgb((int)(c >> 24), (int)(c & 255), (int)((c >> 8) & 255), (int)((c >> 16) & 255));
        }
        public void Dispose() { }
    }

    sealed class PixelBrush : IDisposable
    {
        internal readonly uint Color;
        public PixelBrush(Color color) { Color = Pack(color); }
        internal static uint Pack(Color c) => (uint)(c.R | c.G << 8 | c.B << 16 | c.A << 24);
        public void Dispose() { }
    }

    sealed class PixelPen : IDisposable
    {
        internal readonly uint Color;
        internal readonly int Width;
        public PixelPen(Color color, int width = 1) { Color = PixelBrush.Pack(color); Width = width; }
        public void Dispose() { }
    }

    sealed class PixelGraphics : IDisposable
    {
        readonly CrossCanvas canvas;
        PixelGraphics(PixelBitmap image) { canvas = image.Canvas; }
        public static PixelGraphics FromImage(PixelBitmap image) => new PixelGraphics(image);
        public Transform2D Save() => canvas.Transform;
        public void Restore(Transform2D state) { canvas.Transform = state; }
        public void TranslateTransform(float x, float y) => canvas.Translate(x, y);
        public void ScaleTransform(float x, float y) => canvas.Scale(x, y);
        public void RotateTransform(float angle) => canvas.Rotate(angle);
        public void Clear(Color color) => canvas.Clear(PixelBrush.Pack(color));
        public void FillRectangle(PixelBrush brush, int x, int y, int width, int height) => canvas.R(brush.Color, x, y, width, height);
        public void FillPolygon(PixelBrush brush, Point[] points)
        {
            var coordinates = new int[points.Length * 2];
            for (int i = 0; i < points.Length; i++) { coordinates[i * 2] = points[i].X; coordinates[i * 2 + 1] = points[i].Y; }
            canvas.Poly(brush.Color, coordinates);
        }
        public void DrawLines(PixelPen pen, Point[] points)
        {
            for (int i = 1; i < points.Length; i++)
            {
                Point a = points[i-1], b = points[i];
                int steps = Math.Max(Math.Abs(b.X-a.X), Math.Abs(b.Y-a.Y));
                for (int step = 0; step <= steps; step++)
                {
                    double t = steps == 0 ? 0 : step / (double)steps;
                    int x = (int)Math.Round(a.X+(b.X-a.X)*t), y = (int)Math.Round(a.Y+(b.Y-a.Y)*t);
                    canvas.R(pen.Color, x-pen.Width/2, y-pen.Width/2, pen.Width, pen.Width);
                }
            }
        }
        public void DrawImageUnscaled(PixelBitmap image, int x, int y)
        {
            for (int row = 0; row < image.Height; row++)
                for (int col = 0; col < image.Width; col++)
                {
                    uint color = image.Canvas.Pixels[row * image.Width + col];
                    if ((color >> 24) != 0) canvas.R(color, x + col, y + row, 1, 1);
                }
        }
        public void Dispose() { }
    }
}
