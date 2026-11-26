using System;
using System.Globalization;

namespace SoulsRevive
{
    /// <summary>Colour palette shared by the Windows and portable shells.</summary>
    static class Palette
    {
        public static string Background(bool light) { return light ? "#EEE4CB" : "#14191E"; }
        public static string Panel(bool light) { return light ? "#DFD2B2" : "#20292D"; }
        public static string Line(bool light) { return light ? "#9C8865" : "#48504A"; }
        public static string Gold(bool light) { return light ? "#81501F" : "#E9B86B"; }
        public static string Text(bool light) { return light ? "#302D29" : "#F1E8CF"; }
        public static string Muted(bool light) { return light ? "#635B4A" : "#A2ADA4"; }
        public static string Green(bool light) { return light ? "#32634A" : "#98C99A"; }
        public static string Primary(bool light) { return light ? "#81501F" : "#55432B"; }
        public const string PrimaryText = "#FFF1D6";
    }

    /// <summary>User-visible text and number formats, shared so both shells say the same thing.</summary>
    static class Ui
    {
        static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

        public static string TimeText(double seconds)
        {
            int n = (int)Math.Ceiling(Preferences.ValidSeconds(seconds));
            return (n / 60).ToString("00", Invariant) + ":" + (n % 60).ToString("00", Invariant);
        }
        /// <summary>Hours:minutes:seconds, with hours growing beyond 24.</summary>
        public static string Duration(double seconds)
        {
            seconds = Preferences.ValidSeconds(seconds);
            return Math.Floor(seconds / 3600).ToString("00", Invariant)
                + ":" + ((int)(seconds / 60 % 60)).ToString("00", Invariant)
                + ":" + ((int)(seconds % 60)).ToString("00", Invariant);
        }
        /// <summary>"1 sa 2 dk 3 sn" / "1h 2m 3s".</summary>
        public static string LongDuration(double seconds)
        {
            long total = (long)Preferences.ValidSeconds(seconds);
            long h = total / 3600, m = total % 3600 / 60, s = total % 60;
            return L.Text(h + " sa " + m + " dk " + s + " sn", h + "h " + m + "m " + s + "s");
        }
        public static string Hours(double seconds) { return (Preferences.ValidSeconds(seconds) / 3600).ToString("0.00", CultureInfo.CurrentCulture); }
        public static string Minutes(double seconds) { return ((int)(Preferences.ValidSeconds(seconds) / 60)).ToString(Invariant); }

        public static string Title { get { return "SOUL REVIVE"; } }
        public static string Tagline { get { return L.Text("KÜÇÜK MOLALAR. YENİDEN DOĞ.", "SMALL BREAKS. RISE AGAIN."); } }
        public static string WorkingMode { get { return L.Text("ÇALIŞIYORUM", "WORKING"); } }
        public static string ChillMode { get { return L.Text("ÇALIŞMIYORUM", "CHILL MODE"); } }
        public static string StatisticsButton { get { return L.Text("İSTATİSTİKLER >", "STATISTICS >"); } }
        public static string Settings { get { return L.Text("Ayarlar", "Settings"); } }
        public static string Minimize { get { return L.Text("Görev çubuğuna küçült", "Minimize to taskbar"); } }
        public static string HideToTray { get { return L.Text("Tepsiye gizle", "Hide in tray"); } }
        public static string Today { get { return L.Text("BUGÜN", "TODAY"); } }
        public static string Total { get { return L.Text("TOPLAM", "TOTAL"); } }
        public static string Health { get { return L.Text("CAN", "HP"); } }

        public static string State(TimerEngine e)
        {
            var p = e.P;
            if (p.Chill) return L.Text("SOLAIRE İLE BİR KADEH MOLASI", "A TOAST WITH SOLAIRE");
            if (p.Paused) return L.Text("ATEŞİN SENİ BEKLİYOR", "YOUR FIRE AWAITS");
            if (p.InBreak) return p.InLongBreak ? L.Text("UZUN MOLA. ŞÖVALYE DERİN UYKUDA.", "LONG BREAK. THE KNIGHT SLEEPS DEEPLY.")
                : L.Text("ŞÖVALYE UYUYOR. SEN BİRAZ YÜRÜ.", "KNIGHT ASLEEP. TIME FOR A WALK.");
            if (e.Due) return L.Text("GECE ÇÖKTÜ. MOLA ZAMANI.", "NIGHT HAS FALLEN. TAKE A BREAK.");
            return L.Text("ODAKLAN. ATEŞİ CANLI TUT.", "FOCUS. KEEP THE FIRE ALIVE.");
        }
        public static string Clock(TimerEngine e) { return e.P.Chill ? L.Text("Şerefe!", "Cheers!") : TimeText(e.P.Remaining); }
        public static string Subtitle(TimerEngine e)
        {
            var p = e.P;
            if (p.Chill) return L.Text("Solaire ile meyhanedesin. İş bekleyebilir.", "At the tavern with Solaire. Work can wait.");
            if (p.Paused) return L.Text("Devam et ile yolculuğunu sürdür", "Select Resume to continue");
            if (p.InBreak) return L.Text("Ayağa kalk, yürü ve biraz esne.", "Stand up, walk and stretch.");
            if (e.Due) return L.Text("Mola ver ya da 5 dakika ertele", "Take a break or snooze 5 minutes");
            return e.NextBreakIsLong ? L.Text("Sıradaki mola uzun mola", "Your next break is a long one")
                : L.Text("Bir sonraki molaya kalan süre", "Time until your next break");
        }
        public static string PrimaryAction(TimerEngine e)
        {
            if (e.P.InBreak) return L.Text("Çalışmaya dön", "Return to work");
            return e.NextBreakIsLong ? L.Text("Uzun mola >", "Long break >") : L.Text("Mola ver >", "Take a break >");
        }
        public static string SecondaryAction(TimerEngine e)
        {
            if (e.Due) return L.Text(TimerEngine.SnoozeMinutes + " dk ertele", "Snooze " + TimerEngine.SnoozeMinutes + " min");
            return e.P.Paused ? L.Text("Devam et", "Resume") : L.Text("Duraklat", "Pause");
        }
        /// <summary>Runs the action behind <see cref="SecondaryAction"/>.</summary>
        public static void RunSecondary(TimerEngine e) { if (e.Due) e.Snooze(); else e.TogglePause(); }
        public static void RunPrimary(TimerEngine e) { if (e.P.InBreak) e.CancelBreak(); else e.BeginBreak(); }

