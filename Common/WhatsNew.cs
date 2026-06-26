namespace FCCH.Common
{
    public static class WhatsNew
    {
        public const int Revision = 1;

        public readonly record struct Entry(string Title, string[] Details);

        public static readonly Entry[] Highlights =
        {
            new("Chest Search", new[]
            {
                "Highlights matches in the open tab.",
            }),
            new("Fast Move", new[]
            {
                "Ctrl + right-click to deposit or withdraw.",
                "Optionally hold keys 1-5 to pick the tab.",
                "Example: Ctrl + 1 + right-click = tab 1.",
            }),
            new("Sort and Merge", new[]
            {
                "Toolbar Sort button sorts the open tab by category, ID, name, or quantity.",
                "Or merge its stacks in place.",
            }),
        };

        public const string ScopeNote = "This window only appears for new features, not fixes.";

        public const string SettingsHint = "Enable or disable these features in Settings.";
    }
}
