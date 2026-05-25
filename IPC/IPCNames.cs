namespace FCCH.IPC;

public static class IPCNames
{
    public const string Prefix = "FCCH";

    public const string IsAvailable             = Prefix + ".IsAvailable";
    public const string IsBusy                  = Prefix + ".IsBusy";

    public const string GetChestItemCount       = Prefix + ".GetChestItemCount";
    public const string GetWithdrawableItemCount = Prefix + ".GetWithdrawableItemCount";
    public const string GetPlayerInventoryCount = Prefix + ".GetPlayerInventoryCount";

    public const string DepositAll              = Prefix + ".DepositAll";
    public const string DepositCustom           = Prefix + ".DepositCustom";
    public const string DepositDuplicates       = Prefix + ".DepositDuplicates";
    public const string DepositGil              = Prefix + ".DepositGil";
    public const string DepositItem             = Prefix + ".DepositItem";
    public const string DepositItems            = Prefix + ".DepositItems";

    public const string WithdrawAll             = Prefix + ".WithdrawAll";
    public const string WithdrawCustom          = Prefix + ".WithdrawCustom";
    public const string WithdrawGil             = Prefix + ".WithdrawGil";
    public const string WithdrawItem            = Prefix + ".WithdrawItem";
    public const string WithdrawItems           = Prefix + ".WithdrawItems";
    public const string WithdrawMissingItems    = Prefix + ".WithdrawMissingItems";
    public const string WithdrawWorkshop        = Prefix + ".WithdrawWorkshop";

    public const string Stop                    = Prefix + ".Stop";
}
