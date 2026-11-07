using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace SoulsRevive
{
    public sealed class Companion : Form
    {
        readonly TimerEngine engine;
        readonly System.Windows.Forms.Timer timer;
        readonly NotifyIcon tray;
        readonly ContextMenuStrip menu;
        readonly EmberButton primary, pause, workMode, chillMode;
        readonly EmberButton settingsButton;
        readonly Soundscape audio = new Soundscape();
        readonly WorkRoutine workRoutine = new WorkRoutine();
        int lastTick=Environment.TickCount, frame, chillFrame, saveTicks;
        bool exiting, dragging;
        Point dragOffset;
        string message="";
        DateTime messageUntil;
        public Companion(Preferences p,bool preview)
        {
            engine=new TimerEngine(p); L.Language=p.Language; Theme.Light=p.LightMode; PixelArt.Equipped=KnightEquipment.From(p);
            chillFrame=preview?PixelArt.TavernArrivalFrames+36:0;
            Text="Soul Revive"; AccessibleName=L.Text("Soul Revive mola sayacı","Soul Revive break timer");
            ClientSize=new Size(384,668); FormBorderStyle=FormBorderStyle.None; AutoScaleMode=AutoScaleMode.None;
            BackColor=Theme.Background; DoubleBuffered=true; StartPosition=FormStartPosition.Manual;
            TopMost=p.AlwaysOnTop; Icon=PixelArt.MakeIcon();
            var area=Screen.PrimaryScreen.WorkingArea; Location=new Point(area.Right-Width-24,area.Bottom-Height-24);
            foreach(var screen in Screen.AllScreens) if(screen.WorkingArea.Contains(new Rectangle(p.X,p.Y,Width,Height))) { Location=new Point(p.X,p.Y); break; }
            primary=Button("",20,486,168,42,delegate { if(p.InBreak)engine.CancelBreak(); else engine.BeginBreak(); },true);
            pause=Button("",196,486,168,42,delegate { engine.TogglePause(); },false);
            workMode=Button(L.Text("ÇALIŞIYORUM","WORKING"),20,74,168,30,delegate { engine.SetChill(false); },false);
            chillMode=Button(L.Text("ÇALIŞMIYORUM","CHILL MODE"),196,74,168,30,delegate { engine.SetChill(true); },false);
            settingsButton=Button("",272,18,28,28,OpenSettings,false);
            settingsButton.GearIcon=true;
            settingsButton.AccessibleName=L.Text("Ayarlar","Settings");
            var hide=Button("_",304,18,28,28,Hide,false); hide.AccessibleName=L.Text("Tepsiye küçült","Minimize to tray");
            var close=Button("X",336,18,28,28,Hide,false); close.AccessibleName=L.Text("Tepsiye gizle","Hide in tray");
            var stats=Button(L.Text("İSTATİSTİKLER >","STATISTICS >"),20,544,344,32,OpenStats,false);
            menu=new ContextMenuStrip {Font=PixelFont.Create(16),BackColor=Theme.Panel,ForeColor=Theme.Text};
            menu.Items.Add("",null,delegate {Reveal();});
            menu.Items.Add("",null,delegate {engine.SetChill(!p.Chill);});
            menu.Items.Add("",null,delegate {Reveal(); OpenSettings();});
            menu.Items.Add(new ToolStripSeparator()); menu.Items.Add("",null,delegate {Quit();});
            tray=new NotifyIcon { Text="Soul Revive", Icon=Icon, ContextMenuStrip=menu, Visible=!preview };
            tray.DoubleClick+=delegate {Reveal();}; tray.BalloonTipClicked+=delegate {Reveal();};
            engine.Changed+=OnChanged;
            audio.Failed += delegate(string error) {
                if (IsDisposed || !IsHandleCreated) return;
                BeginInvoke((Action)delegate {
                    message = L.Text("Ses başlatılamadı: ", "Audio could not start: ") + error;
                    messageUntil = DateTime.Now.AddSeconds(15);
                    Invalidate();
                });
            };
            timer=new System.Windows.Forms.Timer {Interval=125};
            timer.Tick+=delegate
            {
                if(Program.QuitRequested!=null && Program.QuitRequested.WaitOne(0)) {Quit(); return;}
                if(Program.ShowRequested!=null && Program.ShowRequested.WaitOne(0))Reveal();
                int now=Environment.TickCount; double delta=unchecked((uint)(now-lastTick))/1000.0; lastTick=now;
                if(delta>5 && !p.Chill) {p.Paused=true; Save();}
                engine.Advance(delta); frame++; if(p.Chill)chillFrame++;
                workRoutine.Advance(!p.Chill && !p.InBreak && !p.Paused && !engine.Due);
                if(++saveTicks>=40) {saveTicks=0;Save();}
                UpdateControls(); if(Visible)Invalidate();
            };
            if(!preview) {SystemEvents.PowerModeChanged+=OnPower; SystemEvents.SessionSwitch+=OnSession; timer.Start(); audio.Apply(p);}
            UpdateControls();
            MouseDown+=delegate(object sender,MouseEventArgs e) {if(e.Button==MouseButtons.Left) {dragging=true;dragOffset=e.Location;Capture=true;}};
            MouseMove+=delegate {if(dragging)Location=new Point(Cursor.Position.X-dragOffset.X,Cursor.Position.Y-dragOffset.Y);};
            MouseUp+=delegate
            {
                if(!dragging)return; dragging=false;Capture=false; var a=Screen.FromControl(this).WorkingArea;
                Location=new Point(Math.Max(a.Left,Math.Min(Left,a.Right-Width)),Math.Max(a.Top,Math.Min(Top,a.Bottom-Height)));
                p.X=Left;p.Y=Top;Save();
            };
        }
        EmberButton Button(string text,int x,int y,int w,int h,Action action,bool primary)
        {var b=new EmberButton(text,new Rectangle(x,y,w,h),primary);b.Click+=delegate{action();};Controls.Add(b);return b;}
        void OnPower(object s,PowerModeChangedEventArgs e) {if(e.Mode==PowerModes.Suspend)SystemPause();else if(e.Mode==PowerModes.Resume)audio.Apply(engine.P);}
        void OnSession(object s,SessionSwitchEventArgs e) {if(e.Reason==SessionSwitchReason.SessionLock)SystemPause();else if(e.Reason==SessionSwitchReason.SessionUnlock)audio.Apply(engine.P);}
        void SystemPause()
        {if(IsDisposed||!IsHandleCreated)return;BeginInvoke((Action)delegate {if(!engine.P.Chill)engine.P.Paused=true;lastTick=Environment.TickCount;audio.Mute();Save();UpdateControls();Invalidate();});}
        void Save() {if(!Storage.Save(engine.P)) {message=L.Text("Kayıt başarısız. Klasör iznini kontrol et.","Save failed. Check folder permissions.");messageUntil=DateTime.Now.AddSeconds(10);}}
        void Reveal() {Show();WindowState=FormWindowState.Normal;Activate();}
        internal void Quit() {exiting=true;Close();}
        void OnChanged(string reason)
        {
            if(reason=="mode")chillFrame=0;
            if(reason=="mode" || reason=="due" || reason=="revived" || reason=="break" || reason=="cancel")workRoutine.Reset();
            if(reason=="due") {Notify(L.Text("Ateş sönüyor, şövalye.","Your fire is fading."),L.Text("Mola ver'e bas; ayağa kalkıp biraz yürü.","Select Take a break; stand up and walk."));Reveal();}
            if(reason=="revived") {message=L.Text("ATEŞ YENİDEN CANLANDI","HUMANITY RESTORED");messageUntil=DateTime.Now.AddSeconds(8);Notify(L.Text("Yeni bir şafak.","A new dawn."),L.Text("Mola tamamlandı. Yeni çalışma başladı.","Break complete. Your next work period has started."));}
            lastTick=Environment.TickCount;Save();UpdateControls();Invalidate();
        }
        void Notify(string title,string text) {tray.ShowBalloonTip(7000,title,text,ToolTipIcon.Info);if(engine.P.Sound)System.Media.SystemSounds.Asterisk.Play();}
        void UpdateControls()
        {
            var p=engine.P;
            primary.Text=p.InBreak?L.Text("Çalışmaya dön","Return to work"):L.Text("Mola ver >","Take a break >"); primary.Enabled=!p.Chill;
            pause.Text=p.Paused?L.Text("Devam et","Resume"):L.Text("Duraklat","Pause");pause.Enabled=!p.Chill;
            primary.AccessibleName=primary.Text;pause.AccessibleName=pause.Text;
            workMode.Text=L.Text("ÇALIŞIYORUM","WORKING");chillMode.Text=L.Text("ÇALIŞMIYORUM","CHILL MODE");
            workMode.Primary=!p.Chill;chillMode.Primary=p.Chill;workMode.Invalidate();chillMode.Invalidate();
            menu.Items[0].Text=L.Text("Şövalyeyi göster","Show knight");menu.Items[1].Text=p.Chill?L.Text("Çalışmaya başla","Start working"):L.Text("Çalışmıyorum","Chill mode");
            menu.Items[2].Text=L.Text("Ayarlar","Settings");menu.Items[4].Text=L.Text("Çıkış","Quit");
            foreach(Control c in Controls)if(c.Top==544)c.Text=L.Text("İSTATİSTİKLER >","STATISTICS >");
            settingsButton.AccessibleName=L.Text("Ayarlar","Settings");
            foreach(Control c in Controls)if(c.Top==18 && c!=settingsButton)c.AccessibleName=L.Text("Tepsiye gizle","Hide in tray");
            tray.Text="Soul Revive · "+(p.Chill?L.Text("Solaire ile meyhanede","At the tavern with Solaire"):p.Paused?L.Text("Duraklatıldı","Paused"):(p.InBreak?L.Text("Mola ","Break "):L.Text("Çalışma ","Work "))+TimeText(p.Remaining));
        }
        internal static string TimeText(double seconds) {int n=(int)Math.Ceiling(seconds);return(n/60).ToString("00")+":"+(n%60).ToString("00");}
        protected override bool ProcessCmdKey(ref Message msg,Keys keyData)
        {if(keyData==Keys.F8){using(var panel=new DeveloperDialog())panel.ShowDialog(this);return true;}return base.ProcessCmdKey(ref msg,keyData);}
        void OpenStats() {using(var dialog=new StatsDialog(engine.P))dialog.ShowDialog(this);}
        void OpenSettings()
        {
            audio.Mute();
            using(var settings=new SettingsDialog(engine.P))
            {
                settings.Updating+=delegate {BeginInvoke((Action)Quit);};
                if(settings.ShowDialog(this)==DialogResult.OK)
                {
                    var p=engine.P;L.Language=p.Language=settings.Language;p.LightMode=settings.LightMode;Theme.Light=p.LightMode;
                    p.ArmorStyle=settings.ArmorStyle;p.SwordStyle=settings.SwordStyle;p.ShieldStyle=settings.ShieldStyle;PixelArt.Equipped=KnightEquipment.From(p);
                    p.AlwaysOnTop=settings.OnTop;p.Sound=settings.PlaySound;p.AudioEnabled=settings.AudioEnabled;
                    p.FireVolume=settings.FireVolume;p.AmbienceVolume=settings.AmbienceVolume;p.MusicVolume=settings.MusicVolume;
                    BackColor=Theme.Background;TopMost=p.AlwaysOnTop;engine.ApplyDurations(settings.Work,settings.Rest);
                    menu.BackColor=Theme.Panel;menu.ForeColor=Theme.Text;foreach(Control c in Controls)c.Invalidate();Invalidate();
                }
            }
            audio.Apply(engine.P);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var p=engine.P;Graphics g=e.Graphics;g.Clear(Theme.Background);Theme.Frame(g,ClientRectangle,Theme.Background);
            Theme.Label(g,"SOUL REVIVE",17,Theme.Gold,new Rectangle(20,14,246,32),ContentAlignment.MiddleLeft,"");
            Theme.Label(g,L.Text("KÜÇÜK MOLALAR. YENİDEN DOĞ.","SMALL BREAKS. RISE AGAIN."),8,Theme.Muted,new Rectangle(20,46,344,20),ContentAlignment.MiddleLeft,"");
            Theme.Frame(g,new Rectangle(18,114,348,188),Theme.Panel);
            using(var scene=PixelArt.Scene(p.Chill?1:engine.Health,p.InBreak,p.Chill?chillFrame:frame,DayCycle.Hour(p),p.Chill,workRoutine.Action,workRoutine.Frame))
            {g.InterpolationMode=InterpolationMode.NearestNeighbor;g.PixelOffsetMode=PixelOffsetMode.Half;g.DrawImage(scene,new Rectangle(20,116,344,184));g.PixelOffsetMode=PixelOffsetMode.Default;}
            string state=p.Chill?L.Text("SOLAIRE İLE BİR KADEH MOLASI","A TOAST WITH SOLAIRE"):p.Paused?L.Text("ATEŞİN SENİ BEKLİYOR","YOUR FIRE AWAITS"):p.InBreak?L.Text("ŞÖVALYE UYUYOR. SEN BİRAZ YÜRÜ.","KNIGHT ASLEEP. TIME FOR A WALK."):engine.Due?L.Text("GECE ÇÖKTÜ. MOLA ZAMANI.","NIGHT HAS FALLEN. TAKE A BREAK."):L.Text("ODAKLAN. ATEŞİ CANLI TUT.","FOCUS. KEEP THE FIRE ALIVE.");
            Theme.Frame(g,new Rectangle(20,312,344,28),Theme.Panel);Theme.Label(g,state,8,Theme.Gold,new Rectangle(24,312,336,28),ContentAlignment.MiddleCenter,"");
            Theme.Label(g,p.Chill?L.Text("Şerefe!","Cheers!"):TimeText(p.Remaining),p.Chill?32:42,Theme.Text,new Rectangle(20,348,344,68),ContentAlignment.MiddleCenter,"");
            string subtitle=p.Chill?L.Text("Solaire ile meyhanedesin. İş bekleyebilir.","At the tavern with Solaire. Work can wait."):p.Paused?L.Text("Devam et ile yolculuğunu sürdür","Select Resume to continue"):p.InBreak?L.Text("Ayağa kalk, yürü ve biraz esne.","Stand up, walk and stretch."):L.Text("Bir sonraki molaya kalan süre","Time until your next break");
            Theme.Label(g,subtitle,9,Theme.Muted,new Rectangle(20,416,344,26),ContentAlignment.MiddleCenter,"");
            double hp=p.Chill?1:Math.Max(0,Math.Min(1,engine.Health));
            Theme.Label(g,L.Text("CAN","HP"),8,Theme.Muted,new Rectangle(20,452,42,20),ContentAlignment.MiddleLeft,"");
            using(var b=new SolidBrush(Theme.Panel))g.FillRectangle(b,58,457,252,8);
            using(var b=new SolidBrush(p.InBreak||p.Chill?Theme.Green:Theme.Gold))g.FillRectangle(b,58,457,(int)(252*hp),8);
            for(int i=1;i<21;i++)using(var pen=new Pen(Theme.Background,2))g.DrawLine(pen,58+i*12,457,58+i*12,465);
            Theme.Label(g,((int)(hp*100))+"%",8,Theme.Muted,new Rectangle(316,452,48,20),ContentAlignment.MiddleCenter,"");
            Theme.Label(g,L.Text("BUGÜN","TODAY"),8,Theme.Muted,new Rectangle(20,588,164,20),ContentAlignment.MiddleCenter,"");
            Theme.Label(g,L.Text("TOPLAM","TOTAL"),8,Theme.Muted,new Rectangle(200,588,164,20),ContentAlignment.MiddleCenter,"");
            Theme.Label(g,StatsDialog.Duration(p.TodayWorkSeconds),12,Theme.Gold,new Rectangle(20,608,164,24),ContentAlignment.MiddleCenter,"");
            Theme.Label(g,StatsDialog.Duration(p.TotalWorkSeconds),12,Theme.Gold,new Rectangle(200,608,164,24),ContentAlignment.MiddleCenter,"");
            string footer=DateTime.Now<messageUntil?message:p.WorkMinutes+L.Text(" dk odak / ","m focus / ")+p.BreakMinutes+L.Text(" dk mola  ·  ","m break  ·  ")+p.Breaks+L.Text(" mola"," breaks");
            Theme.Label(g,footer,8,Theme.Muted,new Rectangle(20,640,344,20),ContentAlignment.MiddleCenter,"");
        }
        protected override void OnFormClosing(FormClosingEventArgs e) {Save();if(!exiting && e.CloseReason==CloseReason.UserClosing) {e.Cancel=true;Hide();return;}base.OnFormClosing(e);}
        protected override void Dispose(bool disposing)
        {if(disposing) {SystemEvents.PowerModeChanged-=OnPower;SystemEvents.SessionSwitch-=OnSession;timer.Stop();timer.Dispose();audio.Dispose();tray.Visible=false;tray.Dispose();menu.Dispose();if(Icon!=null)Icon.Dispose();}base.Dispose(disposing);}
    }

    sealed class PixelLabel : Control
    {
        public float TextSize=10;
        public Color Ink;
        public PixelLabel(string text,Rectangle rect,Color color,float size) {Text=text;Bounds=rect;Ink=color;TextSize=size;SetStyle(ControlStyles.SupportsTransparentBackColor|ControlStyles.OptimizedDoubleBuffer|ControlStyles.UserPaint,true);BackColor=Color.Transparent;}
        protected override void OnPaint(PaintEventArgs e)
        {
            using(var font=PixelFont.Create(PixelFont.TextPixels(TextSize)))
            using(var brush=new SolidBrush(Ink))
            using(var format=new StringFormat())
            {
                e.Graphics.TextRenderingHint=System.Drawing.Text.TextRenderingHint.SingleBitPerPixelGridFit;
                format.LineAlignment=Text.Contains("\n") || Height>90 ? StringAlignment.Near : StringAlignment.Center;
                format.Trimming=StringTrimming.EllipsisCharacter;
                e.Graphics.DrawString(Text,font,brush,ClientRectangle,format);
            }
        }
    }

    sealed class StatsDialog : Form
    {
        readonly System.Windows.Forms.Timer refresh = new System.Windows.Forms.Timer { Interval = 1000 };
        public static string Hours(double seconds) {return(seconds/3600).ToString("0.00",System.Globalization.CultureInfo.CurrentCulture);}
        public static string Duration(double seconds)
        {
            seconds = Preferences.ValidSeconds(seconds);
            return Math.Floor(seconds / 3600).ToString("00", System.Globalization.CultureInfo.InvariantCulture)
                + ":" + ((int)(seconds / 60 % 60)).ToString("00")
                + ":" + ((int)(seconds % 60)).ToString("00");
        }
        public StatsDialog(Preferences p)
        {
            p.CheckDay();Text=L.Text("Soul Revive · İstatistikler","Soul Revive · Statistics");ClientSize=new Size(430,488);AutoScaleMode=AutoScaleMode.None;BackColor=Theme.Background;
            FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=false;MinimizeBox=false;StartPosition=FormStartPosition.CenterParent;ShowInTaskbar=false;Icon=PixelArt.MakeIcon();
            Add(L.Text("ATEŞİNİN HİKÂYESİ","YOUR BONFIRE JOURNEY"),20,18,40,Theme.Gold,16);
            Add(L.Text("TOPLAM ÇALIŞMA","TOTAL WORK TIME"),20,76,22,Theme.Muted,9);
            var total = Add("",20,104,62,Theme.Gold,28);
            var totalTime = Add("",20,168,24,Theme.Muted,9);
            var today = Add("",20,212,36,Theme.Text,15);
            var todayTime = Add("",20,250,24,Theme.Muted,9);
            var sessions = Add("",20,298,28,Theme.Text,11);
            var movement = Add("",20,334,28,Theme.Text,11);
            Add(L.Text("Yalnızca aktif çalışma süresi sayılır.\nDuraklatma, mola ve chill modu hariç.","Counts active work time only.\nPause, break and chill time are excluded."),20,376,48,Theme.Muted,9);
            Action update = delegate {
                p.CheckDay();
                total.Text = Hours(p.TotalWorkSeconds) + L.Text(" saat", " hours");
                totalTime.Text = Duration(p.TotalWorkSeconds);
                today.Text = L.Text("BUGÜN ", "TODAY ") + Hours(p.TodayWorkSeconds) + L.Text(" saat", " hours");
                todayTime.Text = Duration(p.TodayWorkSeconds);
                sessions.Text = p.CompletedSessions + L.Text(" tamamlanan çalışma turu", " completed work sessions");
                movement.Text = L.Text("Bugün: ", "Today: ") + p.Breaks + L.Text(" mola / ", " breaks / ")
                    + (int)(p.MovementSeconds / 60) + L.Text(" dk hareket", " min movement");
                Invalidate(true);
            };
            update();
            refresh.Tick += delegate { update(); };
            refresh.Start();
            var close=new EmberButton(L.Text("Ateşe dön","Back to the fire"),new Rectangle(20,436,390,36),true);close.DialogResult=DialogResult.OK;Controls.Add(close);AcceptButton=close;CancelButton=close;
        }
        PixelLabel Add(string s,int x,int y,int height,Color color,float size)
        {
            var label = new PixelLabel(s,new Rectangle(x,y,390,height),color,size);
            Controls.Add(label);
            return label;
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) refresh.Dispose();
            base.Dispose(disposing);
        }
    }

    sealed class SettingsDialog : Form
    {
        readonly NumericUpDown work,rest;
        readonly CheckBox top,sound,startup,audioEnabled;
        readonly ComboBox language,theme,armor,sword,shield;
        readonly TrackBar fire,ambience,music;
        readonly Soundscape preview=new Soundscape();
        readonly Panel generalPanel,soundPanel,updatePanel,equipmentPanel;
        readonly EmberButton save,cancel;
        UpdateInfo available;
        bool busy;
        public event Action Updating;
        public int Work {get{return(int)work.Value;}} public int Rest {get{return(int)rest.Value;}}
        public bool OnTop {get{return top.Checked;}} public bool PlaySound {get{return sound.Checked;}}
        public string Language {get{return language.SelectedIndex==1?"en":"tr";}} public bool LightMode {get{return theme.SelectedIndex==1;}}
        public int ArmorStyle {get{return armor.SelectedIndex;}} public int SwordStyle {get{return sword.SelectedIndex;}} public int ShieldStyle {get{return shield.SelectedIndex;}}
        public bool AudioEnabled {get{return audioEnabled.Checked;}} public int FireVolume {get{return fire.Value;}}
        public int AmbienceVolume {get{return ambience.Value;}} public int MusicVolume {get{return music.Value;}}
        public SettingsDialog(Preferences p)
        {
            Text=L.Text("Soul Revive · Ayarlar","Soul Revive · Settings");ClientSize=new Size(540,626);AutoScaleMode=AutoScaleMode.None;
            BackColor=Theme.Background;ForeColor=Theme.Text;Font=PixelFont.Create(22);Icon=PixelArt.MakeIcon();
            FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=false;MinimizeBox=false;StartPosition=FormStartPosition.CenterParent;ShowInTaskbar=false;
            Add(this,L.Text("ATEŞİNİN RİTMİ","YOUR BONFIRE ROUTINE"),20,14,500,38,Theme.Gold,17);
            generalPanel=Panel();soundPanel=Panel();updatePanel=Panel();equipmentPanel=Panel();
            var general=Tab(L.Text("GENEL","GENERAL"),20,119,generalPanel);
            Tab(L.Text("SESLER","SOUNDS"),147,119,soundPanel);Tab(L.Text("GÜNCELLEME","UPDATES"),274,119,updatePanel);
            Tab(L.Text("ŞÖVALYE","KNIGHT"),401,119,equipmentPanel);
            Add(equipmentPanel,L.Text("Zırh","Armor"),0,4,150,36,Theme.Text,12);
            armor=Combo(equipmentPanel,180,4,new[]{L.Text("Çelik şövalye","Steel knight"),L.Text("Kara şövalye","Black knight"),L.Text("Altın muhafız","Golden guardian")},Math.Max(0,Math.Min(2,p.ArmorStyle)));
            Add(equipmentPanel,L.Text("Kılıç","Sword"),0,60,150,36,Theme.Text,12);
            sword=Combo(equipmentPanel,180,60,new[]{L.Text("Uzun kılıç","Longsword"),L.Text("Geniş kılıç","Broadsword"),L.Text("İnce kılıç","Estoc")},Math.Max(0,Math.Min(2,p.SwordStyle)));
            Add(equipmentPanel,L.Text("Kalkan","Shield"),0,116,150,36,Theme.Text,12);
            shield=Combo(equipmentPanel,180,116,new[]{L.Text("Haçlı kalkan","Cross shield"),L.Text("Yuvarlak kalkan","Round shield"),L.Text("Mavi muhafız","Blue guardian")},Math.Max(0,Math.Min(2,p.ShieldStyle)));
            Add(equipmentPanel,L.Text("EKİPMAN ÖNİZLEMESİ","EQUIPMENT PREVIEW"),0,172,500,30,Theme.Gold,11);
            var equipmentPreview=new EquipmentPreview {Bounds=new Rectangle(78,212,344,184),Selection=delegate{return new KnightEquipment(ArmorStyle,SwordStyle,ShieldStyle);}};equipmentPanel.Controls.Add(equipmentPreview);
            foreach(var picker in new[]{armor,sword,shield})picker.SelectedIndexChanged+=delegate {equipmentPreview.Invalidate();};
            Add(equipmentPanel,L.Text("Seçimlerini uygulamak için Kaydet.","Select Save to equip your choices."),0,410,500,28,Theme.Muted,10);
            Add(generalPanel,L.Text("Çalışma süresi (dakika)","Work time (minutes)"),0,4,345,30,Theme.Text,11);work=Number(generalPanel,382,4,1,180,p.WorkMinutes);
            Add(generalPanel,L.Text("Mola süresi (dakika)","Break time (minutes)"),0,48,345,30,Theme.Text,11);rest=Number(generalPanel,382,48,1,30,p.BreakMinutes);
            Add(generalPanel,L.Text("Dil","Language"),0,100,150,30,Theme.Text,11);language=Combo(generalPanel,180,100,new[]{"Türkçe","English"},p.Language=="en"?1:0);
            Add(generalPanel,L.Text("Görünüm","Appearance"),0,148,150,30,Theme.Text,11);theme=Combo(generalPanel,180,148,new[]{"Dark mode","Lit mode"},p.LightMode?1:0);
            top=Check(generalPanel,L.Text("Her zaman üstte göster","Always on top"),202,p.AlwaysOnTop);
            sound=Check(generalPanel,L.Text("Mola bildirim sesi","Break reminder sound"),242,p.Sound);
            startup=Check(generalPanel,L.Text("Windows açılışında başlat","Start at Windows sign-in"),282,Startup.Enabled);
            Add(generalPanel,L.Text("Süreyi değiştirmek yeni bir tur başlatır.\nEkran kilidi ve uyku çalışmayı duraklatır.\nMolada sen hareket et; şövalyen dinlensin.","Changing durations starts a new round.\nLock and sleep pause your work timer.\nMove during breaks while your knight rests."),0,334,500,72,Theme.Muted,9);
            var developer=new EmberButton(L.Text("Geliştirici paneli / F8","Developer panel / F8"),new Rectangle(0,412,500,28),false);
            developer.Click+=delegate {using(var panel=new DeveloperDialog())panel.ShowDialog(this);};generalPanel.Controls.Add(developer);
            audioEnabled=Check(soundPanel,L.Text("Müzik ve ortam seslerini çal","Play music and ambience"),0,p.AudioEnabled);
            fire=Volume(soundPanel,L.Text("ATEŞ / ÇITIRTI","FIRE / CRACKLE"),64,p.FireVolume);
            ambience=Volume(soundPanel,L.Text("AMBIYANS / RÜZGÂR","AMBIENCE / WIND"),154,p.AmbienceVolume);
            music=Volume(soundPanel,L.Text("MÜZİK / BONFIRE MELODİSİ","MUSIC / BONFIRE MELODY"),244,p.MusicVolume);
            var listen=new EmberButton(L.Text("Önizle / durdur","Preview / stop"),new Rectangle(0,348,244,36),false);bool listening=false;
            Action play=delegate {preview.Apply(new Preferences{AudioEnabled=true,FireVolume=fire.Value,AmbienceVolume=ambience.Value,MusicVolume=music.Value});};
            listen.Click+=delegate {listening=!listening;listen.Text=listening?L.Text("Önizlemeyi durdur","Stop preview"):L.Text("Önizle / durdur","Preview / stop");if(listening)play();else preview.Mute();};soundPanel.Controls.Add(listen);
            foreach(var slider in new[]{fire,ambience,music})slider.MouseUp+=delegate {if(listening)play();};
            var soundStatus = new PixelLabel(L.Text("Kaydet sonrası arka planda çalar.\nTüm sesler çevrimdışı çalışır.","Select Save to keep audio playing in the background.\nAll sounds work offline."),new Rectangle(0,394,500,48),Theme.Muted,9);
            soundPanel.Controls.Add(soundStatus);
            preview.Failed += delegate(string error) {
                if (IsDisposed || !IsHandleCreated) return;
                BeginInvoke((Action)delegate {
                    listening = false;
                    listen.Text = L.Text("Önizle / durdur", "Preview / stop");
                    soundStatus.Text = L.Text("Ses başlatılamadı: ", "Audio could not start: ") + error;
                    soundStatus.Invalidate();
                });
            };
            Add(updatePanel,L.Text("YÜKLÜ SÜRÜM / ","INSTALLED VERSION / ")+Updates.VersionText,0,4,500,32,Theme.Gold,13);
            Add(updatePanel,L.Text("Yeni sürümleri buradan kontrol et.\nOnayladığında indirilir, doğrulanır ve yeniden açılır.\nAyarların ve istatistiklerin korunur.","Check for new versions here.\nApproved updates download, verify and restart.\nYour settings and statistics are preserved."),0,52,500,86,Theme.Text,10);
            var status=new PixelLabel(L.Text("Kontrol için internet bağlantısı gerekir.","An internet connection is needed to check."),new Rectangle(0,218,500,140),Theme.Muted,10);updatePanel.Controls.Add(status);
            var check=new EmberButton(L.Text("Güncellemeleri kontrol et","Check for updates"),new Rectangle(0,156,500,42),false);updatePanel.Controls.Add(check);
            var install=new EmberButton(L.Text("İndir ve güncelle","Download and update"),new Rectangle(0,376,500,42),true);install.Enabled=false;updatePanel.Controls.Add(install);
            check.Click+=async delegate
            {
                available=null;SetBusy(true);install.Enabled=false;status.Text=L.Text("Yayınlar kontrol ediliyor...","Checking releases...");status.Invalidate();
                try {available=await Task.Factory.StartNew<UpdateInfo>(Updates.Check);if(IsDisposed)return;status.Text=available==null?L.Text("Güncelsin. Ateşin yanmaya devam ediyor.","You are up to date. Your fire keeps burning."):L.Text("Yeni sürüm hazır: ","New version available: ")+available.Version.ToString(3)+"\n"+L.Text("İndir ve güncelle ile devam et.","Select Download and update to continue.");install.Enabled=available!=null;}
                catch(Exception ex) {if(!IsDisposed)status.Text=ex.Message;}
                finally {if(!IsDisposed){SetBusy(false);status.Invalidate();}}
            };
            install.Click+=async delegate
            {
                if(available==null)return;
                if(MessageBox.Show(this,L.Text("Güncelleme indirilip doğrulanacak. Soul Revive kapanıp yeniden açılacak. Devam edilsin mi?","The update will download and verify, then Soul Revive will close and reopen. Continue?"),"Soul Revive · "+available.Version.ToString(3),MessageBoxButtons.YesNo,MessageBoxIcon.Question)!=DialogResult.Yes)return;
                SetBusy(true);install.Enabled=false;
                try
                {
                    await Task.Factory.StartNew(delegate {Updates.DownloadAndStage(available,delegate(int n) {if(!IsDisposed&&IsHandleCreated)BeginInvoke((Action)delegate {status.Text=L.Text("İndiriliyor: %","Downloading: %")+n;status.Invalidate();});});});
                    if(Updating!=null)Updating();DialogResult=DialogResult.Abort;Close();
                }
                catch(Exception ex) {status.Text=ex.Message;SetBusy(false);install.Enabled=true;status.Invalidate();}
            };
            save=new EmberButton(L.Text("Kaydet","Save"),new Rectangle(278,570,242,38),true);cancel=new EmberButton(L.Text("Vazgeç","Cancel"),new Rectangle(20,570,242,38),false);
            cancel.DialogResult=DialogResult.Cancel;save.Click+=delegate {try{if(startup.Checked!=Startup.Enabled)Startup.Set(startup.Checked);DialogResult=DialogResult.OK;Close();}catch(Exception ex){MessageBox.Show(this,ex.Message,"Soul Revive",MessageBoxButtons.OK,MessageBoxIcon.Warning);}};
            Controls.Add(save);Controls.Add(cancel);AcceptButton=save;CancelButton=cancel;ShowSection(0);general.Primary=true;
        }
        void SetBusy(bool value) {busy=value;save.Enabled=cancel.Enabled=!value;foreach(Control c in updatePanel.Controls)if(c is EmberButton)c.Enabled=!value&&(c.Top!=376||available!=null);}
        internal void ShowSection(int index) {generalPanel.Visible=index==0;soundPanel.Visible=index==1;updatePanel.Visible=index==2;equipmentPanel.Visible=index==3;foreach(Control c in Controls)if(c is EmberButton&&c.Top==66){((EmberButton)c).Primary=c.Left==20+127*index;c.Invalidate();}}
        protected override void OnFormClosing(FormClosingEventArgs e) {if(busy&&DialogResult!=DialogResult.Abort)e.Cancel=true;base.OnFormClosing(e);}
        protected override void Dispose(bool disposing) {if(disposing)preview.Dispose();base.Dispose(disposing);}
        Panel Panel() {var p=new Panel {Bounds=new Rectangle(20,116,500,440),BackColor=Theme.Background};Controls.Add(p);return p;}
        EmberButton Tab(string text,int x,int width,Panel panel) {var b=new EmberButton(text,new Rectangle(x,66,width,34),false);b.Click+=delegate {ShowSection(panel==soundPanel?1:panel==updatePanel?2:panel==equipmentPanel?3:0);};Controls.Add(b);return b;}
        static void Add(Control host,string s,int x,int y,int w,int h,Color color,float size) {host.Controls.Add(new PixelLabel(s,new Rectangle(x,y,w,h),color,size));}
        static NumericUpDown Number(Control host,int x,int y,int min,int max,int value) {var n=new NumericUpDown {Bounds=new Rectangle(x,y,118,36),Minimum=min,Maximum=max,Value=value,Font=PixelFont.Create(20),BackColor=Theme.Panel,ForeColor=Theme.Text,BorderStyle=BorderStyle.FixedSingle,AccessibleName=y==4?L.Text("Çalışma süresi","Work duration"):L.Text("Mola süresi","Break duration")};host.Controls.Add(n);return n;}
        static CheckBox Check(Control host,string text,int y,bool value) {var c=new CheckBox {Text=text,Checked=value,Bounds=new Rectangle(0,y,500,30),UseCompatibleTextRendering=true,FlatStyle=FlatStyle.Flat};host.Controls.Add(c);return c;}
        static ComboBox Combo(Control host,int x,int y,string[] items,int selected) {var c=new PixelCombo {Bounds=new Rectangle(x,y,320,36)};c.Items.AddRange(items);c.SelectedIndex=selected;host.Controls.Add(c);return c;}
        static TrackBar Volume(Control host,string text,int y,int value)
        {
            var label=new PixelLabel(text+" / "+value+"%",new Rectangle(0,y,500,28),Theme.Gold,11);host.Controls.Add(label);
            var slider=new PixelSlider {Bounds=new Rectangle(0,y+30,500,42),Minimum=0,Maximum=100,TickFrequency=10,Value=value,BackColor=Theme.Background,AccessibleName=text,SmallChange=5,LargeChange=10};
            slider.ValueChanged+=delegate {label.Text=text+" / "+slider.Value+"%";label.Invalidate();};host.Controls.Add(slider);return slider;
        }
    }
}
