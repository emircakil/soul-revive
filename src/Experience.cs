using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;


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
                byte[] data;
                using (var copy = new MemoryStream()) { stream.CopyTo(copy); data = copy.ToArray(); }
                memory = Marshal.AllocCoTaskMem(data.Length); Marshal.Copy(data, 0, memory, data.Length);
                collection.AddMemoryFont(memory, data.Length);
                uint count = 0; AddFontMemResourceEx(memory, (uint)data.Length, IntPtr.Zero, ref count);
                Family = collection.Families[0];
            }
        }
        static readonly System.Collections.Generic.Dictionary<float, Font> cache = new System.Collections.Generic.Dictionary<float, Font>();
        /// <summary>Creates a font the caller owns and disposes.</summary>
        public static Font Create(float pixels) { Initialize(); return new Font(Family, pixels, FontStyle.Regular, GraphicsUnit.Pixel); }
        /// <summary>Shared font for painting; owned by the cache and never disposed by callers.</summary>
        public static Font Cached(float pixels)
        {
            Font font;
            lock (cache) if (!cache.TryGetValue(pixels, out font)) cache[pixels] = font = Create(pixels);
            return font;
        }
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

    // Plays the shared SoundSynth loop on its own winmm stream; rendering happens off the UI thread.
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
                    byte[] wave = SoundSynth.Render(fire, ambience, music, 32);
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
        /// <summary>Kept for callers and tests; the synthesis itself lives in <see cref="SoundSynth"/>.</summary>
        public static byte[] Render(int fire, int ambience, int music, int seconds) { return SoundSynth.Render(fire, ambience, music, seconds); }
        void Stop() { if (player != null) { player.Dispose(); player = null; } }
        public void Mute() { lock (gate) { ++generation; Stop(); } }
        public void Dispose() { lock (gate) { disposed=true; ++generation; Stop(); } }
    }
}
