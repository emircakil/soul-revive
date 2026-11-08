using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;

[assembly: AssemblyVersion("1.3.1.0")]
[assembly: AssemblyFileVersion("1.3.1.0")]
[assembly: AssemblyProduct("Soul Revive")]
[assembly: AssemblyDescription("Pixel bonfire desktop companion")]

namespace SoulsRevive
{
    static class PixelFont
    {
        static readonly PrivateFontCollection collection = new PrivateFontCollection();
        static IntPtr memory;
        public static FontFamily Family;
        public static void Initialize()
        {
            if (Family != null) return;
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("PixelFont.ttf"))
            {
                if (stream == null) throw new InvalidOperationException("Embedded pixel font is missing.");
                byte[] data = new byte[stream.Length]; stream.Read(data, 0, data.Length);
                memory = Marshal.AllocCoTaskMem(data.Length); Marshal.Copy(data, 0, memory, data.Length);
                collection.AddMemoryFont(memory, data.Length);
                uint count = 0; AddFontMemResourceEx(memory, (uint)data.Length, IntPtr.Zero, ref count);
                Family = collection.Families[0];
            }
        }
        public static Font Create(float pixels) { Initialize(); return new Font(Family, pixels, FontStyle.Regular, GraphicsUnit.Pixel); }
        public static float TextPixels(float size)
        {
            return Math.Max(18, (float)Math.Round(size * 1.8f));
        }
        [DllImport("gdi32.dll")] static extern IntPtr AddFontMemResourceEx(IntPtr font, uint size, IntPtr reserved, ref uint count);
    }

    static class ArtworkExport
    {
        public static void Save(string directory)
        {
            Directory.CreateDirectory(directory);
            int[] sizes={16,24,32,48,64,128,256};var images=new byte[sizes.Length][];
            for(int i=0;i<sizes.Length;i++)using(var bitmap=PixelArt.IconBitmap(sizes[i]))using(var ms=new MemoryStream()) {bitmap.Save(ms,System.Drawing.Imaging.ImageFormat.Png);images[i]=ms.ToArray();}
            using(var file=File.Create(Path.Combine(directory,"app.ico")))using(var writer=new BinaryWriter(file))
            {
                writer.Write((short)0);writer.Write((short)1);writer.Write((short)sizes.Length);int offset=6+16*sizes.Length;
                for(int i=0;i<sizes.Length;i++) {writer.Write((byte)(sizes[i]==256?0:sizes[i]));writer.Write((byte)(sizes[i]==256?0:sizes[i]));writer.Write((short)0);writer.Write((short)1);writer.Write((short)32);writer.Write(images[i].Length);writer.Write(offset);offset+=images[i].Length;}
                foreach(byte[] data in images)writer.Write(data);
            }
            using(var bitmap=PixelArt.IconBitmap(256))bitmap.Save(Path.Combine(directory,"app-icon.png"),System.Drawing.Imaging.ImageFormat.Png);
        }
    }

    // All three original sound layers are synthesized locally and mixed into one loop.
    // No downloads, codecs, copyrighted recordings, or extra runtime dependencies.
    sealed class Soundscape : IDisposable
    {
        readonly object gate = new object();
        LoopingAudio player;
        int generation;
        bool disposed;
        public string LastError = "";
        public event Action<string> Failed;
        internal uint PlaybackBytes { get { lock(gate) return player == null ? 0 : player.PlaybackBytes; } }
        public void Apply(Preferences p)
        {
            int request;
            lock (gate)
            {
                if (disposed) return;
                request = ++generation;
                Stop();
                LastError = "";
            }
            if (!p.AudioEnabled || (p.FireVolume + p.AmbienceVolume + p.MusicVolume == 0)) return;
            int fire = p.FireVolume, ambience = p.AmbienceVolume, music = p.MusicVolume;
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    byte[] wave = Render(fire, ambience, music, 32);
                    lock (gate)
                    {
                        if (disposed || request != generation) return;
                        player = new LoopingAudio(wave);
                    }
                }
                catch (Exception ex)
                {
                    lock (gate)
                    {
                        if (disposed || request != generation) return;
                        LastError = ex.Message;
                    }
                    var failed = Failed;
                    if (failed != null) failed(ex.Message);
                }
            });
        }
        public static byte[] Render(int fire, int ambience, int music, int seconds)
        {
            const int rate = 16000;
            using (var ms = new MemoryStream())
            using (var writer = new BinaryWriter(ms))
            {
                int samples = rate * seconds;
                writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + samples * 2);
                writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16); writer.Write((short)1); writer.Write((short)1);
                writer.Write(rate); writer.Write(rate*2); writer.Write((short)2); writer.Write((short)16);
                writer.Write(System.Text.Encoding.ASCII.GetBytes("data")); writer.Write(samples * 2);
                var random = new Random(7491); double low = 0, crackle = 0;
                int[] roots = { 45, 41, 48, 43 }; int[] melody = { 69,72,76,72,67,71,74,71,64,67,72,67,62,67,71,67 };
                for (int i = 0; i < samples; i++)
                {
                    double t = i / (double)rate, noise = random.NextDouble()*2-1;
                    low += .012 * (noise-low);
                    if (random.NextDouble() < .0008) crackle = (random.NextDouble()*.7+.3);
                    crackle *= .97;
                    double flame = (low*2.8 + noise*crackle*.65 + noise*.035) * (.7+.15*Math.Sin(t*1.8));
                    double air = low*2 * (.55+.3*Math.Sin(2*Math.PI*t/32)) + Math.Sin(t*4000)*Math.Pow(Math.Max(0,Math.Sin(t*.8)),20)*.018;
                    int chord = roots[((int)t/8)%4]; double pad = 0;
                    foreach (int note in new[] { chord, chord+7, chord+12 })
                    { double f = 440*Math.Pow(2,(note-69)/12.0); pad += Math.Sin(t*f*2*Math.PI)*.065; }
                    double phase = t%2, frequency = 440*Math.Pow(2,(melody[((int)t/2)%16]-69)/12.0);
                    double pluck = (Math.Sin(2*Math.PI*frequency*t)+.22*Math.Sin(4*Math.PI*frequency*t))*Math.Exp(-phase*2.4)*Math.Min(1,phase*50)*.16;
                    double fade = Math.Min(1, Math.Min(t/.04, (seconds-t)/.04));
                    double sample = (flame*fire/100.0 + air*ambience/100.0 + (pad+pluck)*music/100.0)*fade*.7;
                    writer.Write((short)(Math.Max(-1,Math.Min(1,sample))*32760));
                }
                return ms.ToArray();
            }
        }
        void Stop() { if (player != null) { player.Dispose(); player = null; } }
        public void Mute() { lock (gate) { ++generation; Stop(); } }
        public void Dispose() { lock (gate) { disposed=true; ++generation; Stop(); } }
    }
}
