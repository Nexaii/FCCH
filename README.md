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
- **Compact Item Names** - Shortens supported materia, grade, and level item names in Custom, Ignore, and Organizer lists while keeping original names for sorting, searching, IPC, and chest operations.
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

IPC calls are available for other plugins. Mutation calls return `true` when FCCH accepts and queues the request, `false` when the command gate refuses or arguments are invalid. The mutation return value is authoritative; `CanAcceptCommand()` and `GetBlockReason()` are advisory.

### Contract version

`FCCH.GetVersion()` returns the IPC contract version as an `int`. Current value: **`3`**. Bumped whenever any IPC signature, return contract, or documented behavior changes. Callers should branch on this value when integrating against multiple FCCH releases.

### Readiness

| IPC | Signature | Description |
| :--- | :--- | :--- |
| `FCCH.IsAvailable` | `bool IsAvailable()` | FCCH IPC is loaded |
| `FCCH.IsBusy` | `bool IsBusy()` | FCCH is indexing or running an operation |
| `FCCH.CanAcceptCommand` | `bool CanAcceptCommand()` | Advisory: the command gate is currently open |
| `FCCH.GetBlockReason` | `string GetBlockReason()` | `""` when ready, otherwise a documented block-reason token (see enumeration below) |
| `FCCH.GetVersion` | `int GetVersion()` | IPC contract version |

#### Block-reason tokens

`GetBlockReason()` is the canonical source. Returned value is never `null`. `GetBlockReason()` may return a non-empty token while `CanAcceptCommand()` returns `true`, specifically the `chest-closed` advisory state, where FCCH will accept a mutation and auto-open the chest. All other tokens correspond to `CanAcceptCommand() == false`.

| Token | Meaning |
| :--- | :--- |
| `""` (empty string) | Ready. Chest open, no operation in flight, plugin available. |
| `"busy"` | An operation is in flight (indexing, moving, organizer job, or pending chest-open). `CanAcceptCommand()` is `false`. |
| `"chest-closed"` | Plugin available and not busy, but the FC chest addon is not visible. FCCH will auto-open the chest on the next mutation IPC. `CanAcceptCommand()` is `true`. |
| `"unavailable"` | Not logged in, no Free Company, or FC chest permissions denied. FCCH cannot operate. `CanAcceptCommand()` is `false`. |

If a new token is added to this list in a future release, `GetVersion()` will be bumped so other plugins can detect the change.

### Counts

| IPC | Signature | Description |
| :--- | :--- | :--- |
| `FCCH.GetChestItemCount` | `long GetChestItemCount(uint itemId)` | Count cached matching items in the FC chest |
| `FCCH.GetPlayerInventoryCount` | `long GetPlayerInventoryCount(uint itemId)` | Count matching items in player inventory |
| `FCCH.GetWithdrawableItemCount` | `long GetWithdrawableItemCount(uint itemId)` | Count cached matching items FCCH can withdraw after permissions, ignore list, and leave-one rules |

### Deposit

| IPC | Signature | Description |
| :--- | :--- | :--- |
| `FCCH.DepositAll` | `bool DepositAll()` | Deposit all allowed items |
| `FCCH.DepositCustom` | `bool DepositCustom()` | Deposit Custom List items |
| `FCCH.DepositDuplicates` | `bool DepositDuplicates()` | Deposit duplicate items |
| `FCCH.DepositGil` | `bool DepositGil(string amount)` | Deposit gil (`5000`, `5k`, `1m`, `all`) |
| `FCCH.DepositItem` | `bool DepositItem(uint itemId, int quantity)` | Deposit up to `quantity` of one item |
| `FCCH.DepositItems` | `bool DepositItems(Dictionary<uint, int> items)` | Deposit requested item quantities |

### Withdraw

| IPC | Signature | Description |
| :--- | :--- | :--- |
| `FCCH.WithdrawAll` | `bool WithdrawAll()` | Withdraw all allowed items |
| `FCCH.WithdrawCustom` | `bool WithdrawCustom()` | Withdraw Custom List items |
| `FCCH.WithdrawGil` | `bool WithdrawGil(string amount)` | Withdraw gil (`5000`, `5k`, `1m`, `all`) |
| `FCCH.WithdrawItem` | `bool WithdrawItem(uint itemId, int quantity)` | Withdraw up to `quantity` of one item |
| `FCCH.WithdrawItems` | `bool WithdrawItems(Dictionary<uint, int> items)` | Withdraw exactly `quantity` of each item |
| `FCCH.WithdrawMissingItems` | `bool WithdrawMissingItems(Dictionary<uint, int> requiredTotals)` | Withdraw only the player's missing amount to reach each required total (top-up) |
| `FCCH.WithdrawWorkshop` | `bool WithdrawWorkshop()` | Withdraw Workshop materials |

### Control

| IPC | Signature | Description |
| :--- | :--- | :--- |
| `FCCH.Stop` | `bool Stop()` | Stop the active FCCH operation |

### Withdraw quantity semantics

- `WithdrawItem(itemId, quantity)` and `WithdrawItems(items)` treat quantities as **exact requested withdrawal amounts**. Pulling 10 of an item moves 10 from the chest regardless of what the player already holds.
- `WithdrawMissingItems(requiredTotals)` treats quantities as **required totals**. For each entry, FCCH subtracts the player's current inventory count and pulls only the missing amount. Already met totals are skipped.
- All item transfer calls respect FC tab permissions, ignore settings, leave-one-per-stack, and available inventory or chest space.

### Example: readiness pre-check

```csharp
var canAccept = plugin.GetIpcSubscriber<bool>("FCCH.CanAcceptCommand").InvokeFunc();
if (!canAccept)
{
    var reason = plugin.GetIpcSubscriber<string>("FCCH.GetBlockReason").InvokeFunc();
    // reason is "" when ready, "busy" when an operation is in flight.
}

var accepted = plugin.GetIpcSubscriber<bool>("FCCH.DepositAll").InvokeFunc();
// accepted is authoritative. CanAcceptCommand() may return true and the mutation still refuse
// if FCCH's state changed between the two calls.
```

### Example: top-up withdraw

```csharp
var required = new Dictionary<uint, int>
{
    [5106] = 999,
    [5107] = 500,
};
var queued = plugin
    .GetIpcSubscriber<Dictionary<uint, int>, bool>("FCCH.WithdrawMissingItems")
    .InvokeFunc(required);
```

### Diagnostics

The General settings tab includes a collapsed **Diagnostics** section for troubleshooting. Internal commands remain available there for maintainers:

| Command | Description |
| :--- | :--- |
| `/fcch debug` | Toggle debug logging |
| `/fcch gildebug` | Trace gil callbacks |
| `/fcch accessprobe` | Dump live Company Chest addon permission state |
| `/fcch fcperms [row]` | Dump raw FC rank permission bytes to plugin log |
| `/fcch ipctest` | Exercise the FCCH IPC surface and report pass/fail to `/xllog` |
