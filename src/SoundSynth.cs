using System;
using System.IO;

namespace SoulsRevive
{
    /// <summary>
    /// Original fire, wind and bonfire-melody layers, synthesized locally into 16 kHz mono PCM WAV.
    /// No downloads, codecs, recordings or extra runtime dependencies. Shared by both shells.
    /// </summary>
    static class SoundSynth
    {
        public const int Rate = 16000;
        static readonly int[] Roots = { 45, 41, 48, 43 };
        static readonly int[] Melody = { 69, 72, 76, 72, 67, 71, 74, 71, 64, 67, 72, 67, 62, 67, 71, 67 };

        /// <summary>
        /// Renders a WAV file. With <paramref name="loop"/> the last sample flows into the first one, so
        /// repeating the buffer has no gap, dip or click; otherwise short fades keep a one-shot clean.
        /// </summary>
        public static byte[] Render(int fire, int ambience, int music, int seconds, bool loop = true)
        {
            short[] pcm = RenderSamples(fire, ambience, music, seconds, loop);
            using (var ms = new MemoryStream(44 + pcm.Length * 2))
            using (var writer = new BinaryWriter(ms))
            {
                writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + pcm.Length * 2);
                writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16); writer.Write((short)1); writer.Write((short)1);
                writer.Write(Rate); writer.Write(Rate * 2); writer.Write((short)2); writer.Write((short)16);
                writer.Write(System.Text.Encoding.ASCII.GetBytes("data")); writer.Write(pcm.Length * 2);
                foreach (short sample in pcm) writer.Write(sample);
                return ms.ToArray();
            }
        }

        /// <summary>A short two-note chime for break reminders.</summary>
        public static byte[] Reminder() { return Render(0, 0, 80, 1, false); }

        public static short[] RenderSamples(int fire, int ambience, int music, int seconds, bool loop)
        {
            int samples = Rate * seconds;
            // Noise layers are rendered past the end and crossfaded into the start (equal power, as
            // the noise is uncorrelated). Music is periodic over 32 s and enveloped at chord changes.
            int overlap = loop ? Math.Min(samples / 4, Rate / 2) : 0;
            var noise = new double[samples + overlap];
            var random = new Random(7491); double low = 0, crackle = 0;
            for (int i = 0; i < noise.Length; i++)
            {
                double t = i / (double)Rate, white = random.NextDouble() * 2 - 1;
                low += .012 * (white - low);
                if (random.NextDouble() < .0008) crackle = random.NextDouble() * .7 + .3;
                crackle *= .97;
                double flame = (low * 2.8 + white * crackle * .65 + white * .035) * (.7 + .15 * Math.Sin(t * 1.8));
                double air = low * 2 * (.55 + .3 * Math.Sin(2 * Math.PI * t / 32)) + Math.Sin(t * 4000) * Math.Pow(Math.Max(0, Math.Sin(t * .8)), 20) * .018;
                noise[i] = flame * fire / 100.0 + air * ambience / 100.0;
            }
            var pcm = new short[samples];
            for (int i = 0; i < samples; i++)
            {
                double t = i / (double)Rate;
                double layer = noise[i];
                if (i < overlap)
                {
                    double w = (i + .5) / overlap;
                    layer = noise[i] * Math.Sin(w * Math.PI / 2) + noise[samples + i] * Math.Cos(w * Math.PI / 2);
                }
                double sample = (layer + MusicAt(t) * music / 100.0) * .7;
                if (!loop) sample *= Math.Min(1, Math.Min(t / .04, (seconds - t) / .04));
                pcm[i] = (short)(Math.Max(-1, Math.Min(1, sample)) * 32760);
            }
            return pcm;
        }

        static double MusicAt(double t)
        {
            int chord = Roots[((int)t / 8) % 4];
            // Fade the pad briefly around each chord change instead of jumping mid-wave.
            double inChord = t % 8, pad = 0;
            double padEnvelope = Math.Min(1, Math.Min(inChord / .05, (8 - inChord) / .05));
            foreach (int note in new[] { chord, chord + 7, chord + 12 })
            { double f = 440 * Math.Pow(2, (note - 69) / 12.0); pad += Math.Sin(t * f * 2 * Math.PI) * .065; }
            double phase = t % 2, frequency = 440 * Math.Pow(2, (Melody[((int)t / 2) % 16] - 69) / 12.0);
            double release = Math.Min(1, (2 - phase) / .03);
            double pluck = (Math.Sin(2 * Math.PI * frequency * t) + .22 * Math.Sin(4 * Math.PI * frequency * t)) * Math.Exp(-phase * 2.4) * Math.Min(1, phase * 50) * release * .16;
            return pad * padEnvelope + pluck;
        }
    }
}
