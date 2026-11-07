using System;
using System.Collections.Generic;

namespace SoulsRevive.Cross
{
    public sealed class KnightEquipment
    {
        public readonly int Armor, Sword, Shield;
        public KnightEquipment(int armor, int sword, int shield)
        {
            Armor = Math.Max(0, Math.Min(2, armor));
            Sword = Math.Max(0, Math.Min(2, sword));
            Shield = Math.Max(0, Math.Min(2, shield));
        }
        public static KnightEquipment From(Preferences p) => new KnightEquipment(p.ArmorStyle, p.SwordStyle, p.ShieldStyle);
    }

    public static class CrossPixelArt
    {
        public const int TavernArrivalFrames = 48;
        public static KnightEquipment Equipped = new KnightEquipment(0, 0, 0);
        static bool armorInk;

        public sealed class ArmorInk : IDisposable
        {
            readonly bool before;
            public ArmorInk(bool enabled) { before = armorInk; armorInk = enabled; }
            public void Dispose() { armorInk = before; }
        }

        static uint EquipmentColor(string hex)
        {
            uint c = CrossCanvas.Hex(hex);
            if (!armorInk || Equipped.Armor == 0) return c;
            byte r = (byte)(c & 0xFF), g = (byte)((c >> 8) & 0xFF), b = (byte)((c >> 16) & 0xFF);
            if (r > g || g > b + 16 || b - r > 65) return c;
            double brightness = (r + g + b) / 765.0;
            if (Equipped.Armor == 1)
                return CrossCanvas.Blend(CrossCanvas.Hex("#131720"), CrossCanvas.Hex("#8B9AB0"), brightness);
            return CrossCanvas.Blend(CrossCanvas.Hex("#33231C"), CrossCanvas.Hex("#F4D08C"), brightness);
        }

        static void R(CrossCanvas c, string hex, int x, int y, int w, int h)
        {
            c.R(EquipmentColor(hex), x, y, w, h);
        }

        static void Poly(CrossCanvas c, string hex, params int[] points)
        {
            c.Poly(EquipmentColor(hex), points);
        }

