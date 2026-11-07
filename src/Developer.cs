using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace SoulsRevive
{
    // Independent preview state: never advances the user's work timer or statistics.
    sealed class AnimationPreview
    {
        public int Selection { get; private set; }
        public int Frame { get; private set; }
        public bool Playing = true;
        public int Length { get { return Selection==5 ? PixelArt.TavernArrivalFrames+128 : WorkRoutine.Duration; } }
        public void Select(int selection) {Selection=Math.Max(0,Math.Min(5,selection));Frame=0;}
        public void Seek(int frame) {Frame=Math.Max(0,Math.Min(Length-1,frame));}
        public void Step(int amount) {Frame=(Frame+amount%Length+Length)%Length;}
        public void Tick() {if(Playing)Step(1);}
        public Bitmap Render(double hour)
        {return PixelArt.Scene(1,Selection==4,Frame,hour,Selection==5,Selection>=1&&Selection<=3?(WorkAction)Selection:WorkAction.Idle,Frame);}
    }

    sealed class DeveloperDialog : Form
    {
        internal readonly AnimationPreview Preview = new AnimationPreview();
        readonly Timer timer = new Timer {Interval=125};
        readonly PixelSlider timeline;
        readonly PixelLabel position;
        readonly EmberButton play;
        readonly PreviewCanvas canvas;
        bool syncing;
        double hour=12;
        public DeveloperDialog()
        {
            Text=L.Text("Soul Revive · Geliştirici paneli","Soul Revive · Developer panel");
            ClientSize=new Size(728,640);AutoScaleMode=AutoScaleMode.None;
            BackColor=Theme.Background;ForeColor=Theme.Text;Font=PixelFont.Create(22);
            FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=false;MinimizeBox=false;
            StartPosition=FormStartPosition.CenterParent;ShowInTaskbar=false;Icon=PixelArt.MakeIcon();
            Controls.Add(new PixelLabel(L.Text("ANİMASYON","ANIMATION"),new Rectangle(20,18,130,36),Theme.Gold,11));
            var choice=new PixelCombo {Bounds=new Rectangle(150,18,290,36),AccessibleName=L.Text("Animasyon seç","Select animation")};
            choice.Items.AddRange(new[]{L.Text("Bekleme","Idle"),L.Text("Kılıç bileme","Sharpen sword"),L.Text("Harita inceleme","Read map"),L.Text("Odun fırlatma","Throw log"),L.Text("Uyku / mola","Sleep / break"),L.Text("Meyhane / Solaire","Tavern / Solaire")});
            choice.SelectedIndex=0;Controls.Add(choice);
            var light=new PixelCombo {Bounds=new Rectangle(460,18,248,36),AccessibleName=L.Text("Sahne ışığı","Scene lighting")};
            light.Items.AddRange(new[]{L.Text("Gündüz","Day"),L.Text("Şafak","Dawn"),L.Text("Gece","Night")});light.SelectedIndex=0;Controls.Add(light);
            canvas=new PreviewCanvas {Bounds=new Rectangle(20,76,688,368),DrawScene=delegate{return Preview.Render(hour);}};Controls.Add(canvas);
            position=new PixelLabel("",new Rectangle(20,454,688,26),Theme.Text,11);Controls.Add(position);
            timeline=new PixelSlider {Bounds=new Rectangle(20,488,688,40),Minimum=0,Maximum=Preview.Length-1,SmallChange=1,LargeChange=8,TickFrequency=8,AccessibleName=L.Text("Animasyon karesi","Animation frame")};Controls.Add(timeline);
            play=Button("",20,544,148,delegate {Preview.Playing=!Preview.Playing;RefreshPreview();});
            Button(L.Text("< Önceki kare","< Previous frame"),180,544,166,delegate {Preview.Playing=false;Preview.Step(-1);RefreshPreview();});
            Button(L.Text("Sonraki kare >","Next frame >"),358,544,166,delegate {Preview.Playing=false;Preview.Step(1);RefreshPreview();});
            Button(L.Text("Başa sar","Restart"),536,544,172,delegate {Preview.Seek(0);RefreshPreview();});
            Controls.Add(new PixelLabel(L.Text("Hız","Speed"),new Rectangle(20,594,60,30),Theme.Muted,10));
            var speed=new PixelCombo {Bounds=new Rectangle(84,590,160,34),AccessibleName=L.Text("Oynatma hızı","Playback speed")};
            speed.Items.AddRange(new[]{"0.5x","1x","2x"});speed.SelectedIndex=1;Controls.Add(speed);
            Controls.Add(new PixelLabel(L.Text("4x piksel önizleme · F8 ile aç","4x pixel preview · Open with F8"),new Rectangle(268,590,440,34),Theme.Muted,10));
            choice.SelectedIndexChanged+=delegate {Preview.Select(choice.SelectedIndex);RefreshPreview();};
            light.SelectedIndexChanged+=delegate {hour=light.SelectedIndex==1?5.5:light.SelectedIndex==2?22:12;canvas.Invalidate();};
            speed.SelectedIndexChanged+=delegate {timer.Interval=speed.SelectedIndex==0?250:speed.SelectedIndex==2?63:125;};
            timeline.MouseDown+=delegate {Preview.Playing=false;RefreshPreview();};
            timeline.ValueChanged+=delegate {if(!syncing){Preview.Playing=false;Preview.Seek(timeline.Value);RefreshPreview();}};
            timer.Tick+=delegate {Preview.Tick();RefreshPreview();};
            Shown+=delegate {timer.Start();};
            RefreshPreview();
        }
        EmberButton Button(string text,int x,int y,int width,Action action)
        {var button=new EmberButton(text,new Rectangle(x,y,width,36),false);button.Click+=delegate {action();};Controls.Add(button);return button;}
        void RefreshPreview()
        {
            syncing=true;timeline.Maximum=Preview.Length-1;timeline.Value=Preview.Frame;syncing=false;
            play.Text=Preview.Playing?L.Text("Duraklat","Pause"):L.Text("Oynat","Play");
            position.Text=L.Text("KARE ","FRAME ")+Preview.Frame+" / "+(Preview.Length-1)+"    ·    "+(Preview.Frame/8.0).ToString("0.00")+" s";
            position.Invalidate();canvas.Invalidate();
        }
        protected override bool ProcessCmdKey(ref Message msg,Keys keyData)
        {if(keyData==Keys.Escape){Close();return true;}return base.ProcessCmdKey(ref msg,keyData);}
        protected override void Dispose(bool disposing)
        {if(disposing){timer.Stop();timer.Dispose();if(Icon!=null)Icon.Dispose();}base.Dispose(disposing);}
        sealed class PreviewCanvas : Control
        {
            public Func<Bitmap> DrawScene;
            public PreviewCanvas(){DoubleBuffered=true;AccessibleName=L.Text("Animasyon önizlemesi","Animation preview");}
            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);if(DrawScene==null)return;
                e.Graphics.InterpolationMode=InterpolationMode.NearestNeighbor;e.Graphics.PixelOffsetMode=PixelOffsetMode.Half;
                using(var scene=DrawScene())e.Graphics.DrawImage(scene,ClientRectangle);
            }
        }
    }
}
