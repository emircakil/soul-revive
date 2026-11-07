using System;
using System.IO;
using Raylib_cs;

namespace SoulsRevive.Cross
{
    public sealed class CrossAudio : IDisposable
    {
        readonly object gate = new object();
        Sound? currentSound;
        Wave? currentWave;
        bool isPlaying;
        int lastFire = -1, lastAmbience = -1, lastMusic = -1;
        bool lastEnabled;

        public CrossAudio()
        {
            try
            {
                if (!Raylib.IsAudioDeviceReady())
                {
                    Raylib.InitAudioDevice();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Audio device initialization error: " + ex.Message);
            }
        }

        public void Apply(Preferences p)
        {
            lock (gate)
            {
                if (!p.AudioEnabled || (p.FireVolume + p.AmbienceVolume + p.MusicVolume == 0))
                {
                    Stop();
                    lastEnabled = false;
                    return;
                }

                if (lastEnabled && p.FireVolume == lastFire && p.AmbienceVolume == lastAmbience && p.MusicVolume == lastMusic && isPlaying)
                {
                    return;
                }

                lastEnabled = true;
                lastFire = p.FireVolume;
                lastAmbience = p.AmbienceVolume;
                lastMusic = p.MusicVolume;

                try
                {
                    byte[] waveData = Soundscape.Render(p.FireVolume, p.AmbienceVolume, p.MusicVolume, 32);
                    Stop();

                    Wave wave = Raylib.LoadWaveFromMemory(".wav", waveData);
                    Sound sound = Raylib.LoadSoundFromWave(wave);
                    currentWave = wave;
                    currentSound = sound;
                    Raylib.PlaySound(sound);
                    isPlaying = true;
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Soundscape render/play error: " + ex.Message);
                    Stop();
                }
            }
        }

        public void Update()
        {
            lock (gate)
            {
                if (isPlaying && currentSound.HasValue)
                {
                    if (!Raylib.IsSoundPlaying(currentSound.Value))
                    {
                        Raylib.PlaySound(currentSound.Value);
                    }
                }
            }
        }

        public void Stop()
        {
            lock (gate)
            {
                isPlaying = false;
                if (currentSound.HasValue)
                {
                    try
                    {
                        Raylib.StopSound(currentSound.Value);
                        Raylib.UnloadSound(currentSound.Value);
                    }
                    catch { }
                    currentSound = null;
                }
                if (currentWave.HasValue)
                {
                    try { Raylib.UnloadWave(currentWave.Value); } catch { }
                    currentWave = null;
                }
            }
        }

        public void Mute() => Stop();

        public void Dispose()
        {
            Stop();
            try
            {
                if (Raylib.IsAudioDeviceReady())
                {
                    Raylib.CloseAudioDevice();
                }
            }
            catch { }
        }
    }
}
