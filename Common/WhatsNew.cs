namespace FCCH.Common
{
    public static class WhatsNew
    {
        public const int Revision = 3;

        public readonly record struct Entry(string Title, string[] Details);

        public static readonly Entry[] Highlights =
        {
            new("Automatic gil deposits", new[]
            {
                "Settings, General tab. Choose a percentage or a fixed amount.",
                "Runs when you open the company chest. Always Keep holds back a minimum.",
                "Gil commands or IPC take priority.",
            }),
            new("Custom and Ignore tabs", new[]
            {
                "Type a category in the search box, like Metal, to add every item at once.",
                "Click the Mode or Max header to set every row shown.",
                "Press a cell and drag to copy that value down the list.",
                "Undo reverses the last 10 changes.",
                "",
                "Category idea by FabioFrog91. Thank you! It led to the whole Custom and Ignore overhaul.",
            }),
        };

        public const string ScopeNote = "This window only appears for new features, not fixes.";

        public const string SettingsHint = "Enable or disable these features in Settings.";
    }
}
