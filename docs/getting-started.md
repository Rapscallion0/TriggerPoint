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
   - **Windows Explorer Integration**: Adds right-click context menu options to launch files or folders directly with TriggerPoint.

5. On the **Initial Preferences** screen:
   - **Appearance Theme**: Select **System Default** (recommended), **Dark Theme**, or **Light Theme**.
   - **Starter Content**: Check or uncheck **Install Starter Pack & Examples** depending on whether you want sample shortcuts, snippets, and workflows or a completely blank canvas.

6. Click **Install**, then click **Finish** to launch TriggerPoint immediately.

### 2. Silent & Automated Enterprise Deployment
For unattended setup or script deployment via Microsoft Intune, SCCM, or PowerShell, `TriggerPointSetup.exe` supports standard Inno Setup command-line switches:

```powershell
# Unattended silent per-user install
TriggerPointSetup.exe /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CURRENTUSER

# Unattended machine-wide install for all users (elevated prompt)
TriggerPointSetup.exe /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /ALLUSERS
```

### 3. Portable Mode Deployment (Zero-Pollution & USB Drives)
For users who prefer running TriggerPoint without installation, or directly from USB flash drives, network shares, and cloud-synced folders:
1. Download `TriggerPoint-v{version}-Portable-win-x64.zip` from [GitHub Releases](https://github.com/Rapscallion0/TriggerPoint/releases).
2. Extract the archive into any folder or external drive (e.g., `E:\TriggerPoint\`).
3. Launch `TriggerPoint.exe`.
4. **First-Run Welcome & Setup**:
   - On first launch, TriggerPoint presents a streamlined Welcome dialog asking you to choose your **Appearance Theme** (System, Dark, or Light with real-time live preview) and whether to **Install Starter Pack & Examples**.
   - Clicking **Get Started** writes your preferences directly into the local `data/` folder.
5. TriggerPoint detects the local `data\` folder (or `portable.dat` sentinel) and immediately activates **Portable Mode**:
   - **Self-Contained Storage**: All settings (`appsettings.json`), actions (`triggerpoint.json`), backups, logs, and cryptographic vault keys are stored entirely within the local `data\` directory.
   - **Zero Host Pollution**: Nothing is written to `%APPDATA%` or host system folders.
   - **Machine-Independent Security**: A portable AES-256-GCM encrypted vault (`vault.key`) protects stored credentials across different PCs without relying on machine-tied DPAPI keys.
   - **Automated Host Cleanup**: Temporary Windows Explorer context menu registrations are automatically scrubbed on exit or drive removal. A standalone `cleanup-host-integration.bat` utility is also included for emergency manual cleanup.
   - **Seamless In-Place Updates**: The built-in updater downloads portable packages, stages files, and performs an atomic background binary swap without ever touching your `data\` directory.

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
- **Command Palette (`Ctrl+Shift+Space`)**: Immediately summons the floating search palette.
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
| <kbd>Ctrl</kbd> + <kbd>Shift</kbd> + <kbd>Space</kbd> | **Spotlight Command Palette** | Opens a floating search bar to launch any action, execute folder items, or evaluate math expressions. |
| <kbd>Ctrl</kbd> + <kbd>Shift</kbd> + <kbd>/</kbd> | **Shortcut Cheat Sheet HUD** | Displays a non-intrusive floating HUD listing all your configured hotkeys and folder structures. |
| <kbd>Ctrl</kbd> + <kbd>Alt</kbd> + <kbd>T</kbd> | **Open Settings Window** | Brings the main TriggerPoint management and workflow window to the front. |

*Note: All three global shortcuts can be customized or disabled in the [Application Settings](configuration.md#global-hotkeys-configuration) dialog.*

---

## Initial Default Configuration

Upon first launch, TriggerPoint checks `%LOCALAPPDATA%\TriggerPoint\config\triggers.json`. If no file exists, it automatically seeds a starter collection of sample actions and folders organized under a single top-level folder so you can immediately explore functionality or easily delete the entire pack in one click:

![Default Loaded Configuration](images/getting-started-initial-settings.png)
<!-- SCREENSHOT REQUIRED: Main Settings Window on first launch displaying the default seeded sample structure in the sidebar tree: "Starter Pack & Examples" folder containing subfolders for Quick Launcher (Cursor Menu), Text Snippets & Templates, Automated Workflows, and Keystroke Automation (Macro). -->

- **Starter Pack & Examples**:
  - **Quick Launcher (Cursor Menu)** (<kbd>Ctrl</kbd> + <kbd>Alt</kbd> + <kbd>Space</kbd>): Floating radial menu at your mouse cursor containing Windows Calculator (`1`), Notepad Scratchpad (`2`), and Search Google (`3`).
  - **Text Snippets & Templates**: Rich snippets demonstrating dynamic tokens like Current Timestamp (<kbd>Ctrl</kbd> + <kbd>Alt</kbd> + <kbd>D</kbd>), Git Conventional Commit with interactive prompts, Meeting Notes Template, and Markdown Code Block clipboard wrapper.
  - **Automated Workflows**: Multi-step sequences like Morning Workspace Setup (launching websites, delayed Notepad launch, and system notification) and Open Temp Directory.
  - **Keystroke Automation (Macro)**: Sequential keystroke playback for duplicating lines down.

You can modify, reorganize, or delete these sample items at any time. If you prefer a blank canvas, simply delete the top-level `Starter Pack & Examples` folder.

---

## Next Steps

Now that TriggerPoint is installed and running, dive into the core features:
- Review the [Interface Overview](interface-overview.md) to familiarize yourself with the main window and controls.
- Learn how to create your own shortcuts and rich text expansions in [Core Workflows](core-workflows.md).
- Explore advanced automation and conditional logic in [Workflows & Automation](workflows-and-scripts.md).
