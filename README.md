<div align="center">
  <h1>FCCH</h1>
  <h3>Free Company Chest Helper</h3>
  <p>
    Automated Free Company Chest management for Final Fantasy XIV.
    <br />
    <a href="#features">Features</a> · <a href="#installation">Installation</a> · <a href="#commands">Commands</a>
  </p>
<p align="center">
    <a href="https://ko-fi.com/nexai">
    <img src="https://img.shields.io/badge/Support%20on-Ko--fi-FF5E5B?style=for-the-badge&logo=kofi&logoColor=white" alt="Ko-fi" />
</a>
</p>


</div>

**Plugin Repository**
```
https://raw.githubusercontent.com/Nexaii/dalamud-plugins/main/repo.json
```

---

## About
**FCCH** - Free Company Chest Helper. Automate deposits, withdrawals, and organization. Manage crystals, custom lists, and workshop projects.

## Features

### Deposit & Withdraw
- **All / Per-Tab** - Deposit or withdraw everything, or target a specific FC tab (1-5).
- **Duplicates** - Deposit only items matching existing chest stacks.
- **Custom List** - Saved deposit/withdraw item lists for repeat tasks (ceruleum, repair kits, etc.).
- **Workshop** - Pull exact materials for Company Workshop projects; auto-refreshes on shopping-list change.
- **Crystals** - Deposit/withdraw shards/crystals/clusters against configurable keep amounts.
- **Gil** - Deposit/withdraw with shorthand amounts (`5k`, `1m`, `all`).

### Organizer
- **Move** - Transfer items between FC chest tabs with filters.
- **Sort** - Reorder items within a tab by category, ID, name, or quantity.

### Utilities
- **Ignore List** - Exclude items from operations. Supports presets.
- **Crystal Config** - Global or per-crystal keep amounts.
- **Leave One per Stack** - Reserve slots; auto-corrected on full-stack moves so the rule never breaks.
- **Export/Import** - Share lists via clipboard.

### Quality of Life
- **Refusal Handling** - Detects "Unable to store/retrieve" log messages, blocks failing tabs after 5 refusals, prints a per-batch summary.
- **Pending-Command Timeout** - Cancels queued operations if the chest does not open within 15 seconds.
- **Persisted Windows** - Settings and toolbar remember position; lock, snap-to-chest, and viewport clamp.
- **Toolbar Dropdowns** - Deposit/Withdraw split-buttons expose tabs 1-5 inline.
- **Diagnostics** - Collapsed troubleshooting area with debug mode, verbose logging, file logging, and internal diagnostic command references.

## Installation
1. **Prerequisites**: [FFXIVQuickLauncher](https://github.com/goatcorp/FFXIVQuickLauncher) with Dalamud enabled.
2. **Add Repository**: `/xlsettings` → Experimental → Custom Plugin Repositories:
   ```
   https://raw.githubusercontent.com/Nexaii/dalamud-plugins/main/repo.json
   ```
3. **Install**: `/xlplugins` → Search **FCCH** → Install.

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

IPC calls are available for other plugins. Action calls return `true` when FCCH accepts the request.

| IPC | Description |
| :--- | :--- |
| `FCCH.IsAvailable()` | FCCH IPC is loaded |
| `FCCH.IsBusy()` | FCCH is indexing or running an operation |
| `FCCH.DepositAll()` | Deposit all allowed items |
| `FCCH.DepositDuplicates()` | Deposit duplicate items |
| `FCCH.DepositCustom()` | Deposit Custom List items |
| `FCCH.WithdrawAll()` | Withdraw all allowed items |
| `FCCH.WithdrawCustom()` | Withdraw Custom List items |
| `FCCH.WithdrawWorkshop()` | Withdraw Workshop materials |
| `FCCH.DepositGil(string amount)` | Deposit gil (`5000`, `5k`, `1m`, `all`) |
| `FCCH.WithdrawGil(string amount)` | Withdraw gil (`5000`, `5k`, `1m`, `all`) |
| `FCCH.Stop()` | Stop the active FCCH operation |

### Diagnostics

The General settings tab includes a collapsed **Diagnostics** section for troubleshooting. Internal commands remain available there for maintainers:

| Command | Description |
| :--- | :--- |
| `/fcch debug` | Toggle debug logging |
| `/fcch gildebug` | Trace gil callbacks |
| `/fcch accessprobe` | Dump live Company Chest addon permission state |
| `/fcch fcperms [row]` | Dump raw FC rank permission bytes to plugin log |