        public static void RenderScene(CrossCanvas c, double health, bool rest, int frame, double hour, bool chill, WorkAction action, int actionFrame)
        {
            if (chill)
            {
                RenderTavern(c, frame);
                return;
            }

            double daylight = DayCycle.Sunlight(hour);
            uint sky = CrossCanvas.Blend(CrossCanvas.Hex("#121C30"), CrossCanvas.Hex("#83B6BE"), daylight);
            double dawn = Math.Max(0, 1 - Math.Abs(hour - 6) / 2.0);
            double dusk = Math.Max(0, 1 - Math.Abs(hour - 18.5) / 2.0);
            sky = CrossCanvas.Blend(sky, CrossCanvas.Hex("#B87978"), Math.Max(dawn, dusk) * 0.7);
            c.Clear(sky);

            for (int y = 32; y < 74; y += 4)
            {
                uint col = CrossCanvas.Blend(sky, CrossCanvas.Hex("#D6AC7D"), (y - 32) / 90.0 * (0.25 + daylight * 0.5));
                c.R(col, 0, y, 172, 4);
            }

            if (hour >= 6 && hour < 19)
            {
                int sunX = 15 + (int)((hour - 6) / 13.0 * 140);
                int sunY = 50 - (int)(Math.Sin((hour - 6) / 13.0 * Math.PI) * 40);
                R(c, "#EBC27E", sunX - 2, sunY + 2, 14, 8);
                R(c, "#FFE4A5", sunX, sunY, 10, 12);
                for (int i = 0; i < 3; i++)
                {
                    int cx = (i * 67 + frame / 20) % 205 - 25;
                    R(c, "#B8CBCA", cx, 18 + i * 9, 22, 3);
                    R(c, "#C9D5CF", cx + 5, 16 + i * 9, 12, 2);
                }
            }
            else
            {
                R(c, "#77828A", 135, 11, 13, 13);
                R(c, "#D9DDC4", 137, 12, 9, 9);
                c.R(sky, 135, 9, 8, 9);
            }

            Poly(c, "#1B2330", 0, 62, 22, 43, 44, 59, 74, 29, 111, 61, 140, 37, 172, 56, 172, 86, 0, 86);
            R(c, "#242A35", 17, 26, 13, 43); R(c, "#242A35", 13, 23, 5, 7); R(c, "#242A35", 22, 20, 4, 8); R(c, "#242A35", 29, 23, 5, 7);
            R(c, "#1C222C", 18, 31, 3, 35); R(c, "#30333A", 25, 37, 3, 6);
            R(c, "#1C232E", 34, 43, 15, 32); R(c, "#252D38", 39, 35, 6, 12);
            R(c, "#333644", 23, 16, 1, 5); R(c, "#66504A", 24, 16, 8, 3);
            R(c, "#1A202A", 144, 46, 17, 31); R(c, "#272D37", 147, 39, 5, 10); R(c, "#272D37", 156, 41, 4, 8);

            for (int i = 0; i < (daylight < 0.15 ? 21 : 0); i++)
            {
                int x = (i * 47 + 9) % 172, y = (i * 19 + 7) % 47;
                R(c, i % 5 == 0 ? "#69717B" : "#353D49", x, y, 1, 1);
            }

            R(c, "#1E242B", 0, 73, 172, 19); R(c, "#2A2D31", 0, 76, 172, 2);
            for (int i = 0; i < 16; i++) R(c, "#363638", (i * 31) % 172, 79 + (i % 4) * 3, 8, 1);
            R(c, "#11151A", 55, 81, 57, 4);

            // Bonfire and buried sword
            R(c, "#392C26", 111, 73, 32, 9); R(c, "#593C2A", 115, 74, 24, 7);
            R(c, "#A36637", 118, 78, 20, 2); R(c, "#805338", 115, 80, 14, 2);
            double beat = 2 * Math.PI * (frame % 16) / 16.0;
            int flicker = (int)Math.Round(2 * Math.Sin(beat) + Math.Sin(3 * beat));
            Poly(c, "#A24628", 119, 77, 116, 68, 121, 60 - flicker, 124, 67, 128, 53 + flicker, 132, 66, 136, 63, 139, 74, 134, 79);
            Poly(c, "#E6853E", 121, 76, 120, 69, 125, 64, 128, 58 + flicker, 132, 70, 135, 72, 133, 77);
            Poly(c, "#FFD48B", 125, 77, 124, 72, 128, 65 + flicker, 130, 73, 132, 77);
            R(c, "#B9BBB0", 128, 43, 2, 25); R(c, "#5E646B", 130, 44, 1, 23);
            R(c, "#99794B", 124, 43, 10, 2); R(c, "#67543F", 128, 36, 2, 7); R(c, "#B6A071", 127, 35, 4, 2);

            for (int i = 0; i < 8; i++)
            {
                int rise = (frame * 2 + i * 7) % 31;
                int drift = (int)Math.Round(rise * 0.15 + 2 * Math.Sin(beat + i));
                R(c, rise < 17 ? "#DF9250" : "#8F4D31", 120 + (i * 11) % 16 + drift, 67 - rise, 1, 1);
            }

            if (rest)
            {
                using (new ArmorInk(true)) DrawRestingKnight(c, frame, true);
            }
            else
            {
                using (new ArmorInk(true)) DrawWorkingKnight(c, health, frame, action, actionFrame);
            }

            R(c, "#4A4740", 45, 82, 7, 3); R(c, "#636055", 46, 81, 4, 1);
            R(c, "#343B38", 9, 79, 1, 5); R(c, "#41493E", 10, 81, 2, 1);
            R(c, "#343B38", 158, 79, 1, 5); R(c, "#41493E", 156, 81, 2, 1);
        }

