# TriggerPoint Master Screenshot Checklist 📸

This document serves as the centralized tracking checklist for all visual screenshot assets required across the TriggerPoint documentation suite. Each entry lists the exact target file path, target view/dialog, expected UI state, active inputs, sample data, and elements that should be in focus during capture.

---

## 1. `docs/index.md`

- [ ] **`images/index-hero-overview.png`**
  - **View / Dialog**: Main Configuration Window (`SettingsWindow.xaml`).
  - **Theme**: Dark Mode.
  - **UI State**: Sidebar tree expanded with sample folders (`General Tools`, `Developer Tools`) and populated action items. Action details pane on the right showing an active action. Global toolbar showing enabled buttons. Status bar showing item counts and version.
  - **Sample Data**: "General Tools" containing "Notepad" (<kbd>Win+N</kbd>) and "Date Snippet" (<kbd>Ctrl+Alt+D</kbd>).
  - **Focus Element**: Main window active and centered.

- [ ] **`images/index-command-palette.png`**
  - **View / Dialog**: Spotlight Command Palette (`CommandPaletteView.xaml`).
  - **Theme**: Dark Mode.
  - **UI State**: Floating palette centered on screen with transparent background and drop shadow. Search box populated with math formula.
  - **Active Inputs**: `=45 * 1.15` in the search text box.
  - **Sample Data**: Math calculation card displaying result `51.75` with "Press Enter to copy result" badge. Search results below displaying recent actions.
  - **Focus Element**: Search text box with blinking caret after formula.

---

## 2. `docs/getting-started.md`

- [ ] **`images/getting-started-installer-scope.png`**
  - **View / Dialog**: Inno Setup Installation Scope Page (`TriggerPointSetup.exe`).
  - **Theme**: Windows standard dialog theme.
  - **UI State**: Setup wizard dialog displaying the dual-scope radio button options.
  - **Active Inputs**: Radio button "Install for me only (recommended, no administrator privileges required)" selected.
  - **Sample Data**: Standard installation wizard header "Select Installation Scope".
  - **Focus Element**: "Next >" button focused.

- [ ] **`images/getting-started-tray-menu.png`**
  - **View / Dialog**: Windows Taskbar Notification Area and Tray Context Menu (`TrayIconService.cs`).
  - **Theme**: Dark / Windows Shell Context Menu.
  - **UI State**: System tray notification area magnified showing the active TriggerPoint reticle icon in Armed status alongside the open context menu.
  - **Sample Data**: Menu items: Open Settings, Check for Updates..., Command Palette (Ctrl + Shift + Space), Shortcut Cheat Sheet (Ctrl + Shift + /), Snooze Global Hotkeys, Reload Configuration, Exit TriggerPoint.
  - **Focus Element**: "Open Settings" menu item hovered.

- [ ] **`images/getting-started-initial-settings.png`**
  - **View / Dialog**: Main Settings Window (`SettingsWindow.xaml`) on clean first launch.
  - **Theme**: Dark Mode.
  - **UI State**: Fresh configuration loaded from default template.
  - **Sample Data**: Sidebar tree showing default sample folders under "Starter Pack & Examples" (Quick Launcher, Text Snippets & Templates, Automated Workflows, Keystroke Automation).
  - **Focus Element**: "Starter Pack & Examples" folder selected in the sidebar tree.

---

## 3. `docs/interface-overview.md`

- [ ] **`images/interface-main-window-layout.png`**
  - **View / Dialog**: Main Configuration Window (`SettingsWindow.xaml`).
  - **Theme**: Dark Mode.
  - **UI State**: Full window overview with numbered visual callout annotations:
    1. Navigation and Filter Bar.
    2. Action Sidebar TreeView.
    3. Global Action Toolbar.
    4. Action Header & Hotkey Recorder.
    5. Action Payload Editor Panel.
    6. Process & Browser URL Context Rules Drawer.
    7. Bottom Status Bar.
  - **Sample Data**: Populated action tree with various item types.
  - **Focus Element**: Window centered with all layout panels visible.

