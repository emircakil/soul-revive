using System;
using System.IO;
namespace SoulsRevive.Cross
{
    public static class Soundscape
    {
        public static byte[] Render(int fire, int ambience, int music, int seconds)
        {
            const int rate = 16000;
            using (var ms = new MemoryStream())
            using (var writer = new BinaryWriter(ms))
            {
                int samples = rate * seconds;
                writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
                writer.Write(36 + samples * 2);
                writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));
                writer.Write(16);
                writer.Write((short)1);
                writer.Write((short)1);
                writer.Write(rate);
                writer.Write(rate * 2);
                writer.Write((short)2);
                writer.Write((short)16);
                writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));
                writer.Write(samples * 2);

                var random = new Random(7491);
                double low = 0, crackle = 0;
                int[] roots = { 45, 41, 48, 43 };
                int[] melody = { 69, 72, 76, 72, 67, 71, 74, 71, 64, 67, 72, 67, 62, 67, 71, 67 };

                for (int i = 0; i < samples; i++)
                {
                    double t = i / (double)rate, noise = random.NextDouble() * 2 - 1;
                    low += 0.012 * (noise - low);
                    if (random.NextDouble() < 0.0008) crackle = (random.NextDouble() * 0.7 + 0.3);
                    crackle *= 0.97;
                    double flame = (low * 2.8 + noise * crackle * 0.65 + noise * 0.035) * (0.7 + 0.15 * Math.Sin(t * 1.8));
                    double air = low * 2 * (0.55 + 0.3 * Math.Sin(2 * Math.PI * t / 32.0)) + Math.Sin(t * 4000) * Math.Pow(Math.Max(0, Math.Sin(t * 0.8)), 20) * 0.018;
                    int chord = roots[((int)t / 8) % 4];
                    double pad = 0;
                    foreach (int note in new[] { chord, chord + 7, chord + 12 })
                    {
                        double f = 440 * Math.Pow(2, (note - 69) / 12.0);
                        pad += Math.Sin(t * f * 2 * Math.PI) * 0.065;
                    }
                    double phase = t % 2, frequency = 440 * Math.Pow(2, (melody[((int)t / 2) % 16] - 69) / 12.0);
                    double pluck = (Math.Sin(2 * Math.PI * frequency * t) + 0.22 * Math.Sin(4 * Math.PI * frequency * t)) * Math.Exp(-phase * 2.4) * Math.Min(1, phase * 50) * 0.16;
                    double fade = Math.Min(1, Math.Min(t / 0.04, (seconds - t) / 0.04));
                    double sample = (flame * fire / 100.0 + air * ambience / 100.0 + (pad + pluck) * music / 100.0) * fade * 0.7;
                    writer.Write((short)(Math.Max(-1, Math.Min(1, sample)) * 32760));
                }
                return ms.ToArray();
            }
        }
    }

    public static class CrossTheme
    {
        public static bool Light;

        public static string BackgroundHex => Light ? "#EEE4CB" : "#14191E";
        public static string PanelHex => Light ? "#DFD2B2" : "#20292D";
        public static string LineHex => Light ? "#9C8865" : "#48504A";
        public static string GoldHex => Light ? "#81501F" : "#E9B86B";
        public static string TextHex => Light ? "#302D29" : "#F1E8CF";
        public static string MutedHex => Light ? "#635B4A" : "#A2ADA4";
        public static string GreenHex => Light ? "#32634A" : "#98C99A";
    }

}
