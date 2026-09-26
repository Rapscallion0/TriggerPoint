# TriggerPoint Interface Overview 🖥

This section provides an in-depth breakdown of TriggerPoint's user interface, including the Main Configuration Window, the Sidebar TreeView, the Action Details Editor, status indicators, and the floating Heads-Up Display (HUD) overlay family.

---

## The Main Configuration Window

The Main Configuration Window is the control center where you create, configure, test, and organize all actions, shortcuts, and workflows.

Open it at any time using <kbd>Ctrl</kbd> + <kbd>Alt</kbd> + <kbd>T</kbd>, double-clicking the system tray icon, or selecting **Open Settings** from the tray menu.

![Main Settings Window Annotated Layout](images/interface-main-window-layout.png)
<!-- SCREENSHOT REQUIRED: Main Configuration Window annotated with numbered callout boxes pointing to: (1) Navigation and Filter Bar, (2) Action Sidebar TreeView, (3) Global Action Toolbar, (4) Action Header & Hotkey Recorder, (5) Action Payload Editor Panel, (6) Process & Browser URL Context Rules, and (7) Bottom Status Bar. -->

---

## 1. Action Sidebar TreeView (Left Pane)

The left panel organizes all your shortcuts and folders into an interactive hierarchical tree.

![TreeView States and Badges](images/interface-treeview-states.png)
<!-- SCREENSHOT REQUIRED: Sidebar TreeView zoomed in showing various item states: an expanded folder with nested actions, an action with an active hotkey badge ("Win + N"), an action with a yellow broken-target warning badge, a disabled semi-transparent item, and the bottom Recycle Bin node with deleted items. -->

### Search, Filtering & Quick Chips
Above the tree, use the real-time search input and filter chips to quickly find actions in large collections:
- **Search Box**: Instant fuzzy filtering matching action names, descriptions, executable paths, or hotkeys. Press <kbd>Esc</kbd> to clear.
- **Filter Chips**:
  - `All`: Shows all active actions and folders.
  - `Folders` (`📁`): Displays only folder structures.
  - `Apps` (`⚡`): Filters for Shell commands and executable launches.
  - `Snippets` (`📝`): Filters for text expansion templates.
  - `Workflows` (`🔀`): Filters for multi-step automated sequences.
  - `Broken Target` (`⚠`): Appears automatically whenever one or more actions reference non-existent files or invalid command paths.
  - `Conflicts` (`⚡`): Filters for actions with hotkey collisions.

### Tree Item Elements
Each row in the tree displays contextual information:
- **Action Type Icon**: Visual symbol reflecting the action type (`📁` Folder, `⚡` App, `📝` Snippet, `🔀` Workflow, `🔴` Macro).
- **Name**: User-defined label.
- **Hotkey Badge**: The registered global shortcut (e.g., <kbd>Ctrl+Alt+P</kbd>). If an action is disabled, the row opacity is reduced to 55%.
- **Status Badges**:
  - `⚠ Red/Yellow Icon`: Indicates a broken file path or invalid shell command.
  - `⚠ Conflict Icon`: Indicates an unresolved hotkey conflict.

### Drag & Drop Hierarchy & Reordering
Reorganize items intuitively using mouse drag-and-drop:
- **Drop Above / Below**: A highlighted insertion line appears between rows to reorder actions at the same level.
- **Drop Inside (Folder Nesting)**: Dropping directly onto a folder card highlights the folder border, moving the item inside that folder.
- **Folder Expansion**: Click the chevron arrow (`▶` / `▼`) to expand or collapse folder branches.

