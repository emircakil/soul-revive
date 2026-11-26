using System;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using System.Reflection;
using System.Threading.Tasks;
using Raylib_cs;

namespace SoulsRevive.Cross
{
    sealed class CrossCompanion : IDisposable
    {
        const int Width = 384, Height = 668;
        readonly Preferences p;
        readonly TimerEngine engine;
        readonly WorkRoutine workRoutine = new WorkRoutine();
        readonly CrossAudio audio;
        readonly DesktopSession session;
        readonly bool startMinimized;

        Font font;
        Texture2D sceneTexture;
        Texture2D previewTexture;
        // What each texture currently shows, so the scene is only re-rasterized when it changes.
        (int, int, bool, bool, bool, WorkAction, int, int, int, int, int, int) sceneKey, previewKey;

        int frame, chillFrame, currentFps, kneel;
        float frameTimer, saveTimer, sessionTimer, showTimer;
        bool quit, wasLocked;
        Preferences draft;
        bool draftAutostart, previewingAudio;
        double devTimer;
        string message = string.Empty;
        DateTime messageUntil;
        DateTime resetArmedUntil;

        // Overlays: 0 = none, 1 = settings, 2 = statistics, 3 = developer
        int activeOverlay;
        int settingsTab; // 0 general, 1 timer, 2 sounds, 3 knight, 4 updates

        Task<CrossUpdates.Release> updateCheck;
        string updateStatus = string.Empty;
        CrossUpdates.Release availableRelease;

        // Developer panel state
        int devActionIndex;
        int devFrame;
        bool devPlaying = true;
        float devSpeed = 1.0f;
        int devLighting; // 0 = day, 1 = dawn, 2 = night
        int DevLength => devActionIndex == 5 ? PixelArt.TavernArrivalFrames + PixelArt.TavernCycleFrames : WorkRoutine.Duration;

        static Texture2D CreateTexture()
        {
            Image image = Raylib.GenImageColor(172, 92, Color.Black);
            Texture2D texture = Raylib.LoadTextureFromImage(image);
            Raylib.UnloadImage(image);
            Raylib.SetTextureFilter(texture, TextureFilter.Point);
            return texture;
        }

        static unsafe void RenderScene(Texture2D texture, ref (int, int, bool, bool, bool, WorkAction, int, int, int, int, int, int) cached,
            double health, bool rest, int frame, double hour, bool chill, WorkAction action, int actionFrame, KnightEquipment equipment, int kneel = -1)
        {
            // Hour is quantized finely enough that the sky still drifts smoothly through a short round.
            var key = (frame, (int)Math.Round(hour * 400), rest, chill, health <= 0, action, actionFrame, equipment.Armor, equipment.Sword, equipment.Shield, 1, kneel);
            if (key.Equals(cached)) return;
            cached = key;
            using var image = PixelArt.EquipmentScene(equipment, health, rest, frame, hour, chill, action, actionFrame, kneel);
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
                if (p.X >= origin.X && p.Y >= origin.Y && p.X + Width <= origin.X + Raylib.GetMonitorWidth(i)
                    && p.Y + Height <= origin.Y + Raylib.GetMonitorHeight(i))
                { Raylib.SetWindowPosition(p.X, p.Y); return; }
            }
        }

        void Save(bool authoritative = false)
        {
            Vector2 position = Raylib.GetWindowPosition();
            p.X = (int)position.X; p.Y = (int)position.Y;
            if (!Storage.Save(p, authoritative)) Flash(Ui.SaveFailed + " " + Storage.LastError, 10);
        }

        void Flash(string text, double seconds) { message = text; messageUntil = DateTime.Now.AddSeconds(seconds); }

        void OpenSettings()
        {
            draft = new Preferences();
            foreach (var field in typeof(Preferences).GetFields()) if (!field.IsLiteral && !field.IsInitOnly) field.SetValue(draft, field.GetValue(p));
            draftAutostart = Autostart.Enabled;
            previewingAudio = false;
            activeOverlay = 1;
        }

        void CancelSettings()
        {
            if (previewingAudio) { previewingAudio = false; audio.Apply(p); }
            draft = null;
            L.Language = p.Language;
            CrossTheme.Light = p.LightMode;
        }

        void ApplySettings()
        {
            engine.ApplyDurations(draft.WorkMinutes, draft.BreakMinutes, draft.LongBreakEvery, draft.LongBreakMinutes);
            p.Language = draft.Language; p.LightMode = draft.LightMode;
            p.AlwaysOnTop = draft.AlwaysOnTop; p.Sound = draft.Sound;
            p.AudioEnabled = draft.AudioEnabled;
            p.FireVolume = draft.FireVolume; p.AmbienceVolume = draft.AmbienceVolume; p.MusicVolume = draft.MusicVolume;
            p.ArmorStyle = draft.ArmorStyle; p.SwordStyle = draft.SwordStyle; p.ShieldStyle = draft.ShieldStyle;
            p.DailyGoalMinutes = draft.DailyGoalMinutes; p.IdlePause = draft.IdlePause; p.IdleMinutes = draft.IdleMinutes; p.FocusOnDue = draft.FocusOnDue;
            p.Validate();
            if (draftAutostart != Autostart.Enabled)
            {
                try { Autostart.Set(draftAutostart); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Flash(L.Text("Otomatik başlatma ayarlanamadı: ", "Could not set start at sign-in: ") + ex.Message, 10); }
            }
            ApplyTopmost();
            previewingAudio = false;
            audio.Apply(p);
            Save();
            CancelSettings();
        }

