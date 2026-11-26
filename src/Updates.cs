using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;

namespace SoulsRevive
{
    [DataContract] sealed class ReleaseAsset
    {
        [DataMember] public string name {get;set;}
        [DataMember] public string browser_download_url {get;set;}
        [DataMember] public string digest {get;set;}
        [DataMember] public long size {get;set;}
    }
    [DataContract] sealed class Release
    {
        [DataMember] public string tag_name {get;set;}
        [DataMember] public bool draft {get;set;}
        [DataMember] public bool prerelease {get;set;}
        [DataMember] public ReleaseAsset[] assets {get;set;}
    }
    sealed class UpdateInfo
    {
        public Version Version;
        public ReleaseAsset Asset;
    }
    static class Updates
    {
        public const string Repository = "https://github.com/emircakil/soul-revive";
        const string LatestApi = "https://api.github.com/repos/emircakil/soul-revive/releases/latest";
        static readonly System.Text.RegularExpressions.Regex DigestPattern = new System.Text.RegularExpressions.Regex(@"\Asha256:[0-9a-fA-F]{64}\z");
        public static Version Current { get { return Assembly.GetExecutingAssembly().GetName().Version; } }
        public static string VersionText { get { return Current.ToString(3); } }
        static string ExecutablePath { get { return System.Windows.Forms.Application.ExecutablePath; } }

        static HttpWebRequest Request(string url)
        {
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; // TLS 1.2
            var request = (HttpWebRequest)WebRequest.Create(url);
            request.UserAgent = "SoulRevive/" + VersionText;
            request.Accept = "application/vnd.github+json";
            request.Timeout = 20000;
            request.ReadWriteTimeout = 20000;
            return request;
        }

        public static UpdateInfo Check()
        {
            try
            {
                using (var response = Request(LatestApi).GetResponse())
                using (var stream = response.GetResponseStream())
                using (var reader = new StreamReader(stream))
                    return Parse(reader.ReadToEnd());
            }
            catch (WebException ex)
            {
                var response = ex.Response as HttpWebResponse;
                if (response != null)
                {
                    var code = response.StatusCode;
                    response.Dispose();
                    if (code == HttpStatusCode.NotFound)
                        throw new InvalidOperationException(L.Text("Henüz yayımlanmış bir sürüm yok. İlk GitHub Release sonrasında buradan güncelleyebilirsin.",
                            "No release has been published yet. Updates will appear here after the first GitHub Release."));
                    if (code == HttpStatusCode.Forbidden || (int)code == 429)
                        throw new InvalidOperationException(L.Text("GitHub sorgu sınırına ulaşıldı. Biraz sonra tekrar dene.", "GitHub rate limit reached. Try again later."));
                }
                throw new InvalidOperationException(L.Text("Güncelleme sunucusuna ulaşılamadı. Bağlantını kontrol edip tekrar dene.",
                    "Could not reach the update server. Check your connection and try again."), ex);
            }
        }

        public static UpdateInfo Parse(string json)
        {
            Release release;
            using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                release = (Release)new DataContractJsonSerializer(typeof(Release)).ReadObject(ms);
            Version version;
            if (release == null || release.draft || release.prerelease || string.IsNullOrEmpty(release.tag_name)
                || !Version.TryParse(release.tag_name.TrimStart('v', 'V'), out version))
                throw new InvalidOperationException(L.Text("Sürüm bilgisi geçersiz.", "Invalid release information."));
            version = Normalize(version);
            if (version <= Current) return null;
            foreach (var asset in release.assets ?? Array.Empty<ReleaseAsset>())
            {
                if (asset.name != "SoulsRevive.exe") continue;
                Uri uri;
                string prefix = Repository + "/releases/download/";
                if (!Uri.TryCreate(asset.browser_download_url, UriKind.Absolute, out uri) || uri.Scheme != "https" || uri.Host != "github.com"
                    || uri.Port != 443 || uri.UserInfo != "" || !uri.AbsoluteUri.StartsWith(prefix, StringComparison.Ordinal))
                    throw new InvalidOperationException("Invalid update URL.");
                if (asset.size < 4096 || asset.size > 64 * 1024 * 1024 || asset.digest == null || !DigestPattern.IsMatch(asset.digest))
                    throw new InvalidOperationException(L.Text("Yayın dosyası veya SHA-256 doğrulaması eksik. Yayıncı dosyayı yeniden yüklemeli.",
                        "The release asset or SHA-256 digest is missing. The publisher must upload a complete release."));
                return new UpdateInfo { Version = version, Asset = asset };
            }
            throw new InvalidOperationException(L.Text("Bu sürümde SoulsRevive.exe bulunamadı.", "This release is missing SoulsRevive.exe."));
        }

