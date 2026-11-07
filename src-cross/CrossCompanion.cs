using System;
using System.IO;
using System.Numerics;
using System.Reflection;
using Raylib_cs;

namespace SoulsRevive.Cross
{
    public sealed class CrossCompanion : IDisposable
    {
        readonly Preferences p;
        readonly TimerEngine engine;
        readonly WorkRoutine workRoutine = new WorkRoutine();
        readonly CrossCanvas canvas = new CrossCanvas(172, 92);
        readonly CrossAudio audio;

        Font font;
        Texture2D sceneTexture;
        Image sceneImage;

        int frame, chillFrame;
        float frameTimer, saveTimer;
        bool isDragging;
        Vector2 dragOffset;

        // Overlays: 0 = none, 1 = settings, 2 = statistics, 3 = developer
        int activeOverlay;
        int settingsTab; // 0 = general, 1 = sounds, 2 = knight

        // Developer panel state
        int devActionIndex;
        int devFrame;
        bool devPlaying = true;
        float devSpeed = 1.0f;
        int devLighting; // 0 = day, 1 = dawn, 2 = night

        public CrossCompanion()
        {
            p = CrossStorage.Load();
            engine = new TimerEngine(p);
            L.Language = p.Language;
            CrossTheme.Light = p.LightMode;
            CrossPixelArt.Equipped = KnightEquipment.From(p);

            Raylib.SetConfigFlags(ConfigFlags.UndecoratedWindow | ConfigFlags.AlwaysRunWindow);
            Raylib.InitWindow(384, 668, "Soul Revive");
            Raylib.SetTargetFPS(60);

            // Load embedded VT323 font
            try
            {
                using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("VT323-Regular.ttf");
                if (stream != null)
                {
                    byte[] data = new byte[stream.Length];
                    stream.ReadExactly(data, 0, data.Length);
                    font = Raylib.LoadFontFromMemory(".ttf", data, 32, null, 0);
                    Raylib.SetTextureFilter(font.Texture, TextureFilter.Point);
                }
                else
                {
                    font = Raylib.GetFontDefault();
                }
            }
            catch
            {
                font = Raylib.GetFontDefault();
            }

            // Create 172x92 texture
            unsafe
            {
                fixed (uint* ptr = canvas.Pixels)
                {
                    sceneImage = new Image
                    {
                        Data = ptr,
                        Width = 172,
                        Height = 92,
                        Mipmaps = 1,
                        Format = PixelFormat.UncompressedR8G8B8A8
                    };
                    sceneTexture = Raylib.LoadTextureFromImage(sceneImage);
                    Raylib.SetTextureFilter(sceneTexture, TextureFilter.Point);
                }
            }

            audio = new CrossAudio();
            audio.Apply(p);

            engine.Changed += reason =>
            {
                if (reason == "mode") chillFrame = 0;
                if (reason == "mode" || reason == "due" || reason == "revived" || reason == "break" || reason == "cancel")
                    workRoutine.Reset();
                CrossStorage.Save(p);
            };
        }

        public void Run()
        {
            while (!Raylib.WindowShouldClose())
            {
                float dt = Raylib.GetFrameTime();
                Update(dt);
                Draw();
            }
            CrossStorage.Save(p);
        }

