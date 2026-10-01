namespace FCCH.Common
{
    public enum ChangeKind
    {
        Fixed,
        Added,
        Changed
    }

    public static class Changelog
    {
        public const int Revision = 4;

        public readonly record struct Entry(ChangeKind Kind, string Text);

        public readonly record struct Version(string Label, string Date, Entry[] Entries);

        public static readonly Version[] Versions =
        {
            new("2.6.0.0", "2026-10-01", new[]
            {
                new Entry(ChangeKind.Added, "Info button in the settings title bar. Opens Changelog, Logs, and Credits."),
                new Entry(ChangeKind.Added, "Logs tab shows recent FCCH activity. Copy, clear, or save it to a file."),
                new Entry(ChangeKind.Changed, "Lock and Snap moved from the title bar to the General tab."),
                new Entry(ChangeKind.Changed, "One chat line per batch, naming the items that did not fit."),
                new Entry(ChangeKind.Fixed, "The gil preview no longer says \"Not logged in.\" when you hold 0 gil."),
                new Entry(ChangeKind.Fixed, "The move count no longer counts HQ items twice with Lower Quality on Deposit."),
            }),
            new("2.5.1.0", "2026-09-02", new[]
            {
                new Entry(ChangeKind.Added, "Import custom lists from Artisan and Teamcraft text lists."),
                new Entry(ChangeKind.Added, "Artisan: Export List. Teamcraft: Copy as Text."),
            }),
            new("2.5.0.0", "2026-08-25", new[]
            {
                new Entry(ChangeKind.Added, "Automatic gil deposits. Settings, General tab. Choose a percentage or a fixed amount."),
                new Entry(ChangeKind.Added, "Type a category in the Custom or Ignore search box, like Metal, to add every item at once."),
                new Entry(ChangeKind.Added, "Press a cell and drag to copy that value down the list. Undo reverses the last 10."),
                new Entry(ChangeKind.Changed, "Gil deposits run when you open the company chest. Always Keep holds back a minimum."),
            }),
            new("2.4.6.0", "2026-08-03", new[]
            {
                new Entry(ChangeKind.Fixed, "A full FC chest no longer drops the frame rate while scrolling item names."),
                new Entry(ChangeKind.Fixed, "Gil withdraw no longer reports a false timeout."),
                new Entry(ChangeKind.Fixed, "An HQ condition no longer sorts the wrong items."),
            }),
            new("2.4.5.0", "2026-07-27", new[]
            {
                new Entry(ChangeKind.Changed, "Removed the old migrator."),
            }),
            new("2.4.4.0", "2026-07-23", new[]
            {
                new Entry(ChangeKind.Changed, "Withdraw pulls from the rear stack first, so your front stacks stay maxed."),
            }),
            new("2.4.3.0", "2026-07-15", new[]
            {
                new Entry(ChangeKind.Added, "Quiet Chat toggle for leaner chat output."),
                new Entry(ChangeKind.Changed, "Organizer jumps to the affected tab, skips the full rescan, and reports one summary line per job."),
            }),
            new("2.4.2.0", "2026-07-11", new[]
            {
                new Entry(ChangeKind.Changed, "Search bar adjustments."),
            }),
            new("2.4.1.0", "2026-07-07", new[]
            {
                new Entry(ChangeKind.Added, "Fast Move handles crystals. No number key needed, and deposits work from any open tab."),
                new Entry(ChangeKind.Fixed, "Corrected the slot order used when depositing and withdrawing."),
            }),
            new("2.4.0.0", "2026-06-26", new[]
            {
                new Entry(ChangeKind.Added, "Fast Move. Hold a modifier and click an item to send it straight to or from the chest."),
                new Entry(ChangeKind.Added, "Chest search bar with highlighted matches."),
                new Entry(ChangeKind.Added, "Merge action consolidates stacks in place."),
                new Entry(ChangeKind.Changed, "Rebuilt Sort to reorder items within the tab natively."),
                new Entry(ChangeKind.Changed, "Crystals: right-click a cell to set its keep amount inline. No more popup."),
            }),
            new("2.3.2.0", "2026-06-05", new[]
            {
                new Entry(ChangeKind.Changed, "Moved to puni.sh. Updates switch over automatically."),
            }),
            new("2.3.1.0", "2026-05-25", new[]
            {
                new Entry(ChangeKind.Fixed, "Stopped the refusal spam during moves."),
                new Entry(ChangeKind.Changed, "Faster indexing between moves."),
            }),
            new("2.3.0.0", "2026-05-13", new[]
            {
                new Entry(ChangeKind.Added, "Compact item names, item context menu, and toolbar customization. Each optional."),
                new Entry(ChangeKind.Added, "Inline per row buttons on the Custom, Ignore, and Workshop tabs."),
                new Entry(ChangeKind.Changed, "Hardened shutdown and teardown for a cleaner exit."),
            }),
            new("2.2.0.0", "2026-05-08", new[]
            {
                new Entry(ChangeKind.Added, "Per tab control (1 to 5) for Deposit and Withdraw."),
                new Entry(ChangeKind.Added, "Deposit Custom List support."),
                new Entry(ChangeKind.Changed, "Updated for game patch API15."),
            }),
            new("2.1.0.1", "2026-03-03", new[]
            {
                new Entry(ChangeKind.Added, "Workshoppa integration."),
                new Entry(ChangeKind.Fixed, "Auto Confirm now checks that the FC chest is open, fixing a trade window gil bug."),
                new Entry(ChangeKind.Fixed, "Updated for game patch 7.45."),
            }),
            new("2.0.0.5", "2026-02-08", new[]
            {
                new Entry(ChangeKind.Fixed, "Stability fixes."),
            }),
            new("2.0.0.0", "2026-01-25", new[]
            {
                new Entry(ChangeKind.Added, "Crystal management. Dedicated tab, deposit and withdraw with keep amounts."),
                new Entry(ChangeKind.Added, "Gil deposit and withdraw commands. gd and gw, with k, m, and all support."),
                new Entry(ChangeKind.Added, "Organizer. Move and Sort items between FC chest tabs."),
                new Entry(ChangeKind.Changed, "Updated the license to GNU Affero General Public License v3.0."),
            }),
        };
    }
}