### The Recycle Bin (`🗑 Recycle Bin`)
TriggerPoint uses safe, non-destructive deletion:
- Deleting an action moves it into the special **Recycle Bin** node at the bottom of the tree.
- Expanding the Recycle Bin lets you select any deleted item and click **Restore Item** to return it to its previous folder location.
- Click **Empty Recycle Bin** to permanently purge all deleted items.
- Items are automatically pruned based on the configurable retention window (default: 30 days; see [Application Settings](configuration.md#recycle-bin-retention)).

---

## 2. Top Action Toolbar

The header toolbar across the top provides persistent control over the active item:

| Toolbar Button | Shortcut | Description |
| :--- | :--- | :--- |
| **Save** | <kbd>Ctrl</kbd> + <kbd>S</kbd> | Commits changes to the atomic transactional configuration store. Disabled when no unsaved changes exist. |
| **Revert** | <kbd>Ctrl</kbd> + <kbd>Z</kbd> | Discards current modifications and restores the last saved state. Uses deep snapshot comparison to automatically toggle dirty status. |
| **Test Action** | <kbd>F5</kbd> | Immediately executes the selected action or workflow in test mode without requiring you to trigger its hotkey. |
| **Duplicate** | <kbd>Ctrl</kbd> + <kbd>D</kbd> | Clones the selected action with a copy suffix. **Safety Guard**: The duplicated action automatically clears its hotkey to prevent instant collisions. |
| **Delete** | <kbd>Delete</kbd> | Moves the active item to the Recycle Bin. |
| **+ Add Action** | <kbd>Ctrl</kbd> + <kbd>N</kbd> | Opens the Add menu to create a new Folder, App/Command, Snippet, Workflow, or Macro. |
| **Application Settings** | — | Opens the global settings dialog (themes, hotkeys, logs, update checker). |
| **Update Badge** | — | A green/accent badge button that automatically appears when a newer version of TriggerPoint is available on GitHub. Clicking it opens the update dialog. |

---

## 3. Action Details Editor (Right Pane)

When you select an action in the tree, the right pane displays its editable parameters:

### Action Header
- **Type Badge Pill**: Visual tag showing the action category (`📁 FOLDER`, `⚡ APP & COMMAND`, `📝 SNIPPET`, etc.).
- **Admin Shield Badge (`🛡 ADMIN`)**: Appears automatically if the action is set to execute with elevated Windows Administrator privileges.
- **Name Box**: The display label used in trees, menus, and the Command Palette.
- **Description Box**: Optional helper text explaining the purpose of the action.
- **Enabled Checkbox**: Toggle to enable or temporarily deactivate an action without deleting it.

### Hotkey Recorder Control
The interactive hotkey recorder enables single-click shortcut capture:

```
[  Win + Alt + S  ]  [ Clear ]
```

1. Click into the hotkey recorder box.
2. Press your desired key combination on your keyboard (e.g., <kbd>Ctrl</kbd> + <kbd>Shift</kbd> + <kbd>E</kbd>). The recorder captures modifier keys (<kbd>Ctrl</kbd>, <kbd>Alt</kbd>, <kbd>Shift</kbd>, <kbd>Win</kbd>) and primary keys simultaneously.
3. Click **Clear** to remove the shortcut entirely.

### Hotkey Conflict Banner
If the recorded shortcut conflicts with another action or a reserved Windows system hotkey, a high-visibility warning banner appears immediately above the recorder:

![Hotkey Conflict Banner](images/interface-conflict-banner.png)
<!-- SCREENSHOT REQUIRED: Hotkey Conflict Banner rendered in Amber/Red above the HotkeyRecorderControl showing "⚠ Internal Conflict Detected: Shortcut 'Ctrl + Alt + N' is already assigned to 'Open Notepad' in General Tools". -->

- **Internal Conflict**: Indicates another TriggerPoint item already uses this shortcut. Click the banner hint to jump directly to the conflicting item.
- **System Conflict**: Indicates an external application or Windows OS component has locked the shortcut. Learn how to address this in [Troubleshooting](troubleshooting.md#system-hotkey-conflicts).

### Presentation Mode & Accelerator Keys (Folders Only)
When a folder is selected, configure how its child items are presented:
- **Presentation Mode**:
  - `Direct (Run immediately)`: Top-level folder for organization only.
  - `Cursor Menu (Popup at mouse)`: Launches a floating quick-menu directly under the cursor upon hotkey press.
  - `Centered Dialog`: Launches a centered menu window on screen.
- **Auto-Number Mode**:
  - `Off`: Only explicit accelerator keys are used.
  - `Smart Fill`: Automatically numbers unassigned menu items (`1–9`, `A–Z`) while preserving custom accelerator keys.
  - `Strict Positional`: Automatically renumbers all items strictly based on their physical tree order.

### Action Payload Editor
The center section dynamically transforms based on the selected action type:
- **Shell Action**: Executable path, command arguments, working directory, and multi-monitor targeting.
- **Snippet Action**: Plain text template box or rich text editor with visual ribbon and live expansion preview card.
- **Workflow Action**: Visual step sequence cards, branch rails, drag-and-drop targets, and AvalonEdit JavaScript script editor.
- **Macro Action**: Keystroke and mouse recording timeline.

### Process & Context Filter Section
At the bottom of every action and folder editor, configure contextual rules to restrict execution to specific programs or browser URLs.

![Window Target Crosshair Tool](images/interface-window-target-crosshair.png)
<!-- SCREENSHOT REQUIRED: WindowTargetingOverlay crosshair tool being dragged across the screen, hovering over a Google Chrome browser window with a target badge displaying the process name "chrome.exe" and detected browser URL pattern. -->

- **Allowed Processes**: Action will **only** trigger when one of these applications is in the foreground.
- **Excluded Processes**: Action will trigger everywhere **except** within these applications.
- **Allowed / Excluded URLs**: Specific web addresses or wildcards (`*github.com*`, `*jira*`) for browser tab scoping.
- **Window Target Crosshair Tool**: Drag the target reticle icon onto any visible window to automatically capture its executable filename and browser URL.

---

## 4. The Floating HUD Overlay Family

TriggerPoint includes three specialized borderless Heads-Up Display (HUD) windows that float above your desktop:

![TriggerPoint Floating HUD Family](images/interface-floating-hud-family.png)
<!-- SCREENSHOT REQUIRED: Composite visual showing the three floating HUD overlays: (1) Spotlight Command Palette with quick search input and result items, (2) Shortcut Cheat Sheet HUD with two-column shortcut layout, and (3) Chord Indicator HUD displaying "Ctrl + K, ... waiting for second key". -->

### A. Spotlight Command Palette (<kbd>Ctrl</kbd> + <kbd>Shift</kbd> + <kbd>Space</kbd>)
- A centered, floating search bar inspired by macOS Spotlight and PowerToys Run.
- Fuzzy searches across all actions, folders, and workflow sequences.
- Displays folder breadcrumbs (`📁 Tools › Development › Edit Hosts`).
- Features the **Instant Math Calculator** (`=25 * 4.5`, `sqrt(144)`).
- Multiple sort modes: Alphabetical, Frequency (usage count), or Custom Tree Order.

### B. Shortcut Cheat Sheet HUD (<kbd>Ctrl</kbd> + <kbd>Shift</kbd> + <kbd>/</kbd>)
- A non-intrusive floating HUD displaying all registered shortcuts grouped by folder.
- Real-time search filter to locate any hotkey in seconds.
- Press any hotkey or click an item directly to execute it and dismiss the HUD.

### C. Chord Indicator HUD
- Appears when you trigger a multi-key leader chord (such as <kbd>Ctrl</kbd> + <kbd>K</kbd>).
- Displays the pending chord status: `Ctrl + K, ... waiting for second key`.
- Shows available secondary key options and their corresponding action names.
- Press <kbd>Esc</kbd> at any time to cancel the chord sequence.

---

## 5. Status Bar Indicators

The footer bar at the bottom of the window displays ambient telemetry:
- **Item Count**: Total number of configured items and folders (`42 items`).
- **Active Hotkeys**: Total number of active registered global shortcuts (`18 active triggers`).
- **Unsaved Edits Indicator**: Highlights when changes are pending save (`● Unsaved changes`).
- **Application Version**: Current version build string (e.g., `v2.0.9`).
