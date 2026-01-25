using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;

namespace FCCH.GameData;

public sealed class WorkshopCache
{
        public WorkshopCache(IDataManager dataManager, IPluginLog pluginLog)
        {
            var crafts = new List<WorkshopCraft>();

            var sheetSequence = dataManager.GetExcelSheet<CompanyCraftSequence>();
            var sheetPart = dataManager.GetExcelSheet<CompanyCraftPart>();
            var sheetProcess = dataManager.GetExcelSheet<CompanyCraftProcess>();
            var sheetSupply = dataManager.GetExcelSheet<CompanyCraftSupplyItem>();
            var sheetItem = dataManager.GetExcelSheet<Item>();

            if (sheetSequence == null || sheetPart == null || sheetProcess == null || sheetSupply == null || sheetItem == null)
            {
                pluginLog.Error("Failed to load one or more Excel sheets.");
                return;
            }

            foreach (var seq in sheetSequence)
            {
                if (seq.CompanyCraftDraftCategory == 0) continue;

                var resultItem = sheetItem.GetRow(seq.ResultItem);
                if (resultItem.RowId == 0) continue;

                var phases = new List<WorkshopCraftPhase>();
                
                for (int i = 0; i < 8; i++)
                {
                    var partId = seq.CompanyCraftPart[i];
                    if (partId == 0 || partId == 65535) continue;

                    CompanyCraftPart part;
                    try
                    {
                        part = sheetPart.GetRow(partId);
                    }
                    catch (Exception ex)
                    {
                        pluginLog.Warning($"Failed to get CompanyCraftPart row {partId}: {ex.Message}");
                        continue;
                    }
                    if (part.RowId == 0) continue;

                    var phaseItems = new List<WorkshopCraftItem>();

                    for (int j = 0; j < 3; j++)
                    {
                        var processId = part.CompanyCraftProcess[j];
                        if (processId == 0 || processId == 65535) continue;

                        CompanyCraftProcess process;
                        try
                        {
                            process = sheetProcess.GetRow(processId);
                        }
                        catch (Exception ex)
                        {
                            pluginLog.Warning($"Failed to get CompanyCraftProcess row {processId}: {ex.Message}");
                            continue;
                        }
                        if (process.RowId == 0) continue;

                        for (int k = 0; k < 12; k++)
                        {
                            var supplyId = process.SupplyItem[k];
                            if (supplyId == 0 || supplyId == 65535) continue;

                            CompanyCraftSupplyItem supply;
                            try
                            {
                                supply = sheetSupply.GetRow(supplyId);
                            }
                            catch (Exception ex)
                            {
                                pluginLog.Warning($"Failed to get CompanyCraftSupplyItem row {supplyId}: {ex.Message}");
                                continue;
                            }
                            if (supply.RowId == 0) continue;

                            Item item;
                            try
                            {
                                item = sheetItem.GetRow(supply.Item);
                            }
                            catch (Exception ex)
                            {
                                pluginLog.Warning($"Failed to get Item row {supply.Item}: {ex.Message}");
                                continue;
                            }
                            if (item.RowId == 0) continue;

                            phaseItems.Add(new WorkshopCraftItem
                            {
                                ItemId = supply.Item,
                                Name = item.Name.ToString(),
                                IconId = item.Icon,
                                SetQuantity = process.SetQuantity[k],
                                SetsRequired = process.SetsRequired[k]
                            });
                        }
                    }

                    if (phaseItems.Count > 0)
                    {
                        phases.Add(new WorkshopCraftPhase
                        {
                            Name = $"Phase {i + 1}",
                            Items = phaseItems.AsReadOnly()
                        });
                    }
                }

                if (phases.Count > 0)
                {
                    crafts.Add(new WorkshopCraft
                    {
                        WorkshopItemId = seq.RowId,
                        ResultItem = seq.ResultItem,
                        Name = resultItem.Name.ToString(),
                        IconId = resultItem.Icon,
                        Category = (WorkshopCraftCategory)seq.Category,
                        Type = seq.CompanyCraftType,
                        Phases = phases.AsReadOnly()
                    });
                }
            }

            Crafts = crafts.AsReadOnly();
            pluginLog.Info($"[FCCH] Loaded {Crafts.Count} workshop crafts.");
        }

    public IReadOnlyList<WorkshopCraft> Crafts { get; private set; } = new List<WorkshopCraft>();
}
