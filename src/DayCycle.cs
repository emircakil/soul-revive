using System;
using System.Drawing;
namespace SoulsRevive
{
    static class DayCycle
    {
        public static double Hour(Preferences p)
        {
            if (p.Chill) return 22;
            if (p.InBreak) return 22;
            return 5.5 + 15 * (1 - Math.Max(0, Math.Min(1, p.Remaining / (p.WorkMinutes * 60.0))));
        }
        public static double Sunlight(double hour) { return Math.Max(0, Math.Sin((hour - 6) * Math.PI / 12)); }
        public static Color Blend(Color a, Color b, double t)
        { t = Math.Max(0, Math.Min(1, t)); return Color.FromArgb((int)(a.R+(b.R-a.R)*t), (int)(a.G+(b.G-a.G)*t), (int)(a.B+(b.B-a.B)*t)); }
    }

}
