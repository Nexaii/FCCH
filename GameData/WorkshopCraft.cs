using System.Collections.Generic;

namespace FCCH.GameData;

public enum WorkshopCraftCategory
{
    AetherialWheels = 0,
    AirshipsSubmersibles = 1,
    Housing = 2,
}

public sealed class WorkshopCraft
{
    public required uint WorkshopItemId { get; init; }
    public required uint ResultItem { get; init; }
    public required string Name { get; init; }
    public required ushort IconId { get; init; }
    public required WorkshopCraftCategory Category { get; init; }
    public required uint Type { get; init; }
    public required IReadOnlyList<WorkshopCraftPhase> Phases { get; init; }
}

public sealed class WorkshopCraftPhase
{
    public required string Name { get; init; }
    public required IReadOnlyList<WorkshopCraftItem> Items { get; init; }
}

public sealed class WorkshopCraftItem
{
    public required uint ItemId { get; init; }
    public required string Name { get; init; }
    public required ushort IconId { get; init; }
    public required int SetQuantity { get; init; }
    public required int SetsRequired { get; init; }
    public int TotalQuantity => SetQuantity * SetsRequired;
}