        static void DrawRestingKnight(CrossCanvas c, int frame, bool asleep)
        {
            int breath = (frame / 12) % 2;
            R(c, "#29252B", 73, 76, 39, 5); Poly(c, "#713B39", 79, 67, 99, 66, 111, 78, 73, 79);
            R(c, "#A65C48", 79, 74, 26, 3);
            R(c, "#394551", 87, 68 + breath, 16, 7); R(c, "#91A0AA", 88, 68 + breath, 13, 3);
            R(c, "#596975", 101, 72, 7, 7); R(c, "#B2BAB9", 101, 72, 5, 2); R(c, "#34404A", 105, 77, 9, 3);
            R(c, "#28323C", 73, 61 + breath, 14, 14); R(c, "#98A7AF", 74, 61 + breath, 12, 12);
            R(c, "#D0D7CE", 75, 62 + breath, 3, 8); R(c, "#394753", 75, 66 + breath, 10, 2);
            R(c, "#C5B184", 78, 67 + breath, 4, 1); R(c, "#667782", 81, 61 + breath, 4, 11);

            c.Push(); c.Translate(85, 39); DrawEquippedShield(c); c.Pop();
            c.Push(); c.Translate(62, 66); c.Scale(0.45f, 0.45f); HeldSword(c, 0, 0, 180); c.Pop();

            if (asleep)
            {
                int rise = (frame / 4) % 16;
                int x = 83 + rise / 4, y = 57 - rise;
                R(c, "#CFD5C3", x, y, 4, 1); R(c, "#CFD5C3", x + 2, y + 1, 1, 1);
                R(c, "#CFD5C3", x + 1, y + 2, 1, 1); R(c, "#CFD5C3", x, y + 3, 4, 1);
            }
        }

        static void DrawWorkingKnight(CrossCanvas c, double health, int frame, WorkAction action, int actionFrame)
        {
            int bob = (frame / 14) % 2;
            int slump = health < 0.25 ? 2 : health < 0.5 ? 1 : 0;
            double activity = action == WorkAction.Idle ? 0.0 : actionFrame < 16 ? actionFrame / 16.0 : actionFrame < 96 ? 1.0 : Math.Max(0, (112 - actionFrame) / 16.0);
            double shieldStow = action == WorkAction.Sharpen ? (actionFrame < 8 ? actionFrame / 8.0 : actionFrame < 104 ? 1.0 : Math.Max(0, (112 - actionFrame) / 8.0)) : activity;

            c.Push();
            int ox = 82, oy = 26;
            if (action == WorkAction.TendFire)
            {
                double walk = actionFrame < 24 ? actionFrame / 24.0 : actionFrame < 88 ? 1.0 : Math.Max(0, (112 - actionFrame) / 24.0);
                ox += (int)Math.Round(16 * walk);
            }
            c.Translate(ox, oy);

            if (action == WorkAction.Sharpen && activity > 0 && activity < 1)
                HeldSword(c, 17, 35, -90 * activity);
            if ((action == WorkAction.Map || action == WorkAction.TendFire) && activity >= 0.35)
                HeldSword(c, 5, 17, 135);

            int wind = (int)Math.Round(Math.Sin(frame * Math.PI / 32.0));
            Poly(c, "#30232B", -9, 18, 7, 18, 13, 29, 18 + wind, 47, 10, 49, -3, 46, -15, 50, -25 + wind, 47, -17, 31);
            Poly(c, "#884A48", -9, 20, -14, 32, -22 + wind, 46, -15, 47, -8, 43, -3, 24);
            Poly(c, "#B06B57", -10, 23, -12, 34, -17 + wind, 44, -14, 44, -7, 31, -5, 21);
            Poly(c, "#60343D", -4, 22, 7, 20, 11, 31, 15 + wind, 46, 9, 46, 3, 41);
            Poly(c, "#9B584E", 5, 22, 8, 30, 11 + wind, 44, 8, 43, 3, 29);

            c.Push();
            c.Translate(0, activity > 0 ? 0 : -bob);
            if (slump == 0)
            {
                R(c, "#3B4652", -6, 38, 5, 14); R(c, "#303A45", 3, 38, 5, 14);
                R(c, "#8A99A2", -5, 39, 3, 8); R(c, "#7D8C96", 4, 39, 3, 8);
                R(c, "#21262B", -7, 51, 6, 4); R(c, "#1B2024", 2, 51, 6, 4);
            }
            else
            {
                R(c, "#3B4652", -8, 38 + slump, 6, 12); R(c, "#303A45", 2, 38 + slump, 6, 12);
                R(c, "#21262B", -9, 49 + slump, 7, 4); R(c, "#1B2024", 2, 49 + slump, 7, 4);
            }
            R(c, "#34424E", -9, 18, 20, 19);
            R(c, "#8E9FA9", -7, 20, 16, 14);
            R(c, "#D5DCD5", -6, 21, 5, 12);
            R(c, "#566470", 2, 21, 6, 12);
            R(c, "#23282D", -8, 35, 18, 4);
            c.Pop();

            // Shield
            c.Push();
            c.Translate((float)(12 * shieldStow), (float)(-10 * shieldStow));
            c.Scale((float)(1 - 0.7 * shieldStow), 1);
            DrawEquippedShield(c);
            c.Pop();

            // Helmet
            float look = 0;
            if (action == WorkAction.Map && activity > 0.5)
                look = (float)(Math.Sin(actionFrame * 0.15) * 2.5);
            c.Push();
            if (action == WorkAction.Map) c.Translate((float)Math.Round(activity) + look, (float)Math.Round(2 * activity));
            if (health < 0.25) c.Translate(1, 2 + Math.Max(bob, 0));
            R(c, "#2A333C", -7, 4 - bob, 15, 15);
            R(c, "#8E9DA7", -6, 5 - bob, 13, 13);
            R(c, "#D2DBD0", -5, 6 - bob, 3, 8);
            R(c, "#2E3A44", -5, 11 - bob, 11, 2);
            R(c, "#BFAC7C", -2, 12 - bob, 4, 1);
            R(c, "#63747E", 2, 5 - bob, 4, 12);
            c.Pop();

            DrawWorkEquipment(c, action, actionFrame, activity);
            c.Pop();
        }

