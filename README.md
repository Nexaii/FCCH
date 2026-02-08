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
**FCCH** – Free Company Chest Helper. Automate deposits, withdrawals, and organization. Manage crystals, custom lists, and workshop projects.

## Features

### Deposit Tools
- **Deposit All** – Deposits all eligible items from your inventory.
- **Deposit Duplicates** – Only deposits items matching existing stacks in the chest.
- **Deposit Crystals** – Deposits shards/crystals/clusters with configurable keep amounts.

### Withdraw Tools
- **Withdraw All** – Empties the current chest tab.
- **Withdraw Custom List** – Create item lists for frequent tasks (ceruleum, repair kits, etc.).
- **Withdraw Workshop** – Pull exact materials for Company Workshop projects.
- **Withdraw Crystals** – Withdraws crystals to hit your configured threshold.

### Organizer
- **Move** – Transfer items between FC chest tabs with filters.
- **Sort** – Reorder items within a tab by category, ID, name, or quantity.

### Utilities
- **Ignore List** – Exclude items from Deposit/Withdraw operations. Supports presets.
- **Crystal Config** – Set global or per crystal keep amounts for automated crystal management.
- **Leave One per Stack** – Preserve slot reservations by keeping 1 item per stack.
- **Export/Import** – Share lists via clipboard.

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
| `/fcch da` | Deposit All |
| `/fcch dd` | Deposit Duplicates |
| `/fcch dc` | Deposit Crystals |
| `/fcch wa` | Withdraw All |
| `/fcch ws` | Withdraw Custom List |
| `/fcch wc` | Withdraw Crystals |
| `/fcch wp` | Withdraw Workshop |
| `/fcch gd <amount>` | Deposit Gil |
| `/fcch gw <amount>` | Withdraw Gil |
| `/fcch info` | Show FC rank, permissions, and tab access |

> **Amount Shorthand:** Gil commands accept raw numbers (`5000`), thousands (`5k`, `15k`), or millions (`1m`, `2.5m`).
