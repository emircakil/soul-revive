using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace SoulsRevive
{
    static class L
    {
        public static string Language = "tr";
        public static string Text(string tr, string en) { return Language == "en" ? en : tr; }
    }
    [DataContract]
    public sealed class Preferences
    {
        [DataMember] public string Language = "tr";
        [DataMember] public double WorkLeftBeforeBreak;
        [DataMember] public int WorkMinutes = 30;
        [DataMember] public int BreakMinutes = 3;
        [DataMember] public bool AlwaysOnTop = true;
        [DataMember] public bool Sound = true;
        [DataMember] public int X = int.MinValue;
        [DataMember] public int Y = int.MinValue;
        [DataMember] public string Day = "";
        [DataMember] public int Breaks;
        [DataMember] public double MovementSeconds;
        [DataMember] public double Remaining = 1800;
        [DataMember] public bool InBreak;
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
        [DataMember] public double TotalWorkSeconds;
        [DataMember] public double TodayWorkSeconds;
        [DataMember] public int CompletedSessions;
        [DataMember] public int SchemaVersion = 2;

        [OnDeserializing] void Defaults(StreamingContext context)
        { WorkMinutes = 30; BreakMinutes = 3; Remaining = 1800; AlwaysOnTop = true; Sound = true; X = Y = int.MinValue; Language = "tr"; FireVolume = 35; AmbienceVolume = 25; MusicVolume = 20; }

        public void Validate()
        {
            ArmorStyle=Math.Max(0,Math.Min(2,ArmorStyle));SwordStyle=Math.Max(0,Math.Min(2,SwordStyle));ShieldStyle=Math.Max(0,Math.Min(2,ShieldStyle));
            WorkMinutes = Math.Max(1, Math.Min(180, WorkMinutes));
            BreakMinutes = Math.Max(1, Math.Min(30, BreakMinutes));
            if (Chill) { InBreak = false; Paused = false; }
            WorkLeftBeforeBreak = Math.Min(WorkMinutes * 60.0, ValidSeconds(WorkLeftBeforeBreak));
            Remaining = double.IsNaN(Remaining) || double.IsInfinity(Remaining) ? WorkMinutes * 60 :
                Math.Max(0, Math.Min(Remaining, (InBreak ? BreakMinutes : WorkMinutes) * 60));
            Breaks = Math.Max(0, Breaks);
            if (double.IsNaN(MovementSeconds) || double.IsInfinity(MovementSeconds)) MovementSeconds = 0;
            MovementSeconds = Math.Max(0, MovementSeconds);
            TotalWorkSeconds = ValidSeconds(TotalWorkSeconds); TodayWorkSeconds = ValidSeconds(TodayWorkSeconds);
            CompletedSessions = Math.Max(0, CompletedSessions);
            FireVolume = Math.Max(0, Math.Min(100, FireVolume)); AmbienceVolume = Math.Max(0, Math.Min(100, AmbienceVolume)); MusicVolume = Math.Max(0, Math.Min(100, MusicVolume));
            Language = Language == "en" ? "en" : "tr"; SchemaVersion = 2;
            CheckDay();
        }
        internal static double ValidSeconds(double n) { return double.IsNaN(n) || double.IsInfinity(n) ? 0 : Math.Max(0, n); }
        public void CheckDay()
        { CheckDay(DateTime.Now); }
        public void CheckDay(DateTime now)
        {
            string today = now.ToString("yyyy-MM-dd");
            if (Day != today) { Day = today; Breaks = 0; MovementSeconds = 0; TodayWorkSeconds = 0; }
        }
    }

    public sealed class TimerEngine
    {
        public readonly Preferences P;
        public event Action<string> Changed;
        public TimerEngine(Preferences p) { P = p; p.Validate(); }
        public bool Due { get { return !P.Chill && !P.InBreak && P.Remaining <= 0; } }
        public double Health
        {
            get { return P.InBreak ? 1 - (1 - P.WorkLeftBeforeBreak / (P.WorkMinutes * 60.0)) * P.Remaining / (P.BreakMinutes * 60.0) : P.Remaining / (P.WorkMinutes * 60.0); }
        }
        public void Advance(double seconds)
        { Advance(seconds, DateTime.Now); }
        public void Advance(double seconds, DateTime now)
        {
            P.CheckDay(now);
            if (P.Chill || P.Paused || seconds <= 0 || seconds > 5 || double.IsNaN(seconds)) return;
            double before = P.Remaining;
            double used = Math.Min(seconds, before);
            P.Remaining = Math.Max(0, before - seconds);
            if (P.InBreak) P.MovementSeconds += used;
            else
            {
                P.TotalWorkSeconds += used;
                // Allocate a tick crossing midnight only to its current-day portion.
                P.TodayWorkSeconds += Math.Min(used, now.TimeOfDay.TotalSeconds);
            }
            if (before > 0 && P.Remaining == 0)
            {
                if (P.InBreak)
                {
                    P.Breaks++;
                    P.InBreak = false;
                    P.Remaining = P.WorkMinutes * 60;
                    Signal("revived");
                }
                else { P.CompletedSessions++; Signal("due"); }
            }
        }
        public void BeginBreak()
        {
            if (P.InBreak || P.Chill) return;
            P.WorkLeftBeforeBreak = P.Remaining;
            P.InBreak = true; P.Paused = false; P.Remaining = P.BreakMinutes * 60; Signal("break");
        }
        public void CancelBreak()
        {
            if (!P.InBreak) return;
            // Returning to work starts a full round without counting a completed break.
            P.InBreak = false; P.Remaining = P.WorkMinutes * 60; P.Paused = false; Signal("cancel");
        }
        public void TogglePause() { if (P.Chill) return; P.Paused = !P.Paused; Signal("pause"); }
        public void SetChill(bool chill)
        {
            if (P.Chill == chill) return;
            P.Chill = chill; P.InBreak = false; P.Paused = false;
            P.Remaining = P.WorkMinutes * 60; Signal("mode");
        }
        public void ApplyDurations(int work, int rest)
        {
            if (P.WorkMinutes != work || P.BreakMinutes != rest)
            {
                P.WorkMinutes = work; P.BreakMinutes = rest;
                P.InBreak = false; P.Remaining = work * 60; P.Paused = false;
            }
            Signal("settings");
        }
        void Signal(string reason) { if (Changed != null) Changed(reason); }
    }

    static class Storage
    {
        public static readonly string DefaultDirectoryPath = GetDefaultDirectory();
        static string GetDefaultDirectory()
        {
#if PORTABLE
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (OperatingSystem.IsMacOS()) return Path.Combine(home, "Library", "Application Support", "SoulsRevive");
            if (OperatingSystem.IsLinux())
            {
                string xdg = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
                return Path.Combine(!string.IsNullOrEmpty(xdg) && Path.IsPathRooted(xdg) ? xdg : Path.Combine(home, ".local", "share"), "SoulsRevive");
            }
#endif
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SoulsRevive");
        }
        public static string DirectoryPath = DefaultDirectoryPath;
        public static string SettingsPath { get { return Path.Combine(DirectoryPath, "settings.json"); } }
        public static string StatisticsPath { get { return Path.Combine(DirectoryPath, "statistics.json"); } }
        public static string LastError = "";
        public static Preferences Load()
        {
            Preferences preferences = null;
            foreach (string path in new[] { SettingsPath, SettingsPath + ".bak" })
            {
                try
                {
                    if (File.Exists(path))
                        using (var f = File.OpenRead(path))
                        {
                            var p = (Preferences)new DataContractJsonSerializer(typeof(Preferences)).ReadObject(f);
                            if (p != null) { preferences = p; break; }
                        }
                }
                catch (Exception ex) { LastError = ex.Message; }
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
                        var saved = (WorkStatistics)new DataContractJsonSerializer(typeof(WorkStatistics)).ReadObject(file);
                        if (saved == null) continue;
                        preferences.TotalWorkSeconds = Math.Max(Preferences.ValidSeconds(preferences.TotalWorkSeconds), Preferences.ValidSeconds(saved.TotalWorkSeconds));
                        preferences.CompletedSessions = Math.Max(preferences.CompletedSessions, saved.CompletedSessions);
                        if (saved.Day == preferences.Day)
                            preferences.TodayWorkSeconds = Math.Max(Preferences.ValidSeconds(preferences.TodayWorkSeconds), Preferences.ValidSeconds(saved.TodayWorkSeconds));
                    }
                }
                catch (Exception ex) { LastError = ex.Message; }
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
                WriteAtomic(StatisticsPath, new WorkStatistics {
                    Day = p.Day, TotalWorkSeconds = p.TotalWorkSeconds,
                    TodayWorkSeconds = p.TodayWorkSeconds, CompletedSessions = p.CompletedSessions
                });
                WriteAtomic(SettingsPath, p);
                LastError = ""; return true;
            }
            catch (Exception ex) { LastError = ex.Message; return false; }
        }
    }

    [DataContract]
    sealed class WorkStatistics
    {
        [DataMember] public string Day;
        [DataMember] public double TotalWorkSeconds;
        [DataMember] public double TodayWorkSeconds;
        [DataMember] public int CompletedSessions;
    }

}
