namespace FCCH.UI
{
    internal static class BulkHeader
    {
        public const int ConfirmThreshold = 50;

        public static string Tooltip(int shown, int total, string destination, bool mixed)
        {
            var scope = shown == total ? $"{shown:N0} items" : $"{shown:N0} shown items";

            return mixed
                ? $"{scope}, mixed. Set all to {destination}."
                : $"Set all {scope} to {destination}";
        }
    }
}
