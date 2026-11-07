using System;
using System.IO;
using System.Numerics;
using System.Reflection;
using Raylib_cs;

namespace SoulsRevive.Cross
{
    sealed class CrossCompanion : IDisposable
    {
        readonly Preferences p;
        readonly TimerEngine engine;
        readonly WorkRoutine workRoutine = new WorkRoutine();
        readonly CrossAudio audio;

        Font font;
        Texture2D sceneTexture;
        Texture2D previewTexture;

        int frame, chillFrame;
        float frameTimer, saveTimer;
        bool quit;
        Preferences draft;
        double devTimer;
        string saveError = string.Empty;


        // Overlays: 0 = none, 1 = settings, 2 = statistics, 3 = developer
        int activeOverlay;
        int settingsTab; // 0 = general, 1 = sounds, 2 = knight

        // Developer panel state
        int devActionIndex;
        int devFrame;
        bool devPlaying = true;
        float devSpeed = 1.0f;
        int devLighting; // 0 = day, 1 = dawn, 2 = night
        int DevLength => devActionIndex == 5 ? PixelArt.TavernArrivalFrames + PixelArt.TavernCycleFrames : 112;

        static Texture2D CreateTexture()
        {
            Image image = Raylib.GenImageColor(172, 92, Color.Black);
            Texture2D texture = Raylib.LoadTextureFromImage(image);
            Raylib.UnloadImage(image);
            Raylib.SetTextureFilter(texture, TextureFilter.Point);
            return texture;
        }

        static unsafe void RenderScene(Texture2D texture, double health, bool rest, int frame,
            double hour, bool chill, WorkAction action, int actionFrame, KnightEquipment equipment)
        {
            using var image = PixelArt.EquipmentScene(equipment, health, rest, frame, hour, chill, action, actionFrame);
            fixed (uint* pixels = image.Canvas.Pixels) Raylib.UpdateTexture(texture, pixels);
        }

        void ApplyTopmost()
        {
            if (p.AlwaysOnTop) Raylib.SetWindowState(ConfigFlags.TopmostWindow);
            else Raylib.ClearWindowState(ConfigFlags.TopmostWindow);
        }

        void RestorePosition()
        {
            for (int i = 0; i < Raylib.GetMonitorCount(); i++)
            {
                Vector2 origin = Raylib.GetMonitorPosition(i);
                if (p.X >= origin.X && p.Y >= origin.Y && p.X + 384 <= origin.X + Raylib.GetMonitorWidth(i)
                    && p.Y + 668 <= origin.Y + Raylib.GetMonitorHeight(i))
                { Raylib.SetWindowPosition(p.X, p.Y); return; }
            }
        }

        void Save()
        {
            Vector2 position = Raylib.GetWindowPosition();
            p.X = (int)position.X; p.Y = (int)position.Y;
            saveError = Storage.Save(p) ? string.Empty : Storage.LastError;
        }

        void OpenSettings()
        {
            draft = new Preferences();
            foreach (var field in typeof(Preferences).GetFields()) field.SetValue(draft, field.GetValue(p));
            activeOverlay = 1;
        }

        void CancelSettings()
        {
            draft = null;
            L.Language = p.Language;
            CrossTheme.Light = p.LightMode;
        }

        void ApplySettings()
        {
            engine.ApplyDurations(draft.WorkMinutes, draft.BreakMinutes);
            p.Language = draft.Language; p.LightMode = draft.LightMode;
            p.AlwaysOnTop = draft.AlwaysOnTop; p.Sound = draft.Sound;
            p.AudioEnabled = draft.AudioEnabled;
            p.FireVolume = draft.FireVolume; p.AmbienceVolume = draft.AmbienceVolume; p.MusicVolume = draft.MusicVolume;
            p.ArmorStyle = draft.ArmorStyle; p.SwordStyle = draft.SwordStyle; p.ShieldStyle = draft.ShieldStyle;
            ApplyTopmost();
            audio.Apply(p);
            Save();
            CancelSettings();
        }

