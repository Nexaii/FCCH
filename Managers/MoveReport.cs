using System.Collections.Generic;
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
            var b = batch.HasValue
                ? ((int, int, int, int)?)(batch.Value.Succeeded, batch.Value.Total, batch.Value.Refused, batch.Value.Blocked)
                : null;
            Send(MoveSummary.Build(b, DrainBuckets()), failed);
        }

        public static void Idle(string emptyMessage)
        {
            if (!HasOverflow)
            {
                Chat.Result(emptyMessage);
                return;
            }

            Send(MoveSummary.Build(null, DrainBuckets()), true);
        }

        private static void Send(string message, bool failed)
        {
            if (failed) Chat.Warn(message);
            else Chat.Result(message);
        }

        private static void DiscardOverflow()
        {
            OperationManager.LastDepositOverflow.Clear();
            OperationManager.LastWithdrawOverflow.Clear();
            OperationManager.LastDuplicateOverflow.Clear();
        }

        private static List<MoveSummary.Bucket> DrainBuckets() => new()
        {
            Drain("stacks full", OperationManager.LastDepositOverflow),
            Drain("inventory full", OperationManager.LastWithdrawOverflow),
            Drain("stacks full", OperationManager.LastDuplicateOverflow),
        };

        private static MoveSummary.Bucket Drain(string reason, List<(uint ItemId, uint Remaining)> overflow)
        {
            if (overflow.Count == 0)
                return new MoveSummary.Bucket(System.Array.Empty<string>(), reason);

            overflow.Sort((a, b) => b.Remaining.CompareTo(a.Remaining));

            var names = new List<string>(overflow.Count);
            foreach (var entry in overflow)
                names.Add(ItemNames.Get(entry.ItemId));

            overflow.Clear();
            return new MoveSummary.Bucket(names, reason);
        }
    }
}
