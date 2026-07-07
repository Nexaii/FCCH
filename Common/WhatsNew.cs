namespace FCCH.Common
{
    public static class WhatsNew
    {
        public const int Revision = 2;

        public readonly record struct Entry(string Title, string[] Details);

        public static readonly Entry[] Highlights =
        {
            new("Fast Move: Crystals", new[]
            {
                "Modifier + right-click a crystal to deposit or withdraw it (default: Ctrl).",
                "No number key needed. Deposits work from any open tab.",
            }),
        };

        public const string ScopeNote = "This window only appears for new features, not fixes.";

        public const string SettingsHint = "Enable or disable these features in Settings.";
    }
}