- [ ] **`images/interface-treeview-states.png`**
  - **View / Dialog**: Sidebar TreeView close-up (`TriggerTreeView`).
  - **Theme**: Dark Mode.
  - **UI State**: Magnified view of the sidebar displaying varied item states:
    - Expanded folder with nested items.
    - Active action with hotkey badge (`Win + N`).
    - Action with broken-target warning badge (`⚠`).
    - Disabled action displaying 55% dimmed opacity.
    - Bottom `🗑 Recycle Bin` root node containing deleted items.
  - **Sample Data**: "General Tools" folder and "Old Backup Tool" broken action.
  - **Focus Element**: Sidebar tree panel.

- [ ] **`images/interface-conflict-banner.png`**
  - **View / Dialog**: Hotkey Recorder Control and Conflict Banner (`HotkeyRecorderControl.xaml`).
  - **Theme**: Dark Mode.
  - **UI State**: Hotkey Conflict warning banner rendered in amber/red directly above the hotkey recorder.
  - **Sample Data**: Conflict text: `"⚠ Internal Conflict Detected: Shortcut 'Ctrl + Alt + N' is already assigned to 'Open Notepad' in General Tools. Click here to jump to conflicting item."`
  - **Focus Element**: Hotkey recorder input box.

- [ ] **`images/interface-window-target-crosshair.png`**
  - **View / Dialog**: Window Targeting Overlay (`WindowTargetingOverlay.xaml`).
  - **Theme**: Transparent desktop overlay.
  - **UI State**: Crosshair reticle icon being dragged from TriggerPoint and hovering over an open Google Chrome window.
  - **Sample Data**: Floating targeting badge displaying: `Target: chrome.exe | URL: *github.com/Rapscallion0/TriggerPoint*`.
  - **Focus Element**: Crosshair cursor positioned over the browser title bar.

- [ ] **`images/interface-floating-hud-family.png`**
  - **View / Dialog**: Composite or side-by-side arrangement of all three floating HUD windows.
  - **Theme**: Dark Mode with transparent backdrop and rounded corners.
  - **UI State**:
    1. Spotlight Command Palette (`CommandPaletteView.xaml`).
    2. Shortcut Cheat Sheet HUD (`CheatSheetHudView.xaml`).
    3. Chord Indicator HUD (`ChordHudView.xaml`) displaying `"Ctrl + K, ... waiting for second key"`.
  - **Sample Data**: Standard shortcuts and chord choices.
  - **Focus Element**: Composite visual arrangement.

---

## 4. `docs/core-workflows.md`

- [ ] **`images/workflow-shell-action-editor.png`**
  - **View / Dialog**: Action Details Editor panel for Shell Action (`SettingsWindow.xaml`).
  - **Theme**: Dark Mode.
  - **UI State**: Configured for an App / Command action.
  - **Active Inputs**: Command box set to `code`, Arguments set to `C:\Projects\TriggerPoint`, Working Directory set to `C:\Projects`, Run as Admin unchecked, Display Target set to `Display 2 (Secondary)`.
  - **Sample Data**: Green "Path Valid" validation status chip visible next to the Command box.
  - **Focus Element**: Arguments text box.

- [ ] **`images/workflow-plain-snippet-tokens.png`**
  - **View / Dialog**: Plain Text Snippet Editor (`SettingsWindow.xaml`).
  - **Theme**: Dark Mode.
  - **UI State**: Plain Text mode selected. Template box contains dynamic text tokens. Contextual Token Assistant pill bar visible below the editor. Live Expansion Preview card populated at the bottom.
  - **Active Inputs**: Template content: `"Meeting Notes - {date:MMMM d, yyyy}\nAttendee: {username}\nAction Items:\n- {cursor}"`.
  - **Sample Data**: Live Expansion Preview card displaying: `"Meeting Notes - September 18, 2026\nAttendee: KevinSommers\nAction Items:\n-"` with stats `"82 chars • 3 tokens"`.
  - **Focus Element**: Template text box.

- [ ] **`images/workflow-rich-text-editor.png`**
  - **View / Dialog**: Rich Text Snippet Editor in Paper Canvas Mode (`SettingsWindow.xaml`).
  - **Theme**: Dark Mode UI with White Paper Canvas (`#FFFFFF`) editor and preview containers.
  - **UI State**: Rich Text mode active. Formatting ribbon displays 11pt font size, Bold active, custom blue text color indicator, and bulleted list. Canvas toggle shows moon icon (`🌙 Theme`).
  - **Active Inputs**: Formatted text: Bold header `"Sprint Review Summary"`, blue subheading `"Release Highlights"`, and two bullet points with checkmarks.
  - **Sample Data**: Live Expansion Preview container matches paper white styling with identical rendered typography.
  - **Focus Element**: RichTextBox editor container.