        static void Arm(CrossCanvas c, int shoulderX, int handX, int handY)
        {
            int elbowX = (shoulderX + handX) / 2 + (shoulderX < handX ? 3 : -3);
            int elbowY = (24 + handY) / 2 + 3;
            c.Lines("#25323C", shoulderX, 24, elbowX, elbowY, handX, handY);
            c.Lines("#8C9BA5", shoulderX, 24, elbowX, elbowY, handX, handY);
        }

        static void Glove(CrossCanvas c, int x, int y)
        {
            R(c, "#34424C", x - 3, y - 2, 6, 6);
            R(c, "#AAB7BD", x - 2, y - 2, 4, 3);
            R(c, "#D0D7D0", x - 2, y - 2, 3, 1);
        }

        static void HeldSword(CrossCanvas c, int x, int y, double angle)
        {
            using (new ArmorInk(false))
            {
                c.Push(); c.Translate(x, y); c.Rotate((float)angle);
                R(c, "#605449", -1, -2, 3, 9); R(c, "#BDA16D", -2, 6, 5, 2);
                R(c, "#AD8C58", -5, -4, 11, 2);
                if (Equipped.Sword == 1)
                {
                    Poly(c, "#CCDDE1", -2, -4, -3, -25, 0, -31, 3, -25, 3, -4);
                    R(c, "#7995A4", 1, -24, 2, 20); R(c, "#F1F0D9", -1, -26, 1, 22);
                }
                else if (Equipped.Sword == 2)
                {
                    R(c, "#ABC7CC", 0, -31, 1, 27); R(c, "#ECF4DD", 0, -32, 1, 2);
                    R(c, "#D2A85D", -6, -4, 13, 2);
                }
                else
                {
                    R(c, "#DFDCD0", -1, -28, 2, 24); R(c, "#8B9DA7", 1, -27, 1, 23);
                    R(c, "#E4E1CF", 0, -31, 1, 4);
                }
                c.Pop();
            }
        }

