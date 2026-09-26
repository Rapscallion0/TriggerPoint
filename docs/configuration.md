# TriggerPoint Application Configuration ⚙

This section documents all system preferences, theme customizations, global shortcut registrations, automatic update policies, Serilog logging parameters, and storage resilience settings in TriggerPoint.

Open the Application Settings window at any time by clicking the **Gear icon (`⚙`)** on the main window toolbar, or by selecting **Application Settings** from the system tray menu.

![Application Settings General Tab](images/config-application-settings-general.png)
<!-- SCREENSHOT REQUIRED: Application Settings modal dialog open to the "General" tab. Shows the Theme preference dropdown set to "Dark Mode", UI Animations switch enabled, Windows 11 Backdrop Effects switch enabled, Success Toast Notifications enabled with Monitor Placement set to "Primary Monitor", and Window Startup Placement set to "Remember Last Position". -->

---

## 1. General Preferences & Appearance

Configure visual presentation and desktop behavior:

### Theme Preference
TriggerPoint features a built-in semantic design system that adapts instantly:
- **System Default**: Automatically tracks your Windows personalization setting (Dark or Light mode).
- **Dark Mode**: High-contrast, sleek slate theme optimized for low-light environments.
- **Light Mode**: Clean, crisp daytime theme with subtle borders and shadows.
- *Note*: Theme transitions apply immediately across all open windows, HUD overlays, dialogs, and code editors without requiring a restart.

### Visual Effects & Animations
- **Enable UI Animations**: Enables smooth keyframe scale pulses, entrance fades, and slide transitions across dialogs, floating HUDs, and token insertion pills. Disable on low-power devices or remote desktop sessions for instant responsiveness.
- **Enable Backdrop Effects**: Controls whether Windows 11 DWM backdrop materials are utilized on supported dialogs.

### Toast Notifications & Display Targeting
TriggerPoint displays subtle, non-intrusive floating toasts in the corner of your screen when actions succeed or warnings occur:
- **Show Success Toasts**: Toggle completion notifications on or off.
- **Toast Placement**: Directs toasts to a specific screen:
  - `Primary Monitor` (default bottom-right corner).
  - `Display 1`, `Display 2`, etc.
  - `Follow Mouse Cursor` (displays on the monitor currently containing your active mouse cursor).

### Window Placement & Startup State
- **Window Startup Placement**: Choose between `Remember Last Position & Size` or `Center on Screen`.

---

## 2. Global Hotkeys Configuration

Customize the core keyboard shortcuts that control TriggerPoint system-wide:

![Global Hotkey Configuration](images/config-hotkey-recorder-modal.png)
<!-- SCREENSHOT REQUIRED: Application Settings Hotkeys section. Three interactive hotkey recorder controls are visible: (1) Open Settings Window set to "Ctrl + Alt + T", (2) Command Palette set to "Ctrl + Shift + Space", and (3) Shortcut Cheat Sheet set to "Ctrl + Shift + /". The Command Palette recorder is in active focus with modifier chips highlighted. -->

| Setting | Default Shortcut | Purpose |
| :--- | :--- | :--- |
| **Open Settings Window** | <kbd>Ctrl</kbd> + <kbd>Alt</kbd> + <kbd>T</kbd> | Brings the main management window to the foreground. |
| **Spotlight Command Palette** | <kbd>Ctrl</kbd> + <kbd>Shift</kbd> + <kbd>Space</kbd> | Summons the floating search and calculator HUD. |
| **Shortcut Cheat Sheet HUD** | <kbd>Ctrl</kbd> + <kbd>Shift</kbd> + <kbd>/</kbd> | Opens the floating full-shortcut reference HUD. |

### Changing a Shortcut
1. Click into the hotkey recorder field.
2. Press the desired combination on your keyboard (e.g., <kbd>Win</kbd> + <kbd>Space</kbd>).
3. The new key combination is verified against Windows system reservations and internal TriggerPoint actions immediately. If a collision is detected, a conflict warning is shown.
4. Click **Save Settings** to apply.

---

## 3. In-App Updates & Auto-Updater

TriggerPoint features a built-in update engine powered directly by the GitHub Releases API.

![Update Available Dialog](images/config-update-available-dialog.png)
<!-- SCREENSHOT REQUIRED: UpdateAvailableDialog modal window displaying "New Version Available: v2.0.9 (Current: v2.0.8)". The release date ("September 18, 2026") and download size ("3.88 MB") are shown above a formatted Markdown release notes changelog viewer. At the bottom, "Install Update Now", "Remind Me Later", and "Skip This Version" buttons are visible. -->