        public CrossCompanion(bool startMinimized = false)
        {
            this.startMinimized = startMinimized;
            p = Storage.Load();
            engine = new TimerEngine(p);
            L.Language = p.Language;
            CrossTheme.Light = p.LightMode;
            PixelArt.Equipped = KnightEquipment.From(p);
            kneel = engine.Due ? PixelArt.KneelFrames : 0;

            Raylib.SetConfigFlags(ConfigFlags.AlwaysRunWindow | ConfigFlags.HighDpiWindow);
            Raylib.InitWindow(Width, Height, "Soul Revive");
            if (!Raylib.IsWindowReady())
                throw new InvalidOperationException("Cannot create the desktop window. Check the display session and OpenGL support.");
            SetFps(30);
            Raylib.SetExitKey(KeyboardKey.Null);
            ApplyTopmost();
            RestorePosition();

            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("PixelFont.ttf"))
            {
                if (stream == null) throw new InvalidOperationException("Embedded font is missing.");
                byte[] data = new byte[stream.Length];
                stream.ReadExactly(data);
                string extra = "ĞğİıŞş←→↑↓–—·";
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
            session = new DesktopSession { WantIdle = p.IdlePause };
            Autostart.Refresh();
            if (startMinimized) Raylib.MinimizeWindow();

            engine.Changed += reason =>
            {
                if (reason == TimerEvent.Mode) chillFrame = 0;
                if (reason is TimerEvent.Mode or TimerEvent.Due or TimerEvent.Revived or TimerEvent.Break or TimerEvent.Cancel)
                    workRoutine.Reset();
                if (reason != TimerEvent.Idle) Save();
                if (reason == TimerEvent.Due)
                {
                    Notifier.Show(Ui.DueTitle, Ui.DueText);
                    if (p.FocusOnDue) { Raylib.RestoreWindow(); Raylib.SetWindowFocused(); }
                }
                if (reason == TimerEvent.Revived) { Flash(Ui.RevivedBanner, 8); Notifier.Show(Ui.RevivedTitle, Ui.RevivedText); }
                if (p.Sound && reason is TimerEvent.Due or TimerEvent.Revived) audio.Remind();
            };
        }

        void SetFps(int fps) { if (fps == currentFps) return; currentFps = fps; Raylib.SetTargetFPS(fps); }