        static void DrawWorkEquipment(CrossCanvas c, WorkAction action, int frame, double blend)
        {
            int rightX = 17, rightY = 35;
            if (action == WorkAction.Sharpen || blend == 0)
            {
                bool honing = action == WorkAction.Sharpen && frame >= 24 && frame < 80;
                double handBlend = frame >= 80 ? Math.Max(0, (88 - frame) / 8.0) : blend;
                double stroke = honing ? Math.Sin(frame * 0.45) : Math.Sin(79 * 0.45);
                int leftX = (int)Math.Round(-12 + handBlend * (13 + 5 * stroke));
                int leftY = (int)Math.Round(34 - 3 * handBlend);
                Arm(c, 12, rightX, rightY);
                if (handBlend > 0) Arm(c, -10, leftX, leftY);
                if (action != WorkAction.Sharpen || blend <= 0 || blend >= 1)
                    HeldSword(c, rightX, rightY, -90 * blend);
                Glove(c, rightX, rightY);
                if (handBlend > 0)
                {
                    Glove(c, leftX, leftY);
                    if (honing) { R(c, "#707775", leftX - 2, leftY + 2, 6, 3); R(c, "#CED1B8", leftX - 1, leftY + 2, 4, 1); }
                    if (honing) DrawHoningSparks(c, frame);
                }
                return;
            }

            if (blend < 0.35)
            {
                double stow = blend / 0.35;
                int gripX = rightX - (int)Math.Round(12 * stow), gripY = rightY - (int)Math.Round(18 * stow);
                HeldSword(c, gripX, gripY, 135 * stow); Arm(c, 12, gripX, gripY); Glove(c, gripX, gripY);
                return;
            }

            if (action == WorkAction.Map)
            {
                int width = (int)Math.Round(26 * Math.Min(1, (blend - 0.35) / 0.65));
                int left = 4 - width / 2, right = 4 + width / 2;
                int fingerX = right, fingerY = 35;
                Arm(c, -10, left, 35); Arm(c, 12, fingerX, fingerY);
                R(c, "#756041", left - 1, 27, width + 3, 17); R(c, "#CEB77F", left, 28, width + 1, 14);
                if (width > 16)
                {
                    R(c, "#A38656", left + 3, 31, width - 6, 1);
                    R(c, "#866C41", left + 5, 34, width - 9, 1);
                    R(c, "#A38656", left + 4, 38, width - 7, 1);
                }
                Glove(c, left, 34); Glove(c, fingerX, fingerY);
                return;
            }

            if (action == WorkAction.TendFire)
            {
                int logX = 14, logY = 33;
                double throwProgress = frame < 40 ? 0.0 : frame < 60 ? (frame - 40) / 20.0 : 1.0;
                if (throwProgress > 0)
                {
                    int startX = 14, startY = 33, targetX = 35, targetY = 46;
                    int curX = (int)Math.Round(startX + (targetX - startX) * throwProgress);
                    int curY = (int)Math.Round(startY + (targetY - startY) * throwProgress - Math.Sin(throwProgress * Math.PI) * 10);
                    DrawWorkLog(c, curX, curY, throwProgress * 120);
                }
                else
                {
                    Arm(c, -10, logX - 4, logY + 1); Arm(c, 12, logX + 6, logY + 2);
                    DrawWorkLog(c, logX, logY, -15);
                    Glove(c, logX - 4, logY + 1); Glove(c, logX + 6, logY + 2);
                }
            }
        }

        static void DrawHoningSparks(CrossCanvas c, int frame)
        {
            int seed = frame * 1337;
            for (int i = 0; i < 3; i++)
            {
                int randX = ((seed >> (i * 3)) & 7) - 3;
                int randY = ((seed >> (i * 3 + 2)) & 7) - 3;
                R(c, (i % 2 == 0) ? "#FFF6A3" : "#FF9B42", 5 + randX, 32 + randY, 1, 1);
            }
        }

        static void DrawWorkLog(CrossCanvas c, int x, int y, double angle)
        {
            c.Push(); c.Translate(x, y); c.Rotate((float)angle);
            R(c, "#392C26", -7, -2, 14, 4); R(c, "#6B4933", -6, -2, 12, 3);
            R(c, "#8F6443", -5, -1, 10, 1);
            c.Pop();
        }