### Update Settings
In Application Settings under **Updates**:
- **Check Frequency**:
  - `On Application Startup`: Checks GitHub every time TriggerPoint launches.
  - `Daily` (default): Checks once every 24 hours in the background.
  - `Weekly`: Checks once every 7 days.
  - `Manual Only`: Disables background polling; updates only check when you click **Check Now** or select **Check for Updates...** from the system tray.
- **Include Pre-Releases**: When enabled, notifies you of beta and preview releases in addition to stable production builds.
- **Silent Install Updates**: When enabled, the installer runs in unattended background mode (`/VERYSILENT`), closes the active daemon, updates the files, and relaunches TriggerPoint automatically without installation wizard prompts.

### On-Demand Update Checks
- Click **Check for Updates Now** in settings, or right-click the system tray reticle icon and choose **Check for Updates...**
- If you are on the newest release, a toast notification confirms: `You're up to date! TriggerPoint vX.Y.Z is the newest release.`
- If an update is available, the **Update Available Dialog** appears.

### The In-App Upgrade Flow
1. Click **Install Update Now**.
2. TriggerPoint downloads the official `TriggerPointSetup.exe` directly from GitHub with real-time download progress tracking.
3. The installer's cryptographic **SHA-256 hash** is verified against the published release digest.
4. TriggerPoint gracefully shuts down background listeners and launches the installer.
5. The application restarts automatically with all your shortcuts and settings intact.

---

## 4. Enterprise-Grade Logging (Serilog)

TriggerPoint integrates a structured logging pipeline powered by Serilog:

![Logging and Retention Settings](images/config-logging-retention.png)
<!-- SCREENSHOT REQUIRED: Application Settings Logging tab showing the Log Level dropdown selected to "Information", Daily Retention slider set to "30 Days", Split Threshold set to "100 MB", and the "Open Logs Directory" button. A snippet of recent log records is shown in the preview box below. -->

### Runtime Log Level Switching (Zero Restart Required)
Switch the log verbosity on the fly:
- `Verbose`: Detailed tracing of every keypress, focus shift, and token evaluation.
- `Debug`: Diagnostic logs including hook registrations and process targeting matches.
- `Information` (default): Operational milestones, configuration loads, action triggers, and update checks.
- `Warning`: Non-fatal issues (e.g., hotkey collisions, missing target executables).
- `Error`: Action execution exceptions and script runtime errors.
- `Fatal`: Critical startup failures.

### Storage & Retention Parameters
- **Daily Rolling File Sink**: Creates a new timestamped log file daily: `triggerpoint-yyyyMMdd.log`.
- **Retention Window**: Configurable slider from **1 to 90 days** (default: 30 days). Older log files are automatically purged to prevent disk clutter.
- **File Split Threshold**: Splits log files if they exceed a specified size (default: 100 MB).
- **Log Location**: Stored locally in `%LOCALAPPDATA%\TriggerPoint\logs\`. Click **Open Logs Directory** to open the folder in Windows Explorer.

---

## 5. Storage Architecture & Disaster Resilience

TriggerPoint stores all user data locally on your computer. No cloud accounts, databases, or network dependencies are required.

### File Storage Locations
All files are located in your Windows User Profile:
- **Configuration Root**: `%LOCALAPPDATA%\TriggerPoint\config\`
  - `triggers.json`: Primary configuration database (shortcuts, workflows, snippets).
  - `triggers.json.bak`: Rolling emergency backup copy.
  - `settings.json`: Application preferences, window geometry, and theme settings.
  - `snapshots\`: Historical timestamped backup snapshots created automatically prior to every write.
- **Logs Directory**: `%LOCALAPPDATA%\TriggerPoint\logs\`

### Transactional Two-Phase Atomic Write
To prevent data corruption during unexpected power outages, Windows crashes, or disk full events:
1. When you click **Save**, TriggerPoint serializes the configuration to a temporary file (`triggers.json.tmp`) and flushes bytes to physical disk storage (`FlushToDisk: true`).
2. If and only if the primary `triggers.json` is verified as valid, TriggerPoint updates `triggers.json.bak` and archives a snapshot in the `snapshots/` folder.
3. The temporary file atomically replaces `triggers.json` via native Win32 `MoveFileEx` with replace semantics.

### Automated Backup Failover
If `triggers.json` is ever corrupted or zeroed out by an external process:
- Upon startup, TriggerPoint detects the invalid file and automatically restores the last known good configuration from `triggers.json.bak`.
- A warning toast alerts you that recovery occurred, ensuring your actions and workflows are never lost.

### Recycle Bin Retention
- When actions are deleted, they are soft-deleted into the Recycle Bin.
- Configure the auto-purge retention window in settings (**0 to 90 days**; default: 30 days). Setting to 0 days disables automatic purging.
