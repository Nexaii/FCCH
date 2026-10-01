using System.Collections.Generic;
using System.Text;

namespace FCCH.Managers
{
    internal static class MoveSummary
    {
        internal readonly struct Bucket
        {
            public readonly IReadOnlyList<string> Names;
            public readonly string Reason;

            public Bucket(IReadOnlyList<string> names, string reason)
            {
                Names = names;
                Reason = reason;
            }
        }

        public static string Build((int Succeeded, int Total, int Refused, int Blocked)? batch, IReadOnlyList<Bucket> buckets)
        {
            var unfit = 0;
            foreach (var bucket in buckets)
                unfit += bucket.Names.Count;

            var clauses = new List<string>();
            string head;

            if (batch.HasValue)
            {
                var b = batch.Value;
                head = b.Succeeded == 0 ? "Nothing moved" : $"Moved {b.Succeeded}/{b.Total + unfit}";
                if (b.Refused > 0) clauses.Add($"{b.Refused} refused");
                if (b.Blocked > 0) clauses.Add($"{b.Blocked} skipped (blocked)");
            }
            else
            {
                head = "Nothing moved";
            }

            foreach (var bucket in buckets)
            {
                if (bucket.Names.Count == 0) continue;
                clauses.Add($"{Names(bucket.Names)} didn't fit ({bucket.Reason})");
            }

            var text = new StringBuilder(head);
            foreach (var clause in clauses)
                text.Append(" · ").Append(clause);
            text.Append('.');
            return text.ToString();
        }

        private static string Names(IReadOnlyList<string> names)
        {
            var text = new StringBuilder(names[0]);
            if (names.Count == 2)
                text.Append($" & {names[1]}");
            else if (names.Count > 2)
                text.Append($", {names[1]} +{names.Count - 2}");
            return text.ToString();
        }
    }
}