- [ ] **`images/workflow-interactive-prompt-dialog.png`**
  - **View / Dialog**: Interactive Prompt Dialog (`InteractivePromptDialog.xaml`).
  - **Theme**: Dark Mode.
  - **UI State**: Modal dialog presented over desktop during snippet or workflow expansion.
  - **Active Inputs**:
    - Single-line Textbox: "Customer Name" set to `Acme Corporation`.
    - Dropdown Selector: "Priority Level" set to `High`.
    - Date Picker Control: "Due Date" set to `2026-09-25`.
  - **Sample Data**: "Confirm (Enter)" button and "Cancel (Esc)" button visible.
  - **Focus Element**: First text input field focused.

- [ ] **`images/workflow-command-palette-calculator.png`**
  - **View / Dialog**: Spotlight Command Palette (`CommandPaletteView.xaml`).
  - **Theme**: Dark Mode with transparent rounded borders.
  - **UI State**: Search bar populated with math formula; instant calculation result card displayed below.
  - **Active Inputs**: `=250 * 1.0825` in the search box.
  - **Sample Data**: Emerald calculation card showing: `Result: 270.625` with clipboard icon and hint `Press Enter to copy to clipboard`. Below, list of matching actions.
  - **Focus Element**: Search text box.

- [ ] **`images/workflow-cheat-sheet-hud.png`**
  - **View / Dialog**: Shortcut Cheat Sheet HUD (`CheatSheetHudView.xaml`).
  - **Theme**: Dark Mode with transparent rounded borders and soft drop shadow.
  - **UI State**: Centered floating HUD. Top search box filtered to `git`. Two responsive columns showing matching shortcuts grouped by folder.
  - **Sample Data**: Shortcuts displayed: `Git Status` (<kbd>Win+G</kbd>), `Git Pull` (<kbd>Ctrl+Alt+P</kbd>), `Open Git Bash` (<kbd>Ctrl+Shift+G</kbd>).
  - **Focus Element**: Search box in header.

- [ ] **`images/workflow-cursor-popup-menu.png`**
  - **View / Dialog**: Floating Cursor Menu (`CursorContextMenuView.xaml`).
  - **Theme**: Dark Mode popup menu anchored at mouse coordinates.
  - **UI State**: Popup menu triggered by hotkey. Items numbered with sequential quick-keys.
  - **Sample Data**: Menu items:
    - `[1] Visual Studio Code`
    - `[2] Windows Terminal`
    - `[3] SQL Database Browser`
    - `[A] Docker Desktop`
    - `[B] Edit Hosts File`
  - **Focus Element**: Menu open at cursor position with item `[1]` hovered.

- [ ] **`images/workflow-context-filter-tags.png`**
  - **View / Dialog**: Process Context Rules section (`TagInputControl.xaml` inside `SettingsWindow.xaml`).
  - **Theme**: Dark Mode.
  - **UI State**: Process context rules drawer expanded.
  - **Active Inputs**:
    - Allowed Processes tag pills: `code.exe`, `windowsterminal.exe`.
    - Excluded Processes tag pill: `slack.exe`.
    - Browser URL pattern tag: `*github.com/*`.
  - **Sample Data**: Crosshair targeting button visible on the right.
  - **Focus Element**: Allowed Processes tag input field.

---

## 5. `docs/workflows-and-scripts.md`

- [ ] **`images/workflow-visual-step-builder.png`**
  - **View / Dialog**: Visual Step Builder (`SettingsWindow.Workflow.cs` inside `SettingsWindow.xaml`).
  - **Theme**: Dark Mode.
  - **UI State**: Vertical pipeline of configured step cards:
    1. Prompt User (`💬` Ticket Number).
    2. If Condition (`⚖` Check Environment).
    3. Launch App (`⚡` Jira Browser).
    4. Delay (`⏱` 500ms).
    5. Inject Snippet (`📝` Resolution Note).
  - **Sample Data**: Steps with populated parameters and expand/collapse chevrons.
  - **Focus Element**: Entire workflow sequence view.

