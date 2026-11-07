using System;
using System.Collections.Generic;

namespace SoulsRevive.Cross
{
    public struct PointF
    {
        public float X, Y;
        public PointF(float x, float y) { X = x; Y = y; }
    }

    public struct Transform2D
    {
        public float M11, M12, M21, M22, DX, DY;

        public static Transform2D Identity => new Transform2D { M11 = 1, M22 = 1 };

        public PointF Transform(float x, float y)
        {
            return new PointF(x * M11 + y * M21 + DX, x * M12 + y * M22 + DY);
        }

        public Transform2D Multiply(Transform2D next)
        {
            return new Transform2D {
                M11 = M11 * next.M11 + M12 * next.M21,
                M12 = M11 * next.M12 + M12 * next.M22,
                M21 = M21 * next.M11 + M22 * next.M21,
                M22 = M21 * next.M12 + M22 * next.M22,
                DX = DX * next.M11 + DY * next.M21 + next.DX,
                DY = DX * next.M12 + DY * next.M22 + next.DY
            };
        }
    }

    public sealed class CrossCanvas
    {
        public readonly int Width;
        public readonly int Height;
        public readonly uint[] Pixels;

        Transform2D current = Transform2D.Identity;
        public Transform2D Transform { get => current; set => current = value; }
        readonly Stack<Transform2D> stack = new Stack<Transform2D>();

        public CrossCanvas(int width = 172, int height = 92)
        {
            Width = width;
            Height = height;
            Pixels = new uint[width * height];
        }

        public void Push() => stack.Push(current);

        public void Pop()
        {
            if (stack.Count > 0) current = stack.Pop();
            else current = Transform2D.Identity;
        }

        public void Translate(float x, float y)
        {
            var t = new Transform2D { M11 = 1, M22 = 1, DX = x, DY = y };
            current = t.Multiply(current);
        }

        public void Rotate(float angleDeg)
        {
            float rad = angleDeg * (float)(Math.PI / 180.0);
            float cos = (float)Math.Cos(rad);
            float sin = (float)Math.Sin(rad);
            var r = new Transform2D { M11 = cos, M12 = sin, M21 = -sin, M22 = cos };
            current = r.Multiply(current);
        }

        public void Scale(float sx, float sy)
        {
            var s = new Transform2D { M11 = sx, M22 = sy };
            current = s.Multiply(current);
        }

        public static uint Hex(string hex)
        {
            if (string.IsNullOrEmpty(hex)) return 0;
            if (hex[0] == '#') hex = hex.Substring(1);
            if (hex.Length == 6)
            {
                byte r = Convert.ToByte(hex.Substring(0, 2), 16);
                byte g = Convert.ToByte(hex.Substring(2, 2), 16);
                byte b = Convert.ToByte(hex.Substring(4, 2), 16);
                return (uint)(r | (g << 8) | (b << 16) | (0xFF << 24));
            }
            if (hex.Length == 8)
            {
                byte a = Convert.ToByte(hex.Substring(0, 2), 16);
                byte r = Convert.ToByte(hex.Substring(2, 2), 16);
                byte g = Convert.ToByte(hex.Substring(4, 2), 16);
                byte b = Convert.ToByte(hex.Substring(6, 2), 16);
                return (uint)(r | (g << 8) | (b << 16) | (a << 24));
            }
            return 0xFFFFFFFF;
        }

        public static uint Blend(uint a, uint b, double t)
        {
            t = Math.Max(0, Math.Min(1, t));
            byte ra = (byte)(a & 0xFF), ga = (byte)((a >> 8) & 0xFF), ba = (byte)((a >> 16) & 0xFF), aa = (byte)((a >> 24) & 0xFF);
            byte rb = (byte)(b & 0xFF), gb = (byte)((b >> 8) & 0xFF), bb = (byte)((b >> 16) & 0xFF), ab = (byte)((b >> 24) & 0xFF);
            int nr = (int)(ra + (rb - ra) * t);
            int ng = (int)(ga + (gb - ga) * t);
            int nb = (int)(ba + (bb - ba) * t);
            int na = (int)(aa + (ab - aa) * t);
            return (uint)(nr | (ng << 8) | (nb << 16) | (na << 24));
        }

        public void Clear(uint color)
        {
            Array.Fill(Pixels, color);
        }