        static void DrawEquippedShield(CrossCanvas c)
        {
            using (new ArmorInk(false))
            {
                if (Equipped.Shield == 1)
                {
                    Poly(c, "#342F2A", -19, 29, -15, 26, -9, 26, -5, 30, -5, 40, -9, 45, -15, 45, -19, 40);
                    Poly(c, "#9D7750", -17, 30, -14, 28, -10, 28, -7, 31, -7, 39, -10, 43, -14, 43, -17, 39);
                    R(c, "#684C35", -13, 29, 1, 13); R(c, "#B8C0BD", -15, 33, 7, 6); R(c, "#E0D7B9", -13, 34, 3, 3);
                }
                else if (Equipped.Shield == 2)
                {
                    Poly(c, "#283747", -19, 26, -5, 26, -5, 41, -12, 48, -19, 41);
                    Poly(c, "#A7B6BE", -18, 27, -6, 27, -6, 40, -12, 46, -18, 40);
                    Poly(c, "#3B607A", -16, 29, -8, 29, -8, 39, -12, 43, -16, 39);
                    Poly(c, "#DCE6CF", -12, 31, -9, 36, -12, 40, -15, 36);
                }
                else
                {
                    Poly(c, "#252A32", -19, 28, -7, 26, -5, 41, -11, 48, -19, 41);
                    Poly(c, "#6E747A", -18, 29, -8, 28, -7, 40, -11, 45, -17, 40);
                    Poly(c, "#354650", -16, 30, -9, 29, -9, 39, -12, 43, -15, 39);
                    R(c, "#B9A16D", -13, 31, 2, 10); R(c, "#B9A16D", -16, 34, 7, 2);
                }
            }
        }

        // Tavern rendering
        public static void RenderTavern(CrossCanvas c, int frame)
        {
            frame = Math.Max(0, frame);
            c.Clear(CrossCanvas.Hex("#322B2A"));
            DrawTavernRoom(c, frame);
            DrawTavernChair(c, 47); DrawTavernChair(c, 126);
            R(c, "#211C1D", 77, 81, 40, 3);
            R(c, "#875C3A", 78, 64, 38, 17); R(c, "#B88354", 77, 63, 40, 3);
            R(c, "#D8A770", 77, 63, 40, 1); R(c, "#4B3224", 80, 67, 34, 14);

            int cycle = frame % 48;
            DrawBottle(c, 92, 53, "#426749");
            DrawBottle(c, 97, 51, "#6B432C");

            int knightTarget = 62;
            int knightX = frame < TavernArrivalFrames
                ? (int)Math.Round(-24 + (knightTarget + 24) * Math.Min(1.0, frame / (double)TavernArrivalFrames))
                : knightTarget;
            bool walking = frame < TavernArrivalFrames;

            DrawTavernKnight(c, 114, frame, true, false);
            DrawTavernKnight(c, knightX, frame, false, walking);

            if (!walking) DrawTavernDrinks(c, cycle, frame);
        }

        static void DrawTavernRoom(CrossCanvas c, int frame)
        {
            // Back wall wooden beams and stone fireplace
            for (int x = 0; x < 172; x += 18) R(c, "#26201F", x, 0, 2, 72);
            R(c, "#211A1A", 0, 72, 172, 20); R(c, "#3A2E2A", 0, 74, 172, 1);
            // Hearth
            R(c, "#1B1617", 6, 24, 36, 48); R(c, "#443936", 4, 22, 40, 4);
            R(c, "#2E2422", 8, 30, 32, 42);
            int fireBeat = frame % 12;
            Poly(c, "#E2692B", 14, 68, 18, 54 - fireBeat % 3, 22, 60, 26, 50 - (fireBeat + 1) % 4, 30, 62, 34, 68);
            Poly(c, "#F5B445", 17, 68, 20, 58, 24, 64, 27, 56, 31, 68);
            // Shield on wall
            c.Push(); c.Translate(145, 20);
            Poly(c, "#603A2B", 0, 0, 14, 0, 14, 16, 7, 22, 0, 16);
            Poly(c, "#D3A056", 2, 2, 12, 2, 12, 14, 7, 19, 2, 14);
            c.Pop();
        }

        static void DrawTavernChair(CrossCanvas c, int x)
        {
            R(c, "#281F1C", x, 54, 5, 29); R(c, "#8F5F38", x + 1, 55, 3, 26);
            R(c, "#AD7D46", x, 55, 5, 2); R(c, "#69422A", x, 72, 17, 4);
            R(c, "#B3844E", x, 72, 17, 1); R(c, "#3A291F", x + 12, 76, 3, 7);
        }