- [ ] **`images/workflow-conditional-branching.png`**
  - **View / Dialog**: `IfCondition` Step Card detail view.
  - **Theme**: Dark Mode.
  - **UI State**: Card expanded showing condition parameters:
    - Left Operand: `{{ENVIRONMENT}}`.
    - Operator: `Equals (=)`.
    - Right Operand: `Production`.
    - Case-Insensitive switch: Enabled.
    - Green THEN rail containing an `Ensure Directory` step.
    - Amber ELSE rail containing a `Launch App` step.
  - **Sample Data**: "▶ Test Condition Live" button in focus.
  - **Focus Element**: Condition step header.

- [ ] **`images/workflow-drag-drop-targeting.png`**
  - **View / Dialog**: Workflow Step Drag-and-Drop in action.
  - **Theme**: Dark Mode.
  - **UI State**: User actively dragging a step card. Directional ghost badge visible near cursor (`↓ Drop after Step 2`). Cyan insertion drop line (`●───────`) rendered between Step 2 and Step 3.
  - **Sample Data**: "Delay 500ms" step being dragged.
  - **Focus Element**: Insertion line drop target.

- [ ] **`images/workflow-variable-picker-dialog.png`**
  - **View / Dialog**: Variable Picker Modal Dialog (`VariablePickerDialog.xaml`).
  - **Theme**: Dark Mode.
  - **UI State**: Search bar at top filtered to `path`. Category chips showing `All`, `System`, `Workflow`, `Clipboard`, `Date`. List of matching tokens displayed below.
  - **Sample Data**: Selected variable: `PROJECT_PATH` (`C:\Source\App`). Description: "Root project directory". "Insert Variable" button enabled.
  - **Focus Element**: "Insert Variable" button.

- [ ] **`images/workflow-secrets-vault-entry.png`**
  - **View / Dialog**: Workflow Variables drawer.
  - **Theme**: Dark Mode.
  - **UI State**: Workflow variable list showing public and secret variables.
  - **Sample Data**:
    - `ENV_NAME` (public, value: `Staging`).
    - `API_KEY` with Secret padlock icon checked, masked value `••••••••••••`, and hint `Encrypted with Windows DPAPI at rest`.
  - **Focus Element**: Secret toggle checkbox for `API_KEY`.

- [ ] **`images/workflow-javascript-avalon-editor.png`**
  - **View / Dialog**: Full JavaScript Workflow Editor mode (`AvalonEdit`).
  - **Theme**: Dark Mode code editor.
  - **UI State**: Code editor with JavaScript syntax highlighting, line numbers, and dark theme background. Top mode switch shows "Visual Mode" return button.
  - **Sample Data**: JavaScript code displayed using `tp.prompt()`, `tp.fs.fileExists()`, and `tp.launch()`.
  - **Focus Element**: Code editor text area at line 12.

- [ ] **`images/workflow-debug-dialog.png`**
  - **View / Dialog**: Workflow Debug Dialog (`WorkflowDebugDialog.xaml`).
  - **Theme**: Dark Mode.
  - **UI State**: Active workflow debugging execution trace. Left panel displays step execution pipeline with completed steps checked green. Right panel displays the Variable Inspection table with current runtime values. Bottom pane displays live Console Output.
  - **Sample Data**: Step 3 completed successfully (Exit Code 0). Console shows timestamped log entries.
  - **Focus Element**: Variable Inspection grid.

---

## 6. `docs/configuration.md`

- [ ] **`images/config-application-settings-general.png`**
  - **View / Dialog**: Application Settings Window (`ApplicationSettingsWindow.xaml`) - General Tab.
  - **Theme**: Dark Mode.
  - **UI State**: General settings pane displaying:
    - Theme dropdown set to `Dark Mode`.
    - UI Animations toggle enabled.
    - Backdrop Effects toggle enabled.
    - Success Toasts enabled with Toast Placement set to `Primary Monitor`.
    - Window Startup Placement set to `Remember Last Position & Size`.
  - **Focus Element**: Theme selection dropdown.

