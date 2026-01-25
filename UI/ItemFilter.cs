using System.Collections.Generic;
using System.Linq;
using Dalamud.Bindings.ImGui;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;

namespace FCCH.UI
{
    public class ItemFilter
    {
        private readonly IDataManager _dataManager;
        
        private HashSet<uint> _craftableItemIds = new();
        
        private List<Item> _filteredItems = new();
        
        private string _searchQuery = "";
        private Item? _selectedItem;

        public ItemFilter(IDataManager dataManager)
        {
            _dataManager = dataManager;
            LoadData();
        }

        private void LoadData()
        {
            var recipeSheet = _dataManager.GetExcelSheet<Recipe>();
            var itemSheet = _dataManager.GetExcelSheet<Item>();

            if (recipeSheet == null || itemSheet == null) return;

            foreach (var recipe in recipeSheet)
            {
                _craftableItemIds.Add(recipe.ItemResult.RowId);
            }

            foreach (var item in itemSheet)
            {
                if (string.IsNullOrEmpty(item.Name.ToString())) continue;

                bool isCraftable = _craftableItemIds.Contains(item.RowId);
                bool isFcStorable = !item.IsUntradable; 
                
                if (isCraftable && isFcStorable)
                {
                    _filteredItems.Add(item);
                }
            }
            
            _filteredItems.Sort((a, b) => string.Compare(a.Name.ToString(), b.Name.ToString(), System.StringComparison.OrdinalIgnoreCase));
        }

        public Item? GetSelectedItem() => _selectedItem;
        public void SetSelectedItem(uint itemId)
        {
             _selectedItem = _filteredItems.FirstOrDefault(x => x.RowId == itemId);
        }

        public void Draw()
        {
            if (ImGui.BeginCombo("Select Item", _selectedItem.HasValue ? _selectedItem.Value.Name.ToString() : "Select..."))
            {
                ImGui.InputTextWithHint("##Search", "Search...", ref _searchQuery, 100);
                
                foreach (var item in _filteredItems)
                {
                    if (!string.IsNullOrEmpty(_searchQuery) && 
                        !item.Name.ToString().Contains(_searchQuery, System.StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    bool isSelected = _selectedItem.HasValue && _selectedItem.Value.RowId == item.RowId;
                    if (ImGui.Selectable(item.Name.ToString(), isSelected))
                    {
                        _selectedItem = item;
                    }
                    
                    if (isSelected)
                    {
                        ImGui.SetItemDefaultFocus();
                    }
                }
                ImGui.EndCombo();
            }
        }
    }
}
