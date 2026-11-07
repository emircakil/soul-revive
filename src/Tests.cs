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
            Storage.DirectoryPath=Path.Combine(output,"persistence-"+Guid.NewGuid().ToString("N"));
            var p=new Preferences();var e=new TimerEngine(p);int due=0,revived=0;
            e.Changed+=delegate(string s){if(s=="due")due++;if(s=="revived")revived++;};
            Check(Near(DayCycle.Hour(p),5.5),"work begins at dawn");
            for(int i=0;i<1800;i++)e.Advance(1);
            Check(e.Due&&due==1&&e.Health==0,"work reaches zero and notifies once");
            Check(Near(p.TotalWorkSeconds,1800)&&p.CompletedSessions==1,"active work and completed sessions counted");
            Check(Near(DayCycle.Hour(p),20.5),"work ends after dark");
            e.Advance(1);Check(due==1&&p.TotalWorkSeconds==1800,"expired timer neither repeats nor accrues work");
            e.BeginBreak();Check(DayCycle.Hour(p)==22,"break scene is night");
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
            p.TotalWorkSeconds=15000;Check(Storage.Save(p),"second save creates backup");File.WriteAllText(Storage.SettingsPath,"broken json");Check(Storage.Load().TotalWorkSeconds==15000,"corrupt settings preserve latest independent statistics");
            File.WriteAllText(Storage.SettingsPath + ".bak", "broken backup");
            loaded = Storage.Load();
            Check(loaded.TotalWorkSeconds == 15000 && loaded.TodayWorkSeconds == 321, "statistics survive complete settings recovery");
            loaded.TotalWorkSeconds = 0; loaded.TodayWorkSeconds = 0;
            Check(Storage.Save(loaded) && loaded.TotalWorkSeconds == 15000 && loaded.TodayWorkSeconds == 321, "saving reset preferences cannot erase accumulated work");
            loaded.Day = DateTime.Today.AddDays(-1).ToString("yyyy-MM-dd");
            Storage.Save(loaded);
            Check(Storage.Load().TodayWorkSeconds == 321, "same-day restart restores daily work");
            File.WriteAllText(Storage.SettingsPath, "broken settings");
            File.WriteAllText(Storage.SettingsPath + ".bak", "broken backup");
            File.WriteAllText(Storage.StatisticsPath, "broken statistics");
            loaded = Storage.Load();
            Check(loaded.TotalWorkSeconds == 15000 && loaded.TodayWorkSeconds == 321, "statistics backup restores work when settings and primary statistics fail");
            var restarted = new TimerEngine(loaded);
            restarted.Advance(1, DateTime.Today.AddHours(12));
            Check(loaded.TotalWorkSeconds == 15001 && loaded.TodayWorkSeconds == 322, "restarted timer continues accumulating saved work");
            Check(StatsDialog.Duration(3661) == "01:01:01" && StatsDialog.Duration(360001) == "100:00:01", "duration retains seconds and lifetime hours above 24");
            Check(Companion.TimeText(.1)=="00:01"&&Companion.TimeText(3600)=="60:00","timer formatting");
            string zeros=new string('0',64),url=Updates.Repository+"/releases/download/v2.0.0/SoulsRevive.exe";
            Check(Updates.Parse(Json("v2.0.0",url,zeros)).Version==new Version(2,0,0,0),"release parser selects verified application asset");
            Check(Updates.Parse("{\"tag_name\":\"v"+Updates.VersionText+"\"}")==null&&Updates.Parse("{\"tag_name\":\"v1.1.0\"}")==null,"equal and older releases never downgrade");
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
            using(var rest=PixelArt.Scene(1,true,0,22,false))using(var work=PixelArt.Scene(1,false,0,12,false))
            {Check(!SamePixels(rest,work),"break keeps its sleeping pose outside the tavern");rest.Save(Path.Combine(output,"sleep.png"));}
            var routine=new WorkRoutine(new Random(42));
            bool[] seen=new bool[4];bool cadence=true;
            for(int tick=0;tick<4000;tick++)
            {
                routine.Advance(true);seen[(int)routine.Action]=true;
                if(routine.Frame<0 || routine.Frame>=WorkRoutine.Duration)cadence=false;
            }
            Check(seen[0]&&seen[1]&&seen[2]&&seen[3]&&cadence,"random work routine visits all tasks and idle within valid frame bounds");
            var frozenAction=routine.Action;int frozenFrame=routine.Frame;
            for(int tick=0;tick<100;tick++)routine.Advance(false);
            Check(routine.Action==frozenAction&&routine.Frame==frozenFrame,"work routine freezes outside active work");
            routine.Reset();Check(routine.Action==WorkAction.Idle&&routine.Frame==0,"mode transitions reset work props and pose");
            int[] poses={0,12,24,48,68,96,111};
            using(var sheet=new Bitmap(224*poses.Length,156*3))
            using(var sheetGraphics=Graphics.FromImage(sheet))
            using(var caption=PixelFont.Create(18))
            {
                sheetGraphics.Clear(Color.FromArgb(20,25,30));
                sheetGraphics.InterpolationMode=System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                sheetGraphics.PixelOffsetMode=System.Drawing.Drawing2D.PixelOffsetMode.Half;
                foreach(WorkAction action in new[]{WorkAction.Sharpen,WorkAction.Map,WorkAction.TendFire})
                {
                    using(var idle=PixelArt.Scene(1,false,0,12,false))
                    using(var first=PixelArt.Scene(1,false,0,12,false,action,0))
                    using(var last=PixelArt.Scene(1,false,0,12,false,action,111))
                    using(var active=PixelArt.Scene(1,false,0,12,false,action,48))
                    {
                        Check(SamePixels(idle,first)&&SamePixels(idle,last),"work animation returns exactly to idle: "+action);
                        Check(!SamePixels(idle,active,new Rectangle(42,25,105,56)),"work animation visibly performs task: "+action);
                        active.Save(Path.Combine(output,"work-"+action+".png"));
                    }
                    for(int index=0;index<poses.Length;index++)
                    using(var pose=PixelArt.Scene(1,false,0,12,false,action,poses[index]))
                    {
                        int x=index*224,y=((int)action-1)*156;
                        sheetGraphics.DrawImage(pose,new Rectangle(x,y,224,132),new Rectangle(38,20,112,66),GraphicsUnit.Pixel);
                        sheetGraphics.DrawString(action+" / "+poses[index],caption,Brushes.Wheat,x+4,y+132);
                    }
                }
                sheet.Save(Path.Combine(output,"work-animation-poses.png"));
            }
            for(int pose=0;pose<WorkRoutine.Duration;pose++)
            using(var sword=PixelArt.Scene(1,false,0,12,false,WorkAction.Sharpen,pose))
                if(sword.GetPixel(82,61).ToArgb()!=ColorTranslator.FromHtml("#AAB7BD").ToArgb())throw new Exception("Sword grip detached at frame "+pose);
            Check(true,"sword grip stays attached to right gauntlet through every sharpening frame");
            foreach(WorkAction task in new[]{WorkAction.Sharpen,WorkAction.Map})
            {
                bool hidden=true;
                for(int pose=8;pose<=103;pose++)using(var view=PixelArt.Scene(1,false,0,12,false,task,pose))
                    if(HasColor(view,ColorTranslator.FromHtml("#B9A16D")))hidden=false;
                Check(hidden,"shield is fully stowed before and during task: "+task);
            }
            bool stoneGone=true;
            for(int pose=80;pose<112;pose++)using(var view=PixelArt.Scene(1,false,0,12,false,WorkAction.Sharpen,pose))
                if(HasColor(view,ColorTranslator.FromHtml("#707775")) || HasColor(view,ColorTranslator.FromHtml("#CED1B8")))stoneGone=false;
            Check(stoneGone,"sharpening stone is put away before sword rises");
            using(var detail=new Bitmap(1120,468))
            using(var g=Graphics.FromImage(detail))
            using(var font=PixelFont.Create(18))
            {
                g.Clear(Color.FromArgb(20,25,30));g.InterpolationMode=System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;g.PixelOffsetMode=System.Drawing.Drawing2D.PixelOffsetMode.Half;
                int[][] frames={new[]{0,2,4,6,8},new[]{76,80,84,88,96},new[]{40,48,52,60,70}};
                for(int row=0;row<3;row++)for(int col=0;col<5;col++)
                using(var view=PixelArt.Scene(1,false,0,12,false,row==2?WorkAction.TendFire:WorkAction.Sharpen,frames[row][col]))
                {
                    g.DrawImage(view,new Rectangle(col*224,row*156,224,132),new Rectangle(38,20,112,66),GraphicsUnit.Pixel);
                    g.DrawString((row==0?"Shield":row==1?"Stop honing":"Throw")+" / "+frames[row][col],font,Brushes.Wheat,col*224+4,row*156+132);
                }
                detail.Save(Path.Combine(output,"work-animation-details.png"));
            }
            using(var left=PixelArt.Scene(1,false,0,12,false,WorkAction.Map,40))
            using(var right=PixelArt.Scene(1,false,0,12,false,WorkAction.Map,72))
            {
                Check(SamePixels(left,right,new Rectangle(50,51,39,22)),"map and both hands remain stationary while reading");
                Check(!SamePixels(left,right,new Rectangle(54,27,26,23)),"helm scans both sides of map");
                left.Save(Path.Combine(output,"map-look-left.png"));right.Save(Path.Combine(output,"map-look-right.png"));
            }
            using(var sparks=PixelArt.Scene(1,false,0,12,false,WorkAction.Sharpen,26))
            using(var ended=PixelArt.Scene(1,false,0,12,false,WorkAction.Sharpen,82))
                Check(HasColor(sparks,ColorTranslator.FromHtml("#DFA052"))&&!HasColor(ended,ColorTranslator.FromHtml("#DFA052")),"tiny honing particles appear during contact and stop afterwards");
            var player=new AnimationPreview();player.Select(2);player.Seek(48);player.Playing=false;player.Tick();
            Check(player.Frame==48,"developer pause freezes exact preview frame");
            player.Step(1);player.Step(-1);Check(player.Frame==48,"developer frame stepping is reversible");
            player.Seek(player.Length-1);player.Playing=true;player.Tick();Check(player.Frame==0,"developer playback loops at last frame");
            player.Select(5);Check(player.Frame==0&&player.Length==PixelArt.TavernArrivalFrames+128,"developer selection resets and includes full tavern sequence");
            player.Seek(9999);Check(player.Frame==player.Length-1,"developer timeline clamps frame bounds");
            using(var release=PixelArt.Scene(1,false,0,12,false,WorkAction.TendFire,52))
            using(var settled=PixelArt.Scene(1,false,0,12,false,WorkAction.TendFire,64))
            using(var beforeReturn=PixelArt.Scene(1,false,0,12,false,WorkAction.TendFire,87))
            {
                var arms=new Rectangle(75,48,31,19);
                Check(!SamePixels(release,settled,arms)&&SamePixels(settled,beforeReturn,arms),"throw hands lower after release and remain relaxed before walking back");
                Check(settled.GetPixel(102,63).ToArgb()==ColorTranslator.FromHtml("#AAB7BD").ToArgb()&&settled.GetPixel(78,63).ToArgb()==ColorTranslator.FromHtml("#AAB7BD").ToArgb(),"both empty hands rest beside the body after throwing");
            }
            using(var sheet=new Bitmap(1120,156))using(var g=Graphics.FromImage(sheet))using(var font=PixelFont.Create(18))
            {
                g.Clear(Color.FromArgb(20,25,30));g.InterpolationMode=System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;g.PixelOffsetMode=System.Drawing.Drawing2D.PixelOffsetMode.Half;
                int[] frames={52,58,64,87,96};
                for(int i=0;i<frames.Length;i++)using(var pose=PixelArt.Scene(1,false,0,12,false,WorkAction.TendFire,frames[i]))
                {g.DrawImage(pose,new Rectangle(i*224,0,224,132),new Rectangle(38,20,112,66),GraphicsUnit.Pixel);g.DrawString("Throw / "+frames[i],font,Brushes.Wheat,i*224+4,132);}
                sheet.Save(Path.Combine(output,"throw-recovery.png"));
            }
            var equippedPreferences=new Preferences{ArmorStyle=2,SwordStyle=1,ShieldStyle=2};
            using(var encoded=new MemoryStream())
            {
                var serializer=new DataContractJsonSerializer(typeof(Preferences));serializer.WriteObject(encoded,equippedPreferences);encoded.Position=0;
                var decoded=(Preferences)serializer.ReadObject(encoded);decoded.Validate();
                Check(decoded.ArmorStyle==2&&decoded.SwordStyle==1&&decoded.ShieldStyle==2,"equipment choices survive settings serialization");
            }
            var invalidEquipment=new Preferences{ArmorStyle=-1,SwordStyle=99,ShieldStyle=-7};invalidEquipment.Validate();
            Check(invalidEquipment.ArmorStyle==0&&invalidEquipment.SwordStyle==2&&invalidEquipment.ShieldStyle==0,"invalid equipment selections are clamped");
            var previousEquipment=PixelArt.Equipped;
            using(var sheet=new Bitmap(1032,552))using(var g=Graphics.FromImage(sheet))
            {
                g.InterpolationMode=System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;g.PixelOffsetMode=System.Drawing.Drawing2D.PixelOffsetMode.Half;
                for(int style=0;style<3;style++)for(int pose=0;pose<3;pose++)
                using(var image=PixelArt.EquipmentScene(new KnightEquipment(style,style,style),1,pose==2,0,12,false,pose==1?WorkAction.Sharpen:WorkAction.Idle,pose==1?48:0))
                    g.DrawImage(image,new Rectangle(pose*344,style*184,344,184));
                sheet.Save(Path.Combine(output,"equipment-options.png"));
            }
            Check(object.ReferenceEquals(previousEquipment,PixelArt.Equipped),"equipment preview restores active equipment after rendering");
            using(var original=PixelArt.EquipmentScene(new KnightEquipment(0,0,0),1,false,0,12,false,WorkAction.Idle,0))
            using(var armor=PixelArt.EquipmentScene(new KnightEquipment(1,0,0),1,false,0,12,false,WorkAction.Idle,0))
            using(var sword=PixelArt.EquipmentScene(new KnightEquipment(0,1,0),1,false,0,12,false,WorkAction.Idle,0))
            using(var shield=PixelArt.EquipmentScene(new KnightEquipment(0,0,1),1,false,0,12,false,WorkAction.Idle,0))
            {
                Check(!SamePixels(original,armor)&&!SamePixels(original,sword)&&!SamePixels(original,shield),"armor sword and shield each change visible equipment independently");
                Check(SamePixels(original,armor,new Rectangle(108,0,64,92)),"armor palette does not recolor scene or bonfire");
            }
            using(var original=PixelArt.EquipmentScene(new KnightEquipment(0,0,0),1,false,70,12,true,WorkAction.Idle,0))
            using(var golden=PixelArt.EquipmentScene(new KnightEquipment(2,0,0),1,false,70,12,true,WorkAction.Idle,0))
            {
                Check(!SamePixels(original,golden,new Rectangle(45,30,32,50)),"equipped armor appears in tavern");
                Check(SamePixels(original,golden,new Rectangle(110,30,30,50)),"player armor leaves Solaire unchanged");
            }
            using(var settings=new SettingsDialog(equippedPreferences))
            {
                Check(settings.ArmorStyle==2&&settings.SwordStyle==1&&settings.ShieldStyle==2,"settings opens with saved equipment selected");
                settings.ShowSection(3);Render(settings,output,"equipment-settings");
            }
            Check(object.ReferenceEquals(previousEquipment,PixelArt.Equipped),"closing equipment settings without saving preserves active equipment");
            bool helmetClear=true;
            using(var sheet=new Bitmap(1344,468))using(var g=Graphics.FromImage(sheet))using(var font=PixelFont.Create(18))
            {
                g.Clear(Color.FromArgb(20,25,30));g.InterpolationMode=System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;g.PixelOffsetMode=System.Drawing.Drawing2D.PixelOffsetMode.Half;
                int[] frames={10,12,14,16,18,20};
                for(int swordStyle=0;swordStyle<3;swordStyle++)
                {
                    var gear=new KnightEquipment(0,swordStyle,0);
                    using(var idle=PixelArt.EquipmentScene(gear,1,false,0,12,false,WorkAction.Idle,0))
                    for(int frame=8;frame<=103;frame++)using(var pose=PixelArt.EquipmentScene(gear,1,false,0,12,false,WorkAction.Sharpen,frame))
                        if(!SamePixels(idle,pose,new Rectangle(58,27,16,18)))helmetClear=false;
                    for(int col=0;col<frames.Length;col++)using(var pose=PixelArt.EquipmentScene(gear,1,false,0,12,false,WorkAction.Sharpen,frames[col]))
                    {
                        g.DrawImage(pose,new Rectangle(col*224,swordStyle*156,224,132),new Rectangle(38,20,112,66),GraphicsUnit.Pixel);
                        g.DrawString("Sword "+swordStyle+" / "+frames[col],font,Brushes.Wheat,col*224+4,swordStyle*156+132);
                    }
                }
                sheet.Save(Path.Combine(output,"sword-layer-turn.png"));
            }
            Check(helmetClear,"all sword variants stay behind the helmet during both sharpening turns");
            int arrival=PixelArt.TavernArrivalFrames;
            using(var morning=PixelArt.Scene(1,false,arrival+36,6,true))using(var evening=PixelArt.Scene(1,false,arrival+36,23,true))
            {Check(SamePixels(morning,evening),"tavern scene does not change with local time");morning.Save(Path.Combine(output,"tavern-toast.png"));}
            using(var entering=PixelArt.Tavern(0))using(var seated=PixelArt.Tavern(arrival))using(var toast=PixelArt.Tavern(arrival+36))using(var sip=PixelArt.Tavern(arrival+76))using(var solaireSip=PixelArt.Tavern(arrival+104))
            {
                Check(!SamePixels(entering,seated,new Rectangle(48,35,28,48)),"knight arrives before sitting at the tavern table");
                Check(!SamePixels(seated,toast,new Rectangle(89,43,20,17))&&!SamePixels(seated,sip,new Rectangle(70,40,18,17))&&!SamePixels(seated,solaireSip,new Rectangle(109,40,16,17)),"shared toast and each companion's drinking poses animate");
                Check(seated.GetPixel(124,60).ToArgb()==ColorTranslator.FromHtml("#E3AF4F").ToArgb(),"Solaire wears the sun crest");
                entering.Save(Path.Combine(output,"tavern-arrival.png"));seated.Save(Path.Combine(output,"tavern-seated.png"));sip.Save(Path.Combine(output,"tavern-sip.png"));solaireSip.Save(Path.Combine(output,"tavern-solaire-sip.png"));
            }
            foreach(bool light in new[]{false,true})foreach(string lang in new[]{"tr","en"})
            {
                var pref=new Preferences{Language=lang,LightMode=light,Day=DateTime.Now.ToString("yyyy-MM-dd"),TotalWorkSeconds=14736,TodayWorkSeconds=7524,CompletedSessions=8,Breaks=3};
                string suffix=(light?"lit-":"dark-")+lang;
                using(var form=new Companion(pref,true))Render(form,output,"main-"+suffix);
                using(var developer=new DeveloperDialog())
                {
                    developer.Preview.Playing=false;developer.Show();Application.DoEvents();
                    foreach(Control control in developer.Controls)
                    {if(control is PixelCombo && control.Left==150)((PixelCombo)control).SelectedIndex=1;if(control is PixelSlider)((PixelSlider)control).Value=26;}
                    foreach(Control control in developer.Controls)if(control is EmberButton && control.Left==358)((EmberButton)control).PerformClick();
                    Check(developer.Preview.Frame==27,"developer next-frame button changes preview: "+suffix);
                    Render(developer,output,"developer-"+suffix);
                }
                using(var dialog=new SettingsDialog(pref))for(int tab=0;tab<4;tab++){dialog.ShowSection(tab);Render(dialog,output,"settings-"+tab+"-"+suffix);}
                using(var dialog=new StatsDialog(pref))
                {
                    bool separated=true;
                    foreach(System.Windows.Forms.Control a in dialog.Controls)
                        foreach(System.Windows.Forms.Control b in dialog.Controls)
                            if(a!=b && a.Bounds.IntersectsWith(b.Bounds))separated=false;
                    Check(separated,"statistics text and button bounds do not overlap: "+suffix);
                    Render(dialog,output,"stats-"+suffix);
                }
                pref.InBreak=true;pref.Remaining=180;using(var form=new Companion(pref,true))Render(form,output,"break-"+suffix);
                pref.InBreak=false;pref.Chill=true;using(var form=new Companion(pref,true))Render(form,output,"chill-"+suffix);
            }
            Theme.Light=false;L.Language="tr";using(var setup=new SetupDialog())Render(setup,output,"setup");
            Check(PixelFont.Family.Name=="VT323","bundled pixel font loaded without system installation");
            report.AppendLine(count+" checks passed.");File.WriteAllText(Path.Combine(output,"results.txt"),report.ToString());
        }
        static bool Equal(byte[] a,byte[] b) {if(a.Length!=b.Length)return false;for(int i=0;i<a.Length;i++)if(a[i]!=b[i])return false;return true;}
        public static void RunAudio(string output)
        {
            Directory.CreateDirectory(output);
            using (var loop = new LoopingAudio(Soundscape.Render(0, 0, 15, 1)))
                AwaitAudio(delegate { return loop.PlaybackBytes > 40000; }, "PCM audio continues beyond its first loop");
            var preferences = new Preferences { AudioEnabled = true, FireVolume = 0, AmbienceVolume = 0, MusicVolume = 20 };
            using (var background = new Soundscape())
            {
                background.Apply(preferences);
                AwaitAudio(delegate { return background.PlaybackBytes > 0; }, "enabled soundscape starts outside Settings");
                using (var preview = new Soundscape())
                {
                    preview.Apply(preferences);
                    AwaitAudio(delegate { return preview.PlaybackBytes > 0; }, "preview has its own playing stream");
                }
                uint before = background.PlaybackBytes;
                System.Media.SystemSounds.Asterisk.Play();
                AwaitAudio(delegate { return background.PlaybackBytes > before + 8000; }, "closing preview and reminder sounds cannot stop background music");
                preferences.AudioEnabled = false;
                background.Apply(preferences);
                Check(background.PlaybackBytes == 0, "master switch stops background playback");
                preferences.AudioEnabled = true;
                background.Apply(preferences);
                AwaitAudio(delegate { return background.PlaybackBytes > 0; }, "reenabling soundscape resumes playback");
                background.Apply(preferences);
                background.Mute();
                System.Threading.Thread.Sleep(500);
                Check(background.PlaybackBytes == 0, "muting cancels pending audio generation");
            }
            File.WriteAllText(Path.Combine(output, "audio-results.txt"), report.ToString());
        }
        static void AwaitAudio(Func<bool> condition, string name)
        {
            var deadline = DateTime.UtcNow.AddSeconds(8);
            while (!condition())
            {
                if (DateTime.UtcNow > deadline) throw new Exception(name);
                System.Threading.Thread.Sleep(25);
            }
            Check(true, name);
        }
        static bool HasColor(Bitmap image,Color color) {for(int y=0;y<image.Height;y++)for(int x=0;x<image.Width;x++)if(image.GetPixel(x,y).ToArgb()==color.ToArgb())return true;return false;}
        static bool SamePixels(Bitmap a,Bitmap b) {return a.Size==b.Size&&SamePixels(a,b,new Rectangle(Point.Empty,a.Size));}
        static bool SamePixels(Bitmap a,Bitmap b,Rectangle area) {for(int y=area.Top;y<area.Bottom;y++)for(int x=area.Left;x<area.Right;x++)if(a.GetPixel(x,y)!=b.GetPixel(x,y))return false;return true;}
        static void Render(Form form,string output,string name)
        {form.Show();Application.DoEvents();using(var b=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(b,new Rectangle(0,0,b.Width,b.Height));b.Save(Path.Combine(output,name+".png"));}form.Hide();}
    }
}