- [ ] **`images/config-hotkey-recorder-modal.png`**
  - **View / Dialog**: Application Settings Window - Hotkeys Section.
  - **Theme**: Dark Mode.
  - **UI State**: Global hotkey fields displayed:
    - Open Settings: `Ctrl + Alt + T`.
    - Command Palette: `Ctrl + Shift + Space` (active focus, modifier chips highlighted).
    - Shortcut Cheat Sheet: `Ctrl + Shift + /`.
  - **Focus Element**: Command Palette hotkey recorder field.

- [ ] **`images/config-update-available-dialog.png`**
  - **View / Dialog**: Update Available Modal Dialog (`UpdateAvailableDialog.xaml`).
  - **Theme**: Dark Mode.
  - **UI State**: Modal dialog displaying:
    - Version Comparison: `New Version: v2.0.9 (Current: v2.0.8)`.
    - Release Date: `September 18, 2026` | Download Size: `3.88 MB`.
    - Formatted Markdown changelog viewer in center.
    - Buttons at bottom: "Install Update Now", "Remind Me Later", "Skip This Version".
  - **Focus Element**: "Install Update Now" accent button.

- [ ] **`images/config-logging-retention.png`**
  - **View / Dialog**: Application Settings Window - Logging Section.
  - **Theme**: Dark Mode.
  - **UI State**: Logging settings displayed:
    - Log Level dropdown set to `Information`.
    - Daily Retention slider set to `30 Days`.
    - Split Threshold set to `100 MB`.
    - "Open Logs Directory" button enabled.
    - Recent log lines preview box showing formatted output.
  - **Focus Element**: Log Level dropdown.

---

## 7. `docs/troubleshooting.md`

- [ ] **`images/troubleshoot-internal-conflict.png`**
  - **View / Dialog**: Hotkey Conflict Banner for Internal Collision.
  - **Theme**: Dark Mode.
  - **UI State**: Amber warning banner rendered above the HotkeyRecorder.
  - **Sample Data**: Text: `"⚠ Internal Conflict Detected: Shortcut 'Ctrl + Alt + N' is already assigned to 'Open Notepad' in General Tools. Click here to jump to conflicting item."`
  - **Focus Element**: Jump link inside banner.

- [ ] **`images/troubleshoot-system-conflict.png`**
  - **View / Dialog**: Hotkey Conflict Banner for System Reservation.
  - **Theme**: Dark Mode.
  - **UI State**: Red warning banner rendered above the HotkeyRecorder.
  - **Sample Data**: Text: `"⚠ System Hotkey Conflict: Shortcut 'Win + G' could not be registered because it is reserved by Windows Game Bar or another running background application."`
  - **Focus Element**: Hotkey recorder field.

- [ ] **`images/troubleshoot-broken-target-chip.png`**
  - **View / Dialog**: Action editor panel with validation error chip.
  - **Theme**: Dark Mode.
  - **UI State**: Broken target chip displayed next to the Command box.
  - **Sample Data**: Text: `"⚠ Target Not Found: Executable 'D:\Tools\old_app.exe' does not exist on disk."` Browse button highlighted.
  - **Focus Element**: Browse (`...`) button.

- [ ] **`images/troubleshoot-log-viewer.png`**
  - **View / Dialog**: Text editor (Notepad / VS Code) opening a Serilog log file.
  - **Theme**: Dark theme code viewer.
  - **UI State**: Log file `triggerpoint-20260918.log` opened.
  - **Sample Data**: Formatted lines showing timestamp, level `[INF]` / `[WRN]` / `[ERR]`, source context `[Win32HotkeyListener]`, and diagnostic exception stack trace.
  - **Focus Element**: Highlighted error log record.

---

## Summary of Checklist Verification

| Documentation Page | Screenshot Image Count | Status |
| :--- | :---: | :---: |
| `docs/index.md` | 2 | Defined & Tracked |
| `docs/getting-started.md` | 3 | Defined & Tracked |
| `docs/interface-overview.md` | 5 | Defined & Tracked |
| `docs/core-workflows.md` | 8 | Defined & Tracked |
| `docs/workflows-and-scripts.md` | 7 | Defined & Tracked |
| `docs/configuration.md` | 4 | Defined & Tracked |
| `docs/troubleshooting.md` | 4 | Defined & Tracked |
| **Total Inventory** | **33 Screenshots** | **100% 1-to-1 Correspondence** |
