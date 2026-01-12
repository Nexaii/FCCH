<div align="center">
  <h1>FCCH</h1>
  <h3>Free Company Chest Helper</h3>
  <p>
    Automated inventory auditing and management for Final Fantasy XIV.
    <br />
    <a href="#-features">Features</a> · <a href="#-installation">Installation</a> · <a href="#-usage">Usage</a>
  </p>
<p align="center">
    <a href="https://github.com/Nexaii/FCCH/blob/main/LICENSE"><img src="https://img.shields.io/github/license/Nexaii/FCCH?style=for-the-badge&label=License&logoColor=d9e0ee&colorA=363a4f&colorB=b7bdf8"/></a>
    <a href="https://ko-fi.com/nexai">
    <img src="https://img.shields.io/badge/Support%20on-Ko--fi-FF5E5B?style=for-the-badge&logo=kofi&logoColor=white" alt="Ko-fi" />
</a>
</p>


</div>

---

## 📖 About
**FCCH** Free Company Chest Helper is a plugin designed to streamline inventory management for Free Companies.

## ✨ Features
### Deposit Tools
*   **Deposit All**: Rapidly deposits all eligible items from your inventory to the chest.
*   **Deposit Duplicates**: "Smart Deposit" mode that scans the chest and only deposits items that already have a stack present, ensuring you never clutter tabs with new item types.

### Retrieval Tools
*   **Withdraw Project**: Add and withdraw specific Company Workshop projects, for the exact amount of materials required to complete the current phase or the entire project.
*   **Withdraw Singles**: Create custom "Singles" lists for frequent tasks (e.g. withdrawing ceruleum tanks, magitek repair kits, submarine components).
*   **Withdraw All**: A powerful tool to rapidly empty the current chest tab.

### Utilities
*   **Stack Logic**: Optional configuration to always leave 1 item per stack in the chest, preserving slot reservations for specific items.
*   **Clipboard Sharing**: Export and import your lists to easily share setups with others.

## 🚀 Installation
1.  **Prerequisites**: Ensure you have [FFXIVQuickLauncher](https://github.com/goatcorp/FFXIVQuickLauncher) installed with Dalamud enabled.
2.  **Add Repository**:
    *   Open Dalamud Settings: type `/xlsettings` in chat.
    *   Navigate to **Experimental** > **Custom Plugin Repositories**.
    *   Add the following URL:
        ```
        https://raw.githubusercontent.com/Nexaii/dalamud-plugins/main/repo.json
        ```
    *   Click **Save**.
3.  **Install**:
    *   Open the Plugin Installer: type `/xlplugins`.
    *   Search for **FCCH**.
    *   Click **Install**.

## � Usage
The primary interface is accessed via the `/fcch` command. You can also use chat commands for quick actions.

| Command | Description |
| :--- | :--- |
| `/fcch` | Open the configuration window. |
| `/fcch da` | **Deposit All**: Deposits all eligible items from your inventory. |
| `/fcch dd` | **Deposit Duplicates**: Deposits only items that match existing stacks in the chest. |
| `/fcch wa` | **Withdraw All**: Withdraws all items from the current chest tab (Use with caution). |
| `/fcch ws` | **Withdraw Singles**: Withdraws items defined in your "Singles" list. |
| `/fcch wp` | **Withdraw Project**: Withdraws materials required for the current Workshop Project. |
| `/fcch info` | Displays current rank permissions and available tabs. |


