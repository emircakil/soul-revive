using System;
using System.Threading;
using System.Threading.Tasks;
using Raylib_cs;

namespace SoulsRevive.Cross
{
    /// <summary>
    /// Background soundscape on a raylib audio stream. The audio thread pulls samples from the current
    /// seamless loop, so playback never gaps when the UI loop is slow, minimized or busy. Loops are
    /// rendered on a worker thread; swapping keeps the position, so volume changes do not restart music.
    /// </summary>
    public sealed unsafe class CrossAudio : IDisposable
    {
        sealed class Loop { public short[] Pcm; public int Position; }
        static Loop current;            // read by the audio thread

        AudioStream stream;
        bool streamReady, streamPaused = true;
        Sound? reminder;
        int generation;
        Task<short[]> pending;
        int pendingGeneration;
        int lastFire = -1, lastAmbience = -1, lastMusic = -1;
        bool wantPlaying, suspended;
        public string LastError { get; private set; } = "";

        public CrossAudio()
        {
            try
            {
                if (!Raylib.IsAudioDeviceReady()) Raylib.InitAudioDevice();
                if (!Raylib.IsAudioDeviceReady()) return;
                // Half a second per buffer comfortably covers slow frames.
                Raylib.SetAudioStreamBufferSizeDefault(8192);
                stream = Raylib.LoadAudioStream(SoundSynth.Rate, 16, 1);
                Raylib.SetAudioStreamCallback(stream, &Fill);
                streamReady = Raylib.IsAudioStreamValid(stream);
            }
            catch (Exception ex) { LastError = ex.Message; }
        }

        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvCdecl) })]
        static void Fill(void* buffer, uint frames)
        {
            short* output = (short*)buffer;
            Loop loop = Volatile.Read(ref current);
            if (loop == null) { for (uint i = 0; i < frames; i++) output[i] = 0; return; }
            short[] pcm = loop.Pcm; int position = loop.Position;
            for (uint i = 0; i < frames; i++)
            {
                output[i] = pcm[position];
                if (++position >= pcm.Length) position = 0;
            }
            loop.Position = position;
        }

        /// <summary>Starts, changes or stops the soundscape to match the preferences. Never blocks on rendering.</summary>
        public void Apply(Preferences p)
        {
            if (!streamReady) return;
            wantPlaying = p.AudioEnabled && p.FireVolume + p.AmbienceVolume + p.MusicVolume > 0;
            if (!wantPlaying) { Volatile.Write(ref current, null); lastFire = -1; UpdatePause(); return; }
            if (p.FireVolume == lastFire && p.AmbienceVolume == lastAmbience && p.MusicVolume == lastMusic && (current != null || pending != null)) { UpdatePause(); return; }
            lastFire = p.FireVolume; lastAmbience = p.AmbienceVolume; lastMusic = p.MusicVolume;
            int fire = p.FireVolume, ambience = p.AmbienceVolume, music = p.MusicVolume;
            pendingGeneration = ++generation;
            pending = Task.Run(() => SoundSynth.RenderSamples(fire, ambience, music, 32, true));
            UpdatePause();
        }

        /// <summary>Call once per frame: installs finished renders on the audio thread's loop.</summary>
        public void Update()
        {
            if (pending == null || !pending.IsCompleted) return;
            var task = pending; pending = null;
            if (pendingGeneration != generation || !wantPlaying) return;
            if (task.IsFaulted) { LastError = task.Exception?.GetBaseException().Message ?? "audio"; return; }
            var previous = Volatile.Read(ref current);
            var next = new Loop { Pcm = task.Result, Position = previous == null ? 0 : previous.Position % task.Result.Length };
            Volatile.Write(ref current, next);
            UpdatePause();
        }

        /// <summary>Silences the soundscape while the screen is locked or the system sleeps.</summary>
        public void Suspend(bool value) { suspended = value; UpdatePause(); }

        void UpdatePause()
        {
            bool pause = suspended || !wantPlaying;
            if (pause == streamPaused) return;
            streamPaused = pause;
            if (pause) Raylib.PauseAudioStream(stream);
            else if (Raylib.IsAudioStreamPlaying(stream)) Raylib.ResumeAudioStream(stream);
            else Raylib.PlayAudioStream(stream);
        }

        public void Mute() { wantPlaying = false; Volatile.Write(ref current, null); lastFire = -1; if (streamReady) UpdatePause(); }

        public void Remind()
        {
            if (!Raylib.IsAudioDeviceReady()) return;
            if (!reminder.HasValue)
            {
                Wave wave = Raylib.LoadWaveFromMemory(".wav", SoundSynth.Reminder());
                reminder = Raylib.LoadSoundFromWave(wave);
                Raylib.UnloadWave(wave);
            }
            Raylib.PlaySound(reminder.Value);
        }

        public void Dispose()
        {
            Volatile.Write(ref current, null);
            try
            {
                if (streamReady) { Raylib.StopAudioStream(stream); Raylib.UnloadAudioStream(stream); streamReady = false; }
                if (reminder.HasValue) { Raylib.UnloadSound(reminder.Value); reminder = null; }
                if (Raylib.IsAudioDeviceReady()) Raylib.CloseAudioDevice();
            }
            catch (Exception) { }
        }
    }
}
