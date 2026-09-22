# TriggerPoint Troubleshooting & Diagnostics 🔍

This guide helps you identify, diagnose, and resolve common issues, hotkey collisions, broken executable paths, script exceptions, and configuration recovery scenarios in TriggerPoint.

---

## 1. Hotkey Conflicts & Collision Handling

TriggerPoint monitors global shortcut registrations using a two-tiered validation engine. When a hotkey cannot be registered or collides with another trigger, TriggerPoint immediately alerts you through multiple indicators:
- The system tray reticle turns **Red** (`Conflict State`).
- A **Conflicts Filter Chip (`⚡ Conflicts`)** appears in the sidebar tree.
- A warning banner appears above the Hotkey Recorder in the editor.

### Internal vs. System Conflicts

#### Case A: Internal Conflict
Two or more actions within TriggerPoint have been assigned the identical shortcut combination.

![Internal Hotkey Conflict Warning](images/troubleshoot-internal-conflict.png)
<!-- SCREENSHOT REQUIRED: Hotkey Conflict Banner displayed in amber above the HotkeyRecorder showing: "⚠ Internal Conflict Detected: Shortcut 'Ctrl + Alt + N' is already assigned to 'Open Notepad' in General Tools. Click here to jump to conflicting item." -->

**Resolution**:
1. Click the link in the warning banner (or click the **Conflicts** filter chip in the sidebar).
2. TriggerPoint automatically navigates to the conflicting action.
3. Either change one of the shortcuts to an alternative combination, or click **Clear** to remove it.
4. Click **Save** (<kbd>Ctrl</kbd> + <kbd>S</kbd>).

---

#### Case B: System / External Hotkey Conflict
An external application (e.g., Discord, GeForce Experience, PowerToys, AMD Adrenalin, or Windows itself) has already registered the shortcut with the operating system. When TriggerPoint calls the Win32 `RegisterHotKey` API, Windows returns an `ERROR_HOTKEY_ALREADY_REGISTERED` error.

![System Hotkey Conflict Banner](images/troubleshoot-system-conflict.png)
<!-- SCREENSHOT REQUIRED: Hotkey Conflict Banner displayed in red above the HotkeyRecorder showing: "⚠ System Hotkey Conflict: Shortcut 'Win + G' could not be registered because it is reserved by Windows Game Bar or another running background application." -->

**Common Windows System Reservations to Avoid**:
- <kbd>Win</kbd> + <kbd>G</kbd> (Xbox Game Bar)
- <kbd>Win</kbd> + <kbd>H</kbd> (Windows Voice Typing)
- <kbd>Win</kbd> + <kbd>L</kbd> (Lock Computer - OS Hardcoded)
- <kbd>Win</kbd> + <kbd>V</kbd> (Windows Clipboard History)
- <kbd>Ctrl</kbd> + <kbd>Alt</kbd> + <kbd>Delete</kbd> (Windows Security Attention Sequence - OS Hardcoded)
- <kbd>PrintScreen</kbd> (Windows Snipping Tool)

**Resolution**:
1. Assign a different key combination with distinct modifier keys (e.g., using <kbd>Ctrl</kbd> + <kbd>Alt</kbd> or <kbd>Ctrl</kbd> + <kbd>Shift</kbd> instead of <kbd>Win</kbd>).
2. If you prefer to keep the shortcut, identify the conflicting application and reassign or disable the shortcut in that program's settings.

---

## 2. Broken Executable Target Warnings

TriggerPoint continuously validates file paths and commands in the background to ensure your shortcuts will not fail when invoked.

![Broken Target Warning Chip](images/troubleshoot-broken-target-chip.png)
<!-- SCREENSHOT REQUIRED: Main window showing an action with a red/amber broken target chip in the TreeView and inside the editor pane displaying: "⚠ Target Not Found: Executable 'D:\Tools\old_app.exe' does not exist on disk." The Browse (...) button is highlighted. -->

### Causes of Broken Targets
- The target application was uninstalled or moved to a different folder.
- A USB drive or network share containing the executable is currently disconnected.
- A typo in the executable path or environment variable.

### Resolution Steps
1. Click the **Broken Targets (`⚠`)** filter chip above the sidebar tree to isolate all failing actions.
2. Select the broken action.
3. Click the **Browse (`...`)** button next to the Command box and locate the new file location, or drag the new executable directly into the window.
4. Click **Save** (<kbd>Ctrl</kbd> + <kbd>S</kbd>). The warning chip disappears immediately upon successful path verification.

---

## 3. Shell Execution & Administrator Elevation

If an App / Command action fails to run or throws an access denied error:

### Issue: "Access is Denied" or Application Requires Elevation
- **Symptoms**: Shortcut fires, but the target program never appears, or a Windows UAC error is logged.
- **Cause**: The application requires Windows Administrator rights (e.g., disk partition tools, command-line system utilities, hosts file editors).
- **Fix**: Open the action in TriggerPoint, check **Run as Administrator**, and click **Save**. When triggered, Windows will present the standard UAC elevation prompt.

### Issue: Multi-Monitor Window Targeting Fails
- **Symptoms**: You configured an action to launch on `Display 2 (Secondary)`, but it opens on the primary screen.
- **Cause**: The secondary monitor was disconnected, turned off, or Windows changed monitor arrangement indices.
- **Fix**: In the action editor, select **Follow Mouse Cursor** or re-select the active display index from the **Display Target** dropdown.

