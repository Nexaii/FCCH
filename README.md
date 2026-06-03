<div align="center">
  <h1>FCCH</h1>
  <h3>Free Company Chest Helper</h3>
  <p>
    Automate deposits, withdrawals, organization, crystals, workshop project pulls, and custom item lists.
    <br />
    <a href="#installation">Installation</a> · <a href="#features">Features</a> · <a href="#commands">Commands</a> · <a href="#ipc">IPC</a>
  </p>
<p align="center">
    <img src="https://img.shields.io/badge/dynamic/json?url=https://raw.githubusercontent.com/Nexaii/dalamud-plugins/main/repo.json&query=$[0].DownloadCount&label=Downloads&color=blue&style=for-the-badge" alt="Downloads" />
    <a href="https://ko-fi.com/nexai">
    <img src="https://img.shields.io/badge/Support%20on-Ko--fi-FF5E5B?style=for-the-badge&logo=kofi&logoColor=white" alt="Ko-fi" />
</a>
</p>


</div>

## Installation
1. **Prerequisites**: [FFXIVQuickLauncher](https://github.com/goatcorp/FFXIVQuickLauncher) with Dalamud enabled.
2. **Add Repository**: `/xlsettings` → Experimental → Custom Plugin Repositories:
   ```
   https://puni.sh/api/repository/nexai
   ```
3. **Install**: `/xlplugins` → Search **FCCH** → Install.

## Features

### Deposit & Withdraw
- **All / Per Tab** - Deposit or withdraw everything, or target a specific FC tab (1-5).
- **Duplicates** - Deposit only items matching existing chest stacks.
- **Custom List** - Saved deposit/withdraw item lists for repeat tasks (ceruleum, repair kits, etc.).
- **Workshop** - Pull exact materials for Company Workshop projects. Refreshes when the project list changes.
- **Crystals** - Deposit/withdraw shards/crystals/clusters with configurable keep amounts.
- **Gil** - Deposit/withdraw with shorthand amounts (`5k`, `1m`, `all`).

### Organizer
- **Move** - Transfer items between FC chest tabs with filters.
- **Sort** - Reorder items within a tab by category, ID, name, or quantity.

### Toolbar
- **Dropdowns** - Deposit/Withdraw split-buttons expose tabs 1-5 inline.
- **Crystal Button** - Left click deposits crystals, right click withdraws.

### Utilities
- **Ignore List** - Exclude items from operations. Supports presets.
- **Crystal Config** - Global or per crystal keep amounts.
- **Leave One per Stack** - Reserve slots so a stack always remains in your inventory.
- **Export/Import** - Share lists via clipboard.
- **Item Context Menu** - Right-click any inventory item to add or remove from Custom or Ignore list. Toggleable.
- **Compact Item Names** - Shortens materia, grade, and level item names in lists. Original names still used for sorting, search, IPC, and chest operations.

## Commands

| Command | Description |
| :--- | :--- |
| `/fcch` | Open settings window |
| `/fcch da` / `da1`..`da5` | Deposit All (or into FC tab N) |
| `/fcch dd` | Deposit Duplicates |
| `/fcch ds` | Deposit Custom List |
| `/fcch dc` | Deposit Crystals |
| `/fcch wa` / `wa1`..`wa5` | Withdraw All (or from FC tab N) |
| `/fcch ws` | Withdraw Custom List |
| `/fcch wc` | Withdraw Crystals |
| `/fcch wp` | Withdraw Workshop |
| `/fcch gd <amount>` | Deposit Gil |
| `/fcch gw <amount>` | Withdraw Gil |
| `/fcch info` | Show FC rank, permissions, and tab access |

> **Amount Shorthand:** Gil commands accept raw numbers (`5000`), thousands (`5k`, `15k`), or millions (`1m`, `2.5m`).

## IPC

Other plugins can drive FCCH via Dalamud IPC. Mutation calls return `bool`. `true` means FCCH accepted the request, `false` means refused (busy, invalid args, or nothing to do).

### Readiness

| IPC | Signature | Description |
| :--- | :--- | :--- |
| `FCCH.IsAvailable` | `bool()` | FCCH plugin is loaded |
| `FCCH.IsBusy` | `bool()` | An operation is running |

### Counts

| IPC | Signature | Description |
| :--- | :--- | :--- |
| `FCCH.GetChestItemCount` | `long(uint itemId)` | Items in the FC chest (cached) |
| `FCCH.GetWithdrawableItemCount` | `long(uint itemId)` | Withdrawable items in the FC chest (cached) |
| `FCCH.GetPlayerInventoryCount` | `long(uint itemId)` | Items in player inventory |

### Deposit

| IPC | Signature | Description |
| :--- | :--- | :--- |
| `FCCH.DepositAll` | `bool()` | Deposit all allowed items |
| `FCCH.DepositCustom` | `bool()` | Deposit Custom List items |
| `FCCH.DepositDuplicates` | `bool()` | Deposit duplicates of items in the chest |
| `FCCH.DepositGil` | `bool(string amount)` | Deposit gil (5000, 5k, 1m, all) |
| `FCCH.DepositItem` | `bool(uint itemId, int quantity)` | Deposit one item up to quantity |
| `FCCH.DepositItems` | `bool(Dictionary<uint,int> items)` | Deposit requested item quantities |

### Withdraw

| IPC | Signature | Description |
| :--- | :--- | :--- |
| `FCCH.WithdrawAll` | `bool()` | Withdraw all allowed items |
| `FCCH.WithdrawCustom` | `bool()` | Withdraw Custom List items |
| `FCCH.WithdrawGil` | `bool(string amount)` | Withdraw gil (5000, 5k, 1m, all) |
| `FCCH.WithdrawItem` | `bool(uint itemId, int quantity)` | Withdraw one item up to quantity |
| `FCCH.WithdrawItems` | `bool(Dictionary<uint,int> items)` | Withdraw exact quantities per item |
| `FCCH.WithdrawMissingItems` | `bool(Dictionary<uint,int> requiredTotals)` | Withdraw only the missing amount to reach each total |
| `FCCH.WithdrawWorkshop` | `bool()` | Withdraw Workshop materials |

### Control

| IPC | Signature | Description |
| :--- | :--- | :--- |
| `FCCH.Stop` | `bool()` | Stop the current operation |

Mutations need the FC chest open. FCCH opens it automatically if closed.

### Diagnostics

The General settings tab has a collapsed Diagnostics section. Developer commands:

| Command | Description |
| :--- | :--- |
| `/fcch debug` | Toggle debug logging |
| `/fcch gildebug` | Trace gil callbacks |
| `/fcch accessprobe` | Dump live Company Chest addon permission state |
| `/fcch fcperms [row]` | Dump raw FC rank permission bytes to plugin log |
