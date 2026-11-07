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
            p.Day = now.AddDays(-1).ToString("yyyy-MM-dd"); p.TodayWorkSeconds = 10;
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
            Check(Soundscape.Render(0,0,0,1).Skip(44).All(b => b == 0), "muted synthesized audio");
            Check(Soundscape.Render(35,25,20,1).Skip(44).Any(b => b != 0), "offline soundscape renders");
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
