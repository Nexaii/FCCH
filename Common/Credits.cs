namespace FCCH.Common
{
    public static class Credits
    {
        public readonly record struct Row(string Role, string Who, string Note);

        public static readonly Row[] Rows =
        {
            new("Inspiration", "Taurenkey (Puni.sh)",
                "In October 2025, the FC Chest Quick Deposit tweak from Pandora's Box sparked the idea for FCCH. I took a different direction from there, and it has come a long way since!"),
            new("Contributors", "FabioFrog91",
                "The searchable bulk lists in the Custom and Ignore tabs started as this user's idea!"),
        };
    }
}
