using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace SoulsRevive
{
    static class L
    {
        public static string Language = "tr";
        public static string Text(string tr, string en) { return Language == "en" ? en : tr; }
    }

    /// <summary>Why the timer state changed. Shared by the Windows and portable shells.</summary>
    public enum TimerEvent { Due, Revived, Break, Cancel, Pause, Mode, Settings, Snooze, Idle }

    [DataContract]
    public sealed class DayRecord
    {
        [DataMember] public string Day = "";
        [DataMember] public double WorkSeconds;
        [DataMember] public int Breaks;
        [DataMember] public double MovementSeconds;
        [DataMember] public int Sessions;
    }

    [DataContract]
    public sealed class Preferences
    {
        public const int HistoryDays = 60;

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
        [DataMember] public int SchemaVersion = 3;
        // Schema 3: long breaks, snooze, daily history/goal, idle pause and focus behaviour.
        [DataMember] public int LongBreakEvery;
        [DataMember] public int LongBreakMinutes = 15;
        [DataMember] public int BreaksSinceLongBreak;
        [DataMember] public bool InLongBreak;
        [DataMember] public bool Snoozed;
        [DataMember] public int TodaySessions;
        [DataMember] public int DailyGoalMinutes;
        [DataMember] public bool IdlePause;
        [DataMember] public int IdleMinutes = 5;
        [DataMember] public bool FocusOnDue;
        [DataMember] public List<DayRecord> History = new List<DayRecord>();

        [OnDeserializing] void Defaults(StreamingContext context)
        {
            WorkMinutes = 30; BreakMinutes = 3; Remaining = 1800; AlwaysOnTop = true; Sound = true;
            X = Y = int.MinValue; Language = "tr"; FireVolume = 35; AmbienceVolume = 25; MusicVolume = 20;
            LongBreakMinutes = 15; IdleMinutes = 5; History = new List<DayRecord>();
        }

        public static string DayKey(DateTime time) { return time.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture); }
        static int Clamp(int value, int min, int max) { return Math.Max(min, Math.Min(max, value)); }
        internal static double ValidSeconds(double n) { return double.IsNaN(n) || double.IsInfinity(n) ? 0 : Math.Max(0, n); }

        public double BreakSeconds { get { return (InLongBreak ? LongBreakMinutes : BreakMinutes) * 60.0; } }

        public void Validate()
        {
            ArmorStyle = Clamp(ArmorStyle, 0, 2); SwordStyle = Clamp(SwordStyle, 0, 2); ShieldStyle = Clamp(ShieldStyle, 0, 2);
            WorkMinutes = Clamp(WorkMinutes, 1, 180);
            BreakMinutes = Clamp(BreakMinutes, 1, 30);
            LongBreakEvery = Clamp(LongBreakEvery, 0, 12);
            LongBreakMinutes = Clamp(LongBreakMinutes, 1, 60);
            BreaksSinceLongBreak = Clamp(BreaksSinceLongBreak, 0, 12);
            DailyGoalMinutes = Clamp(DailyGoalMinutes, 0, 720);
            IdleMinutes = Clamp(IdleMinutes, 1, 60);
            if (Chill) { InBreak = false; Paused = false; }
            if (!InBreak) InLongBreak = false;
            if (InBreak) Snoozed = false;
            WorkLeftBeforeBreak = Math.Min(WorkMinutes * 60.0, ValidSeconds(WorkLeftBeforeBreak));
            Remaining = double.IsNaN(Remaining) || double.IsInfinity(Remaining) ? WorkMinutes * 60 :
                Math.Max(0, Math.Min(Remaining, InBreak ? BreakSeconds : WorkMinutes * 60));
            Breaks = Math.Max(0, Breaks);
            MovementSeconds = ValidSeconds(MovementSeconds);
            TotalWorkSeconds = ValidSeconds(TotalWorkSeconds); TodayWorkSeconds = ValidSeconds(TodayWorkSeconds);
            CompletedSessions = Math.Max(0, CompletedSessions); TodaySessions = Math.Max(0, TodaySessions);
            FireVolume = Clamp(FireVolume, 0, 100); AmbienceVolume = Clamp(AmbienceVolume, 0, 100); MusicVolume = Clamp(MusicVolume, 0, 100);
            Language = Language == "en" ? "en" : "tr"; SchemaVersion = 3;
            History = NormalizeHistory(History);
            CheckDay();
        }

        internal static List<DayRecord> NormalizeHistory(IEnumerable<DayRecord> records)
        {
            var byDay = new Dictionary<string, DayRecord>();
            DateTime parsed;
            foreach (var r in records ?? Enumerable.Empty<DayRecord>())
            {
                if (r == null || !DateTime.TryParseExact(r.Day, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed)) continue;
                DayRecord existing;
                if (!byDay.TryGetValue(r.Day, out existing)) byDay[r.Day] = existing = new DayRecord { Day = r.Day };
                // Merging two copies of the same day never loses recorded time.
                existing.WorkSeconds = Math.Max(existing.WorkSeconds, ValidSeconds(r.WorkSeconds));
                existing.MovementSeconds = Math.Max(existing.MovementSeconds, ValidSeconds(r.MovementSeconds));
                existing.Breaks = Math.Max(existing.Breaks, Math.Max(0, r.Breaks));
                existing.Sessions = Math.Max(existing.Sessions, Math.Max(0, r.Sessions));
            }
            return byDay.Values.OrderBy(r => r.Day, StringComparer.Ordinal).Skip(Math.Max(0, byDay.Count - HistoryDays)).ToList();
        }

        public void CheckDay() { CheckDay(DateTime.Now); }
        public void CheckDay(DateTime now)
        {
            string today = DayKey(now);
            if (Day == today) return;
            if (!string.IsNullOrEmpty(Day) && (TodayWorkSeconds > 0 || Breaks > 0 || MovementSeconds > 0 || TodaySessions > 0))
            {
                var list = new List<DayRecord>(History ?? new List<DayRecord>());
                list.Add(new DayRecord { Day = Day, WorkSeconds = TodayWorkSeconds, Breaks = Breaks, MovementSeconds = MovementSeconds, Sessions = TodaySessions });
                History = NormalizeHistory(list);
            }
            Day = today; Breaks = 0; MovementSeconds = 0; TodayWorkSeconds = 0; TodaySessions = 0;
        }

        internal void AddToHistory(string day, double workSeconds)
        {
            if (workSeconds <= 0) return;
            var list = new List<DayRecord>(History ?? new List<DayRecord>());
            var record = list.FirstOrDefault(r => r.Day == day);
            if (record == null) list.Add(record = new DayRecord { Day = day });
            record.WorkSeconds += workSeconds;
            History = NormalizeHistory(list);
        }

        /// <summary>Work seconds for the given day, including today's running total.</summary>
        public double WorkOn(DateTime day)
        {
            string key = DayKey(day);
            if (key == Day) return TodayWorkSeconds;
            var record = (History ?? new List<DayRecord>()).FirstOrDefault(r => r.Day == key);
            return record == null ? 0 : record.WorkSeconds;
        }

        /// <summary>Consecutive days meeting the daily goal (or with at least one minute of work when no goal is set).</summary>
        public int Streak(DateTime now)
        {
            double target = DailyGoalMinutes > 0 ? DailyGoalMinutes * 60.0 : 60;
            int streak = 0;
            DateTime day = now.Date;
            if (WorkOn(day) < target) day = day.AddDays(-1); // Today still counts once it is reached.
            while (WorkOn(day) >= target && streak < 3660) { streak++; day = day.AddDays(-1); }
            return streak;
        }
    }

    public sealed class TimerEngine
    {
        public const int SnoozeMinutes = 5;
        public readonly Preferences P;
        public event Action<TimerEvent> Changed;
        public TimerEngine(Preferences p) { P = p; p.Validate(); }
        public bool Due { get { return !P.Chill && !P.InBreak && P.Remaining <= 0; } }
        public bool Working { get { return !P.Chill && !P.InBreak && !P.Paused && !Due; } }
        public bool NextBreakIsLong { get { return P.LongBreakEvery > 0 && P.BreaksSinceLongBreak + 1 >= P.LongBreakEvery; } }
        public double Health
        {
            get { return P.InBreak ? 1 - (1 - P.WorkLeftBeforeBreak / (P.WorkMinutes * 60.0)) * P.Remaining / P.BreakSeconds : P.Remaining / (P.WorkMinutes * 60.0); }
        }
        public void Advance(double seconds) { Advance(seconds, DateTime.Now); }
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
                // Allocate a tick crossing midnight to each day it belongs to.
                double todayPart = Math.Min(used, now.TimeOfDay.TotalSeconds);
                P.TodayWorkSeconds += todayPart;
                P.AddToHistory(Preferences.DayKey(now.AddDays(-1)), used - todayPart);
            }
            if (before > 0 && P.Remaining == 0)
            {
                if (P.InBreak)
                {
                    P.Breaks++;
                    P.BreaksSinceLongBreak = P.InLongBreak ? 0 : P.BreaksSinceLongBreak + 1;
                    P.InBreak = false; P.InLongBreak = false; P.Snoozed = false;
                    P.Remaining = P.WorkMinutes * 60;
                    Signal(TimerEvent.Revived);
                }
                else
                {
                    // A snoozed round was already counted when it first expired.
                    if (!P.Snoozed) { P.CompletedSessions++; P.TodaySessions++; }
                    Signal(TimerEvent.Due);
                }
            }
        }
        public void BeginBreak()
        {
            if (P.InBreak || P.Chill) return;
            P.WorkLeftBeforeBreak = P.Remaining;
            P.InLongBreak = NextBreakIsLong;
            P.InBreak = true; P.Paused = false; P.Snoozed = false; P.Remaining = P.BreakSeconds; Signal(TimerEvent.Break);
        }
        public void CancelBreak()
        {
            if (!P.InBreak) return;
            // Returning to work starts a full round without counting a completed break.
            P.InBreak = false; P.InLongBreak = false; P.Remaining = P.WorkMinutes * 60; P.Paused = false; Signal(TimerEvent.Cancel);
        }
        public void TogglePause() { if (P.Chill) return; P.Paused = !P.Paused; Signal(TimerEvent.Pause); }
        /// <summary>Gives a few more minutes of work after the round has expired.</summary>
        public void Snooze()
        {
            if (!Due) return;
            P.Remaining = Math.Min(SnoozeMinutes, P.WorkMinutes) * 60; P.Snoozed = true; P.Paused = false; Signal(TimerEvent.Snooze);
        }
        /// <summary>
        /// Pauses work after the user was away and removes the idle time that was already counted.
        /// Returns true when the timer was paused.
        /// </summary>
        public bool PauseForIdle(double idleSeconds, DateTime now)
        {
            if (!Working || double.IsNaN(idleSeconds) || idleSeconds <= 0) return false;
            P.CheckDay(now);
            double rollback = Math.Min(Math.Min(idleSeconds, P.WorkMinutes * 60 - P.Remaining), P.TotalWorkSeconds);
            rollback = Math.Max(0, rollback);
            P.Remaining += rollback;
            P.TotalWorkSeconds -= rollback;
            P.TodayWorkSeconds = Math.Max(0, P.TodayWorkSeconds - Math.Min(rollback, now.TimeOfDay.TotalSeconds));
            P.Paused = true;
            Signal(TimerEvent.Idle);
            return true;
        }
        public void SetChill(bool chill)
        {
            if (P.Chill == chill) return;
            P.Chill = chill; P.InBreak = false; P.InLongBreak = false; P.Paused = false; P.Snoozed = false;
            P.Remaining = P.WorkMinutes * 60; Signal(TimerEvent.Mode);
        }
        public void ApplyDurations(int work, int rest) { ApplyDurations(work, rest, P.LongBreakEvery, P.LongBreakMinutes); }
        public void ApplyDurations(int work, int rest, int longEvery, int longMinutes)
        {
            P.LongBreakEvery = Math.Max(0, Math.Min(12, longEvery));
            P.LongBreakMinutes = Math.Max(1, Math.Min(60, longMinutes));
            if (P.WorkMinutes != work || P.BreakMinutes != rest)
            {
                P.WorkMinutes = work; P.BreakMinutes = rest;
                P.InBreak = false; P.InLongBreak = false; P.Snoozed = false; P.Remaining = work * 60; P.Paused = false;
            }
            Signal(TimerEvent.Settings);
        }
        /// <summary>Clears lifetime and daily statistics. Callers persist with Storage.Save(p, true).</summary>
        public void ResetStatistics()
        {
            P.TotalWorkSeconds = 0; P.TodayWorkSeconds = 0; P.CompletedSessions = 0; P.TodaySessions = 0;
            P.Breaks = 0; P.MovementSeconds = 0; P.History = new List<DayRecord>();
            Signal(TimerEvent.Settings);
        }
        void Signal(TimerEvent reason) { var handler = Changed; if (handler != null) handler(reason); }
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

        // The preferences object this process last wrote, and the statistics file time it produced.
        // While both still match, the in-memory values are authoritative and may go down (idle rollback, reset).
        static Preferences lastSaved;
        static DateTime lastStatisticsWrite;

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
                        {
                            preferences.TodayWorkSeconds = Math.Max(Preferences.ValidSeconds(preferences.TodayWorkSeconds), Preferences.ValidSeconds(saved.TodayWorkSeconds));
                            preferences.TodaySessions = Math.Max(preferences.TodaySessions, saved.TodaySessions);
                        }
                        else if (!string.IsNullOrEmpty(saved.Day) && string.CompareOrdinal(saved.Day, preferences.Day) < 0 && saved.TodayWorkSeconds > 0)
                        {
                            // Statistics from an earlier day that settings never archived still belong in history.
                            var older = new System.Collections.Generic.List<DayRecord>(preferences.History ?? new System.Collections.Generic.List<DayRecord>());
                            older.Add(new DayRecord { Day = saved.Day, WorkSeconds = saved.TodayWorkSeconds, Sessions = saved.TodaySessions });
                            preferences.History = Preferences.NormalizeHistory(older);
                        }
                        if (saved.History != null)
                            preferences.History = Preferences.NormalizeHistory((preferences.History ?? new System.Collections.Generic.List<DayRecord>()).Concat(saved.History));
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
        public static bool Save(Preferences p) { return Save(p, false); }
        /// <summary>
        /// Saves settings and statistics. With <paramref name="authoritative"/>, lower statistics
        /// (reset, idle rollback) replace the saved ones and their backups instead of being merged.
        /// </summary>
        public static bool Save(Preferences p, bool authoritative)
        {
            try
            {
                Directory.CreateDirectory(DirectoryPath);
                bool trusted = authoritative || (ReferenceEquals(p, lastSaved) && File.Exists(StatisticsPath)
                    && File.GetLastWriteTimeUtc(StatisticsPath) == lastStatisticsWrite);
                if (trusted) p.CheckDay(); else RestoreStatistics(p);
                var statistics = new WorkStatistics {
                    Day = p.Day, TotalWorkSeconds = p.TotalWorkSeconds, TodayWorkSeconds = p.TodayWorkSeconds,
                    CompletedSessions = p.CompletedSessions, TodaySessions = p.TodaySessions, History = p.History
                };
                // Writing twice also replaces the .bak copies, so older totals cannot return on the next start.
                for (int i = 0; i < (authoritative ? 2 : 1); i++)
                {
                    WriteAtomic(StatisticsPath, statistics);
                    WriteAtomic(SettingsPath, p);
                }
                lastSaved = p; lastStatisticsWrite = File.GetLastWriteTimeUtc(StatisticsPath);
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
        [DataMember] public int TodaySessions;
        [DataMember] public System.Collections.Generic.List<DayRecord> History;
    }
}