        public CrossCompanion()
        {
            p = Storage.Load();
            engine = new TimerEngine(p);
            L.Language = p.Language;
            CrossTheme.Light = p.LightMode;
            PixelArt.Equipped = KnightEquipment.From(p);

            Raylib.SetConfigFlags(ConfigFlags.AlwaysRunWindow | ConfigFlags.HighDpiWindow);
            Raylib.InitWindow(384, 668, "Soul Revive");
            Raylib.SetTargetFPS(60);
            Raylib.SetExitKey(KeyboardKey.Null);
            ApplyTopmost();
            RestorePosition();

            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("PixelFont.ttf"))
            {
                if (stream == null) throw new InvalidOperationException("Embedded font is missing.");
                byte[] data = new byte[stream.Length];
                stream.ReadExactly(data);
                string extra = "ĞğİıŞş←→↑↓–—";
                var codepoints = new int[256 + extra.Length];
                for (int i = 0; i < 256; i++) codepoints[i] = 32 + i;
                for (int i = 0; i < extra.Length; i++) codepoints[256 + i] = extra[i];
                font = Raylib.LoadFontFromMemory(".ttf", data, 32, codepoints, codepoints.Length);
                if (!Raylib.IsFontValid(font)) throw new InvalidOperationException("Cannot load the pixel font.");
                Raylib.SetTextureFilter(font.Texture, TextureFilter.Point);
            }
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("AppIcon.png"))
            using (var data = new MemoryStream())
            {
                stream.CopyTo(data);
                Image icon = Raylib.LoadImageFromMemory(".png", data.ToArray());
                Raylib.SetWindowIcon(icon);
                Raylib.UnloadImage(icon);
            }
            sceneTexture = CreateTexture();
            previewTexture = CreateTexture();
            audio = new CrossAudio();
            audio.Apply(p);

