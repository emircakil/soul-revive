using System;
using System.Drawing;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Windows.Forms;

namespace SoulsRevive
{
    static class Tests
    {
        static int count;
        static readonly StringBuilder report=new StringBuilder();
        static void Check(bool ok,string name) {if(!ok)throw new Exception(name);count++;report.AppendLine("PASS "+name);}
        static void Reject(Action action,string name) {bool rejected=false;try{action();}catch{rejected=true;}Check(rejected,name);}
        static bool Near(double a,double b) {return Math.Abs(a-b)<.0001;}
        static string Json(string version,string url,string hash) {return "{\"tag_name\":\""+version+"\",\"assets\":[{\"name\":\"SoulsRevive.exe\",\"browser_download_url\":\""+url+"\",\"size\":5000,\"digest\":\"sha256:"+hash+"\"}]}";}
        public static void Run(string output)
        {
            Directory.CreateDirectory(output);string failure=Path.Combine(output,"failure.txt");if(File.Exists(failure))File.Delete(failure);
            Storage.DirectoryPath=Path.Combine(output,"persistence");
            var p=new Preferences();var e=new TimerEngine(p);int due=0,revived=0;
            e.Changed+=delegate(string s){if(s=="due")due++;if(s=="revived")revived++;};
            Check(Near(DayCycle.Hour(p,DateTime.Now),5.5),"work begins at dawn");
            for(int i=0;i<1800;i++)e.Advance(1);
            Check(e.Due&&due==1&&e.Health==0,"work reaches zero and notifies once");
            Check(Near(p.TotalWorkSeconds,1800)&&p.CompletedSessions==1,"active work and completed sessions counted");
            Check(Near(DayCycle.Hour(p,DateTime.Now),20.5),"work ends after dark");
            e.Advance(1);Check(due==1&&p.TotalWorkSeconds==1800,"expired timer neither repeats nor accrues work");
            e.BeginBreak();Check(DayCycle.Hour(p,DateTime.Now)==22,"break scene is night");
            e.Advance(5);e.TogglePause();double before=p.Remaining;e.Advance(2);Check(p.Remaining==before,"pause freezes time");
            e.TogglePause();e.Advance(3600);Check(p.Remaining==before,"sleep gap excluded");
            e.Advance(double.NaN);e.Advance(double.PositiveInfinity);Check(p.Remaining==before,"nonfinite elapsed time excluded");
            for(int i=0;i<175;i++)e.Advance(1);
            Check(!p.InBreak&&p.Breaks==1&&revived==1&&p.Remaining==1800,"completed break starts next work round");
            Check(p.MovementSeconds==180&&p.TotalWorkSeconds==1800,"break time excluded from work stats");
            p.Remaining=0;e.BeginBreak();e.Advance(2);e.CancelBreak();Check(!e.Due&&p.Remaining==1800&&p.Breaks==1,"early return starts full round without awarding break");
            e.Advance(1);Check(p.Remaining==1799,"new round counts down");
            e.SetChill(true);before=p.TotalWorkSeconds;e.Advance(5);e.BeginBreak();e.TogglePause();
            Check(p.Chill&&!p.InBreak&&!p.Paused&&p.TotalWorkSeconds==before&&!e.Due,"chill disables timers, breaks, and work accumulation");
            Check(Near(DayCycle.Hour(p,DateTime.Today.AddHours(14.5)),14.5),"chill uses local PC clock");
            e.SetChill(false);Check(p.Remaining==1800&&!p.Paused,"return from chill starts a fresh dawn");
            e.ApplyDurations(45,5);Check(p.Remaining==2700&&!p.InBreak,"duration change resets cycle");
            p.Paused=true;e.ApplyDurations(45,5);Check(p.Paused,"unrelated settings preserve pause");
            p.Day="2000-01-01";p.TodayWorkSeconds=900;p.CheckDay();Check(p.Breaks==0&&p.MovementSeconds==0&&p.TodayWorkSeconds==0&&p.TotalWorkSeconds==before,"daily rollover preserves lifetime work");
            p.Paused=false;p.Day=DateTime.Today.AddDays(-1).ToString("yyyy-MM-dd");e.Advance(.5,DateTime.Today.AddMilliseconds(100));
            Check(Near(p.TodayWorkSeconds,.1)&&Near(p.TotalWorkSeconds,before+.5),"midnight tick apportions daily and lifetime totals");
            p.WorkMinutes=0;p.BreakMinutes=999;p.Remaining=double.NaN;p.FireVolume=-9;p.MusicVolume=999;p.TotalWorkSeconds=double.PositiveInfinity;p.Validate();
            Check(p.WorkMinutes==1&&p.BreakMinutes==30&&p.Remaining==60&&p.FireVolume==0&&p.MusicVolume==100&&p.TotalWorkSeconds==0,"invalid settings constrained");
            using(var ms=new MemoryStream(Encoding.UTF8.GetBytes("{\"WorkMinutes\":30,\"BreakMinutes\":3,\"Remaining\":900,\"Breaks\":2}")))
            {var legacy=(Preferences)new DataContractJsonSerializer(typeof(Preferences)).ReadObject(ms);legacy.Validate();Check(legacy.FireVolume==35&&legacy.Language=="tr"&&legacy.TotalWorkSeconds==0&&legacy.Remaining==900,"old settings migrate with sensible defaults");}
            p.Language="en";p.X=150;p.Y=160;p.TotalWorkSeconds=12456;p.TodayWorkSeconds=321;p.Chill=true;p.LightMode=true;p.AudioEnabled=true;p.FireVolume=71;p.AmbienceVolume=43;p.MusicVolume=19;
            Check(Storage.Save(p),"settings saved atomically");var loaded=Storage.Load();
            Check(loaded.TotalWorkSeconds==12456&&loaded.TodayWorkSeconds==321&&loaded.Chill&&loaded.LightMode&&loaded.AudioEnabled&&loaded.FireVolume==71&&loaded.AmbienceVolume==43&&loaded.MusicVolume==19,"new preferences and statistics survive restart");
            Check(loaded.X==150&&loaded.Y==160&&loaded.Remaining==60,"position and timer preserved");
            using(var localized=new Companion(loaded,true))using(var settings=new SettingsDialog(loaded)) {Check(settings.Language=="en"&&settings.Text.Contains("Settings"),"English settings survive restart");Check(localized.Controls[0].Text.Contains("Take a break"),"English action button");}
            p.TotalWorkSeconds=15000;Check(Storage.Save(p),"second save creates backup");File.WriteAllText(Storage.SettingsPath,"broken json");Check(Storage.Load().TotalWorkSeconds==12456,"corrupt settings recover previous backup");
            Check(Companion.TimeText(.1)=="00:01"&&Companion.TimeText(3600)=="60:00","timer formatting");
            string zeros=new string('0',64),url=Updates.Repository+"/releases/download/v2.0.0/SoulsRevive.exe";
            Check(Updates.Parse(Json("v2.0.0",url,zeros)).Version==new Version(2,0,0,0),"release parser selects verified application asset");
            Check(Updates.Parse("{\"tag_name\":\"v1.1.0\"}")==null&&Updates.Parse("{\"tag_name\":\"v1.0.0\"}")==null,"equal and older releases never downgrade");
            Reject(delegate{Updates.Parse(Json("v2.0.0","http://github.com/emircakil/soul-revive/releases/download/bad",zeros));},"updater rejects insecure transport");
            Reject(delegate{Updates.Parse(Json("v2.0.0","https://github.com/other/project/releases/download/bad",zeros));},"updater restricts repository");
            Reject(delegate{Updates.Parse(Json("v2.0.0",url,"bad"));},"updater requires SHA-256 digest");
            Reject(delegate{Updates.Parse("{\"tag_name\":\"v2.0.0\",\"prerelease\":true}");},"prereleases are excluded");
            string payload=Path.Combine(output,"payload.exe");File.Copy(Application.ExecutablePath,payload,true);
            var info=new UpdateInfo{Version=Updates.Current,Asset=new ReleaseAsset {size=new FileInfo(payload).Length,digest="sha256:"+Updates.Hash(payload)}};
            Updates.Verify(payload,info);Check(true,"download size, hash and assembly version verified");
            info.Asset.digest="sha256:"+zeros;Reject(delegate{Updates.Verify(payload,info);},"tampered downloads rejected before application closes");
            string target=Path.Combine(output,"replace-target.txt"),replacement=Path.Combine(output,"replace-new.txt");File.WriteAllText(target,"old");File.WriteAllText(replacement,"new");
            Updates.ReplaceWithRollback(replacement,target);Check(File.ReadAllText(target)=="new"&&File.ReadAllText(target+".update-backup")=="old","atomic replacement keeps rollback copy");
            using(var locked=new FileStream(target,FileMode.Open,FileAccess.Read,FileShare.None))Reject(delegate{Updates.ReplaceWithRollback(replacement,target);},"locked update target fails safely");
            Check(File.ReadAllText(target)=="new","failed replacement leaves original intact");
            byte[] mute=Soundscape.Render(0,0,0,1),fire=Soundscape.Render(80,0,0,1),air=Soundscape.Render(0,80,0,1),music=Soundscape.Render(0,0,80,1);
            Check(mute.Length==32044&&Encoding.ASCII.GetString(mute,0,4)=="RIFF","soundscape produces valid PCM WAV");
            bool silent=true;for(int i=44;i<mute.Length;i++)if(mute[i]!=0)silent=false;Check(silent,"all volume controls at zero produce silence");
            Check(!Equal(fire,air)&&!Equal(air,music)&&!Equal(music,mute),"fire, ambience and music are independent layers");
            File.WriteAllBytes(Path.Combine(output,"soundscape.wav"),Soundscape.Render(35,25,35,8));
            using(var dawn=PixelArt.Scene(1,false,0,5.5,false))using(var noon=PixelArt.Scene(.5,false,0,12,false))using(var night=PixelArt.Scene(0,false,0,20.5,false))
            {Check(dawn.GetPixel(0,0)!=noon.GetPixel(0,0)&&noon.GetPixel(0,0)!=night.GetPixel(0,0),"dawn, daylight and night have distinct skies");dawn.Save(Path.Combine(output,"dawn.png"));noon.Save(Path.Combine(output,"day.png"));night.Save(Path.Combine(output,"night.png"));}
            using(var rest=PixelArt.Scene(1,true,0,22,false))using(var walk=PixelArt.Scene(1,false,0,12,true))using(var chill=PixelArt.Scene(1,false,180,12,true))
            {Check(rest.GetPixel(76,63)!=walk.GetPixel(76,63)||rest.GetPixel(80,70)!=walk.GetPixel(80,70),"break uses distinct sleeping pose");rest.Save(Path.Combine(output,"sleep.png"));walk.Save(Path.Combine(output,"chill-walk.png"));chill.Save(Path.Combine(output,"chill-rest.png"));}
            foreach(bool light in new[]{false,true})foreach(string lang in new[]{"tr","en"})
            {
                var pref=new Preferences{Language=lang,LightMode=light,Day=DateTime.Now.ToString("yyyy-MM-dd"),TotalWorkSeconds=14736,TodayWorkSeconds=7524,CompletedSessions=8,Breaks=3};
                string suffix=(light?"lit-":"dark-")+lang;
                using(var form=new Companion(pref,true))Render(form,output,"main-"+suffix);
                using(var dialog=new SettingsDialog(pref))for(int tab=0;tab<3;tab++){dialog.ShowSection(tab);Render(dialog,output,"settings-"+tab+"-"+suffix);}
                using(var dialog=new StatsDialog(pref))Render(dialog,output,"stats-"+suffix);
                pref.InBreak=true;pref.Remaining=180;using(var form=new Companion(pref,true))Render(form,output,"break-"+suffix);
                pref.InBreak=false;pref.Chill=true;using(var form=new Companion(pref,true))Render(form,output,"chill-"+suffix);
            }
            Theme.Light=false;L.Language="tr";using(var setup=new SetupDialog())Render(setup,output,"setup");
            Check(PixelFont.Family.Name=="Tiny5","bundled pixel font loaded without system installation");
            report.AppendLine(count+" checks passed.");File.WriteAllText(Path.Combine(output,"results.txt"),report.ToString());
        }
        static bool Equal(byte[] a,byte[] b) {if(a.Length!=b.Length)return false;for(int i=0;i<a.Length;i++)if(a[i]!=b[i])return false;return true;}
        static void Render(Form form,string output,string name)
        {form.Show();Application.DoEvents();using(var b=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(b,new Rectangle(0,0,b.Width,b.Height));b.Save(Path.Combine(output,name+".png"));}form.Hide();}
    }
}