        static Version Normalize(Version v) { return new Version(v.Major, v.Minor, Math.Max(0, v.Build), Math.Max(0, v.Revision)); }

        public static string Hash(string file)
        {
            using (var sha = SHA256.Create())
            using (var f = File.OpenRead(file))
                return BitConverter.ToString(sha.ComputeHash(f)).Replace("-", "").ToLowerInvariant();
        }

        public static void Verify(string file, UpdateInfo update)
        {
            if (new FileInfo(file).Length != update.Asset.size || !string.Equals(Hash(file), update.Asset.digest.Substring(7), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(L.Text("İndirilen dosya doğrulanamadı. Mevcut sürüm korunuyor.", "The download failed verification. Your current version is preserved."));
            if (Normalize(AssemblyName.GetAssemblyName(file).Version) != update.Version)
                throw new InvalidDataException("Release and executable versions do not match.");
            // Once releases are Authenticode signed, an update must carry a valid signature from the same publisher.
            string publisher = Signature.Publisher(ExecutablePath);
            if (publisher != null && !string.Equals(publisher, Signature.Publisher(file), StringComparison.Ordinal))
                throw new InvalidDataException(L.Text("Güncellemenin dijital imzası yayıncıyla eşleşmiyor. Mevcut sürüm korunuyor.",
                    "The update's digital signature does not match the publisher. Your current version is preserved."));
        }

        /// <summary>Executable path of a process, or null when Windows does not allow reading it.</summary>
        internal static string ProcessPath(Process process)
        {
            try { return process.MainModule.FileName; }
            catch (System.ComponentModel.Win32Exception) { return null; }
            catch (InvalidOperationException) { return null; }
            catch (NotSupportedException) { return null; }
        }

        /// <summary>Removes updater and uninstaller copies left in the temp folder by earlier runs.</summary>
        public static void CleanupTemporaryFiles()
        {
            try
            {
                var cutoff = DateTime.UtcNow.AddMinutes(-10);
                var temp = new DirectoryInfo(Path.GetTempPath());
                foreach (var dir in temp.EnumerateDirectories("SoulRevive-update-*"))
                    try { if (dir.LastWriteTimeUtc < cutoff) dir.Delete(true); }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                foreach (var file in temp.EnumerateFiles("SoulRevive-remove-*.exe"))
                    try { if (file.LastWriteTimeUtc < cutoff) file.Delete(); }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (System.Security.SecurityException) { }
        }

        public static void DownloadAndStage(UpdateInfo update, Action<int> progress)
        {
            string dir = Path.Combine(Path.GetTempPath(), "SoulRevive-update-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            string payload = Path.Combine(dir, "SoulsRevive.new.exe"), helper = Path.Combine(dir, "SoulRevive.Updater.exe");
            try
            {
                using (var response = Request(update.Asset.browser_download_url).GetResponse())
                using (var input = response.GetResponseStream())
                using (var output = File.Create(payload))
                {
                    byte[] buffer = new byte[65536];
                    int n;
                    long total = 0;
                    while ((n = input.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        total += n;
                        if (total > update.Asset.size) throw new InvalidDataException("Download exceeds expected size.");
                        output.Write(buffer, 0, n);
                        if (progress != null) progress((int)(total * 100 / update.Asset.size));
                    }
                }
                Verify(payload, update);
                File.Copy(ExecutablePath, helper);
                // Preflight write access before asking the running companion to close.
                string probe = Path.Combine(Path.GetDirectoryName(ExecutablePath), ".update-" + Guid.NewGuid().ToString("N"));
                using (File.Create(probe)) { }
                File.Delete(probe);
                string readyName = @"Local\SoulsRevive.Update." + Guid.NewGuid().ToString("N");
                using (var ready = new System.Threading.EventWaitHandle(false, System.Threading.EventResetMode.ManualReset, readyName))
                {
                    string profileArgument = string.Equals(Path.GetFullPath(Storage.DirectoryPath), Storage.DefaultDirectoryPath, StringComparison.OrdinalIgnoreCase)
                        ? "" : " " + Quote(Storage.DirectoryPath);
                    var start = new ProcessStartInfo(helper, "--apply-update " + Process.GetCurrentProcess().Id + " " + Quote(ExecutablePath) + " " + Quote(payload)
                        + " " + update.Asset.digest.Substring(7) + " " + Quote(readyName) + profileArgument)
                    { UseShellExecute = false, CreateNoWindow = true };
                    using (Process.Start(start))
                        if (!ready.WaitOne(15000)) throw new TimeoutException("The update helper did not start. Your current version is preserved.");
                }
            }
            catch
            {
                if (File.Exists(payload)) File.Delete(payload);
                throw;
            }
        }

        internal static string Quote(string value)
        {
            if (value.Contains("\"")) throw new ArgumentException("Invalid path.");
            return "\"" + value + "\"";
        }

        public static void Apply(string[] args)
        {
            if (args.Length != 6 && args.Length != 7) throw new ArgumentException("Invalid update arguments.");
            string restartArguments = args.Length == 7 ? "--profile " + Quote(Path.GetFullPath(args[6])) : "";
            int pid = int.Parse(args[1]);
            string target = Path.GetFullPath(args[2]), payload = Path.GetFullPath(args[3]);
            using (var parent = Process.GetProcessById(pid))
            {
                if (!string.Equals(ProcessPath(parent), target, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Update target mismatch.");
                using (var ready = System.Threading.EventWaitHandle.OpenExisting(args[5])) ready.Set();
                if (!parent.WaitForExit(45000)) throw new TimeoutException("The companion is still running. Please retry the update.");
            }
            if (!string.Equals(Hash(payload), args[4], StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Update integrity check failed.");
            ReplaceWithRollback(payload, target);
            try { Process.Start(new ProcessStartInfo(target, restartArguments) { UseShellExecute = true }); }
            catch
            {
                string backup = target + ".update-backup";
                if (File.Exists(backup))
                {
                    File.Replace(backup, target, target + ".failed-update");
                    Process.Start(new ProcessStartInfo(target, restartArguments) { UseShellExecute = true });
                }
                throw;
            }
            try
            {
                using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\SoulsRevive", true))
                    if (key != null && string.Equals(key.GetValue("InstallLocation") as string, Path.GetDirectoryName(target), StringComparison.OrdinalIgnoreCase))
                        key.SetValue("DisplayVersion", AssemblyName.GetAssemblyName(target).Version.ToString(3));
            }
            catch (System.Security.SecurityException) { }
            catch (UnauthorizedAccessException) { }
            File.Delete(payload);
            // This helper still runs from the temp folder; the restarted app removes it on a later start.
        }

        internal static void ReplaceWithRollback(string payload, string target)
        {
            string staged = target + ".new", backup = target + ".update-backup";
            File.Copy(payload, staged, true);
            try { File.Replace(staged, target, backup); }
            catch
            {
                if (File.Exists(staged)) File.Delete(staged);
                throw;
            }
        }
    }

    /// <summary>Authenticode verification through WinVerifyTrust (no UI, no network revocation fetch).</summary>
    static class Signature
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct TrustFileInfo { public uint Size; [MarshalAs(UnmanagedType.LPWStr)] public string Path; public IntPtr File; public IntPtr KnownSubject; }
        [StructLayout(LayoutKind.Sequential)]
        struct TrustData
        {
            public uint Size; public IntPtr PolicyCallbackData; public IntPtr SipClientData; public uint UiChoice; public uint RevocationChecks;
            public uint UnionChoice; public IntPtr FileInfo; public uint StateAction; public IntPtr StateData; public IntPtr UrlReference;
            public uint ProviderFlags; public uint UiContext; public IntPtr SignatureSettings;
        }
        [DllImport("wintrust.dll", CharSet = CharSet.Unicode)] static extern int WinVerifyTrust(IntPtr window, ref Guid action, ref TrustData data);
        static readonly Guid GenericVerifyV2 = new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

        public static bool IsTrusted(string path)
        {
            var file = new TrustFileInfo { Size = (uint)Marshal.SizeOf<TrustFileInfo>(), Path = path };
            IntPtr fileInfo = Marshal.AllocHGlobal(Marshal.SizeOf<TrustFileInfo>());
            try
            {
                Marshal.StructureToPtr(file, fileInfo, false);
                // WTD_UI_NONE, WTD_REVOKE_NONE, WTD_CHOICE_FILE, WTD_CACHE_ONLY_URL_RETRIEVAL.
                var data = new TrustData { Size = (uint)Marshal.SizeOf<TrustData>(), UiChoice = 2, RevocationChecks = 0, UnionChoice = 1, FileInfo = fileInfo, ProviderFlags = 0x1000 };
                Guid action = GenericVerifyV2;
                return WinVerifyTrust(IntPtr.Zero, ref action, ref data) == 0;
            }
            catch (EntryPointNotFoundException) { return false; }
            catch (DllNotFoundException) { return false; }
            finally { Marshal.DestroyStructure<TrustFileInfo>(fileInfo); Marshal.FreeHGlobal(fileInfo); }
        }
        /// <summary>Subject of a valid Authenticode signer, or null when the file is unsigned or invalid.</summary>
        public static string Publisher(string path)
        {
            if (!IsTrusted(path)) return null;
            try { return new System.Security.Cryptography.X509Certificates.X509Certificate2(System.Security.Cryptography.X509Certificates.X509Certificate.CreateFromSignedFile(path)).Subject; }
            catch (System.Security.Cryptography.CryptographicException) { return null; }
        }
    }
}
