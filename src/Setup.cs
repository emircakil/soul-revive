using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace SoulsRevive
{
    static class Installation
    {
        const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\SoulsRevive";
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        public static string DirectoryPath { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SoulsRevive"); } }
        public static string Executable { get { return Path.Combine(DirectoryPath, "SoulsRevive.exe"); } }
        static string[] ShortcutPaths
        {
            get
            {
                return new[] {
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Soul Revive.lnk"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Soul Revive.lnk")
                };
            }
        }

        public static void Extract(string destination)
        {
            Directory.CreateDirectory(destination);
            using (var source = Assembly.GetExecutingAssembly().GetManifestResourceStream("SoulsRevive.Payload"))
            {
                if (source == null) throw new InvalidOperationException("Installer payload is missing.");
                string staged = Path.Combine(destination, "SoulsRevive.exe.new");
                using (var target = File.Create(staged)) source.CopyTo(target);
                string exe = Path.Combine(destination, "SoulsRevive.exe");
                if (File.Exists(exe)) File.Replace(staged, exe, exe + ".install-backup");
                else File.Move(staged, exe);
            }
        }

        public static void Install(bool desktop, bool startup)
        {
            bool existing = File.Exists(Executable);
            // Ask the running instance to save and quit instead of terminating it.
            RequestQuit();
            foreach (var process in Process.GetProcessesByName("SoulsRevive"))
                using (process)
                    if (string.Equals(Updates.ProcessPath(process), Executable, StringComparison.OrdinalIgnoreCase) && !process.WaitForExit(15000))
                        throw new IOException(L.Text("Önce tepsi simgesine sağ tıkla ve Çıkış'ı seç; sonra tekrar Kur'a bas.",
                            "Right-click the Soul Revive tray icon and choose Quit, then retry Install."));
            Extract(DirectoryPath);
            Shortcut(ShortcutPaths[1]);
            if (desktop) Shortcut(ShortcutPaths[0]);
            // Existing preferences, including startup, always survive an upgrade.
            if (!existing)
                using (var key = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (startup) key.SetValue("SoulsRevive", Updates.Quote(Executable) + " --startup");
                    else key.DeleteValue("SoulsRevive", false);
                }
            using (var key = Registry.CurrentUser.CreateSubKey(UninstallKey))
            {
                key.SetValue("DisplayName", "Soul Revive");
                key.SetValue("DisplayVersion", Updates.VersionText);
                key.SetValue("Publisher", "Soul Revive");
                key.SetValue("DisplayIcon", Executable + ",0");
                key.SetValue("InstallLocation", DirectoryPath);
                key.SetValue("UninstallString", Updates.Quote(Executable) + " --uninstall");
                key.SetValue("NoModify", 1);
                key.SetValue("NoRepair", 1);
            }
        }

        static void RequestQuit()
        {
            try { using (var signal = EventWaitHandle.OpenExisting(@"Local\SoulsRevive.Quit")) signal.Set(); }
            catch (WaitHandleCannotBeOpenedException) { }
        }

        static void Shortcut(string path)
        {
            object shell = null, shortcut = null;
            try
            {
                shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
                shortcut = shell.GetType().InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { path });
                var properties = new[] {
                    new[] { "TargetPath", Executable }, new[] { "WorkingDirectory", DirectoryPath },
                    new[] { "Description", "Soul Revive pixel bonfire companion" }, new[] { "IconLocation", Executable + ",0" }
                };
                foreach (var item in properties)
                    shortcut.GetType().InvokeMember(item[0], BindingFlags.SetProperty, null, shortcut, new object[] { item[1] });
                shortcut.GetType().InvokeMember("Save", BindingFlags.InvokeMethod, null, shortcut, null);
            }
            finally
            {
                if (shortcut != null) System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shortcut);
                if (shell != null) System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);
            }
        }

        static string ShortcutTarget(string path)
        {
            object shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")), shortcut = null;
            try
            {
                shortcut = shell.GetType().InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { path });
                return shortcut.GetType().InvokeMember("TargetPath", BindingFlags.GetProperty, null, shortcut, null) as string;
            }
            finally
            {
                if (shortcut != null) System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shortcut);
                System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);
            }
        }

        public static void Uninstall()
        {
            if (MessageBox.Show(L.Text("Soul Revive kaldırılsın mı? Ayarlar ve istatistikler tekrar kurulum için saklanacak.",
                    "Uninstall Soul Revive? Settings and statistics will be kept for a future reinstall."),
                    "Soul Revive", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            if (!string.Equals(Application.ExecutablePath, Executable, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Run uninstall from the installed application.");
            string helper = Path.Combine(Path.GetTempPath(), "SoulRevive-remove-" + Guid.NewGuid().ToString("N") + ".exe");
            File.Copy(Executable, helper);
            Process.Start(new ProcessStartInfo(helper, "--remove-installation " + Process.GetCurrentProcess().Id) { UseShellExecute = false, CreateNoWindow = true });
        }

        public static void Remove(int parentId)
        {
            try { using (var parent = Process.GetProcessById(parentId)) parent.WaitForExit(15000); }
            catch (ArgumentException) { }
            RequestQuit();
            foreach (var process in Process.GetProcessesByName("SoulsRevive"))
                using (process)
                    if (string.Equals(Updates.ProcessPath(process), Executable, StringComparison.OrdinalIgnoreCase)) process.WaitForExit(15000);
            using (var key = Registry.CurrentUser.CreateSubKey(RunKey)) key.DeleteValue("SoulsRevive", false);
            Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false);
            foreach (var path in ShortcutPaths)
                if (File.Exists(path) && string.Equals(ShortcutTarget(path), Executable, StringComparison.OrdinalIgnoreCase)) File.Delete(path);
            // Settings and statistics live next to the executable and are kept on purpose.
            foreach (string suffix in new[] { "", ".new", ".install-backup", ".update-backup", ".failed-update" })
                if (File.Exists(Executable + suffix)) File.Delete(Executable + suffix);
            MessageBox.Show(L.Text("Soul Revive kaldırıldı. Çalışma kayıtların saklandı.", "Soul Revive was removed. Your work history was kept."), "Soul Revive");
            // This helper runs from the temp folder; ask cmd to delete it shortly after it exits.
            try
            {
                Process.Start(new ProcessStartInfo("cmd.exe", "/d /c ping -n 3 127.0.0.1 >nul & del /f /q " + Updates.Quote(Application.ExecutablePath))
                    { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden });
            }
            catch (System.ComponentModel.Win32Exception) { }
        }
    }
    sealed class SetupDialog : Form
    {
        public SetupDialog()
        {
            Text = "Soul Revive · Setup"; ClientSize = new Size(500, 448);
            BackColor = Theme.Background; ForeColor = Theme.Text; Font = PixelFont.Create(22);
            FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen; Icon = PixelArt.MakeIcon();
            Controls.Add(new PixelLabel("SOUL / REVIVE", new Rectangle(24, 22, 452, 46), Theme.Gold, 23));
            var language = new PixelCombo { Bounds = new Rectangle(300, 78, 176, 36) };
            language.Items.AddRange(Ui.LanguageNames); language.SelectedIndex = 0; Controls.Add(language);
            var title = new PixelLabel("", new Rectangle(24, 80, 260, 30), Theme.Gold, 12); Controls.Add(title);
            var description = new PixelLabel("", new Rectangle(24, 130, 452, 84), Theme.Text, 11); Controls.Add(description);
            var desktop = new CheckBox { Checked = true, Bounds = new Rectangle(24, 234, 452, 32), FlatStyle = FlatStyle.Flat, UseCompatibleTextRendering = true }; Controls.Add(desktop);
            var startup = new CheckBox { Checked = true, Bounds = new Rectangle(24, 278, 452, 32), FlatStyle = FlatStyle.Flat, UseCompatibleTextRendering = true }; Controls.Add(startup);
            var status = new PixelLabel("", new Rectangle(24, 320, 452, 48), Theme.Muted, 9); Controls.Add(status);
            var install = new EmberButton("", new Rectangle(24, 386, 452, 40), true); Controls.Add(install);
            Action localize = delegate
            {
                L.Language = language.SelectedIndex == 1 ? "en" : "tr";
                title.Text = L.Text("ATEŞİNİ YAK.", "LIGHT YOUR FIRE.");
                description.Text = L.Text("Pixel şövalyen ve bonfire'ın masaüstüne geliyor.\nKişisel hesabına kurulur; yönetici izni gerekmez.\nSürüm ",
                    "A pixel knight and bonfire for your desktop.\nInstalls for your account; no admin rights needed.\nVersion ") + Updates.VersionText;
                desktop.Text = L.Text("Masaüstü kısayolu oluştur", "Create a desktop shortcut");
                startup.Text = L.Text("Windows açılışında tepside başlat", "Start in the tray at Windows sign-in");
                status.Text = L.Text("Mevcut kurulumda ayarlar ve başlangıç tercihi korunur.", "Upgrades preserve settings and startup preferences.");
                install.Text = L.Text("Kur ve başlat >", "Install and launch >");
                Invalidate(true);
            };
            language.SelectedIndexChanged += delegate { localize(); };
            localize();
            install.Click += delegate
            {
                install.Enabled = false;
                try
                {
                    Installation.Install(desktop.Checked, startup.Checked);
                    if (!File.Exists(Storage.SettingsPath)) Storage.Save(new Preferences { Language = L.Language });
                    Process.Start(Installation.Executable);
                    DialogResult = DialogResult.OK; Close();
                }
                catch (Exception ex) { status.Text = ex.Message; status.Invalidate(); install.Enabled = true; }
            };
            AcceptButton = install;
            Dpi.Apply(this);
        }
        protected override void Dispose(bool disposing) { if (disposing && Icon != null) Icon.Dispose(); base.Dispose(disposing); }
    }

    static class SetupProgram
    {
        [STAThread]
        static void Main(string[] args)
        {
            try
            {
                if (args.Length == 2 && args[0] == "--extract") { Installation.Extract(Path.GetFullPath(args[1])); return; }
                Dpi.Initialize();
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                PixelFont.Initialize();
                using (var form = new SetupDialog()) Application.Run(form);
            }
            catch (Exception ex)
            {
                if (args.Length > 0) { Environment.ExitCode = 1; return; }
                MessageBox.Show(ex.Message, "Soul Revive setup", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
