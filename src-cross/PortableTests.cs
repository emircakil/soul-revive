using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization.Json;

namespace SoulsRevive.Cross
{
    static class PortableTests
    {
        static int passed;
        static void Check(bool condition, string name)
        {
            if (!condition) throw new InvalidOperationException("FAIL " + name);
            passed++;
            Console.WriteLine("PASS " + name);
        }
        static bool Near(double a, double b) => Math.Abs(a-b) < .00001;

        public static int Run(string output)
        {
            output = Path.GetFullPath(output);
            Directory.CreateDirectory(output);
            Storage.DirectoryPath = Path.Combine(output, "profile-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Storage.DirectoryPath);
            DateTime now = DateTime.Today.AddHours(12);
            var p = new Preferences { WorkMinutes = 1, BreakMinutes = 2, Remaining = 1 };
            var engine = new TimerEngine(p);
            engine.Advance(.25, now);
            Check(Near(p.TotalWorkSeconds, .25), "fractional work seconds");
            engine.TogglePause(); engine.Advance(1, now);
            Check(Near(p.Remaining, .75), "pause preserves remaining time");
            engine.TogglePause(); engine.Advance(2, now); engine.Advance(2, now);
            Check(engine.Due && p.CompletedSessions == 1 && Near(p.TotalWorkSeconds, 1), "expiry counted exactly once");
            p.Validate();
            Check(engine.Due, "expired round survives validation/restart");
            engine.BeginBreak(); engine.Advance(.125, now);
            Check(Near(p.MovementSeconds, .125) && engine.Health > 0, "fractional break seconds and health recovery");
            p.Validate();
            Check(Near(p.Remaining, 119.875), "break can be longer than work");
            engine.CancelBreak();
            Check(p.Breaks == 0 && p.Remaining == 60 && !p.Paused, "early return starts fresh round");
            engine.BeginBreak(); p.Remaining = .125; engine.Advance(.5, now);
            Check(p.Breaks == 1 && !p.InBreak && p.Remaining == 60 && Near(p.MovementSeconds, .25), "break completion counts only elapsed break time");
            double work = p.TotalWorkSeconds;
            engine.Advance(60, now); engine.Advance(double.NaN, now); engine.Advance(double.PositiveInfinity, now);
            Check(p.TotalWorkSeconds == work, "sleep gaps and invalid deltas ignored");
            engine.SetChill(true); engine.Advance(1, now);
            Check(p.TotalWorkSeconds == work, "chill excludes work");
            engine.SetChill(false); engine.Advance(.5, now);
            engine.ApplyDurations(1, 2);
            Check(Near(p.Remaining, 59.5), "unrelated settings preserve timer");
            engine.ApplyDurations(2, 3);
            Check(p.Remaining == 120, "duration change resets timer");
            p.Day = Preferences.DayKey(now.AddDays(-1)); p.TodayWorkSeconds = 10;
            engine.Advance(1, now.Date.AddSeconds(.2));
            Check(Near(p.TodayWorkSeconds, .2), "midnight allocates only new-day seconds");
            p.Remaining = double.NaN; p.TotalWorkSeconds = double.NaN; p.TodayWorkSeconds = -1;
            p.Validate();
            Check(p.Remaining == 120 && p.TotalWorkSeconds == 0 && p.TodayWorkSeconds == 0, "corrupt numerical preferences sanitized");

            File.WriteAllText(Storage.SettingsPath, "{\"TotalWorkSeconds\":42}");
            var legacy = Storage.Load(); legacy.Validate();
            Check(legacy.WorkMinutes == 30 && legacy.BreakMinutes == 3 && legacy.Sound && legacy.TotalWorkSeconds == 42, "legacy settings receive defaults");
            legacy.TotalWorkSeconds = 123; legacy.TodayWorkSeconds = 12;
            Check(Storage.Save(legacy), "atomic settings/statistics save");
            legacy.TotalWorkSeconds = 456; Check(Storage.Save(legacy), "atomic replacement");
            File.WriteAllText(Storage.SettingsPath, "broken");
            var recovered = Storage.Load();
            Check(recovered.TotalWorkSeconds == 456, "backup settings and independent statistics recover");
            File.WriteAllText(Storage.StatisticsPath, "broken");
            File.WriteAllText(Storage.SettingsPath + ".bak", "broken");
            Check(Storage.Load().TotalWorkSeconds == 123, "statistics backup recovers independently");
            string lockPath = Path.Combine(Storage.DirectoryPath, "app.lock");
            using (var first = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
            {
                bool refused = false;
                try { using var second = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
                catch (IOException) { refused = true; }
                Check(refused, "profile lock excludes second instance");
            }
            using (var reopened = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
                Check(true, "profile lock released without unlinking");

            if (OperatingSystem.IsMacOS()) Check(Storage.DefaultDirectoryPath.EndsWith("Library/Application Support/SoulsRevive"), "macOS data directory");
            if (OperatingSystem.IsLinux()) Check(Path.IsPathRooted(Storage.DefaultDirectoryPath), "absolute XDG data directory");
            using (var font = Assembly.GetExecutingAssembly().GetManifestResourceStream("PixelFont.ttf"))
                Check(font != null && font.Length > 10000, "embedded font resource");

            var routine = new WorkRoutine(new Random(123));
            for (int i = 0; i < 24; i++) routine.Advance(true);
            Check(routine.Action != WorkAction.Idle, "shared work routine starts");
            int frame = routine.Frame; routine.Advance(false);
            Check(frame == routine.Frame, "paused animation freezes");
            for (int i = 0; i < 112; i++) routine.Advance(true);
            Check(routine.Action == WorkAction.Idle, "work routine completes at 14 seconds");
            foreach (WorkAction action in Enum.GetValues<WorkAction>())
            {
                using var a = PixelArt.EquipmentScene(new KnightEquipment(0,0,0), 1, false, 40, 12, false, action, 40);
                using var b = PixelArt.EquipmentScene(new KnightEquipment(2,1,2), 1, false, 40, 12, false, action, 40);
                Check(a.Canvas.Pixels.Distinct().Count() > 30, action + " artwork contains full scene");
                Check(!a.Canvas.Pixels.SequenceEqual(b.Canvas.Pixels), action + " equipment changes artwork");
                WritePpm(Path.Combine(output, action + ".ppm"), a);
                for (int f = 0; f < 112; f++)
                    using (var image = PixelArt.Scene(1, false, f, 12, false, action, f)) { }
            }
            using (var dawn = PixelArt.Scene(1, false, 0, 5.5, false))
            using (var night = PixelArt.Scene(1, false, 0, 22, false))
                Check(!dawn.Canvas.Pixels.SequenceEqual(night.Canvas.Pixels), "day/night changes scene");
            using (var sleep = PixelArt.Scene(.5, true, 20, 22, false)) WritePpm(Path.Combine(output, "sleep.ppm"), sleep);
            using (var tavern = PixelArt.Scene(1, false, 100, 5.5, true))
            using (var later = PixelArt.Scene(1, false, 100, 22, true))
            {
                Check(tavern.Canvas.Pixels.SequenceEqual(later.Canvas.Pixels), "shared tavern independent of clock");
                WritePpm(Path.Combine(output, "tavern.ppm"), tavern);
            }
            for (int f = 0; f < PixelArt.TavernArrivalFrames + PixelArt.TavernCycleFrames; f++)
                using (var image = PixelArt.Tavern(f)) { }
            // Animation polish: the fire follows the timer, the thrown log burns, nothing pops.
            static int Count(PixelBitmap image, string hex) { uint c = CrossCanvas.Hex(hex); return image.Canvas.Pixels.Count(p => p == c); }
            using (var full = PixelArt.Scene(1, false, 0, 12, false))
            using (var low = PixelArt.Scene(.15, false, 0, 12, false))
            using (var outFire = PixelArt.Scene(0, false, 0, 20.5, false))
                Check(Count(full, "#FFD48B") > Count(low, "#FFD48B") && Count(low, "#FFD48B") >= Count(outFire, "#FFD48B") && Count(outFire, "#A24628") > 0,
                    "bonfire burns lower as the round runs out but never goes dark");
            using (var calm = PixelArt.Scene(1, false, 0, 12, false, WorkAction.TendFire, 60))
            using (var flare = PixelArt.Scene(1, false, 0, 12, false, WorkAction.TendFire, 80))
                Check(Count(flare, "#A24628") > Count(calm, "#A24628"), "fresh log makes the fire flare");
            using (var landed = PixelArt.Scene(1, false, 0, 12, false, WorkAction.TendFire, 74))
            using (var gone = PixelArt.Scene(1, false, 0, 12, false, WorkAction.TendFire, 88))
                Check(Count(landed, "#2A1A16") > 0 && Count(gone, "#2A1A16") == 0, "thrown log chars and burns away");
            using (var before = PixelArt.Scene(1, false, 9, 12, false, WorkAction.Sharpen, 8))
            using (var after = PixelArt.Scene(1, false, 9, 12, false, WorkAction.Sharpen, 9))
            {
                bool same = true;
                for (int y = 24; y < 30; y++) for (int x = 58; x < 74; x++) if (before.GetPixel(x, y) != after.GetPixel(x, y)) same = false;
                Check(same, "helmet does not jump when a task starts mid-breath");
            }
            {
                var stages = new PixelBitmap[PixelArt.KneelFrames + 1];
                for (int k = 0; k <= PixelArt.KneelFrames; k++) stages[k] = PixelArt.Scene(0, false, 0, 20.5, false, WorkAction.Idle, 0, k);
                bool distinct = true;
                for (int k = 1; k <= PixelArt.KneelFrames; k++) if (stages[k].Canvas.Pixels.SequenceEqual(stages[k - 1].Canvas.Pixels)) distinct = false;
                using (var still = PixelArt.Scene(0, false, 0, 20.5, false))
                    Check(distinct && stages[PixelArt.KneelFrames].Canvas.Pixels.SequenceEqual(still.Canvas.Pixels), "knight sinks to one knee over several frames");
                foreach (var stage in stages) stage.Dispose();
                int kneel = 0; for (int i = 0; i < 10; i++) kneel = PixelArt.StepKneel(kneel, true);
                int peak = kneel; kneel = PixelArt.StepKneel(kneel, false);
                Check(peak == PixelArt.KneelFrames && kneel == PixelArt.KneelFrames - 1, "kneel holds while expired and reverses after a snooze");
            }
            using (var dusk = PixelArt.Scene(1, false, 0, 17.5, false))
            using (var night = PixelArt.Scene(1, false, 0, 21, false))
            using (var noon = PixelArt.Scene(1, false, 0, 12, false))
                Check(noon.GetPixel(140, 15) != night.GetPixel(140, 15) && dusk.GetPixel(140, 15) != night.GetPixel(140, 15) && dusk.GetPixel(140, 15) != noon.GetPixel(140, 15),
                    "moon fades in through dusk instead of popping");
            using (var walking = PixelArt.Tavern(PixelArt.TavernArrivalFrames - 7))
            using (var sitting = PixelArt.Tavern(PixelArt.TavernArrivalFrames - 3))
            using (var seated = PixelArt.Tavern(PixelArt.TavernArrivalFrames))
                Check(!walking.Canvas.Pixels.SequenceEqual(sitting.Canvas.Pixels) && !sitting.Canvas.Pixels.SequenceEqual(seated.Canvas.Pixels), "knight lowers into the chair before drinking");

            Check(SoundSynth.Render(0,0,0,1).Skip(44).All(b => b == 0), "muted synthesized audio");
            Check(SoundSynth.Render(35,25,20,1).Skip(44).Any(b => b != 0), "offline soundscape renders");
            short[] loop = SoundSynth.RenderSamples(50, 50, 50, 4, true);
            int largest = 0; for (int i = 1; i < loop.Length; i++) largest = Math.Max(largest, Math.Abs(loop[i] - loop[i - 1]));
            Check(Math.Abs(loop[0] - loop[^1]) <= Math.Max(2000, largest), "soundscape loop is seamless");
            Check(SoundSynth.Reminder().Length > 44, "reminder chime renders");

            // Timer features shared with Windows.
            var snooze = new Preferences { WorkMinutes = 10 }; var timer = new TimerEngine(snooze); snooze.CheckDay(now);
            for (int i = 0; i < 600; i++) timer.Advance(1, now);
            Ui.RunSecondary(timer);
            Check(!timer.Due && snooze.Remaining == 300 && snooze.CompletedSessions == 1, "snooze adds five minutes once");
            var longBreak = new Preferences { WorkMinutes = 1, BreakMinutes = 1, LongBreakEvery = 2, LongBreakMinutes = 5 }; var lb = new TimerEngine(longBreak); longBreak.CheckDay(now);
            lb.BeginBreak(); for (int i = 0; i < 60; i++) lb.Advance(1, now);
            Check(lb.NextBreakIsLong && Ui.PrimaryAction(lb).Contains(">"), "second break is announced as long");
            lb.BeginBreak(); Check(longBreak.InLongBreak && longBreak.Remaining == 300, "long break uses its own duration");
            var idle = new Preferences(); var idleEngine = new TimerEngine(idle); idle.CheckDay(now);
            for (int i = 0; i < 400; i++) idleEngine.Advance(1, now);
            Check(idleEngine.PauseForIdle(300, now) && idle.Paused && Near(idle.TotalWorkSeconds, 100), "idle pause rolls back idle work");
            var history = new Preferences { DailyGoalMinutes = 1 }; history.Day = Preferences.DayKey(now.AddDays(-1)); history.TodayWorkSeconds = 120; history.CheckDay(now);
            Check(history.History.Count == 1 && history.Streak(now) == 1, "history and streak survive the day change");
            L.Language = "tr"; Check(Ui.LongDuration(3723) == "1 sa 2 dk 3 sn" && Ui.Minutes(150.7) == "2", "Turkish duration units");
            L.Language = "en"; Check(Ui.LongDuration(3723) == "1h 2m 3s", "English duration units");
            var breakEngine = new TimerEngine(new Preferences()); breakEngine.BeginBreak();
            Check(Ui.PrimaryAction(breakEngine) == "Return to work", "break button returns to work without claiming completion");
            L.Language = "tr";

            // Reset survives a restart even though statistics normally only grow.
            var reset = Storage.Load(); var resetEngine = new TimerEngine(reset); reset.TotalWorkSeconds = 999; Storage.Save(reset);
            resetEngine.ResetStatistics(); Storage.Save(reset, true);
            Check(Storage.Load().TotalWorkSeconds == 0, "statistics reset is persistent");

            // Platform helpers.
            Check(DesktopSession.ParseNumber("(uint64 12345,)") == 12345 && DesktopSession.ParseNumber(null) < 0, "idle monitor reply parsing");
            string entry = Autostart.DesktopEntry("/home/a b/$x\\SoulsRevive");
            Check(entry.Contains("Exec=\"/home/a b/\\\\$x\\\\\\\\SoulsRevive\" --startup"), "autostart desktop entry escapes the path");
            File.WriteAllText(Path.Combine(output, "autostart.desktop"), Autostart.DesktopEntry("/opt/Soul Revive/50%/SoulsRevive"));
            File.WriteAllText(Path.Combine(output, "launch-agent.plist"), Autostart.LaunchAgent("/Applications/SoulRevive.app/Contents/MacOS/SoulsRevive"));
            Check(Autostart.LaunchAgent("/Apps/A&B/SoulsRevive").Contains("<string>/Apps/A&amp;B/SoulsRevive</string><string>--startup</string>"), "launch agent escapes XML");
            Check(Notifier.AppleScriptString("say \"hi\" \\ ok") == "\"say \\\"hi\\\" \\\\ ok\"", "notification text is escaped for AppleScript");
            var current = new Version(1, 3, 1);
            Check(CrossUpdates.Parse("{\"tag_name\":\"v1.4.0\",\"html_url\":\"https://github.com/emircakil/soul-revive/releases/tag/v1.4.0\"}", current)?.Version == new Version(1, 4, 0), "newer release detected");
            Check(CrossUpdates.Parse("{\"tag_name\":\"v1.3.1\"}", current) == null && CrossUpdates.Parse("{\"tag_name\":\"v9.0.0\",\"prerelease\":true}", current) == null, "same, older and prereleases ignored");
            Check(CrossUpdates.Parse("{\"tag_name\":\"v2.0.0\",\"html_url\":\"https://evil.example/x\"}", current).Url == CrossUpdates.ReleasesPage, "release link limited to the project");
            AppFiles.RequestShow(); Check(AppFiles.TakeShowRequest() && !AppFiles.TakeShowRequest(), "second launch hands off to the running window");
            Check(AppFiles.LogCrash(new InvalidOperationException("test crash")) != null && File.ReadAllText(AppFiles.CrashLogPath).Contains("test crash"), "crash log written to the profile");
            File.WriteAllText(Path.Combine(output, "results.txt"), $"PASS {passed} portable checks\n");
            Console.WriteLine($"PASS {passed} portable checks");
            return 0;
        }

        static void WritePpm(string path, PixelBitmap image)
        {
            using var file = File.Create(path);
            byte[] header = System.Text.Encoding.ASCII.GetBytes($"P6\n{image.Width} {image.Height}\n255\n");
            file.Write(header);
            foreach (uint pixel in image.Canvas.Pixels)
            { file.WriteByte((byte)pixel); file.WriteByte((byte)(pixel >> 8)); file.WriteByte((byte)(pixel >> 16)); }
        }
    }
}