        public static string Footer(Preferences p)
        {
            string text = p.WorkMinutes + L.Text(" dk odak / ", "m focus / ") + p.BreakMinutes + L.Text(" dk mola  ·  ", "m break  ·  ")
                + p.Breaks + L.Text(" mola", " breaks");
            return p.DailyGoalMinutes > 0 ? text + "  ·  " + Goal(p) : text;
        }
        public static string Goal(Preferences p)
        {
            int percent = p.DailyGoalMinutes <= 0 ? 0 : (int)Math.Min(999, p.TodayWorkSeconds / (p.DailyGoalMinutes * 60.0) * 100);
            return L.Text("hedef %", "goal ") + percent + L.Text("", "%");
        }
        public static string Tray(TimerEngine e)
        {
            var p = e.P;
            string state = p.Chill ? L.Text("Solaire ile meyhanede", "At the tavern with Solaire")
                : p.Paused ? L.Text("Duraklatıldı", "Paused")
                : (p.InBreak ? L.Text("Mola ", "Break ") : L.Text("Çalışma ", "Work ")) + TimeText(p.Remaining);
            return "Soul Revive · " + state;
        }

        public static string DueTitle { get { return L.Text("Ateş sönüyor, şövalye.", "Your fire is fading."); } }
        public static string DueText { get { return L.Text("Mola ver'e bas; ayağa kalkıp biraz yürü.", "Select Take a break; stand up and walk."); } }
        public static string RevivedTitle { get { return L.Text("Yeni bir şafak.", "A new dawn."); } }
        public static string RevivedText { get { return L.Text("Mola tamamlandı. Yeni çalışma başladı.", "Break complete. Your next work period has started."); } }
        public static string RevivedBanner { get { return L.Text("ATEŞ YENİDEN CANLANDI", "HUMANITY RESTORED"); } }
        public static string IdleBanner { get { return L.Text("UZAKTAYDIN. SAYAÇ DURAKLATILDI.", "YOU WERE AWAY. TIMER PAUSED."); } }
        public static string SaveFailed { get { return L.Text("Kayıt başarısız. Klasör iznini kontrol et.", "Save failed. Check folder permissions."); } }

        public static readonly string[] LanguageNames = { "Türkçe", "English" };
        public static readonly string[] ThemeNames = { "Dark mode", "Lit mode" };
        public static string[] ArmorNames { get { return new[] { L.Text("Çelik şövalye", "Steel knight"), L.Text("Kara şövalye", "Black knight"), L.Text("Altın muhafız", "Golden guardian") }; } }
        public static string[] SwordNames { get { return new[] { L.Text("Uzun kılıç", "Longsword"), L.Text("Geniş kılıç", "Broadsword"), L.Text("İnce kılıç", "Estoc") }; } }
        public static string[] ShieldNames { get { return new[] { L.Text("Haçlı kalkan", "Cross shield"), L.Text("Yuvarlak kalkan", "Round shield"), L.Text("Mavi muhafız", "Blue guardian") }; } }
        public static string[] AnimationNames { get { return new[] { L.Text("Bekleme", "Idle"), L.Text("Kılıç bileme", "Sharpen sword"), L.Text("Harita inceleme", "Read map"), L.Text("Odun fırlatma", "Throw log"), L.Text("Uyku / mola", "Sleep / break"), L.Text("Meyhane / Solaire", "Tavern / Solaire") }; } }
        public static string[] LightingNames { get { return new[] { L.Text("Gündüz", "Day"), L.Text("Şafak", "Dawn"), L.Text("Gece", "Night") }; } }

        /// <summary>Short weekday labels for the last seven days, oldest first, ending today.</summary>
        public static string[] WeekLabels(DateTime now)
        {
            var culture = new CultureInfo(L.Language == "en" ? "en-US" : "tr-TR");
            var labels = new string[7];
            for (int i = 0; i < 7; i++)
            {
                string name = culture.DateTimeFormat.GetAbbreviatedDayName(now.Date.AddDays(i - 6).DayOfWeek);
                labels[i] = name.Length > 3 ? name.Substring(0, 3) : name;
            }
            return labels;
        }
        public static double[] WeekSeconds(Preferences p, DateTime now)
        {
            var values = new double[7];
            for (int i = 0; i < 7; i++) values[i] = p.WorkOn(now.Date.AddDays(i - 6));
            return values;
        }
        public static string StreakText(Preferences p, DateTime now)
        {
            int streak = p.Streak(now);
            string goal = p.DailyGoalMinutes > 0 ? L.Text("Günlük hedef ", "Daily goal ") + p.DailyGoalMinutes + L.Text(" dk · ", " min · ") : "";
            return goal + L.Text("Seri: ", "Streak: ") + streak + L.Text(" gün", streak == 1 ? " day" : " days");
        }
    }
}