        void Update(float dt)
        {
            // F8 toggles developer panel
            if (Raylib.IsKeyPressed(KeyboardKey.F8))
            {
                activeOverlay = activeOverlay == 3 ? 0 : 3;
            }
            if (Raylib.IsKeyPressed(KeyboardKey.Escape) && activeOverlay != 0)
            {
                activeOverlay = 0;
            }

            // Window dragging
            Vector2 mouse = Raylib.GetMousePosition();
            if (Raylib.IsMouseButtonPressed(MouseButton.Left) && mouse.Y < 40 && mouse.X < 260)
            {
                isDragging = true;
                dragOffset = mouse;
            }
            if (isDragging)
            {
                if (Raylib.IsMouseButtonDown(MouseButton.Left))
                {
                    Vector2 currentPos = Raylib.GetWindowPosition();
                    Vector2 newPos = currentPos + mouse - dragOffset;
                    Raylib.SetWindowPosition((int)newPos.X, (int)newPos.Y);
                }
                else
                {
                    isDragging = false;
                }
            }

            // Timer advance
            engine.Advance(dt);

            // Animation frame ticks (~8 FPS = 125ms)
            frameTimer += dt;
            if (frameTimer >= 0.125f)
            {
                frameTimer -= 0.125f;
                frame++;
                if (p.Chill) chillFrame++;
                workRoutine.Advance(!p.Chill && !p.InBreak && !p.Paused && !engine.Due);
            }

            // Developer animation advance
            if (activeOverlay == 3 && devPlaying)
            {
                devFrame++;
            }

            // Audio loop check
            audio.Update();

            // Auto-save every 5 seconds
            saveTimer += dt;
            if (saveTimer >= 5.0f)
            {
                saveTimer = 0;
                CrossStorage.Save(p);
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

            Raylib.EndDrawing();
        }

        void DrawCompanion()
        {
            Color bg = HexColor(CrossTheme.BackgroundHex);
            Vector2 mouse = Raylib.GetMousePosition();
            bool click = Raylib.IsMouseButtonPressed(MouseButton.Left);

            // Border Frame
            DrawPixelFrame(0, 0, 384, 668, HexColor(CrossTheme.BackgroundHex));

            // Header Title
            DrawPixelText("SOUL REVIVE", 20, 14, 22, HexColor(CrossTheme.GoldHex));
            DrawPixelText(L.Text("KÜÇÜK MOLALAR. YENİDEN DOĞ.", "SMALL BREAKS. RISE AGAIN."), 20, 42, 14, HexColor(CrossTheme.MutedHex));

            // Title Buttons
            // Gear / Settings
            if (DrawButton("⚙", 272, 16, 28, 28, false, mouse, click))
            {
                activeOverlay = activeOverlay == 1 ? 0 : 1;
            }
            // Minimize
            if (DrawButton("_", 304, 16, 28, 28, false, mouse, click))
            {
                Raylib.MinimizeWindow();
            }
            // Close
            if (DrawButton("X", 336, 16, 28, 28, false, mouse, click))
            {
                Raylib.CloseWindow();
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
            CrossPixelArt.RenderScene(canvas, p.Chill ? 1.0 : engine.Health, p.InBreak, p.Chill ? chillFrame : frame,
                DayCycle.Hour(p), p.Chill, workRoutine.Action, workRoutine.Frame);
            unsafe
            {
                fixed (uint* ptr = canvas.Pixels)
                {
                    Raylib.UpdateTexture(sceneTexture, ptr);
                }
            }
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
                if (DrawButton("-", dx + 210, cy - 4, 32, 28, false, mouse, click) && p.WorkMinutes > 1) { p.WorkMinutes--; engine.ApplyDurations(p.WorkMinutes, p.BreakMinutes); }
                DrawCenteredText(p.WorkMinutes.ToString(), dx + 242, cy, 40, 18, HexColor(CrossTheme.GoldHex));
                if (DrawButton("+", dx + 282, cy - 4, 32, 28, false, mouse, click) && p.WorkMinutes < 180) { p.WorkMinutes++; engine.ApplyDurations(p.WorkMinutes, p.BreakMinutes); }

                cy += 44;
                DrawPixelText(L.Text("Mola süresi (dk):", "Break time (min):"), dx + 20, cy, 16, HexColor(CrossTheme.TextHex));
                if (DrawButton("-", dx + 210, cy - 4, 32, 28, false, mouse, click) && p.BreakMinutes > 1) { p.BreakMinutes--; engine.ApplyDurations(p.WorkMinutes, p.BreakMinutes); }
                DrawCenteredText(p.BreakMinutes.ToString(), dx + 242, cy, 40, 18, HexColor(CrossTheme.GoldHex));
                if (DrawButton("+", dx + 282, cy - 4, 32, 28, false, mouse, click) && p.BreakMinutes < 30) { p.BreakMinutes++; engine.ApplyDurations(p.WorkMinutes, p.BreakMinutes); }

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

                cy += 50;
                string devButtonText = L.Text("Geliştirici Paneli (F8)", "Developer Panel (F8)");
                if (DrawButton(devButtonText, dx + 20, cy, 312, 32, false, mouse, click))
                {
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
                    audio.Apply(p);
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
                    CrossPixelArt.Equipped = KnightEquipment.From(p);
                }

                cy += 38;
                DrawPixelText(L.Text("Kılıç:", "Sword:"), dx + 20, cy, 14, HexColor(CrossTheme.TextHex));
                if (DrawButton(swordNames[p.SwordStyle], dx + 120, cy - 4, 194, 26, false, mouse, click))
                {
                    p.SwordStyle = (p.SwordStyle + 1) % 3;
                    CrossPixelArt.Equipped = KnightEquipment.From(p);
                }

                cy += 38;
                DrawPixelText(L.Text("Kalkan:", "Shield:"), dx + 20, cy, 14, HexColor(CrossTheme.TextHex));
                if (DrawButton(shieldNames[p.ShieldStyle], dx + 120, cy - 4, 194, 26, false, mouse, click))
                {
                    p.ShieldStyle = (p.ShieldStyle + 1) % 3;
                    CrossPixelArt.Equipped = KnightEquipment.From(p);
                }

                cy += 46;
                // Live equipment preview
                DrawPixelFrame(dx + 20, cy, 312, 140, HexColor(CrossTheme.PanelHex));
                Raylib.DrawTexturePro(sceneTexture, new Rectangle(0, 0, 172, 92), new Rectangle(dx + 22, cy + 2, 308, 136), Vector2.Zero, 0, Color.White);
            }

            // Bottom Close/Save Button
            if (DrawButton(L.Text("KAYDET VE KAPAT", "SAVE & CLOSE"), dx + 20, dy + dh - 48, 312, 36, true, mouse, click))
            {
                CrossStorage.Save(p);
                audio.Apply(p);
                activeOverlay = 0;
            }
        }

        void DrawVolumeControl(int x, int y, string label, ref int volume, Vector2 mouse, bool click)
        {
            DrawPixelText(label, x, y, 14, HexColor(CrossTheme.TextHex));
            if (DrawButton("-", x + 180, y - 4, 30, 26, false, mouse, click) && volume > 0)
            {
                volume = Math.Max(0, volume - 10);
                audio.Apply(p);
            }
            DrawCenteredText($"{volume}%", x + 210, y, 40, 14, HexColor(CrossTheme.GoldHex));
            if (DrawButton("+", x + 250, y - 4, 30, 26, false, mouse, click) && volume < 100)
            {
                volume = Math.Min(100, volume + 10);
                audio.Apply(p);
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

            CrossPixelArt.RenderScene(canvas, 1.0, isSleep, devFrame, hour, isTavern, curAction, devFrame % 112);
            unsafe
            {
                fixed (uint* ptr = canvas.Pixels)
                {
                    Raylib.UpdateTexture(sceneTexture, ptr);
                }
            }

            DrawPixelFrame(dx + 12, dy + 122, 340, 184, HexColor(CrossTheme.PanelHex));
            Raylib.DrawTexturePro(sceneTexture, new Rectangle(0, 0, 172, 92), new Rectangle(dx + 14, dy + 124, 336, 180), Vector2.Zero, 0, Color.White);

            int cy = dy + 320;
            // Controls: Play/Pause, Step
            if (DrawButton(devPlaying ? "PAUSE" : "PLAY", dx + 12, cy, 80, 30, devPlaying, mouse, click))
            {
                devPlaying = !devPlaying;
            }
            if (DrawButton("<-", dx + 98, cy, 40, 30, false, mouse, click)) devFrame = Math.Max(0, devFrame - 1);
            if (DrawButton("->", dx + 144, cy, 40, 30, false, mouse, click)) devFrame++;
            DrawCenteredText($"F: {devFrame}", dx + 190, cy + 6, 80, 16, HexColor(CrossTheme.TextHex));

            // Speed
            if (DrawButton("0.5x", dx + 266, cy, 40, 30, devSpeed == 0.5f, mouse, click)) devSpeed = 0.5f;
            if (DrawButton("1x", dx + 312, cy, 40, 30, devSpeed == 1.0f, mouse, click)) devSpeed = 1.0f;

            cy += 44;
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
            return $"{h}s {m}d {sec}sn";
        }

        public void Dispose()
        {
            audio?.Dispose();
            Raylib.UnloadTexture(sceneTexture);
            Raylib.CloseWindow();
        }
    }
}
