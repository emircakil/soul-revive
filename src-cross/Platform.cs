using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SoulsRevive.Cross
{
    /// <summary>
    /// Screen-lock and input-idle state for macOS and Linux, polled on a background thread so the
    /// UI loop never waits for a helper process. Only timestamps are read, never input content.
    /// </summary>
    sealed class DesktopSession : IDisposable
    {
        readonly Thread thread;
        readonly ManualResetEventSlim stop = new ManualResetEventSlim();
        volatile bool locked;
        double idleSeconds = -1;
        volatile bool wantIdle;
        LinuxLockMethod lockMethod = LinuxLockMethod.Unknown;
        LinuxIdleMethod idleMethod = LinuxIdleMethod.Unknown;

        enum LinuxLockMethod { Unknown, Logind, FreedesktopScreenSaver, GnomeScreenSaver, None }
        enum LinuxIdleMethod { Unknown, Mutter, X11, None }

        public bool Locked => locked;
        /// <summary>Seconds since the last keyboard/mouse input, or a negative value when unknown.</summary>
        public double IdleSeconds => Volatile.Read(ref idleSeconds);
        /// <summary>Idle time is only measured while the user has idle pause enabled.</summary>
        public bool WantIdle { get => wantIdle; set => wantIdle = value; }
        public bool IdleSupported => OperatingSystem.IsMacOS() || idleMethod != LinuxIdleMethod.None;

        public DesktopSession()
        {
            thread = new Thread(Poll) { IsBackground = true, Name = "Soul Revive session monitor", Priority = ThreadPriority.BelowNormal };
            thread.Start();
        }

        void Poll()
        {
            while (!stop.Wait(TimeSpan.FromSeconds(4)))
            {
                try { locked = QueryLocked(); } catch (Exception) { locked = false; }
                try { Volatile.Write(ref idleSeconds, wantIdle ? QueryIdle() : -1); } catch (Exception) { Volatile.Write(ref idleSeconds, -1.0); }
            }
        }

        bool QueryLocked()
        {
            if (OperatingSystem.IsMacOS()) return MacSession.ScreenLocked();
            if (!OperatingSystem.IsLinux()) return false;
            if (lockMethod is LinuxLockMethod.Unknown or LinuxLockMethod.Logind)
            {
                string session = Environment.GetEnvironmentVariable("XDG_SESSION_ID");
                string hint = Run("loginctl", "show-session", string.IsNullOrEmpty(session) ? "self" : session, "-p", "LockedHint", "--value");
                if (hint == "yes" || hint == "no") { lockMethod = LinuxLockMethod.Logind; if (hint == "yes") return true; }
                else if (lockMethod == LinuxLockMethod.Logind) lockMethod = LinuxLockMethod.Unknown;
            }
            // Some desktops only report the lock through their screensaver service.
            if (lockMethod is LinuxLockMethod.Unknown or LinuxLockMethod.Logind or LinuxLockMethod.FreedesktopScreenSaver)
            {
                string active = Run("gdbus", "call", "--session", "--dest", "org.freedesktop.ScreenSaver", "--object-path", "/org/freedesktop/ScreenSaver", "--method", "org.freedesktop.ScreenSaver.GetActive");
                if (active != null && active.Contains("true")) return true;
                if (active != null && lockMethod == LinuxLockMethod.Unknown) lockMethod = LinuxLockMethod.FreedesktopScreenSaver;
            }
            if (lockMethod is LinuxLockMethod.Unknown or LinuxLockMethod.GnomeScreenSaver)
            {
                string active = Run("gdbus", "call", "--session", "--dest", "org.gnome.ScreenSaver", "--object-path", "/org/gnome/ScreenSaver", "--method", "org.gnome.ScreenSaver.GetActive");
                if (active != null) { lockMethod = LinuxLockMethod.GnomeScreenSaver; return active.Contains("true"); }
            }
            if (lockMethod == LinuxLockMethod.Unknown) lockMethod = LinuxLockMethod.None;
            return false;
        }

        double QueryIdle()
        {
            if (OperatingSystem.IsMacOS()) return MacSession.IdleSeconds();
            if (!OperatingSystem.IsLinux()) return -1;
            if (idleMethod is LinuxIdleMethod.Unknown or LinuxIdleMethod.Mutter)
            {
                // GNOME (X11 and Wayland): "(uint64 12345,)" in milliseconds.
                string reply = Run("gdbus", "call", "--session", "--dest", "org.gnome.Mutter.IdleMonitor", "--object-path", "/org/gnome/Mutter/IdleMonitor/Core", "--method", "org.gnome.Mutter.IdleMonitor.GetIdletime");
                double ms = ParseNumber(reply);
                if (ms >= 0) { idleMethod = LinuxIdleMethod.Mutter; return ms / 1000; }
            }
            // XScreenSaver only sees X11 input, so it is trusted only in a real X11 session (not XWayland).
            if ((idleMethod is LinuxIdleMethod.Unknown or LinuxIdleMethod.X11) && Environment.GetEnvironmentVariable("XDG_SESSION_TYPE") == "x11")
            {
                double seconds = X11Idle.Seconds();
                if (seconds >= 0) { idleMethod = LinuxIdleMethod.X11; return seconds; }
            }
            idleMethod = LinuxIdleMethod.None;
            return -1;
        }

        internal static double ParseNumber(string reply)
        {
            if (string.IsNullOrEmpty(reply)) return -1;
            // Skip the type width in "uint64 123": take the last number in the reply.
            string[] parts = reply.Split(new[] { ' ', '(', ')', ',' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = parts.Length - 1; i >= 0; i--)
                if (double.TryParse(parts[i], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double value)) return value;
            return -1;
        }

        /// <summary>Runs a helper with a short timeout and returns trimmed stdout, or null on failure.</summary>
        internal static string Run(string file, params string[] arguments)
        {
            try
            {
                var start = new ProcessStartInfo(file) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
                foreach (string argument in arguments) start.ArgumentList.Add(argument);
                using var process = Process.Start(start);
                if (process == null) return null;
                var output = process.StandardOutput.ReadToEndAsync();
                process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(2000)) { try { process.Kill(true); } catch (Exception) { } return null; }
                return process.ExitCode == 0 ? output.Result.Trim() : null;
            }
            catch (Exception) { return null; }
        }

        public void Dispose() { stop.Set(); thread.Join(1000); stop.Dispose(); }
    }

    static class MacSession
    {
        const string CoreGraphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
        const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
        [DllImport(CoreGraphics)] static extern IntPtr CGSessionCopyCurrentDictionary();
        [DllImport(CoreGraphics)] static extern double CGEventSourceSecondsSinceLastEventType(int source, uint eventType);
        [DllImport(CoreFoundation)] static extern IntPtr CFDictionaryGetValue(IntPtr dictionary, IntPtr key);
        [DllImport(CoreFoundation)] static extern IntPtr CFStringCreateWithCString(IntPtr allocator, string text, uint encoding);
        [DllImport(CoreFoundation)] static extern bool CFBooleanGetValue(IntPtr boolean);
        [DllImport(CoreFoundation)] static extern void CFRelease(IntPtr value);

        public static bool ScreenLocked()
        {
            IntPtr session = CGSessionCopyCurrentDictionary();
            if (session == IntPtr.Zero) return false;
            IntPtr key = CFStringCreateWithCString(IntPtr.Zero, "CGSSessionScreenIsLocked", 0x08000100);
            try
            {
                IntPtr value = CFDictionaryGetValue(session, key);
                return value != IntPtr.Zero && CFBooleanGetValue(value);
            }
            finally { CFRelease(key); CFRelease(session); }
        }
        // kCGEventSourceStateHIDSystemState and kCGAnyInputEventType.
        public static double IdleSeconds() => CGEventSourceSecondsSinceLastEventType(1, uint.MaxValue);
    }

    static class X11Idle
    {
        [StructLayout(LayoutKind.Sequential)]
        struct ScreenSaverInfo { public IntPtr Window; public int State; public int Kind; public UIntPtr TilOrSince; public UIntPtr Idle; public UIntPtr EventMask; }
        [DllImport("libX11.so.6")] static extern IntPtr XOpenDisplay(IntPtr name);
        [DllImport("libX11.so.6")] static extern IntPtr XDefaultRootWindow(IntPtr display);
        [DllImport("libX11.so.6")] static extern int XCloseDisplay(IntPtr display);
        [DllImport("libXss.so.1")] static extern int XScreenSaverQueryInfo(IntPtr display, IntPtr drawable, ref ScreenSaverInfo info);
        public static double Seconds()
        {
            try
            {
                IntPtr display = XOpenDisplay(IntPtr.Zero);
                if (display == IntPtr.Zero) return -1;
                try
                {
                    var info = new ScreenSaverInfo();
                    return XScreenSaverQueryInfo(display, XDefaultRootWindow(display), ref info) != 0 ? (ulong)info.Idle / 1000.0 : -1;
                }
                finally { XCloseDisplay(display); }
            }
            catch (DllNotFoundException) { return -1; }
            catch (EntryPointNotFoundException) { return -1; }
        }
    }

    /// <summary>Desktop notifications through the system's own tools; failures are silent.</summary>
    static class Notifier
    {
        public static void Show(string title, string body)
        {
            Task.Run(() =>
            {
                if (OperatingSystem.IsMacOS())
                    DesktopSession.Run("osascript", "-e", "display notification " + AppleScriptString(body) + " with title " + AppleScriptString(title));
                else if (OperatingSystem.IsLinux())
                    DesktopSession.Run("notify-send", "--app-name=Soul Revive", "--icon=soulrevive", title, body);
            });
        }
        public static void Alert(string title, string body)
        {
            if (OperatingSystem.IsMacOS())
                DesktopSession.Run("osascript", "-e", "display alert " + AppleScriptString(title) + " message " + AppleScriptString(body) + " as critical");
            else if (OperatingSystem.IsLinux() && DesktopSession.Run("zenity", "--error", "--title=" + title, "--text=" + body) == null
                && DesktopSession.Run("kdialog", "--title", title, "--error", body) == null)
                DesktopSession.Run("notify-send", "--urgency=critical", title, body);
        }
        internal static string AppleScriptString(string text) => "\"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    }

    /// <summary>Start at sign-in: an XDG autostart entry on Linux, a LaunchAgent on macOS.</summary>
    static class Autostart
    {
        const string Label = "io.github.emircakil.soulrevive";
        public static bool Supported => OperatingSystem.IsLinux() || OperatingSystem.IsMacOS();
        internal static string FilePath
        {
            get
            {
                string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                if (OperatingSystem.IsMacOS()) return Path.Combine(home, "Library", "LaunchAgents", Label + ".plist");
                string config = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
                if (string.IsNullOrEmpty(config) || !Path.IsPathRooted(config)) config = Path.Combine(home, ".config");
                return Path.Combine(config, "autostart", "soulrevive.desktop");
            }
        }
        public static bool Enabled => Supported && File.Exists(FilePath);
        public static void Set(bool enabled)
        {
            if (!Supported) return;
            if (!enabled) { if (File.Exists(FilePath)) File.Delete(FilePath); return; }
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            File.WriteAllText(FilePath, OperatingSystem.IsMacOS() ? LaunchAgent(Executable) : DesktopEntry(Executable), new UTF8Encoding(false));
        }
        /// <summary>Points an existing entry at this executable again after the app was moved or updated.</summary>
        public static void Refresh() { try { if (Enabled && !File.ReadAllText(FilePath).Contains(Executable)) Set(true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
        static string Executable => Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "SoulsRevive");
        internal static string DesktopEntry(string executable)
        {
            // Desktop Entry spec: quote the path and escape ", `, $ and \ inside it.
            var quoted = new StringBuilder("\"");
            foreach (char c in executable)
            {
                if (c is '"' or '`' or '$' or '\\') quoted.Append('\\');
                quoted.Append(c);
                if (c == '%') quoted.Append('%'); // field-code marker
            }
            quoted.Append('"');
            return "[Desktop Entry]\nType=Application\nName=Soul Revive\nComment=Pixel bonfire work and break companion\nExec=" + quoted.ToString().Replace("\\", "\\\\") + " --startup\nIcon=soulrevive\nTerminal=false\nX-GNOME-Autostart-enabled=true\n";
        }
        internal static string LaunchAgent(string executable)
        {
            string path = System.Security.SecurityElement.Escape(executable);
            return "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<!DOCTYPE plist PUBLIC \"-//Apple//DTD PLIST 1.0//EN\" \"http://www.apple.com/DTDs/PropertyList-1.0.dtd\">\n"
                + "<plist version=\"1.0\"><dict><key>Label</key><string>" + Label + "</string>"
                + "<key>ProgramArguments</key><array><string>" + path + "</string><string>--startup</string></array>"
                + "<key>RunAtLoad</key><true/><key>ProcessType</key><string>Interactive</string></dict></plist>\n";
        }
    }

    /// <summary>Checks GitHub for a newer stable release. Portable builds are replaced by the user, so it only links to it.</summary>
    static class CrossUpdates
    {
        public const string LatestApi = "https://api.github.com/repos/emircakil/soul-revive/releases/latest";
        public const string ReleasesPage = "https://github.com/emircakil/soul-revive/releases/latest";
        public sealed record Release(Version Version, string Url);

        public static async Task<Release> CheckAsync()
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("SoulRevive/" + Program.Version);
            client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            using var response = await client.GetAsync(LatestApi).ConfigureAwait(false);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound) throw new InvalidOperationException(L.Text("Henüz yayımlanmış bir sürüm yok.", "No release has been published yet."));
            if ((int)response.StatusCode is 403 or 429) throw new InvalidOperationException(L.Text("GitHub sorgu sınırına ulaşıldı. Biraz sonra tekrar dene.", "GitHub rate limit reached. Try again later."));
            response.EnsureSuccessStatusCode();
            return Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false), Version.Parse(Program.Version));
        }

        internal static Release Parse(string json, Version current)
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.TryGetProperty("draft", out var draft) && draft.ValueKind == JsonValueKind.True) return null;
            if (root.TryGetProperty("prerelease", out var pre) && pre.ValueKind == JsonValueKind.True) return null;
            string tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() : null;
            if (tag == null || !Version.TryParse(tag.TrimStart('v', 'V'), out var version)) throw new InvalidOperationException(L.Text("Sürüm bilgisi geçersiz.", "Invalid release information."));
            version = new Version(version.Major, version.Minor, Math.Max(0, version.Build));
            current = new Version(current.Major, current.Minor, Math.Max(0, current.Build));
            if (version <= current) return null;
            string url = root.TryGetProperty("html_url", out var u) ? u.GetString() : null;
            // Only ever open the project's own release page.
            if (url == null || !url.StartsWith("https://github.com/emircakil/soul-revive/releases/", StringComparison.Ordinal)) url = ReleasesPage;
            return new Release(version, url);
        }
    }

    /// <summary>Crash log and second-instance hand-off, kept in the profile directory.</summary>
    static class AppFiles
    {
        public static string ShowRequestPath => Path.Combine(Storage.DirectoryPath, "show.request");
        public static string CrashLogPath => Path.Combine(Storage.DirectoryPath, "crash.log");
        public static void RequestShow() { try { File.WriteAllText(ShowRequestPath, DateTime.UtcNow.ToString("o")); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
        public static bool TakeShowRequest()
        {
            try { if (!File.Exists(ShowRequestPath)) return false; File.Delete(ShowRequestPath); return true; }
            catch (IOException) { return false; } catch (UnauthorizedAccessException) { return false; }
        }
        public static string LogCrash(Exception ex)
        {
            try
            {
                Directory.CreateDirectory(Storage.DirectoryPath);
                // Keep the log small: the newest report plus the end of older ones.
                string previous = File.Exists(CrashLogPath) ? File.ReadAllText(CrashLogPath) : "";
                if (previous.Length > 64 * 1024) previous = previous.Substring(previous.Length - 64 * 1024);
                File.WriteAllText(CrashLogPath, previous + "=== " + DateTime.Now.ToString("u") + " · Soul Revive " + Program.Version + " · " + RuntimeInformation.OSDescription + "\n" + ex + "\n\n");
                return CrashLogPath;
            }
            catch (Exception) { return null; }
        }
    }
}
