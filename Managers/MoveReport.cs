using System.Collections.Generic;
using System.Text;
using FCCH.Common;

namespace FCCH.Managers
{
    internal static class MoveReport
    {
        public static bool HasOverflow =>
            OperationManager.LastDepositOverflow.Count > 0
            || OperationManager.LastWithdrawOverflow.Count > 0
            || OperationManager.LastDuplicateOverflow.Count > 0;

        public static void Completed((int Succeeded, int Total, int Refused, int Blocked, bool Suppressed)? batch)
        {
            if (batch.HasValue && batch.Value.Suppressed)
            {
                DiscardOverflow();
                return;
            }

            if (!batch.HasValue && !HasOverflow)
                return;

            var failed = HasOverflow;
            var text = new StringBuilder();

            if (batch.HasValue)
            {
                var b = batch.Value;
                text.Append(b.Succeeded == 0 ? "Nothing moved." : $"{b.Succeeded}/{b.Total} moved.");
                if (b.Refused > 0) text.Append($" {b.Refused} refused.");
                if (b.Blocked > 0) text.Append($" {b.Blocked} skipped (blocked tabs).");
            }
            else
            {
                text.Append("Nothing moved.");
            }

            AppendOverflow(text);
            Send(text.ToString(), failed);
        }

        public static void Idle(string emptyMessage)
        {
            if (!HasOverflow)
            {
                ChatHelper.Info(emptyMessage);
                return;
            }

            var text = new StringBuilder("Nothing moved.");
            AppendOverflow(text);
            Send(text.ToString(), true);
        }

        private static void Send(string message, bool failed)
        {
            if (failed) ChatHelper.Alert(message);
            else ChatHelper.Info(message);
        }

        private static void DiscardOverflow()
        {
            OperationManager.LastDepositOverflow.Clear();
            OperationManager.LastWithdrawOverflow.Clear();
            OperationManager.LastDuplicateOverflow.Clear();
        }

        private static void AppendOverflow(StringBuilder text)
        {
            AppendList(text, "stacks full", OperationManager.LastDepositOverflow);
            AppendList(text, "inventory full", OperationManager.LastWithdrawOverflow);
            AppendList(text, "stacks full", OperationManager.LastDuplicateOverflow);
        }

        private static void AppendList(StringBuilder text, string reason, List<(uint ItemId, uint Remaining)> overflow)
        {
            if (overflow.Count == 0) return;

            overflow.Sort((a, b) => b.Remaining.CompareTo(a.Remaining));

            if (text.Length > 0) text.Append(' ');
            text.Append(ItemNames.Get(overflow[0].ItemId));

            if (overflow.Count == 2)
                text.Append($" and {ItemNames.Get(overflow[1].ItemId)}");
            else if (overflow.Count == 3)
                text.Append($", {ItemNames.Get(overflow[1].ItemId)} and {ItemNames.Get(overflow[2].ItemId)}");
            else if (overflow.Count > 3)
                text.Append($", {ItemNames.Get(overflow[1].ItemId)} and {overflow.Count - 2} more");

            text.Append($" did not fit ({reason}).");
            overflow.Clear();
        }
    }
}
