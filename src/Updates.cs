using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Reflection;
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
        public static Version Current { get { return Assembly.GetExecutingAssembly().GetName().Version; } }
        public static string VersionText { get { return Current.ToString(3); } }
        static HttpWebRequest Request(string url)
        {
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
            var request=(HttpWebRequest)WebRequest.Create(url);
            request.UserAgent="SoulRevive/"+VersionText; request.Accept="application/vnd.github+json";
            request.Timeout=20000; request.ReadWriteTimeout=20000; return request;
        }
        public static UpdateInfo Check()
        {
            try
            {
                using(var response=Request("https://api.github.com/repos/emircakil/soul-revive/releases/latest").GetResponse())
                using(var stream=response.GetResponseStream())
                using(var reader=new StreamReader(stream)) return Parse(reader.ReadToEnd());
            }
            catch(WebException ex)
            {
                var response=ex.Response as HttpWebResponse;
                if(response!=null)
                {
                    var code=response.StatusCode; response.Dispose();
                    if(code==HttpStatusCode.NotFound) throw new InvalidOperationException(L.Text("Henüz yayımlanmış bir sürüm yok. İlk GitHub Release sonrasında buradan güncelleyebilirsin.","No release has been published yet. Updates will appear here after the first GitHub Release."));
                    if(code==HttpStatusCode.Forbidden || (int)code==429) throw new InvalidOperationException(L.Text("GitHub sorgu sınırına ulaşıldı. Biraz sonra tekrar dene.","GitHub rate limit reached. Try again later."));
                }
                throw new InvalidOperationException(L.Text("Güncelleme sunucusuna ulaşılamadı. Bağlantını kontrol edip tekrar dene.","Could not reach the update server. Check your connection and try again."),ex);
            }
        }
        public static UpdateInfo Parse(string json)
        {
            Release release;
            using(var ms=new MemoryStream(Encoding.UTF8.GetBytes(json))) release=(Release)new DataContractJsonSerializer(typeof(Release)).ReadObject(ms);
            Version version;
            if(release==null || release.draft || release.prerelease || string.IsNullOrEmpty(release.tag_name) || !Version.TryParse(release.tag_name.TrimStart('v','V'),out version))
                throw new InvalidOperationException(L.Text("Sürüm bilgisi geçersiz.","Invalid release information."));
            version=Normalize(version);
            if(version<=Current) return null;
            foreach(var asset in release.assets ?? new ReleaseAsset[0])
                if(asset.name=="SoulsRevive.exe")
                {
                    Uri uri;
                    string prefix=Repository+"/releases/download/";
                    if(!Uri.TryCreate(asset.browser_download_url,UriKind.Absolute,out uri) || uri.Scheme!="https" || uri.Host!="github.com" || uri.Port!=443 || uri.UserInfo!="" || !uri.AbsoluteUri.StartsWith(prefix,StringComparison.Ordinal)) throw new InvalidOperationException("Invalid update URL.");
                    if(asset.size<4096 || asset.size>64*1024*1024 || asset.digest==null || !System.Text.RegularExpressions.Regex.IsMatch(asset.digest,@"\Asha256:[0-9a-fA-F]{64}\z"))
                        throw new InvalidOperationException(L.Text("Yayın dosyası veya SHA-256 doğrulaması eksik. Yayıncı dosyayı yeniden yüklemeli.","The release asset or SHA-256 digest is missing. The publisher must upload a complete release."));
                    return new UpdateInfo {Version=version,Asset=asset};
                }
            throw new InvalidOperationException(L.Text("Bu sürümde SoulsRevive.exe bulunamadı.","This release is missing SoulsRevive.exe."));
        }
        static Version Normalize(Version v) { return new Version(v.Major,v.Minor,Math.Max(0,v.Build),Math.Max(0,v.Revision)); }
        public static string Hash(string file)
        { using(var sha=SHA256.Create()) using(var f=File.OpenRead(file)) return BitConverter.ToString(sha.ComputeHash(f)).Replace("-","").ToLowerInvariant(); }
        public static void Verify(string file,UpdateInfo update)
        {
            if(new FileInfo(file).Length!=update.Asset.size || !string.Equals(Hash(file),update.Asset.digest.Substring(7),StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException(L.Text("İndirilen dosya doğrulanamadı. Mevcut sürüm korunuyor.","The download failed verification. Your current version is preserved."));
            if(Normalize(AssemblyName.GetAssemblyName(file).Version)!=update.Version) throw new InvalidDataException("Release and executable versions do not match.");
        }
        public static void DownloadAndStage(UpdateInfo update,Action<int> progress)
        {
            string dir=Path.Combine(Path.GetTempPath(),"SoulRevive-update-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
            string payload=Path.Combine(dir,"SoulsRevive.new.exe"), helper=Path.Combine(dir,"SoulRevive.Updater.exe");
            try
            {
                using(var response=Request(update.Asset.browser_download_url).GetResponse())
                using(var input=response.GetResponseStream())
                using(var output=File.Create(payload))
                {
                    byte[] buffer=new byte[65536]; int n; long total=0;
                    while((n=input.Read(buffer,0,buffer.Length))>0)
                    {
                        total+=n; if(total>update.Asset.size) throw new InvalidDataException("Download exceeds expected size.");
                        output.Write(buffer,0,n); if(progress!=null)progress((int)(total*100/update.Asset.size));
                    }
                }
                Verify(payload,update);
                File.Copy(System.Windows.Forms.Application.ExecutablePath,helper);
                // Preflight write access before asking the running companion to close.
                string probe=Path.Combine(Path.GetDirectoryName(System.Windows.Forms.Application.ExecutablePath),".update-"+Guid.NewGuid().ToString("N"));
                using(File.Create(probe)) {} File.Delete(probe);
                string readyName=@"Local\SoulsRevive.Update."+Guid.NewGuid().ToString("N");
                using(var ready=new System.Threading.EventWaitHandle(false,System.Threading.EventResetMode.ManualReset,readyName))
                {
                    var start=new ProcessStartInfo(helper,"--apply-update "+Process.GetCurrentProcess().Id+" "+Quote(System.Windows.Forms.Application.ExecutablePath)+" "+Quote(payload)+" "+update.Asset.digest.Substring(7)+" "+Quote(readyName));
                    start.UseShellExecute=false; start.CreateNoWindow=true;
                    using(var process=Process.Start(start)) if(!ready.WaitOne(15000))throw new TimeoutException("The update helper did not start. Your current version is preserved.");
                }
            }
            catch { if(File.Exists(payload))File.Delete(payload); throw; }
        }
        internal static string Quote(string value) { if(value.Contains("\""))throw new ArgumentException("Invalid path."); return "\""+value+"\""; }
        public static void Apply(string[] args)
        {
            if(args.Length!=6)throw new ArgumentException("Invalid update arguments.");
            int pid=int.Parse(args[1]); string target=Path.GetFullPath(args[2]),payload=Path.GetFullPath(args[3]);
            using(var parent=Process.GetProcessById(pid))
            {
                if(!string.Equals(parent.MainModule.FileName,target,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Update target mismatch.");
                using(var ready=System.Threading.EventWaitHandle.OpenExisting(args[5]))ready.Set();
                if(!parent.WaitForExit(45000))throw new TimeoutException("The companion is still running. Please retry the update.");
            }
            if(!string.Equals(Hash(payload),args[4],StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Update integrity check failed.");
            ReplaceWithRollback(payload,target);
            try {Process.Start(new ProcessStartInfo(target) {UseShellExecute=true});}
            catch
            {
                string backup=target+".update-backup";
                if(File.Exists(backup)) {File.Replace(backup,target,target+".failed-update");Process.Start(new ProcessStartInfo(target){UseShellExecute=true});}
                throw;
            }
            try {using(var key=Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\SoulsRevive",true))if(key!=null&&string.Equals(key.GetValue("InstallLocation") as string,Path.GetDirectoryName(target),StringComparison.OrdinalIgnoreCase))key.SetValue("DisplayVersion",AssemblyName.GetAssemblyName(target).Version.ToString(3));}catch(System.Security.SecurityException) {}catch(UnauthorizedAccessException) {}
            File.Delete(payload);
            // The running helper is left in the OS temp folder; the executable and user data stay separate.
        }
        internal static void ReplaceWithRollback(string payload,string target)
        {
            string staged=target+".new",backup=target+".update-backup";
            File.Copy(payload,staged,true);
            try { File.Replace(staged,target,backup); }
            catch { if(File.Exists(staged))File.Delete(staged); throw; }
        }
    }
}