            engine.Changed += reason =>
            {
                if (reason == "mode") chillFrame = 0;
                if (reason == "mode" || reason == "due" || reason == "revived" || reason == "break" || reason == "cancel")
                    workRoutine.Reset();
                Save();
                if (p.Sound && (reason == "due" || reason == "revived")) audio.Remind();
            };
        }

        public void Run(int smokeFrames = 0, string screenshot = null)
        {
            DateTime previous = DateTime.UtcNow;
            int rendered = 0;
            while (!quit && !Raylib.WindowShouldClose())
            {
                DateTime now = DateTime.UtcNow;
                double elapsed = (now - previous).TotalSeconds;
                previous = now;
                if (elapsed > 5 || elapsed < 0)
                {
                    if (!p.Chill && !p.Paused) engine.TogglePause();
                    elapsed = 0;
                }
                Update((float)elapsed);
                Draw();
                if (smokeFrames > 0 && ++rendered >= smokeFrames)
                {
                    if (screenshot != null) RunUiChecks(screenshot);
                    break;
                }
            }
            Save();
        }

        void Capture(string path)
        {
            // TakeScreenshot prefixes the working directory on some raylib builds.
            // ExportImage accepts absolute paths on every supported platform.
            Image image = Raylib.LoadImageFromScreen();
            try { if (!Raylib.ExportImage(image, path)) throw new IOException("Screenshot export failed: " + path); }
            finally { Raylib.UnloadImage(image); }
        }

        void RunUiChecks(string screenshot)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(screenshot));
            Capture(screenshot);
            foreach (char letter in "ÇçĞğİıÖöŞşÜü")
                if (Raylib.GetGlyphIndex(font, letter) == Raylib.GetGlyphIndex(font, '?'))
                    throw new InvalidOperationException("Missing Turkish glyph: " + letter);
            double remaining = p.Remaining, total = p.TotalWorkSeconds;
            int armor = p.ArmorStyle;
            OpenSettings(); draft.ArmorStyle = (armor + 1) % 3; draft.WorkMinutes++;
            CancelSettings(); activeOverlay = 0;
            if (p.Remaining != remaining || p.ArmorStyle != armor) throw new InvalidOperationException("Cancel changed saved settings.");
            OpenSettings(); draft.ArmorStyle = (armor + 1) % 3;
            ApplySettings(); activeOverlay = 0;
            if (p.Remaining != remaining || p.TotalWorkSeconds != total || p.ArmorStyle == armor)
                throw new InvalidOperationException("Equipment save changed the timer or failed to apply.");
            foreach (string language in new[] { "tr", "en" })
            foreach (bool light in new[] { false, true })
            {
                p.Language = language; p.LightMode = light; L.Language = language; CrossTheme.Light = light;
                for (int view = 0; view < 8; view++)
                {
                    CancelSettings(); activeOverlay = 0;
                    p.Chill = view == 1; p.InBreak = view == 2;
                    if (view >= 3 && view <= 5) { OpenSettings(); settingsTab = view - 3; }
                    if (view == 6) activeOverlay = 2;
                    if (view == 7) { activeOverlay = 3; devActionIndex = 2; devFrame = 50; }
                    Draw(); Draw(); // Populate both swap-chain buffers before reading back.
                    string name = $"{Path.GetFileNameWithoutExtension(screenshot)}-{language}-{(light ? "light" : "dark")}-{view}.png";
                    Capture(Path.Combine(Path.GetDirectoryName(screenshot), name));
                }
            }
            CancelSettings(); activeOverlay = 0;
            Console.WriteLine("PASS native window, Turkish glyphs, settings transactions and 32 UI previews");
        }
        void Update(float dt)
        {
            // F8 toggles developer panel
            if (Raylib.IsKeyPressed(KeyboardKey.F8))
            {
                CancelSettings();
                activeOverlay = activeOverlay == 3 ? 0 : 3;
            }
            if (Raylib.IsKeyPressed(KeyboardKey.Escape) && activeOverlay != 0)
            {
                CancelSettings();
                activeOverlay = 0;
            }

            // Timer advance
            engine.Advance(dt);

            // Animation frame ticks (~8 FPS = 125ms)
            frameTimer += dt;
            while (frameTimer >= 0.125f)
            {
                frameTimer -= 0.125f;
                frame++;
                if (p.Chill) chillFrame++;
                workRoutine.Advance(!p.Chill && !p.InBreak && !p.Paused && !engine.Due);
            }

            // Developer animation advance
            if (activeOverlay == 3 && devPlaying)
            {
                devTimer += dt * devSpeed;
                while (devTimer >= 0.125) { devTimer -= 0.125; devFrame = (devFrame + 1) % DevLength; }
            }

            // Audio loop check
            audio.Update();

            // Auto-save every 5 seconds
            saveTimer += dt;
            if (saveTimer >= 5.0f)
            {
                saveTimer = 0;
                Save();
            }
        }

        void Draw()
        {
            Raylib.BeginDrawing();
            Color bg = HexColor(CrossTheme.BackgroundHex);
            Raylib.ClearBackground(bg);

            // Main Companion View
            DrawCompanion();

            // Overlay Dialogs
            if (activeOverlay == 1) DrawSettingsOverlay();
            else if (activeOverlay == 2) DrawStatsOverlay();
            else if (activeOverlay == 3) DrawDevOverlay();

            if (saveError.Length > 0) DrawCenteredText(L.Text("Kayıt başarısız: diski kontrol et", "Save failed: check disk access"), 10, 640, 364, 16, Color.Red);
            Raylib.EndDrawing();
        }

        void DrawCompanion()
        {
            Color bg = HexColor(CrossTheme.BackgroundHex);
            Vector2 mouse = Raylib.GetMousePosition();
            bool click = activeOverlay == 0 && Raylib.IsMouseButtonPressed(MouseButton.Left);

            // Border Frame
            DrawPixelFrame(0, 0, 384, 668, HexColor(CrossTheme.BackgroundHex));

            // Header Title
            DrawPixelText("SOUL REVIVE", 20, 14, 22, HexColor(CrossTheme.GoldHex));
            DrawPixelText(L.Text("KÜÇÜK MOLALAR. YENİDEN DOĞ.", "SMALL BREAKS. RISE AGAIN."), 20, 42, 14, HexColor(CrossTheme.MutedHex));

            // Title Buttons
            // Gear / Settings
            if (DrawButton("", 272, 16, 28, 28, false, mouse, click))
            {
                OpenSettings();
            }
            Color gear = HexColor(CrossTheme.GoldHex);
            Raylib.DrawRectangle(281, 25, 10, 10, gear);
            Raylib.DrawRectangle(284, 22, 4, 16, gear);
            Raylib.DrawRectangle(278, 28, 16, 4, gear);
            Raylib.DrawRectangle(284, 28, 4, 4, HexColor(CrossTheme.PanelHex));
            // Minimize
            if (DrawButton("_", 304, 16, 28, 28, false, mouse, click))
            {
                Raylib.MinimizeWindow();
            }
            // Close
            if (DrawButton("X", 336, 16, 28, 28, false, mouse, click))
            {
                quit = true;
                return;
            }

            // Mode Buttons
            bool workActive = !p.Chill;
            if (DrawButton(L.Text("ÇALIŞIYORUM", "WORKING"), 20, 72, 168, 30, workActive, mouse, click))
            {
                engine.SetChill(false);
            }
            if (DrawButton(L.Text("ÇALIŞMIYORUM", "CHILL MODE"), 196, 72, 168, 30, !workActive, mouse, click))
            {
                engine.SetChill(true);
            }

            // Scene Canvas Render
            RenderScene(sceneTexture, p.Chill ? 1 : engine.Health, p.InBreak, p.Chill ? chillFrame : frame,
                DayCycle.Hour(p), p.Chill, workRoutine.Action, workRoutine.Frame, KnightEquipment.From(p));
            // Outer canvas frame
            DrawPixelFrame(18, 112, 348, 188, HexColor(CrossTheme.PanelHex));
            Raylib.DrawTexturePro(sceneTexture, new Rectangle(0, 0, 172, 92), new Rectangle(20, 114, 344, 184), Vector2.Zero, 0, Color.White);

            // Status Banner
            string state = p.Chill ? L.Text("SOLAIRE İLE BİR KADEH MOLASI", "A TOAST WITH SOLAIRE")
                : p.Paused ? L.Text("ATEŞİN SENİ BEKLİYOR", "YOUR FIRE AWAITS")
                : p.InBreak ? L.Text("ŞÖVALYE UYUYOR. SEN BİRAZ YÜRÜ.", "KNIGHT ASLEEP. TIME FOR A WALK.")
                : engine.Due ? L.Text("GECE ÇÖKTÜ. MOLA ZAMANI.", "NIGHT HAS FALLEN. TAKE A BREAK.")
                : L.Text("ODAKLAN. ATEŞİ CANLI TUT.", "FOCUS. KEEP THE FIRE ALIVE.");
            DrawPixelFrame(20, 308, 344, 28, HexColor(CrossTheme.PanelHex));
            DrawCenteredText(state, 20, 312, 344, 14, HexColor(CrossTheme.GoldHex));

            // Main Clock
            string clockText = p.Chill ? L.Text("Şerefe!", "Cheers!") : FormatSeconds(p.Remaining);
            DrawCenteredText(clockText, 20, 344, 344, 44, HexColor(CrossTheme.TextHex));

            // Subtitle
            string sub = p.Chill ? L.Text("Solaire ile meyhanedesin. İş bekleyebilir.", "At the tavern with Solaire. Work can wait.")
                : p.Paused ? L.Text("Devam et ile yolculuğunu sürdür.", "Select Resume to continue.")
                : p.InBreak ? L.Text("Ayağa kalk, yürü ve biraz esne.", "Stand up, walk and stretch.")
                : L.Text("Bir sonraki molaya kalan süre", "Time until your next break");
            DrawCenteredText(sub, 20, 408, 344, 16, HexColor(CrossTheme.MutedHex));

            // HP Bar
            double hp = p.Chill ? 1.0 : Math.Max(0, Math.Min(1, engine.Health));
            DrawPixelText(L.Text("CAN", "HP"), 20, 446, 14, HexColor(CrossTheme.MutedHex));
            Raylib.DrawRectangle(58, 451, 252, 8, HexColor(CrossTheme.PanelHex));
            Color hpColor = (p.InBreak || p.Chill) ? HexColor(CrossTheme.GreenHex) : HexColor(CrossTheme.GoldHex);
            Raylib.DrawRectangle(58, 451, (int)(252 * hp), 8, hpColor);
            for (int i = 1; i < 21; i++)
            {
                Raylib.DrawLine(58 + i * 12, 451, 58 + i * 12, 459, bg);
            }
            DrawCenteredText($"{(int)(hp * 100)}%", 316, 446, 48, 14, HexColor(CrossTheme.MutedHex));

            // Primary & Pause Buttons
            string primText = p.InBreak ? L.Text("MOLAYI BİTİR", "FINISH BREAK") : L.Text("MOLA VER", "TAKE A BREAK");
            if (DrawButton(primText, 20, 480, 168, 42, true, mouse, click))
            {
                if (p.InBreak) engine.CancelBreak();
                else engine.BeginBreak();
            }

            string pauseText = p.Paused ? L.Text("DEVAM ET", "RESUME") : L.Text("DURAKLAT", "PAUSE");
            if (DrawButton(pauseText, 196, 480, 168, 42, false, mouse, click))
            {
                engine.TogglePause();
            }

            // Statistics Button
            if (DrawButton(L.Text("İSTATİSTİKLER >", "STATISTICS >"), 20, 538, 344, 32, false, mouse, click))
            {
                activeOverlay = 2;
            }

            // Bottom Work Totals
            string todayStr = $"{L.Text("BUGÜN", "TODAY")}: {FormatWorkDuration(p.TodayWorkSeconds)}";
            string totalStr = $"{L.Text("TOPLAM", "TOTAL")}: {FormatWorkDuration(p.TotalWorkSeconds)}";
            DrawCenteredText(todayStr, 20, 584, 164, 14, HexColor(CrossTheme.MutedHex));
            DrawCenteredText(totalStr, 200, 584, 164, 14, HexColor(CrossTheme.MutedHex));
        }

        void DrawSettingsOverlay()
        {
            Preferences p = draft;
            Vector2 mouse = Raylib.GetMousePosition();
            bool click = Raylib.IsMouseButtonPressed(MouseButton.Left);

            // Dim background
            Raylib.DrawRectangle(0, 0, 384, 668, new Color(0, 0, 0, 180));

            // Dialog Frame
            int dw = 352, dh = 588, dx = 16, dy = 40;
            DrawPixelFrame(dx, dy, dw, dh, HexColor(CrossTheme.BackgroundHex));

            // Title
            DrawPixelText(L.Text("AYARLAR", "SETTINGS"), dx + 16, dy + 16, 22, HexColor(CrossTheme.GoldHex));

            // Tabs
            if (DrawButton(L.Text("Genel", "General"), dx + 14, dy + 50, 100, 28, settingsTab == 0, mouse, click)) settingsTab = 0;
            if (DrawButton(L.Text("Sesler", "Sounds"), dx + 124, dy + 50, 100, 28, settingsTab == 1, mouse, click)) settingsTab = 1;
            if (DrawButton(L.Text("Şövalye", "Knight"), dx + 234, dy + 50, 104, 28, settingsTab == 2, mouse, click)) settingsTab = 2;

            int cy = dy + 96;

            if (settingsTab == 0)
            {
                // General Tab
                DrawPixelText(L.Text("Çalışma süresi (dk):", "Work time (min):"), dx + 20, cy, 16, HexColor(CrossTheme.TextHex));
                if (DrawButton("-", dx + 210, cy - 4, 32, 28, false, mouse, click) && p.WorkMinutes > 1) { p.WorkMinutes--; }
                DrawCenteredText(p.WorkMinutes.ToString(), dx + 242, cy, 40, 18, HexColor(CrossTheme.GoldHex));
                if (DrawButton("+", dx + 282, cy - 4, 32, 28, false, mouse, click) && p.WorkMinutes < 180) { p.WorkMinutes++; }

                cy += 44;
                DrawPixelText(L.Text("Mola süresi (dk):", "Break time (min):"), dx + 20, cy, 16, HexColor(CrossTheme.TextHex));
                if (DrawButton("-", dx + 210, cy - 4, 32, 28, false, mouse, click) && p.BreakMinutes > 1) { p.BreakMinutes--; }
                DrawCenteredText(p.BreakMinutes.ToString(), dx + 242, cy, 40, 18, HexColor(CrossTheme.GoldHex));
                if (DrawButton("+", dx + 282, cy - 4, 32, 28, false, mouse, click) && p.BreakMinutes < 30) { p.BreakMinutes++; }

                cy += 48;
                DrawPixelText(L.Text("Dil / Language:", "Language:"), dx + 20, cy, 16, HexColor(CrossTheme.TextHex));
                if (DrawButton(p.Language == "tr" ? "Türkçe" : "English", dx + 210, cy - 4, 104, 28, false, mouse, click))
                {
                    p.Language = p.Language == "tr" ? "en" : "tr";
                    L.Language = p.Language;
                }

                cy += 44;
                DrawPixelText(L.Text("Tema / Theme:", "Theme:"), dx + 20, cy, 16, HexColor(CrossTheme.TextHex));
                if (DrawButton(CrossTheme.Light ? L.Text("Aydınlık", "Light") : L.Text("Karanlık", "Dark"), dx + 210, cy - 4, 104, 28, false, mouse, click))
                {
                    CrossTheme.Light = !CrossTheme.Light;
                    p.LightMode = CrossTheme.Light;
                }

                cy += 40;
                if (DrawButton(p.AlwaysOnTop ? L.Text("[X] Üstte tut", "[X] Always on top") : L.Text("[ ] Üstte tut", "[ ] Always on top"), dx + 20, cy, 312, 30, false, mouse, click)) p.AlwaysOnTop = !p.AlwaysOnTop;
                cy += 40;
                if (DrawButton(p.Sound ? L.Text("[X] Hatırlatma sesi", "[X] Reminder sound") : L.Text("[ ] Hatırlatma sesi", "[ ] Reminder sound"), dx + 20, cy, 312, 30, false, mouse, click)) p.Sound = !p.Sound;
                cy += 40;
                string devButtonText = L.Text("Geliştirici Paneli (F8)", "Developer Panel (F8)");
                if (DrawButton(devButtonText, dx + 20, cy, 312, 32, false, mouse, click))
                {
                    CancelSettings();
                    activeOverlay = 3;
                }
            }
            else if (settingsTab == 1)
            {
                // Sounds Tab
                string audioToggle = p.AudioEnabled ? L.Text("[X] Müzik ve sesleri çal", "[X] Play music and ambience")
                    : L.Text("[ ] Müzik ve sesleri çal", "[ ] Play music and ambience");
                if (DrawButton(audioToggle, dx + 20, cy, 312, 32, p.AudioEnabled, mouse, click))
                {
                    p.AudioEnabled = !p.AudioEnabled;

                }

                cy += 48;
                DrawVolumeControl(dx + 20, cy, L.Text("ATEŞ / ÇITIRTI", "FIRE / CRACKLE"), ref p.FireVolume, mouse, click);
                cy += 52;
                DrawVolumeControl(dx + 20, cy, L.Text("AMBİYANS / RÜZGÂR", "AMBIENCE / WIND"), ref p.AmbienceVolume, mouse, click);
                cy += 52;
                DrawVolumeControl(dx + 20, cy, L.Text("MÜZİK / EZGİ", "MUSIC / MELODY"), ref p.MusicVolume, mouse, click);
            }
            else if (settingsTab == 2)
            {
                // Knight Equipment Tab
                string[] armorNames = { L.Text("Çelik şövalye", "Steel knight"), L.Text("Kara şövalye", "Black knight"), L.Text("Altın muhafız", "Golden guardian") };
                string[] swordNames = { L.Text("Uzun kılıç", "Longsword"), L.Text("Geniş kılıç", "Broadsword"), L.Text("Estoc", "Estoc") };
                string[] shieldNames = { L.Text("Haçlı kalkan", "Cross shield"), L.Text("Yuvarlak kalkan", "Round shield"), L.Text("Mavi muhafız", "Blue guardian") };

                DrawPixelText(L.Text("Zırh:", "Armor:"), dx + 20, cy, 14, HexColor(CrossTheme.TextHex));
                if (DrawButton(armorNames[p.ArmorStyle], dx + 120, cy - 4, 194, 26, false, mouse, click))
                {
                    p.ArmorStyle = (p.ArmorStyle + 1) % 3;

                }

                cy += 38;
                DrawPixelText(L.Text("Kılıç:", "Sword:"), dx + 20, cy, 14, HexColor(CrossTheme.TextHex));
                if (DrawButton(swordNames[p.SwordStyle], dx + 120, cy - 4, 194, 26, false, mouse, click))
                {
                    p.SwordStyle = (p.SwordStyle + 1) % 3;

                }

                cy += 38;
                DrawPixelText(L.Text("Kalkan:", "Shield:"), dx + 20, cy, 14, HexColor(CrossTheme.TextHex));
                if (DrawButton(shieldNames[p.ShieldStyle], dx + 120, cy - 4, 194, 26, false, mouse, click))
                {
                    p.ShieldStyle = (p.ShieldStyle + 1) % 3;

                }

                cy += 46;
                RenderScene(previewTexture, 1, false, frame, 12, false, WorkAction.Idle, 0, KnightEquipment.From(p));
                // Live equipment preview
                DrawPixelFrame(dx + 20, cy, 312, 164, HexColor(CrossTheme.PanelHex));
                Raylib.DrawTexturePro(previewTexture, new Rectangle(35, 12, 104, 80), new Rectangle(dx + 72, cy + 2, 208, 160), Vector2.Zero, 0, Color.White);
            }

            if (DrawButton(L.Text("KAYDET", "SAVE"), dx + 20, dy + dh - 48, 150, 36, true, mouse, click))
            {
                ApplySettings();
                activeOverlay = 0;
            }
            if (DrawButton(L.Text("İPTAL", "CANCEL"), dx + 182, dy + dh - 48, 150, 36, false, mouse, click))
            {
                CancelSettings();
                activeOverlay = 0;
            }
        }
        void DrawVolumeControl(int x, int y, string label, ref int volume, Vector2 mouse, bool click)
        {
            DrawPixelText(label, x, y, 14, HexColor(CrossTheme.TextHex));
            if (DrawButton("-", x + 180, y - 4, 30, 26, false, mouse, click) && volume > 0)
            {
                volume = Math.Max(0, volume - 10);

            }
            DrawCenteredText($"{volume}%", x + 210, y, 40, 14, HexColor(CrossTheme.GoldHex));
            if (DrawButton("+", x + 250, y - 4, 30, 26, false, mouse, click) && volume < 100)
            {
                volume = Math.Min(100, volume + 10);

            }
        }

        void DrawStatsOverlay()
        {
            Vector2 mouse = Raylib.GetMousePosition();
            bool click = Raylib.IsMouseButtonPressed(MouseButton.Left);

            Raylib.DrawRectangle(0, 0, 384, 668, new Color(0, 0, 0, 180));

            int dw = 344, dh = 400, dx = 20, dy = 130;
            DrawPixelFrame(dx, dy, dw, dh, HexColor(CrossTheme.BackgroundHex));

            DrawPixelText(L.Text("İSTATİSTİKLER", "STATISTICS"), dx + 20, dy + 20, 22, HexColor(CrossTheme.GoldHex));

            int cy = dy + 64;
            DrawPixelText($"{L.Text("Bugün Çalışma", "Today's Work")}:", dx + 20, cy, 16, HexColor(CrossTheme.TextHex));
            DrawPixelText(FormatWorkDuration(p.TodayWorkSeconds), dx + 200, cy, 16, HexColor(CrossTheme.GoldHex));

            cy += 40;
            DrawPixelText($"{L.Text("Toplam Çalışma", "Lifetime Work")}:", dx + 20, cy, 16, HexColor(CrossTheme.TextHex));
            DrawPixelText(FormatWorkDuration(p.TotalWorkSeconds), dx + 200, cy, 16, HexColor(CrossTheme.GoldHex));

            cy += 40;
            DrawPixelText($"{L.Text("Tamamlanan Tur", "Completed Rounds")}:", dx + 20, cy, 16, HexColor(CrossTheme.TextHex));
            DrawPixelText(p.CompletedSessions.ToString(), dx + 200, cy, 16, HexColor(CrossTheme.GoldHex));

            cy += 40;
            DrawPixelText($"{L.Text("Bugünkü Molalar", "Today's Breaks")}:", dx + 20, cy, 16, HexColor(CrossTheme.TextHex));
            DrawPixelText(p.Breaks.ToString(), dx + 200, cy, 16, HexColor(CrossTheme.GoldHex));

            cy += 40;
            DrawPixelText($"{L.Text("Hareket Süresi", "Movement Time")}:", dx + 20, cy, 16, HexColor(CrossTheme.TextHex));
            DrawPixelText($"{p.MovementSeconds / 60} {L.Text("dk", "min")}", dx + 200, cy, 16, HexColor(CrossTheme.GoldHex));

            if (DrawButton(L.Text("KAPAT", "CLOSE"), dx + 20, dy + dh - 50, 304, 34, true, mouse, click))
            {
                activeOverlay = 0;
            }
        }

        void DrawDevOverlay()
        {
            Vector2 mouse = Raylib.GetMousePosition();
            bool click = Raylib.IsMouseButtonPressed(MouseButton.Left);

            Raylib.DrawRectangle(0, 0, 384, 668, new Color(0, 0, 0, 200));

            int dw = 364, dh = 600, dx = 10, dy = 34;
            DrawPixelFrame(dx, dy, dw, dh, HexColor(CrossTheme.BackgroundHex));

            DrawPixelText("DEVELOPER PANEL (F8)", dx + 16, dy + 16, 20, HexColor(CrossTheme.GoldHex));
            if (DrawButton("X", dx + dw - 40, dy + 14, 26, 26, false, mouse, click))
            {
                activeOverlay = 0;
                return;
            }

            // Animation Selector
            string[] anims = { "Idle", "Sharpen", "Map", "Log", "Sleep", "Tavern" };
            for (int i = 0; i < anims.Length; i++)
            {
                int bx = dx + 12 + (i % 3) * 114;
                int by = dy + 50 + (i / 3) * 32;
                if (DrawButton(anims[i], bx, by, 110, 28, devActionIndex == i, mouse, click))
                {
                    devActionIndex = i;
                    devFrame = 0;
                }
            }

            // Render Preview Canvas
            WorkAction curAction = devActionIndex == 1 ? WorkAction.Sharpen : devActionIndex == 2 ? WorkAction.Map : devActionIndex == 3 ? WorkAction.TendFire : WorkAction.Idle;
            bool isSleep = devActionIndex == 4;
            bool isTavern = devActionIndex == 5;
            double hour = devLighting == 0 ? 12.0 : devLighting == 1 ? 6.0 : 22.0;

            RenderScene(previewTexture, 1, isSleep, devFrame, hour, isTavern, curAction, devFrame % 112, KnightEquipment.From(p));
            DrawPixelFrame(dx + 8, dy + 122, 348, 188, HexColor(CrossTheme.PanelHex));
            Raylib.DrawTexturePro(previewTexture, new Rectangle(0, 0, 172, 92), new Rectangle(dx + 10, dy + 124, 344, 184), Vector2.Zero, 0, Color.White);

            int cy = dy + 320;
            // Controls: Play/Pause, Step
            if (DrawButton(devPlaying ? "PAUSE" : "PLAY", dx + 12, cy, 80, 30, devPlaying, mouse, click))
            {
                devPlaying = !devPlaying;
            }
            if (DrawButton("<-", dx + 98, cy, 40, 30, false, mouse, click)) { devPlaying = false; devFrame = Math.Max(0, devFrame - 1); }
            if (DrawButton("->", dx + 144, cy, 40, 30, false, mouse, click)) { devPlaying = false; devFrame = (devFrame + 1) % DevLength; }
            DrawCenteredText($"F: {devFrame}", dx + 190, cy + 6, 80, 16, HexColor(CrossTheme.TextHex));

            // Speed
            if (DrawButton("0.5x", dx + 266, cy, 40, 30, devSpeed == 0.5f, mouse, click)) devSpeed = 0.5f;
            if (DrawButton("1x", dx + 312, cy, 40, 30, devSpeed == 1.0f, mouse, click)) devSpeed = 1.0f;

            cy += 38;
            if (DrawButton("RESTART", dx + 12, cy, 100, 26, false, mouse, click)) { devFrame = 0; devTimer = 0; }
            if (DrawButton("2x", dx + 120, cy, 60, 26, devSpeed == 2, mouse, click)) devSpeed = 2;
            cy += 38;
            Raylib.DrawRectangle(dx + 12, cy, 340, 8, HexColor(CrossTheme.PanelHex));
            Raylib.DrawRectangle(dx + 12, cy, (int)(340.0 * devFrame / (DevLength - 1)), 8, HexColor(CrossTheme.GoldHex));
            if (Raylib.IsMouseButtonDown(MouseButton.Left) && Raylib.CheckCollisionPointRec(mouse, new Rectangle(dx + 12, cy - 8, 340, 24)))
            { devPlaying = false; devFrame = (int)Math.Clamp((mouse.X - dx - 12) / 340 * (DevLength - 1), 0, DevLength - 1); }
            cy += 32;
            // Lighting
            DrawPixelText("Lighting:", dx + 16, cy + 4, 14, HexColor(CrossTheme.TextHex));
            if (DrawButton("Day", dx + 90, cy, 70, 26, devLighting == 0, mouse, click)) devLighting = 0;
            if (DrawButton("Dawn", dx + 166, cy, 70, 26, devLighting == 1, mouse, click)) devLighting = 1;
            if (DrawButton("Night", dx + 242, cy, 70, 26, devLighting == 2, mouse, click)) devLighting = 2;
        }

        bool DrawButton(string text, int x, int y, int w, int h, bool primary, Vector2 mouse, bool click)
        {
            Rectangle rect = new Rectangle(x, y, w, h);
            bool hover = Raylib.CheckCollisionPointRec(mouse, rect);
            Color fill = primary
                ? HexColor(CrossTheme.Light ? "#81501F" : "#55432B")
                : HexColor(CrossTheme.PanelHex);

            if (hover)
            {
                fill = new Color((byte)Math.Min(255, fill.R + 25), (byte)Math.Min(255, fill.G + 25), (byte)Math.Min(255, fill.B + 25), fill.A);
            }

            DrawPixelFrame(x, y, w, h, fill);
            Color textColor = primary ? HexColor("#FFF1D6") : HexColor(CrossTheme.TextHex);
            DrawCenteredText(text, x, y + (h - 18) / 2, w, 16, textColor);

            return hover && click;
        }

        void DrawPixelFrame(int x, int y, int w, int h, Color fill)
        {
            Raylib.DrawRectangle(x, y, w, h, fill);
            Color line = HexColor(CrossTheme.LineHex);
            Color gold = HexColor(CrossTheme.GoldHex);
            Raylib.DrawRectangleLines(x, y, w, h, line);
            // 4 corner pixel dots
            Raylib.DrawRectangle(x, y, 4, 4, gold);
            Raylib.DrawRectangle(x + w - 4, y, 4, 4, gold);
            Raylib.DrawRectangle(x, y + h - 4, 4, 4, gold);
            Raylib.DrawRectangle(x + w - 4, y + h - 4, 4, 4, gold);
        }

        void DrawPixelText(string text, int x, int y, int size, Color color)
        {
            Raylib.DrawTextEx(font, text, new Vector2(x, y), size, 1, color);
        }

        void DrawCenteredText(string text, int x, int y, int w, int size, Color color)
        {
            Vector2 measurement = Raylib.MeasureTextEx(font, text, size, 1);
            int tx = x + (int)((w - measurement.X) / 2);
            Raylib.DrawTextEx(font, text, new Vector2(tx, y), size, 1, color);
        }

        static Color HexColor(string hex)
        {
            uint c = CrossCanvas.Hex(hex);
            return new Color((byte)(c & 0xFF), (byte)((c >> 8) & 0xFF), (byte)((c >> 16) & 0xFF), (byte)((c >> 24) & 0xFF));
        }

        static string FormatSeconds(double s)
        {
            int total = (int)Math.Max(0, Math.Ceiling(s));
            return $"{total / 60:00}:{total % 60:00}";
        }

        static string FormatWorkDuration(double s)
        {
            int total = (int)Math.Max(0, s);
            int h = total / 3600;
            int m = (total % 3600) / 60;
            int sec = total % 60;
            return L.Text($"{h}s {m}d {sec}sn", $"{h}h {m}m {sec}s");
        }

        public void Dispose()
        {
            audio?.Dispose();
            Raylib.UnloadTexture(sceneTexture);
            Raylib.UnloadTexture(previewTexture);
            Raylib.UnloadFont(font);
            Raylib.CloseWindow();
        }
    }
}
