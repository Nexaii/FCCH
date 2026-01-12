using System.Collections.Generic;
using System.Linq;
using Dalamud.Bindings.ImGui;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;

namespace FC_Chest_Helper
{
    public class ItemFilter
    {
        private readonly IDataManager _dataManager;
        
        // Cache of Item IDs that have at least one recipe associated with them
        private HashSet<uint> _craftableItemIds = new();
        
        // The final filtered list of items to display in the dropdown
        private List<Item> _filteredItems = new();
        
        // For the UI search box
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

            // 1. Build the "Is Craftable" Cache
            // We look at every recipe and grab the "ItemResult" ID.
            // This effectively gives us a list of every item ID that can be crafted.
            foreach (var recipe in recipeSheet)
            {
                _craftableItemIds.Add(recipe.ItemResult.RowId);
            }

            // 2. Build the Filtered List
            // We iterate over all Items and keep only those that match our 2 criteria:
            // A) It is in our craftable cache
            // B) It is NOT Untradable (IsUntradable == false means it can go in FC chest)
            foreach (var item in itemSheet)
            {
                // Skip empty/invalid items
                if (string.IsNullOrEmpty(item.Name.ToString())) continue;

                bool isCraftable = _craftableItemIds.Contains(item.RowId);
                bool isFcStorable = !item.IsUntradable; // If it's tradeable, it's FC storable
                
                if (isCraftable && isFcStorable)
                {
                    _filteredItems.Add(item);
                }
            }
            
            // Sort by name for easier searching
            _filteredItems.Sort((a, b) => string.Compare(a.Name.ToString(), b.Name.ToString(), System.StringComparison.OrdinalIgnoreCase));
        }

        public Item? GetSelectedItem() => _selectedItem;
        public void SetSelectedItem(uint itemId)
        {
             _selectedItem = _filteredItems.FirstOrDefault(x => x.RowId == itemId);
        }

        public void Draw()
        {
            // A simple search box to filter the dropdown further (optional but recommended for long lists)
            // ImGui.InputText("Search##ItemSearch", ref _searchQuery, 100); // Moved into combo for cleaner UI if possible, or keep separate

            if (ImGui.BeginCombo("Select Item", _selectedItem.HasValue ? _selectedItem.Value.Name.ToString() : "Select..."))
            {
                // Search box inside the combo
                ImGui.InputTextWithHint("##Search", "Search...", ref _searchQuery, 100);
                
                foreach (var item in _filteredItems)
                {
                    // Simple text filter for the UI rendering
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
