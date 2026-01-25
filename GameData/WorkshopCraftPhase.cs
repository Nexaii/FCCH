using System.Collections.Generic;

namespace FCCH.GameData;

public sealed class WorkshopCraftPhase
{
    public required string Name { get; init; }
    public required IReadOnlyList<WorkshopCraftItem> Items { get; init; }
}