        public void R(uint color, int x, int y, int w, int h)
        {
            if (w <= 0 || h <= 0) return;
            // Check if axis-aligned (no rotation/skew)
            if (Math.Abs(current.M12) < 0.001f && Math.Abs(current.M21) < 0.001f && current.M11 > 0 && current.M22 > 0)
            {
                PointF p0 = current.Transform(x, y);
                PointF p1 = current.Transform(x + w, y + h);
                int x0 = Math.Max(0, (int)Math.Round(Math.Min(p0.X, p1.X)));
                int x1 = Math.Min(Width, (int)Math.Round(Math.Max(p0.X, p1.X)));
                int y0 = Math.Max(0, (int)Math.Round(Math.Min(p0.Y, p1.Y)));
                int y1 = Math.Min(Height, (int)Math.Round(Math.Max(p0.Y, p1.Y)));
                for (int cy = y0; cy < y1; cy++)
                {
                    int row = cy * Width;
                    for (int cx = x0; cx < x1; cx++) Pixels[row + cx] = color;
                }
            }
            else
            {
                Poly(color, x, y, x + w, y, x + w, y + h, x, y + h);
            }
        }

        public void R(string hex, int x, int y, int w, int h) => R(Hex(hex), x, y, w, h);

        public void Poly(uint color, params int[] rawPoints)
        {
            int n = rawPoints.Length / 2;
            if (n < 3) return;
            PointF[] pts = new PointF[n];
            float minY = float.MaxValue, maxY = float.MinValue;
            for (int i = 0; i < n; i++)
            {
                pts[i] = current.Transform(rawPoints[i * 2], rawPoints[i * 2 + 1]);
                if (pts[i].Y < minY) minY = pts[i].Y;
                if (pts[i].Y > maxY) maxY = pts[i].Y;
            }

            int yStart = Math.Max(0, (int)Math.Floor(minY));
            int yEnd = Math.Min(Height - 1, (int)Math.Ceiling(maxY));
            var nodeX = new List<float>(n);

            for (int y = yStart; y <= yEnd; y++)
            {
                nodeX.Clear();
                float scanY = y + 0.5f;
                int j = n - 1;
                for (int i = 0; i < n; i++)
                {
                    float y_i = pts[i].Y, y_j = pts[j].Y;
                    if ((y_i < scanY && y_j >= scanY) || (y_j < scanY && y_i >= scanY))
                    {
                        float x = pts[i].X + (scanY - y_i) / (y_j - y_i) * (pts[j].X - pts[i].X);
                        nodeX.Add(x);
                    }
                    j = i;
                }
                nodeX.Sort();

                int row = y * Width;
                for (int i = 0; i < nodeX.Count - 1; i += 2)
                {
                    int xStart = Math.Max(0, (int)Math.Round(nodeX[i]));
                    int xEnd = Math.Min(Width, (int)Math.Round(nodeX[i + 1]));
                    for (int x = xStart; x < xEnd; x++)
                    {
                        Pixels[row + x] = color;
                    }
                }
            }
        }

        public void Poly(string hex, params int[] rawPoints) => Poly(Hex(hex), rawPoints);

        public void Line(uint color, float x0, float y0, float x1, float y1)
        {
            PointF p0 = current.Transform(x0, y0);
            PointF p1 = current.Transform(x1, y1);
            int ix0 = (int)Math.Round(p0.X), iy0 = (int)Math.Round(p0.Y);
            int ix1 = (int)Math.Round(p1.X), iy1 = (int)Math.Round(p1.Y);
            int dx = Math.Abs(ix1 - ix0), dy = Math.Abs(iy1 - iy0);
            int sx = ix0 < ix1 ? 1 : -1, sy = iy0 < iy1 ? 1 : -1;
            int err = dx - dy;

            while (true)
            {
                if (ix0 >= 0 && ix0 < Width && iy0 >= 0 && iy0 < Height)
                    Pixels[iy0 * Width + ix0] = color;
                if (ix0 == ix1 && iy0 == iy1) break;
                int e2 = 2 * err;
                if (e2 > -dy) { err -= dy; ix0 += sx; }
                if (e2 < dx) { err += dx; iy0 += sy; }
            }
        }

        public void Lines(string hex, params int[] rawPoints)
        {
            uint col = Hex(hex);
            for (int i = 0; i < rawPoints.Length / 2 - 1; i++)
            {
                Line(col, rawPoints[i * 2], rawPoints[i * 2 + 1], rawPoints[(i + 1) * 2], rawPoints[(i + 1) * 2 + 1]);
            }
        }
    }
}