        static void DrawBottle(CrossCanvas c, int x, int y, string glass)
        {
            R(c, "#181414", x, y + 4, 4, 8); R(c, glass, x + 1, y + 5, 2, 7);
            R(c, "#846241", x + 1, y, 2, 4);
        }

        static void DrawTavernKnight(CrossCanvas c, int x, int frame, bool solaire, bool walking)
        {
            using (new ArmorInk(!solaire))
            {
                c.Push();
                int breath = walking ? -(int)Math.Round(Math.Abs(Math.Sin(frame * Math.PI / 4.0))) : ((frame / 12 + (solaire ? 1 : 0)) % 2);
                c.Translate(x, breath);

                // Body
                if (solaire)
                {
                    R(c, "#1B1718", -8, 48, 16, 24);
                    Poly(c, "#D5DCDB", -7, 49, 7, 49, 9, 68, -9, 68);
                    DrawSunCrest(c, -4, 52);
                    R(c, "#1B1718", -8, 68, 16, 4);
                    // Solaire's iron helm
                    R(c, "#2A2E32", -6, 34, 12, 15);
                    R(c, "#94A3A8", -5, 35, 10, 13);
                    R(c, "#CED6D4", -4, 36, 3, 9);
                    R(c, "#384148", -5, 41, 10, 2);
                    Poly(c, "#8F3A2B", 3, 30, 8, 25, 4, 34); // Red feather
                }
                else
                {
                    // Knight
                    R(c, "#2A333C", -7, 34, 14, 15);
                    R(c, "#8E9DA7", -6, 35, 12, 13);
                    R(c, "#D2DBD0", -5, 36, 3, 8);
                    R(c, "#2E3A44", -5, 41, 10, 2);
                    R(c, "#BFAC7C", -2, 42, 4, 1);
                    R(c, "#63747E", 2, 35, 4, 12);
                    R(c, "#34424E", -8, 48, 16, 20);
                    R(c, "#8E9FA9", -6, 50, 12, 14);
                    R(c, "#D5DCD5", -5, 51, 4, 12);
                    R(c, "#23282D", -7, 65, 14, 4);
                }

                // Legs
                R(c, "#1B1718", -6, 69, 5, 12); R(c, "#1B1718", 2, 69, 5, 12);
                c.Pop();
            }
        }

        static void DrawSunCrest(CrossCanvas c, int x, int y)
        {
            // Holy Sun of Astora
            R(c, "#D97B2A", x + 1, y + 1, 6, 6);
            R(c, "#F5DE5B", x + 2, y + 2, 4, 4);
            R(c, "#D97B2A", x + 3, y - 1, 2, 2);
            R(c, "#D97B2A", x + 3, y + 7, 2, 2);
            R(c, "#D97B2A", x - 1, y + 3, 2, 2);
            R(c, "#D97B2A", x + 7, y + 3, 2, 2);
        }

        static void DrawTavernDrinks(CrossCanvas c, int cycle, int frame)
        {
            // Tankards on the table or sipping
            bool sippingKnight = cycle >= 12 && cycle < 24;
            bool sippingSolaire = cycle >= 26 && cycle < 38;

            if (!sippingKnight)
            {
                DrawTankard(c, 73, 58, false);
                Arm(c, 62 + 6, 73, 62);
                Glove(c, 73, 62);
            }
            else
            {
                DrawTankard(c, 64, 46, false);
                Arm(c, 62 + 6, 64, 48);
                Glove(c, 64, 48);
            }

            if (!sippingSolaire)
            {
                DrawTankard(c, 102, 58, true);
                Arm(c, 114 - 6, 102, 62);
                Glove(c, 102, 62);
            }
            else
            {
                DrawTankard(c, 111, 46, true);
                Arm(c, 114 - 6, 111, 48);
                Glove(c, 111, 48);
            }
        }

        static void DrawTankard(CrossCanvas c, int x, int y, bool handleRight)
        {
            R(c, "#443C3A", x, y, 7, 9);
            R(c, "#94877A", x + 1, y + 1, 5, 7);
            R(c, "#D5C9B8", x + 1, y, 5, 2); // Foam
            int hx = handleRight ? x + 6 : x - 2;
            R(c, "#3A3432", hx, y + 2, 2, 5);
        }
    }
}
