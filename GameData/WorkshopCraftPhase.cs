using System.Collections.Generic;

namespace FC_Chest_Helper.GameData;

public sealed class WorkshopCraftPhase
{
    public required string Name { get; init; }
    public required IReadOnlyList<WorkshopCraftItem> Items { get; init; }
}