        public void Run(int smokeFrames = 0, string screenshot = null)
        {
            // Elapsed time comes from a monotonic clock; the wall clock only detects sleep, which
            // the monotonic clock does not count on macOS and Linux.
            var clock = Stopwatch.StartNew();
            double previous = 0;
            DateTime previousWall = DateTime.UtcNow;
            int rendered = 0;
            while (!quit && !Raylib.WindowShouldClose())
            {
                double now = clock.Elapsed.TotalSeconds, elapsed = now - previous;
                DateTime wall = DateTime.UtcNow;
                double wallElapsed = (wall - previousWall).TotalSeconds;
                previous = now; previousWall = wall;
                if (elapsed > 5 || wallElapsed > 5 || wallElapsed < -5)
                {
                    if (!p.Chill && !p.Paused) engine.TogglePause();
                    elapsed = 0;
                }
                Update((float)elapsed);
                // Lower the frame rate when nobody is looking; animation only changes 8 times a second.
                SetFps(Raylib.IsWindowMinimized() ? 4 : Raylib.IsWindowFocused() || smokeFrames > 0 ? 30 : 12);
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
            foreach (char letter in "ÇçĞğİıÖöŞşÜü·")
                if (Raylib.GetGlyphIndex(font, letter) == Raylib.GetGlyphIndex(font, '?'))
                    throw new InvalidOperationException("Missing glyph: " + letter);
            double remaining = p.Remaining, total = p.TotalWorkSeconds;
            int armor = p.ArmorStyle, goal = p.DailyGoalMinutes;
            OpenSettings(); draft.ArmorStyle = (armor + 1) % 3; draft.WorkMinutes++; draft.DailyGoalMinutes += 30;
            CancelSettings(); activeOverlay = 0;
            if (p.Remaining != remaining || p.ArmorStyle != armor || p.DailyGoalMinutes != goal) throw new InvalidOperationException("Cancel changed saved settings.");
            OpenSettings(); draft.ArmorStyle = (armor + 1) % 3; draft.LongBreakEvery = 4; draft.DailyGoalMinutes = 90;
            ApplySettings(); activeOverlay = 0;
            if (p.Remaining != remaining || p.TotalWorkSeconds != total || p.ArmorStyle == armor || p.LongBreakEvery != 4 || p.DailyGoalMinutes != 90)
                throw new InvalidOperationException("Settings save changed the timer or failed to apply.");
            const int views = 10;
            foreach (string language in new[] { "tr", "en" })
            foreach (bool light in new[] { false, true })
            {
                p.Language = language; p.LightMode = light; L.Language = language; CrossTheme.Light = light;
                for (int view = 0; view < views; view++)
                {
                    CancelSettings(); activeOverlay = 0;
                    p.Chill = view == 1; p.InBreak = view == 2;
                    if (view >= 3 && view <= 7) { OpenSettings(); settingsTab = view - 3; }
                    if (view == 8) activeOverlay = 2;
                    if (view == 9) { activeOverlay = 3; devActionIndex = 2; devFrame = 50; }
                    Draw(); Draw(); // Populate both swap-chain buffers before reading back.
                    string name = $"{Path.GetFileNameWithoutExtension(screenshot)}-{language}-{(light ? "light" : "dark")}-{view}.png";
                    Capture(Path.Combine(Path.GetDirectoryName(screenshot), name));
                }
            }
            CancelSettings(); activeOverlay = 0; p.Chill = false; p.InBreak = false;
            Console.WriteLine($"PASS native window, Turkish glyphs, settings transactions and {views * 4} UI previews");
        }

        void Update(float dt)
        {
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
            if (activeOverlay == 0)
            {
                if (Raylib.IsKeyPressed(KeyboardKey.Space) && !p.Chill) Ui.RunSecondary(engine);
                if (Raylib.IsKeyPressed(KeyboardKey.B) && !p.Chill) Ui.RunPrimary(engine);
                if (Raylib.IsKeyPressed(KeyboardKey.C)) engine.SetChill(!p.Chill);
            }

            engine.Advance(dt);

            // Animation frame ticks (~8 FPS = 125ms)
            frameTimer += dt;
            while (frameTimer >= 0.125f)
            {
                frameTimer -= 0.125f;
                frame++;
                if (p.Chill) chillFrame++;
                workRoutine.Advance(engine.Working);
                kneel = PixelArt.StepKneel(kneel, engine.Due);
            }

            if (activeOverlay == 3 && devPlaying)
            {
                devTimer += dt * devSpeed;
                while (devTimer >= 0.125) { devTimer -= 0.125; devFrame = (devFrame + 1) % DevLength; }
            }

            audio.Update();

            // A second launch asks this window to come forward.
            showTimer += dt;
            if (showTimer >= 1f) { showTimer = 0; if (AppFiles.TakeShowRequest()) { Raylib.RestoreWindow(); Raylib.SetWindowFocused(); } }

            sessionTimer += dt;
            if (sessionTimer >= 1f)
            {
                sessionTimer = 0;
                session.WantIdle = p.IdlePause;
                bool locked = session.Locked;
                if (locked && !wasLocked)
                {
                    if (!p.Chill) p.Paused = true;
                    audio.Suspend(true); Save();
                }
                else if (!locked && wasLocked) audio.Suspend(false);
                wasLocked = locked;
                CheckIdle();
            }

            if (updateCheck != null && updateCheck.IsCompleted)
            {
                if (updateCheck.IsFaulted)
                    updateStatus = updateCheck.Exception?.GetBaseException() is InvalidOperationException known ? known.Message
                        : L.Text("Güncelleme sunucusuna ulaşılamadı.", "Could not reach the update server.");
                else
                {
                    availableRelease = updateCheck.Result;
                    updateStatus = availableRelease == null ? L.Text("Güncelsin. Ateşin yanmaya devam ediyor.", "You are up to date. Your fire keeps burning.")
                        : L.Text("Yeni sürüm hazır: ", "New version available: ") + availableRelease.Version.ToString(3);
                }
                updateCheck = null;
            }

            // Nothing changes while paused, expired or in chill mode, so there is nothing to write.
            saveTimer += dt;
            if (saveTimer >= 5.0f)
            {
                saveTimer = 0;
                if (engine.Working || p.InBreak) Save();
            }
        }

        void CheckIdle()
        {
            if (!p.IdlePause || !engine.Working) return;
            double idle = session.IdleSeconds;
            if (idle < p.IdleMinutes * 60) return;
            if (engine.PauseForIdle(idle, DateTime.Now)) { Save(true); Flash(Ui.IdleBanner, 30); }
        }

        void Draw()
        {
            Raylib.BeginDrawing();
            Raylib.ClearBackground(HexColor(CrossTheme.BackgroundHex));
            DrawCompanion();
            if (activeOverlay == 1 && draft != null) DrawSettingsOverlay();
            else if (activeOverlay == 2) DrawStatsOverlay();
            else if (activeOverlay == 3) DrawDevOverlay();
            Raylib.EndDrawing();
        }

        void DrawCompanion()
        {
            Color bg = HexColor(CrossTheme.BackgroundHex);
            Vector2 mouse = Raylib.GetMousePosition();
            bool click = activeOverlay == 0 && Raylib.IsMouseButtonPressed(MouseButton.Left);

            DrawPixelFrame(0, 0, Width, Height, bg);
            DrawPixelText(Ui.Title, 20, 14, 22, HexColor(CrossTheme.GoldHex));
            DrawPixelText(Ui.Tagline, 20, 42, 14, HexColor(CrossTheme.MutedHex));

            if (DrawButton("", 272, 16, 28, 28, false, mouse, click)) OpenSettings();
            Color gear = HexColor(CrossTheme.GoldHex);
            Raylib.DrawRectangle(281, 25, 10, 10, gear);
            Raylib.DrawRectangle(284, 22, 4, 16, gear);
            Raylib.DrawRectangle(278, 28, 16, 4, gear);
            Raylib.DrawRectangle(284, 28, 4, 4, HexColor(CrossTheme.PanelHex));
            if (DrawButton("_", 304, 16, 28, 28, false, mouse, click)) Raylib.MinimizeWindow();
            if (DrawButton("X", 336, 16, 28, 28, false, mouse, click)) { quit = true; return; }

            if (DrawButton(Ui.WorkingMode, 20, 72, 168, 30, !p.Chill, mouse, click)) engine.SetChill(false);
            if (DrawButton(Ui.ChillMode, 196, 72, 168, 30, p.Chill, mouse, click)) engine.SetChill(true);

            RenderScene(sceneTexture, ref sceneKey, p.Chill ? 1 : engine.Health, p.InBreak, p.Chill ? chillFrame : frame,
                DayCycle.Hour(p), p.Chill, workRoutine.Action, workRoutine.Frame, KnightEquipment.From(p), kneel);
            DrawPixelFrame(18, 112, 348, 188, HexColor(CrossTheme.PanelHex));
            Raylib.DrawTexturePro(sceneTexture, new Rectangle(0, 0, 172, 92), new Rectangle(20, 114, 344, 184), Vector2.Zero, 0, Color.White);

            DrawPixelFrame(20, 308, 344, 28, HexColor(CrossTheme.PanelHex));
            DrawCenteredText(Ui.State(engine), 20, 312, 344, 14, HexColor(CrossTheme.GoldHex));
            DrawCenteredText(Ui.Clock(engine), 20, 344, 344, 44, HexColor(CrossTheme.TextHex));
            DrawCenteredText(Ui.Subtitle(engine), 20, 408, 344, 16, HexColor(CrossTheme.MutedHex));

            double hp = p.Chill ? 1.0 : Math.Max(0, Math.Min(1, engine.Health));
            DrawPixelText(Ui.Health, 20, 446, 14, HexColor(CrossTheme.MutedHex));
            Raylib.DrawRectangle(58, 451, 252, 8, HexColor(CrossTheme.PanelHex));
            Color hpColor = (p.InBreak || p.Chill) ? HexColor(CrossTheme.GreenHex) : HexColor(CrossTheme.GoldHex);
            Raylib.DrawRectangle(58, 451, (int)(252 * hp), 8, hpColor);
            for (int i = 1; i < 21; i++) Raylib.DrawLine(58 + i * 12, 451, 58 + i * 12, 459, bg);
            DrawCenteredText($"{(int)(hp * 100)}%", 316, 446, 48, 14, HexColor(CrossTheme.MutedHex));

            if (DrawButton(Ui.PrimaryAction(engine), 20, 480, 168, 42, true, mouse, click && !p.Chill, !p.Chill)) Ui.RunPrimary(engine);
            if (DrawButton(Ui.SecondaryAction(engine), 196, 480, 168, 42, false, mouse, click && !p.Chill, !p.Chill)) Ui.RunSecondary(engine);
            if (DrawButton(Ui.StatisticsButton, 20, 538, 344, 32, false, mouse, click)) { activeOverlay = 2; resetArmedUntil = DateTime.MinValue; }

            DrawCenteredText(Ui.Today, 20, 582, 164, 14, HexColor(CrossTheme.MutedHex));
            DrawCenteredText(Ui.Total, 200, 582, 164, 14, HexColor(CrossTheme.MutedHex));
            DrawCenteredText(Ui.Duration(p.TodayWorkSeconds), 20, 600, 164, 20, HexColor(CrossTheme.GoldHex));
            DrawCenteredText(Ui.Duration(p.TotalWorkSeconds), 200, 600, 164, 20, HexColor(CrossTheme.GoldHex));
            string footer = DateTime.Now < messageUntil ? message : Ui.Footer(p);
            DrawCenteredText(footer, 10, 640, 364, 14, DateTime.Now < messageUntil && message.StartsWith(Ui.SaveFailed) ? Color.Red : HexColor(CrossTheme.MutedHex));
        }

        void DrawSettingsOverlay()
        {
            Preferences d = draft;
            Vector2 mouse = Raylib.GetMousePosition();
            bool click = Raylib.IsMouseButtonPressed(MouseButton.Left);
            Color text = HexColor(CrossTheme.TextHex), muted = HexColor(CrossTheme.MutedHex), gold = HexColor(CrossTheme.GoldHex);

            Raylib.DrawRectangle(0, 0, Width, Height, new Color(0, 0, 0, 180));
            int dw = 352, dh = 588, dx = 16, dy = 40;
            DrawPixelFrame(dx, dy, dw, dh, HexColor(CrossTheme.BackgroundHex));
            DrawPixelText(L.Text("AYARLAR", "SETTINGS"), dx + 16, dy + 14, 22, gold);

            string[] tabs = { L.Text("Genel", "General"), L.Text("Zaman", "Timer"), L.Text("Sesler", "Sounds"), L.Text("Şövalye", "Knight"), L.Text("Sürüm", "Updates") };
            for (int i = 0; i < tabs.Length; i++)
                if (DrawButton(tabs[i], dx + 14 + (i % 3) * 110, dy + 46 + (i / 3) * 30, 104, 26, settingsTab == i, mouse, click)) settingsTab = i;

            int cy = dy + 118;
            if (settingsTab == 0)
            {
                DrawPixelText(L.Text("Dil:", "Language:"), dx + 20, cy, 16, text);
                if (DrawButton(d.Language == "tr" ? "Türkçe" : "English", dx + 210, cy - 4, 104, 28, false, mouse, click))
                { d.Language = d.Language == "tr" ? "en" : "tr"; L.Language = d.Language; }
                cy += 40;
                DrawPixelText(L.Text("Tema:", "Theme:"), dx + 20, cy, 16, text);
                if (DrawButton(CrossTheme.Light ? L.Text("Aydınlık", "Light") : L.Text("Karanlık", "Dark"), dx + 210, cy - 4, 104, 28, false, mouse, click))
                { CrossTheme.Light = !CrossTheme.Light; d.LightMode = CrossTheme.Light; }
                cy += 40;
                if (DrawToggle(L.Text("Her zaman üstte", "Always on top"), d.AlwaysOnTop, dx + 20, cy, mouse, click)) d.AlwaysOnTop = !d.AlwaysOnTop;
                cy += 36;
                if (DrawToggle(L.Text("Mola bildirim sesi", "Break reminder sound"), d.Sound, dx + 20, cy, mouse, click)) d.Sound = !d.Sound;
                cy += 36;
                if (Autostart.Supported && DrawToggle(L.Text("Oturum açılışında başlat", "Start at sign-in"), draftAutostart, dx + 20, cy, mouse, click)) draftAutostart = !draftAutostart;
                cy += 44;
                if (DrawButton(L.Text("Geliştirici paneli (F8)", "Developer panel (F8)"), dx + 20, cy, 312, 32, false, mouse, click))
                { CancelSettings(); activeOverlay = 3; return; }
                cy += 46;
                DrawLines(L.Text("Kısayollar: Boşluk duraklat · B mola\nC chill · F8 panel · Esc kapat", "Keys: Space pause · B break\nC chill · F8 panel · Esc closes"), dx + 20, cy, 14, muted);
            }
            else if (settingsTab == 1)
            {
                DrawStepper(L.Text("Çalışma (dk)", "Work (min)"), ref d.WorkMinutes, 1, 180, 1, null, dx, cy, mouse, click); cy += 36;
                DrawStepper(L.Text("Mola (dk)", "Break (min)"), ref d.BreakMinutes, 1, 30, 1, null, dx, cy, mouse, click); cy += 36;
                DrawStepper(L.Text("Uzun mola her N tur", "Long break every N"), ref d.LongBreakEvery, 0, 12, 1, L.Text("kapalı", "off"), dx, cy, mouse, click); cy += 36;
                DrawStepper(L.Text("Uzun mola (dk)", "Long break (min)"), ref d.LongBreakMinutes, 1, 60, 1, null, dx, cy, mouse, click); cy += 36;
                DrawStepper(L.Text("Günlük hedef (dk)", "Daily goal (min)"), ref d.DailyGoalMinutes, 0, 720, 15, L.Text("yok", "none"), dx, cy, mouse, click); cy += 40;
                if (DrawToggle(L.Text("Boştayken duraklat", "Pause when idle"), d.IdlePause, dx + 20, cy, mouse, click)) d.IdlePause = !d.IdlePause;
                cy += 38;
                DrawStepper(L.Text("Boşta sayılma (dk)", "Idle after (min)"), ref d.IdleMinutes, 1, 60, 1, null, dx, cy, mouse, click); cy += 36;
                if (DrawToggle(L.Text("Mola zamanı öne getir", "Bring forward when due"), d.FocusOnDue, dx + 20, cy, mouse, click)) d.FocusOnDue = !d.FocusOnDue;
                cy += 42;
                string idleNote = session.IdleSupported ? L.Text("Boşta süre çalışmadan düşülür.", "Idle time is removed from work.")
                    : L.Text("Bu masaüstü boşta süresini bildirmiyor.", "This desktop does not report idle time.");
                DrawLines(L.Text("Süreyi değiştirmek yeni tur başlatır.\n", "Changing durations starts a new round.\n") + idleNote, dx + 20, cy, 14, muted);
            }
            else if (settingsTab == 2)
            {
                bool changed = false;
                if (DrawToggle(L.Text("Müzik ve ortam sesleri", "Music and ambience"), d.AudioEnabled, dx + 20, cy, mouse, click)) { d.AudioEnabled = !d.AudioEnabled; changed = true; }
                cy += 46;
                changed |= DrawVolumeControl(dx + 20, cy, L.Text("ATEŞ / ÇITIRTI", "FIRE / CRACKLE"), ref d.FireVolume, mouse, click); cy += 58;
                changed |= DrawVolumeControl(dx + 20, cy, L.Text("AMBİYANS / RÜZGÂR", "AMBIENCE / WIND"), ref d.AmbienceVolume, mouse, click); cy += 58;
                changed |= DrawVolumeControl(dx + 20, cy, L.Text("MÜZİK / EZGİ", "MUSIC / MELODY"), ref d.MusicVolume, mouse, click); cy += 62;
                if (DrawButton(previewingAudio ? L.Text("Önizlemeyi durdur", "Stop preview") : L.Text("Önizle", "Preview"), dx + 20, cy, 312, 32, previewingAudio, mouse, click))
                {
                    previewingAudio = !previewingAudio; changed = true;
                    if (!previewingAudio) audio.Apply(p);
                }
                if (previewingAudio && changed) audio.Apply(new Preferences { AudioEnabled = true, FireVolume = d.FireVolume, AmbienceVolume = d.AmbienceVolume, MusicVolume = d.MusicVolume });
                cy += 46;
                DrawLines(L.Text("Kaydet ile arka planda çalar.\nTüm sesler çevrimdışı üretilir.", "Save keeps audio playing.\nAll sounds are made offline."), dx + 20, cy, 14, muted);
            }
            else if (settingsTab == 3)
            {
                DrawPixelText(L.Text("Zırh:", "Armor:"), dx + 20, cy, 14, text);
                if (DrawButton(Ui.ArmorNames[d.ArmorStyle], dx + 120, cy - 4, 194, 26, false, mouse, click)) d.ArmorStyle = (d.ArmorStyle + 1) % 3;
                cy += 38;
                DrawPixelText(L.Text("Kılıç:", "Sword:"), dx + 20, cy, 14, text);
                if (DrawButton(Ui.SwordNames[d.SwordStyle], dx + 120, cy - 4, 194, 26, false, mouse, click)) d.SwordStyle = (d.SwordStyle + 1) % 3;
                cy += 38;
                DrawPixelText(L.Text("Kalkan:", "Shield:"), dx + 20, cy, 14, text);
                if (DrawButton(Ui.ShieldNames[d.ShieldStyle], dx + 120, cy - 4, 194, 26, false, mouse, click)) d.ShieldStyle = (d.ShieldStyle + 1) % 3;
                cy += 46;
                RenderScene(previewTexture, ref previewKey, 1, false, frame, 12, false, WorkAction.Idle, 0, KnightEquipment.From(d));
                DrawPixelFrame(dx + 20, cy, 312, 164, HexColor(CrossTheme.PanelHex));
                Raylib.DrawTexturePro(previewTexture, new Rectangle(35, 12, 104, 80), new Rectangle(dx + 72, cy + 2, 208, 160), Vector2.Zero, 0, Color.White);
            }
            else
            {
                DrawPixelText(L.Text("YÜKLÜ SÜRÜM / ", "INSTALLED VERSION / ") + Program.Version, dx + 20, cy, 16, gold);
                cy += 30;
                DrawLines(L.Text("Yeni sürüm varsa indirme sayfasını\naçabilirsin. Ayarların ve istatistiklerin\nprofil klasöründe korunur.",
                    "If a new version exists, open its\ndownload page. Settings and statistics\nstay in your profile folder."), dx + 20, cy, 14, text);
                cy += 66;
                bool checking = updateCheck != null;
                if (DrawButton(checking ? L.Text("Kontrol ediliyor...", "Checking...") : L.Text("Güncellemeleri kontrol et", "Check for updates"), dx + 20, cy, 312, 32, false, mouse, click && !checking, !checking))
                {
                    availableRelease = null; updateStatus = string.Empty;
                    updateCheck = CrossUpdates.CheckAsync();
                }
                cy += 44;
                DrawLines(updateStatus.Length > 0 ? updateStatus : L.Text("Kontrol için internet gerekir.", "Checking needs an internet connection."), dx + 20, cy, 14, muted);
                cy += 40;
                if (availableRelease != null && DrawButton(L.Text("İndirme sayfasını aç", "Open download page"), dx + 20, cy, 312, 32, true, mouse, click))
                    Raylib.OpenURL(availableRelease.Url);
            }

            if (DrawButton(L.Text("KAYDET", "SAVE"), dx + 20, dy + dh - 48, 150, 36, true, mouse, click))
            { ApplySettings(); activeOverlay = 0; return; }
            if (DrawButton(L.Text("İPTAL", "CANCEL"), dx + 182, dy + dh - 48, 150, 36, false, mouse, click))
            { CancelSettings(); activeOverlay = 0; }
        }

        bool DrawToggle(string label, bool value, int x, int y, Vector2 mouse, bool click)
            => DrawButton((value ? "[X] " : "[ ] ") + label, x, y, 312, 30, false, mouse, click);

        void DrawStepper(string label, ref int value, int min, int max, int step, string zeroText, int dx, int y, Vector2 mouse, bool click)
        {
            DrawPixelText(label, dx + 20, y, 14, HexColor(CrossTheme.TextHex));
            if (DrawButton("-", dx + 206, y - 5, 32, 26, false, mouse, click)) value = Math.Max(min, value - step);
            DrawCenteredText(value == 0 && zeroText != null ? zeroText : value.ToString(), dx + 238, y, 58, 16, HexColor(CrossTheme.GoldHex));
            if (DrawButton("+", dx + 296, y - 5, 32, 26, false, mouse, click)) value = Math.Min(max, value + step);
        }

        /// <summary>Label, draggable bar and fine +/- buttons; returns true when the value changed.</summary>
        bool DrawVolumeControl(int x, int y, string label, ref int volume, Vector2 mouse, bool click)
        {
            int before = volume;
            DrawPixelText(label, x, y, 14, HexColor(CrossTheme.TextHex));
            DrawCenteredText($"{volume}%", x + 240, y, 72, 14, HexColor(CrossTheme.GoldHex));
            var bar = new Rectangle(x, y + 24, 236, 12);
            Raylib.DrawRectangleRec(bar, HexColor(CrossTheme.LineHex));
            Raylib.DrawRectangle(x, y + 24, (int)(236 * volume / 100.0), 12, HexColor(CrossTheme.GoldHex));
            Raylib.DrawRectangle(x + (int)(236 * volume / 100.0) - 3, y + 20, 6, 20, HexColor(CrossTheme.TextHex));
            if (Raylib.IsMouseButtonDown(MouseButton.Left) && Raylib.CheckCollisionPointRec(mouse, new Rectangle(x - 6, y + 16, 248, 28)))
                volume = Math.Clamp((int)Math.Round((mouse.X - x) / 236 * 20) * 5, 0, 100);
            if (DrawButton("-", x + 246, y + 18, 30, 24, false, mouse, click)) volume = Math.Max(0, volume - 5);
            if (DrawButton("+", x + 282, y + 18, 30, 24, false, mouse, click)) volume = Math.Min(100, volume + 5);
            return volume != before;
        }

        void DrawStatsOverlay()
        {
            Vector2 mouse = Raylib.GetMousePosition();
            bool click = Raylib.IsMouseButtonPressed(MouseButton.Left);
            Color text = HexColor(CrossTheme.TextHex), gold = HexColor(CrossTheme.GoldHex), muted = HexColor(CrossTheme.MutedHex);
            p.CheckDay();

            Raylib.DrawRectangle(0, 0, Width, Height, new Color(0, 0, 0, 180));
            int dw = 344, dh = 572, dx = 20, dy = 48;
            DrawPixelFrame(dx, dy, dw, dh, HexColor(CrossTheme.BackgroundHex));
            DrawPixelText(L.Text("İSTATİSTİKLER", "STATISTICS"), dx + 20, dy + 18, 22, gold);

            int cy = dy + 58;
            void Row(string label, string value)
            {
                DrawPixelText(label, dx + 20, cy, 16, text);
                DrawPixelText(value, dx + 186, cy, 16, gold);
                cy += 28;
            }
            Row(L.Text("Bugün çalışma", "Today's work"), Ui.Duration(p.TodayWorkSeconds));
            if (p.DailyGoalMinutes > 0) Row(L.Text("Günlük hedef", "Daily goal"), Ui.Goal(p));
            Row(L.Text("Toplam çalışma", "Lifetime work"), Ui.LongDuration(p.TotalWorkSeconds));
            Row(L.Text("Tamamlanan tur", "Completed rounds"), p.CompletedSessions + L.Text(" · bugün ", " · today ") + p.TodaySessions);
            Row(L.Text("Bugünkü molalar", "Today's breaks"), p.Breaks.ToString());
            Row(L.Text("Hareket süresi", "Movement time"), Ui.Minutes(p.MovementSeconds) + L.Text(" dk", " min"));
            cy += 4;
            DrawPixelText(Ui.StreakText(p, DateTime.Now), dx + 20, cy, 16, gold);
            cy += 34;
            DrawPixelText(L.Text("SON 7 GÜN", "LAST 7 DAYS"), dx + 20, cy, 14, muted);
            cy += 22;
            DrawWeekChart(dx + 20, cy, 304, 96);

            bool armed = DateTime.Now < resetArmedUntil;
            if (DrawButton(armed ? L.Text("Emin misin? Sil", "Sure? Delete") : L.Text("Sıfırla", "Reset"), dx + 20, dy + dh - 50, 146, 34, armed, mouse, click))
            {
                if (!armed) resetArmedUntil = DateTime.Now.AddSeconds(4);
                else { engine.ResetStatistics(); Save(true); resetArmedUntil = DateTime.MinValue; }
            }
            if (DrawButton(L.Text("KAPAT", "CLOSE"), dx + 178, dy + dh - 50, 146, 34, true, mouse, click)) activeOverlay = 0;
        }

        void DrawWeekChart(int x, int y, int w, int h)
        {
            var now = DateTime.Now;
            double[] values = Ui.WeekSeconds(p, now);
            string[] labels = Ui.WeekLabels(now);
            double goal = p.DailyGoalMinutes * 60.0, max = Math.Max(goal, 3600);
            foreach (double v in values) max = Math.Max(max, v);
            int chartHeight = h - 22, slot = w / 7;
            for (int i = 0; i < 7; i++)
            {
                int barHeight = (int)Math.Round(chartHeight * values[i] / max), bx = x + i * slot + slot / 4, bw = slot / 2;
                Raylib.DrawRectangle(bx, y, bw, chartHeight, HexColor(CrossTheme.PanelHex));
                Raylib.DrawRectangle(bx, y + chartHeight - barHeight, bw, barHeight, HexColor(goal > 0 && values[i] >= goal ? CrossTheme.GreenHex : CrossTheme.GoldHex));
                DrawCenteredText(labels[i], x + i * slot, y + chartHeight + 4, slot, 14, HexColor(i == 6 ? CrossTheme.TextHex : CrossTheme.MutedHex));
            }
            if (goal > 0)
            {
                int gy = y + chartHeight - (int)Math.Round(chartHeight * goal / max);
                for (int gx = x; gx < x + w; gx += 6) Raylib.DrawRectangle(gx, gy, 3, 1, HexColor(CrossTheme.TextHex));
            }
        }

        void DrawDevOverlay()
        {
            Vector2 mouse = Raylib.GetMousePosition();
            bool click = Raylib.IsMouseButtonPressed(MouseButton.Left);
            Color text = HexColor(CrossTheme.TextHex);

            Raylib.DrawRectangle(0, 0, Width, Height, new Color(0, 0, 0, 200));
            int dw = 364, dh = 600, dx = 10, dy = 34;
            DrawPixelFrame(dx, dy, dw, dh, HexColor(CrossTheme.BackgroundHex));

            DrawPixelText(L.Text("GELİŞTİRİCİ PANELİ (F8)", "DEVELOPER PANEL (F8)"), dx + 16, dy + 16, 20, HexColor(CrossTheme.GoldHex));
            if (DrawButton("X", dx + dw - 40, dy + 14, 26, 26, false, mouse, click)) { activeOverlay = 0; return; }

            string[] anims = { L.Text("Bekleme", "Idle"), L.Text("Bileme", "Sharpen"), L.Text("Harita", "Map"), L.Text("Odun", "Log"), L.Text("Uyku", "Sleep"), L.Text("Meyhane", "Tavern") };
            for (int i = 0; i < anims.Length; i++)
            {
                int bx = dx + 12 + (i % 3) * 114, by = dy + 50 + (i / 3) * 32;
                if (DrawButton(anims[i], bx, by, 110, 28, devActionIndex == i, mouse, click)) { devActionIndex = i; devFrame = 0; }
            }

            WorkAction curAction = devActionIndex == 1 ? WorkAction.Sharpen : devActionIndex == 2 ? WorkAction.Map : devActionIndex == 3 ? WorkAction.TendFire : WorkAction.Idle;
            double hour = devLighting == 0 ? 12.0 : devLighting == 1 ? 6.0 : 22.0;
            RenderScene(previewTexture, ref previewKey, 1, devActionIndex == 4, devFrame, hour, devActionIndex == 5, curAction, devFrame % WorkRoutine.Duration, KnightEquipment.From(p));
            DrawPixelFrame(dx + 8, dy + 122, 348, 188, HexColor(CrossTheme.PanelHex));
            Raylib.DrawTexturePro(previewTexture, new Rectangle(0, 0, 172, 92), new Rectangle(dx + 10, dy + 124, 344, 184), Vector2.Zero, 0, Color.White);

            int cy = dy + 320;
            if (DrawButton(devPlaying ? L.Text("DURAKLAT", "PAUSE") : L.Text("OYNAT", "PLAY"), dx + 12, cy, 92, 30, devPlaying, mouse, click)) devPlaying = !devPlaying;
            if (DrawButton("<", dx + 110, cy, 36, 30, false, mouse, click)) { devPlaying = false; devFrame = (devFrame - 1 + DevLength) % DevLength; }
            if (DrawButton(">", dx + 152, cy, 36, 30, false, mouse, click)) { devPlaying = false; devFrame = (devFrame + 1) % DevLength; }
            DrawCenteredText(L.Text("KARE ", "FRAME ") + devFrame, dx + 192, cy + 6, 160, 16, text);

            cy += 38;
            if (DrawButton(L.Text("BAŞA SAR", "RESTART"), dx + 12, cy, 120, 26, false, mouse, click)) { devFrame = 0; devTimer = 0; }
            if (DrawButton("0.5x", dx + 140, cy, 64, 26, devSpeed == 0.5f, mouse, click)) devSpeed = 0.5f;
            if (DrawButton("1x", dx + 212, cy, 64, 26, devSpeed == 1.0f, mouse, click)) devSpeed = 1.0f;
            if (DrawButton("2x", dx + 284, cy, 64, 26, devSpeed == 2f, mouse, click)) devSpeed = 2f;
            cy += 38;
            Raylib.DrawRectangle(dx + 12, cy, 340, 8, HexColor(CrossTheme.PanelHex));
            Raylib.DrawRectangle(dx + 12, cy, (int)(340.0 * devFrame / (DevLength - 1)), 8, HexColor(CrossTheme.GoldHex));
            if (Raylib.IsMouseButtonDown(MouseButton.Left) && Raylib.CheckCollisionPointRec(mouse, new Rectangle(dx + 12, cy - 8, 340, 24)))
            { devPlaying = false; devFrame = (int)Math.Clamp((mouse.X - dx - 12) / 340 * (DevLength - 1), 0, DevLength - 1); }
            cy += 32;
            DrawPixelText(L.Text("Işık:", "Light:"), dx + 16, cy + 4, 14, text);
            string[] lights = Ui.LightingNames;
            for (int i = 0; i < 3; i++)
                if (DrawButton(lights[i], dx + 90 + i * 76, cy, 70, 26, devLighting == i, mouse, click)) devLighting = i;
        }

        bool DrawButton(string text, int x, int y, int w, int h, bool primary, Vector2 mouse, bool click, bool enabled = true)
        {
            Rectangle rect = new Rectangle(x, y, w, h);
            bool hover = enabled && Raylib.CheckCollisionPointRec(mouse, rect);
            Color fill = primary && enabled ? HexColor(CrossTheme.PrimaryHex) : HexColor(CrossTheme.PanelHex);
            if (hover) fill = new Color((byte)Math.Min(255, fill.R + 25), (byte)Math.Min(255, fill.G + 25), (byte)Math.Min(255, fill.B + 25), fill.A);
            DrawPixelFrame(x, y, w, h, fill);
            Color textColor = !enabled ? HexColor(CrossTheme.MutedHex) : primary ? HexColor(Palette.PrimaryText) : HexColor(CrossTheme.TextHex);
            DrawCenteredText(text, x, y + (h - 18) / 2, w, 16, textColor);
            return hover && click;
        }

        void DrawPixelFrame(int x, int y, int w, int h, Color fill)
        {
            Raylib.DrawRectangle(x, y, w, h, fill);
            Color gold = HexColor(CrossTheme.GoldHex);
            Raylib.DrawRectangleLines(x, y, w, h, HexColor(CrossTheme.LineHex));
            Raylib.DrawRectangle(x, y, 4, 4, gold);
            Raylib.DrawRectangle(x + w - 4, y, 4, 4, gold);
            Raylib.DrawRectangle(x, y + h - 4, 4, 4, gold);
            Raylib.DrawRectangle(x + w - 4, y + h - 4, 4, 4, gold);
        }

        void DrawPixelText(string text, int x, int y, int size, Color color) => Raylib.DrawTextEx(font, text, new Vector2(x, y), size, 1, color);

        void DrawLines(string text, int x, int y, int size, Color color)
        {
            foreach (string line in text.Split('\n')) { DrawPixelText(line, x, y, size, color); y += size + 4; }
        }

        void DrawCenteredText(string text, int x, int y, int w, int size, Color color)
        {
            Vector2 measurement = Raylib.MeasureTextEx(font, text, size, 1);
            Raylib.DrawTextEx(font, text, new Vector2(x + (int)((w - measurement.X) / 2), y), size, 1, color);
        }

        static readonly System.Collections.Generic.Dictionary<string, Color> colors = new();
        static Color HexColor(string hex)
        {
            if (colors.TryGetValue(hex, out Color cached)) return cached;
            uint c = CrossCanvas.Hex(hex);
            return colors[hex] = new Color((byte)(c & 0xFF), (byte)((c >> 8) & 0xFF), (byte)((c >> 16) & 0xFF), (byte)((c >> 24) & 0xFF));
        }

        public void Dispose()
        {
            session?.Dispose();
            audio?.Dispose();
            Raylib.UnloadTexture(sceneTexture);
            Raylib.UnloadTexture(previewTexture);
            Raylib.UnloadFont(font);
            Raylib.CloseWindow();
        }
    }
}
