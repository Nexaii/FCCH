#if DEBUG
using System.Collections.Generic;
using FCCH.Common;

namespace FCCH.Managers
{
    internal static class ChatSim
    {
        private static readonly uint[] SampleItems = { 10373, 27849, 12537, 5111 };

        public static void Run(MoveManager moveManager)
        {
            if (moveManager.IsProcessing)
            {
                ChatHelper.Warning("simchat: a move is running. Wait for it to finish.");
                return;
            }

            ChatHelper.Reply("simchat: expect 11 lines below. A 12th means suppression is broken.");

            Batch(14, 14, 0, 0);
            Batch(12, 14, 2, 0);
            Batch(9, 14, 3, 2);

            FillDeposit(1);
            Batch(2, 2, 0, 0);
            FillDeposit(2);
            Batch(2, 2, 0, 0);
            FillDeposit(3);
            Batch(2, 2, 0, 0);
            FillDeposit(130);
            Batch(2, 2, 0, 0);

            FillDeposit(1);
            Batch(0, 3, 0, 0);

            FillWithdraw(130);
            Batch(3, 3, 0, 0);

            MoveReport.Idle("No duplicates to deposit.");

            FillDeposit(2);
            MoveReport.Idle("No items to deposit.");

            FillDeposit(2);
            MoveReport.Completed((5, 5, 0, 0, true));
        }

        private static void Batch(int succeeded, int total, int refused, int blocked)
        {
            MoveReport.Completed((succeeded, total, refused, blocked, false));
        }

        private static void FillDeposit(int count) => Fill(OperationManager.LastDepositOverflow, count);

        private static void FillWithdraw(int count) => Fill(OperationManager.LastWithdrawOverflow, count);

        private static void Fill(List<(uint ItemId, uint Remaining)> list, int count)
        {
            list.Clear();
            for (var i = 0; i < count; i++)
            {
                var itemId = i < SampleItems.Length ? SampleItems[i] : 1000u + (uint)i;
                list.Add((itemId, (uint)(2000 - i * 10)));
            }
        }
    }
}
#endif
