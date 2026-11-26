namespace SoulsRevive.Cross
{
    /// <summary>The shared <see cref="Palette"/> for the current theme.</summary>
    public static class CrossTheme
    {
        public static bool Light;

        public static string BackgroundHex => Palette.Background(Light);
        public static string PanelHex => Palette.Panel(Light);
        public static string LineHex => Palette.Line(Light);
        public static string GoldHex => Palette.Gold(Light);
        public static string TextHex => Palette.Text(Light);
        public static string MutedHex => Palette.Muted(Light);
        public static string GreenHex => Palette.Green(Light);
        public static string PrimaryHex => Palette.Primary(Light);
    }
}