---

## 4. Workflow Step & JavaScript Script Errors

When an automated workflow encounters an unexpected condition:

### Common Script Runtime Errors

| Error Message | Cause | Resolution |
| :--- | :--- | :--- |
| `ReferenceError: x is not defined` | A script references a variable that has not been initialized. | Verify variable names in the **Workflow Variables** drawer, or declare it using `tp.vars.set("x", value)`. |
| `FileNotFoundException` | A file check or file read step referenced a missing path. | Precede file read operations with an `Ensure Directory` step or an `IfCondition` testing `File Exists`. |
| `Condition evaluation timeout` | A regex pattern in an `IfCondition` caused catastrophic backtracking. | Simplify the regular expression or test it in the "▶ Test Condition Live" modal. |

### Using the Workflow Debug Dialog
To inspect execution flow and step errors:
1. Select the failing workflow.
2. Click **Test Action** (<kbd>F5</kbd>) or choose **Debug Workflow**.
3. The **Workflow Debug Dialog** opens:
   - Green checks denote successfully completed steps.
   - A red cross denotes the exact step where an exception occurred.
   - The right-hand **Variable Inspector** shows variable values at each stage.
   - The **Console Log** at the bottom outputs the exact stack trace and error message.

---

## 5. Diagnostic Logging & Serilog Inspection

When troubleshooting subtle issues, TriggerPoint's built-in Serilog logging pipeline captures comprehensive diagnostic telemetry:

![Serilog Log Viewer Inspection](images/troubleshoot-log-viewer.png)
<!-- SCREENSHOT REQUIRED: Notepad or VS Code displaying an opened "triggerpoint-20260918.log" file from %LOCALAPPDATA%\TriggerPoint\logs. Highlighted log records show timestamp, log level [INF] / [WRN] / [ERR], component source ("Win32HotkeyListener"), and diagnostic exception details. -->

### Accessing Log Files
1. Open **Application Settings** (`⚙`).
2. Go to the **Logging** section and click **Open Logs Directory**.
3. Windows Explorer opens directly to `%LOCALAPPDATA%\TriggerPoint\logs\`.
4. Open the latest file: `triggerpoint-yyyyMMdd.log`.

### Enabling Debug / Verbose Logging
To capture maximum detail during an active investigation:
1. In Application Settings under **Logging**, change **Log Level** from `Information` to `Debug` or `Verbose`.
2. The change takes effect **immediately** without restarting TriggerPoint.
3. Reproduce your issue.
4. Inspect the log file for detailed Win32 API calls, token evaluation chains, and window focus hooks.
5. *Remember*: Return the log level to `Information` after finishing diagnostics to minimize disk write activity.

---

## 6. Disaster Recovery & Configuration Rollback

TriggerPoint includes multi-layered redundancy to ensure your configuration is never permanently lost:

### Level 1: Automatic Failover (`triggers.json.bak`)
If `triggers.json` is corrupted, truncated, or zeroed out (e.g., due to an unexpected power loss during write), TriggerPoint detects the corruption on launch, automatically loads `triggers.json.bak`, and displays a recovery toast:
`Restored configuration from backup (.bak) file.`

### Level 2: Historical Snapshots
TriggerPoint archives a timestamped copy of your configuration in `%LOCALAPPDATA%\TriggerPoint\config\snapshots\` prior to every successful save operation.

**Manual Rollback Steps**:
1. Exit TriggerPoint (right-click tray icon > **Exit**).
2. Open Windows Explorer and navigate to:
   `%LOCALAPPDATA%\TriggerPoint\config\snapshots\`
3. Locate the snapshot file representing the date and time you wish to restore (e.g., `triggers-2026-09-18T10-30-00.json`).
4. Copy this file to `%LOCALAPPDATA%\TriggerPoint\config\triggers.json` (overwriting the current file).
5. Relaunch TriggerPoint. Your previous configuration is restored completely.

### Level 3: Restoring Deleted Items from the Recycle Bin
Accidentally deleted a shortcut or folder?
1. Open the Settings Window (<kbd>Ctrl</kbd> + <kbd>Alt</kbd> + <kbd>T</kbd>).
2. Scroll to the bottom of the sidebar tree and expand **🗑 Recycle Bin**.
3. Select the deleted action.
4. Click **Restore Item** in the toolbar. The action is returned to its original folder.

---

## 7. Update & Network Troubleshooting

If the in-app update checker fails or cannot connect:

### Issue: "Could not check for updates. GitHub API rate limit exceeded."
- **Cause**: Unauthenticated GitHub API calls are limited to 60 requests per hour per IP address. If sharing an office network or VPN, rate limits may be temporarily reached.
- **Resolution**: Wait a few minutes and try again, or download the installer directly from the [GitHub Releases](https://github.com/Rapscallion0/TriggerPoint/releases) page.

### Issue: Network Firewall / Corporate Proxy Blocks Download
- **Cause**: Corporate security proxies or endpoint security policies may restrict downloads from `github.com`.
- **Resolution**: Whitelist `https://api.github.com` and `https://github.com/Rapscallion0/TriggerPoint/releases/` in your proxy settings, or download `TriggerPointSetup.exe` manually.
