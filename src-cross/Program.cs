using System;
using System.IO;

namespace SoulsRevive.Cross
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            try
            {
                if (args.Length == 2 && args[0] == "--profile")
                {
                    CrossStorage.DirectoryPath = Path.GetFullPath(args[1]);
                }
                if (args.Length == 2 && args[0] == "--version-file")
                {
                    File.WriteAllText(args[1], "1.3.0.0");
                    return;
                }

                Directory.CreateDirectory(CrossStorage.DirectoryPath);
                string lockFile = Path.Combine(CrossStorage.DirectoryPath, "app.lock");

                FileStream lockStream = null;
                try
                {
                    lockStream = new FileStream(lockFile, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                }
                catch (IOException)
                {
                    // Already running instance
                    Console.WriteLine("Soul Revive is already running.");
                    return;
                }

                using (var app = new CrossCompanion())
                {
                    app.Run();
                }

                lockStream?.Dispose();
                try { File.Delete(lockFile); } catch { }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Soul Revive fatal error: " + ex);
            }
        }
    }
}
