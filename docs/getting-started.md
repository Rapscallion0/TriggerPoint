# Getting Started with TriggerPoint 🚀

This guide walks you through system requirements, installation, initial launch, default shortcuts, and orientation within TriggerPoint.

---

## System Requirements

Before installing TriggerPoint, verify that your workstation meets the following minimum requirements:

- **Operating System**: Windows 10 (64-bit, version 1809 or later) or Windows 11 (64-bit, all editions).
- **Runtime Environment**: .NET 9.0 Desktop Runtime (`Microsoft.WindowsDesktop.App` 9.0+). If not already installed, the installer or application will prompt to download it directly from Microsoft.
- **Hardware**: 
  - 1 GHz or faster x64 processor.
  - 150 MB available disk storage space.
  - 120 MB RAM during peak execution; ~25 MB typical idle background footprint.
  - Display with 100% to 250% DPI scaling support (TriggerPoint is Per-Monitor DPI v2 aware).

---

## Installation & Deployment

TriggerPoint is distributed as a single-file Inno Setup installer: `TriggerPointSetup.exe`.

### 1. Interactive Installation
1. Download the latest installer from the official [GitHub Releases](https://github.com/Rapscallion0/TriggerPoint/releases) page.
2. Double-click `TriggerPointSetup.exe`.
3. Choose your installation scope:

![Inno Setup Scope Selection](images/getting-started-installer-scope.png)
<!-- SCREENSHOT REQUIRED: Inno Setup installation wizard dialog presenting the dual installation scope radio buttons: "Install for me only (recommended, no admin privileges required)" and "Install for all users (requires administrator privileges)". -->

- **Per-User Installation (Recommended)**: Installs directly to `%LOCALAPPDATA%\Programs\TriggerPoint`. Does **not** require Windows Administrator (UAC) elevation. Ideal for corporate machines, non-admin accounts, and portable workflows.
- **All-Users Machine-Wide Installation**: Installs to `C:\Program Files\TriggerPoint`. Requires Administrator elevation; makes shortcuts available to all local users.

4. On the **Additional Tasks** screen:
   - **Create a desktop shortcut**: Places a desktop launcher for quick access.
   - **Start TriggerPoint when Windows starts**: Registers a registry Run key in `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` so TriggerPoint is armed immediately upon login.
5. Click **Install**, then click **Finish** to launch TriggerPoint immediately.

### 2. Silent & Automated Enterprise Deployment
For unattended setup or script deployment via Microsoft Intune, SCCM, or PowerShell, `TriggerPointSetup.exe` supports standard Inno Setup command-line switches:

```powershell
# Unattended silent per-user install
TriggerPointSetup.exe /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CURRENTUSER

# Unattended machine-wide install for all users (elevated prompt)
TriggerPointSetup.exe /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /ALLUSERS
```

---

## First Launch & System Tray Orientation

When TriggerPoint starts, it initializes in the background and places a dynamic reticle icon in your Windows Taskbar Notification Area (system tray):

![System Tray Notification Area and Context Menu](images/getting-started-tray-menu.png)
<!-- SCREENSHOT REQUIRED: Windows system tray notification area magnified to highlight the TriggerPoint reticle icon in the Armed status, alongside the opened right-click context menu showing: Open Settings, Check for Updates..., Command Palette, Shortcut Cheat Sheet, Snooze Global Hotkeys, Reload Configuration, and Exit. -->

### Tray Icon Status States
The reticle icon changes color to provide immediate ambient feedback:

| Reticle State | Visual Appearance | Meaning |
| :--- | :--- | :--- |
| **Armed** | Clean Accent / White Reticle | All shortcuts registered, daemon active, and listening for triggers. |
| **Snoozed** | Amber / Orange Reticle | Global hotkey listener is temporarily paused. Triggers will not fire. |
| **Conflict** | Red Alert Reticle | One or more hotkeys could not be registered due to collision with another app. |

### Tray Context Menu Actions
Right-click the tray icon at any time to access quick controls:
- **Open Settings**: Opens the primary configuration and workflow editor window.
- **Check for Updates...**: Queries GitHub Releases for new updates and opens the update dialog.
- **Command Palette (`Alt+Space`)**: Immediately summons the floating search palette.
- **Shortcut Cheat Sheet (`Ctrl+Shift+/`)**: Opens the floating hotkey HUD.
- **Snooze Global Hotkeys**: Toggles listener pause on/off. Useful when running full-screen games or applications with overlapping shortcut requirements.
- **Reload Configuration**: Re-reads configuration files from disk without restarting the application.
- **Exit TriggerPoint**: Gracefully shuts down background listeners and exits.

> [!TIP]
> You can also double-click the tray icon at any time to instantly open the Settings Window.

---

## Default Global Shortcuts

TriggerPoint is pre-configured with three ergonomic global shortcuts that work from any application in Windows:

| Shortcut Key | Action | Description |
| :--- | :--- | :--- |
| <kbd>Alt</kbd> + <kbd>Space</kbd> | **Spotlight Command Palette** | Opens a floating search bar to launch any action, execute folder items, or evaluate math expressions. |
| <kbd>Ctrl</kbd> + <kbd>Shift</kbd> + <kbd>/</kbd> | **Shortcut Cheat Sheet HUD** | Displays a non-intrusive floating HUD listing all your configured hotkeys and folder structures. |
| <kbd>Ctrl</kbd> + <kbd>Alt</kbd> + <kbd>T</kbd> | **Open Settings Window** | Brings the main TriggerPoint management and workflow window to the front. |

*Note: All three global shortcuts can be customized or disabled in the [Application Settings](configuration.md#global-hotkeys-configuration) dialog.*

---

## Initial Default Configuration

Upon first launch, TriggerPoint checks `%LOCALAPPDATA%\TriggerPoint\config\triggers.json`. If no file exists, it automatically seeds a starter collection of sample actions and folders so you can immediately explore functionality:

![Default Loaded Configuration](images/getting-started-initial-settings.png)
<!-- SCREENSHOT REQUIRED: Main Settings Window on first launch displaying the default seeded sample structure in the sidebar tree: "General Tools" folder (containing Notepad, Calculator, and Date Snippet actions) and "Developer Tools" folder (containing Command Prompt and Git Bash actions). -->

- **General Tools**:
  - `Notepad` (<kbd>Win</kbd> + <kbd>N</kbd>): Launches the Windows text editor.
  - `Date Snippet` (<kbd>Ctrl</kbd> + <kbd>Alt</kbd> + <kbd>D</kbd>): Expands into today's formatted date stamp: `{date:yyyy-MM-dd}`.
  - `Calculator` (<kbd>Win</kbd> + <kbd>C</kbd>): Opens the Windows calculator.
- **Developer Tools**:
  - `Command Prompt`: Launches `cmd.exe` in your user home directory.
  - `PowerShell`: Launches an interactive PowerShell console.

You can modify, reorganize, or delete these sample items at any time.

---

## Next Steps

Now that TriggerPoint is installed and running, dive into the core features:
- Review the [Interface Overview](interface-overview.md) to familiarize yourself with the main window and controls.
- Learn how to create your own shortcuts and rich text expansions in [Core Workflows](core-workflows.md).
- Explore advanced automation and conditional logic in [Workflows & Automation](workflows-and-scripts.md).
