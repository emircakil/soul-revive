using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace SoulsRevive.Cross
{
    public static class DayCycle
    {
        public static double Hour(Preferences p)
        {
            if (p.Chill) return 22;
            if (p.InBreak) return 22;
            return 5.5 + 15 * (1 - Math.Max(0, Math.Min(1, p.Remaining / (p.WorkMinutes * 60.0))));
        }

        public static double Sunlight(double hour)
        {
            return Math.Max(0, Math.Sin((hour - 6) * Math.PI / 12.0));
        }
    }

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

    public enum WorkAction { Idle, Sharpen, Map, TendFire }

    public sealed class WorkRoutine
    {
        public WorkAction Action = WorkAction.Idle;
        public int Frame;
        int idleFramesRemaining = 80;
        readonly Random random = new Random();

        public void Advance(bool active)
        {
            if (!active) return;
            if (Action == WorkAction.Idle)
            {
                if (--idleFramesRemaining <= 0)
                {
                    int roll = random.Next(3);
                    Action = roll == 0 ? WorkAction.Sharpen : roll == 1 ? WorkAction.Map : WorkAction.TendFire;
                    Frame = 0;
                }
            }
            else
            {
                if (++Frame >= 112)
                {
                    Action = WorkAction.Idle;
                    Frame = 0;
                    idleFramesRemaining = 80 + random.Next(80);
                }
            }
        }

        public void Reset()
        {
            Action = WorkAction.Idle;
            Frame = 0;
            idleFramesRemaining = 80;
        }
    }

    [DataContract]
    public sealed class Preferences
    {
        [DataMember] public int WorkMinutes = 30;
        [DataMember] public int BreakMinutes = 3;
        [DataMember] public double Remaining = 1800;
        [DataMember] public bool InBreak;
        [DataMember] public int Breaks;
        [DataMember] public int MovementSeconds;
        [DataMember] public bool Paused;
        [DataMember] public bool Chill;
        [DataMember] public bool LightMode;
        [DataMember] public int ArmorStyle;
        [DataMember] public int SwordStyle;
        [DataMember] public int ShieldStyle;
        [DataMember] public int FireVolume = 35;
        [DataMember] public int AmbienceVolume = 25;
        [DataMember] public int MusicVolume = 20;
        [DataMember] public bool AudioEnabled;
        [DataMember] public bool Sound = true;
        [DataMember] public bool AlwaysOnTop;
        [DataMember] public string Language = "tr";
        [DataMember] public int X = -1;
        [DataMember] public int Y = -1;
        [DataMember] public double TotalWorkSeconds;
        [DataMember] public double TodayWorkSeconds;
        [DataMember] public int CompletedSessions;
        [DataMember] public string Day = DateTime.Today.ToString("yyyy-MM-dd");

        public void Validate()
        {
            ArmorStyle = Math.Max(0, Math.Min(2, ArmorStyle));
            SwordStyle = Math.Max(0, Math.Min(2, SwordStyle));
            ShieldStyle = Math.Max(0, Math.Min(2, ShieldStyle));
            WorkMinutes = Math.Max(1, Math.Min(180, WorkMinutes));
            BreakMinutes = Math.Max(1, Math.Min(30, BreakMinutes));
            if (Chill) { InBreak = false; Paused = false; }
            if (Remaining <= 0 || Remaining > WorkMinutes * 60)
                Remaining = InBreak ? BreakMinutes * 60 : WorkMinutes * 60;
            FireVolume = Math.Max(0, Math.Min(100, FireVolume));
            AmbienceVolume = Math.Max(0, Math.Min(100, AmbienceVolume));
            MusicVolume = Math.Max(0, Math.Min(100, MusicVolume));
            CheckDay();
        }

        public void CheckDay()
        {
            string today = DateTime.Today.ToString("yyyy-MM-dd");
            if (Day != today)
            {
                Day = today;
                TodayWorkSeconds = 0;
                Breaks = 0;
                MovementSeconds = 0;
            }
        }
    }

    [DataContract]
    public sealed class WorkStatistics
    {
        [DataMember] public string Day;
        [DataMember] public double TotalWorkSeconds;
        [DataMember] public double TodayWorkSeconds;
        [DataMember] public int CompletedSessions;
    }

    public sealed class TimerEngine
    {
        public readonly Preferences P;
        public event Action<string> Changed;

        public TimerEngine(Preferences p)
        {
            P = p;
            p.Validate();
        }

        public bool Due => !P.Chill && !P.InBreak && P.Remaining <= 0;
        public double Health => P.InBreak ? 0 : Math.Max(0, Math.Min(1, P.Remaining / (P.WorkMinutes * 60.0)));

        public void BeginBreak()
        {
            P.InBreak = true;
            P.Paused = false;
            P.Chill = false;
            P.Remaining = P.BreakMinutes * 60;
            Signal("break");
        }

        public void CancelBreak()
        {
            P.InBreak = false;
            P.Remaining = P.WorkMinutes * 60;
            Signal("cancel");
        }

        public void TogglePause()
        {
            if (P.Chill) return;
            P.Paused = !P.Paused;
            Signal("pause");
        }

        public void SetChill(bool chill)
        {
            if (P.Chill == chill) return;
            P.Chill = chill;
            if (chill)
            {
                P.InBreak = false;
                P.Paused = false;
            }
            else
            {
                P.Remaining = P.WorkMinutes * 60;
            }
            Signal("mode");
        }

        public void ApplyDurations(int work, int rest)
        {
            P.WorkMinutes = Math.Max(1, Math.Min(180, work));
            P.BreakMinutes = Math.Max(1, Math.Min(30, rest));
            P.InBreak = false;
            P.Remaining = P.WorkMinutes * 60;
            Signal("durations");
        }

        public void Advance(double delta)
        {
            if (P.Chill || P.Paused || delta <= 0 || double.IsNaN(delta) || double.IsInfinity(delta)) return;
            delta = Math.Min(delta, 5.0);
            P.CheckDay();

            if (P.InBreak)
            {
                P.Remaining -= delta;
                P.MovementSeconds += (int)delta;
                if (P.Remaining <= 0)
                {
                    P.Breaks++;
                    P.InBreak = false;
                    P.Remaining = P.WorkMinutes * 60;
                    Signal("revived");
                }
            }
            else
            {
                if (P.Remaining > 0)
                {
                    double actual = Math.Min(delta, P.Remaining);
                    P.Remaining -= actual;
                    P.TodayWorkSeconds += actual;
                    P.TotalWorkSeconds += actual;
                    if (P.Remaining <= 0)
                    {
                        P.CompletedSessions++;
                        Signal("due");
                    }
                }
            }
        }

        void Signal(string reason) => Changed?.Invoke(reason);
    }

    public static class L
    {
        public static string Language = "tr";
        public static string Text(string tr, string en) => Language == "en" ? en : tr;
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

    public static class CrossStorage
    {
        public static readonly string DefaultDirectoryPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SoulsRevive");
        public static string DirectoryPath = DefaultDirectoryPath;

        public static string SettingsPath => Path.Combine(DirectoryPath, "settings.json");
        public static string StatisticsPath => Path.Combine(DirectoryPath, "statistics.json");

        public static Preferences Load()
        {
            Preferences preferences = null;
            foreach (string path in new[] { SettingsPath, SettingsPath + ".bak" })
            {
                try
                {
                    if (File.Exists(path))
                    {
                        using (var f = File.OpenRead(path))
                        {
                            var serializer = new DataContractJsonSerializer(typeof(Preferences));
                            preferences = (Preferences)serializer.ReadObject(f);
                            if (preferences != null) break;
                        }
                    }
                }
                catch { }
            }
            if (preferences == null) preferences = new Preferences();
            RestoreStatistics(preferences);
            return preferences;
        }

        static void RestoreStatistics(Preferences preferences)
        {
            preferences.CheckDay();
            foreach (string path in new[] { StatisticsPath, StatisticsPath + ".bak" })
            {
                try
                {
                    if (!File.Exists(path)) continue;
                    using (var file = File.OpenRead(path))
                    {
                        var serializer = new DataContractJsonSerializer(typeof(WorkStatistics));
                        var saved = (WorkStatistics)serializer.ReadObject(file);
                        if (saved == null) continue;
                        preferences.TotalWorkSeconds = Math.Max(preferences.TotalWorkSeconds, saved.TotalWorkSeconds);
                        preferences.CompletedSessions = Math.Max(preferences.CompletedSessions, saved.CompletedSessions);
                        if (saved.Day == preferences.Day)
                            preferences.TodayWorkSeconds = Math.Max(preferences.TodayWorkSeconds, saved.TodayWorkSeconds);
                    }
                }
                catch { }
            }
        }

        static void WriteAtomic<T>(string path, T value)
        {
            string temporary = path + ".tmp";
            using (var file = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                new DataContractJsonSerializer(typeof(T)).WriteObject(file, value);
                file.Flush(true);
            }
            if (File.Exists(path)) File.Replace(temporary, path, path + ".bak");
            else File.Move(temporary, path);
        }

        public static bool Save(Preferences p)
        {
            try
            {
                Directory.CreateDirectory(DirectoryPath);
                RestoreStatistics(p);
                WriteAtomic(StatisticsPath, new WorkStatistics
                {
                    Day = p.Day,
                    TotalWorkSeconds = p.TotalWorkSeconds,
                    TodayWorkSeconds = p.TodayWorkSeconds,
                    CompletedSessions = p.CompletedSessions
                });
                WriteAtomic(SettingsPath, p);
                return true;
            }
            catch { return false; }
        }
    }
}
