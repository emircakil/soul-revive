using System;
using System.IO;
using System.Reflection;

namespace SoulsRevive.Cross
{
    static class Program
    {
        internal static string Version => Assembly.GetExecutingAssembly().GetName().Version.ToString(3);

        [STAThread]
        static int Main(string[] args)
        {
            try
            {
                int smokeFrames = 0;
                string screenshot = null;
                bool isolated = false;
                for (int i = 0; i < args.Length; i++)
                {
                    switch (args[i])
                    {
                        case "--version": Console.WriteLine(Version); return 0;
                        case "--version-file": File.WriteAllText(args[++i], Version); return 0;
                        case "--self-test": return PortableTests.Run(args[++i]);
                        case "--profile": Storage.DirectoryPath = Path.GetFullPath(args[++i]); isolated = true; break;
                        case "--smoke-test": smokeFrames = 30; screenshot = Path.GetFullPath(args[++i]); break;
                        default: throw new ArgumentException("Unknown option: " + args[i]);
                    }
                }
                if (smokeFrames > 0 && !isolated) throw new ArgumentException("--smoke-test requires an isolated --profile directory.");
                Directory.CreateDirectory(Storage.DirectoryPath);
                // Keep the lock file: unlinking it after close races a second process
                // holding the old inode on Unix. OS locks release automatically on exit.
                using var instance = new FileStream(Path.Combine(Storage.DirectoryPath, "app.lock"),
                    FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                using var app = new CrossCompanion();
                app.Run(smokeFrames, screenshot);
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Soul Revive: " + ex);
                return 1;
            }
        }
    }
}
