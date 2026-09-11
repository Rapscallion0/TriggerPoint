# TriggerPoint ⚡

**Precision shortcuts. Instant menus. Zero bloat.**  
*High-performance hotkey daemon, cursor menu launcher & dynamic snippet expander for Windows.*

[![.NET](https://img.shields.io/badge/.NET-9.0--windows-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![Platform](https://img.shields.io/badge/Platform-Windows%2010%20%2F%2011%20x64-0078D6?logo=windows)](https://microsoft.com/windows)
[![Version](https://img.shields.io/badge/Version-v2.0.4-blue.svg)]()
[![License: MIT](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)
[![Tests](https://img.shields.io/badge/Tests-123%20Passed%20(100%25)-brightgreen)]()

---

## Overview

**TriggerPoint** is a fast, lightweight Windows productivity tool that puts your most frequent actions at your fingertips. Launch apps, run scripts, paste dynamic snippets, and open custom menus anywhere on your screen using simple system-wide shortcuts.

Designed to stay out of your way, TriggerPoint runs quietly in your system tray with near-zero memory footprint and lightning-fast response times.

---

## What's New in v2.0.0 🚀

- **Visual Precision Drag-and-Drop & Ghost Preview**: 3-zone drop targeting with visual guide indicators (Insert Above line, Move Into folder highlight, Insert Below line) and a floating preview badge that follows the mouse cursor during drags.
- **Spring-Loaded Folders**: Pausing over a collapsed folder during a drag operation automatically springs it open (~650ms) for precise placement into nested hierarchies.
- **Persistent Folder Collapse/Expand States**: Tree folder states are remembered across application launches and restarts. Dropping items preserves individual folder states rather than expanding the entire tree.
- **Inconspicuous Collapse/Expand All Toggle**: A subtle 32x32 button (`⊟` / `⊞`) in the tree header toggles all folders between collapsed and expanded in a single click.
- **Tree Menu Inline Name Editing (`<F2>`)**: Rename any folder or action directly within the tree view using `<F2>` or right-click context menu. Automatically commits on `<Enter>` or clicking outside, with full keystroke isolation.
- **Active-Screen Toast Notifications**: Non-activating, auto-dismissing toast popups appear on the monitor currently containing the mouse cursor. Provides instant feedback for execution success or friendly diagnostics for missing executables.
- **Shortcut & Target Validation Engine**: Validates executables, URLs, custom URI protocols, system `PATH` binaries, and environment variable paths (`%WINDIR%`, `%APPDATA%`). Shows error badges (`✕`), live path diagnostics in the editor, and a one-click filter chip (`[✕ X Broken]`).
- **Recursive Folder Duplication (`Ctrl+D`)**: Deep-clones entire folder trees arbitrarily deep, retaining nested hierarchy while resetting hotkeys to prevent global shortcut conflicts.
- **Command Palette Context Fallbacks**: When an action has no description, the palette displays its folder breadcrumbs (`📁 Tools › Development`), command target path, or snippet preview so entries are never blank.
- **Recycle Bin & Safe Deletion**: Deleted items are moved to a pinned Recycle Bin with customizable retention policies, one-click restoration, or permanent purging.
- **Modular Backup & Export**: Export entire action configurations or isolated subfolder branches to standalone `.json` packages for sharing or backup.

---

## Key Features

### ⚡ Precision Global Hotkeys & Conflict Detection
- Fast Win32 `RegisterHotKey` global shortcut registration.
- **Two-tiered conflict detection**: Warns immediately if a shortcut is claimed by another TriggerPoint action or by an external system process/Windows utility.
- Interactive hotkey recorder with live visual combination builder.

### 🎯 Window & Browser Target Crosshair Tool
- Drag an interactive crosshair target onto any open application window to automatically extract its executable path and process name.
- **Active Browser Tab URL Filter**: Detects when targeting Chromium/Firefox browsers and prompts to capture and filter on the current URL pattern.

### 📋 Floating Cursor Menus & Folder Launchers
- Launch grouped actions directly under your mouse cursor with custom accelerator keys.
- Organize shortcuts into folders and hierarchical launcher menus.
- Full keyboard navigation and context menu support for rapid item management.

### 📝 Dynamic Snippets & Text Expansion
- Send keystrokes or clipboard-injected templates directly into the focused window.
- **Dynamic Date & Time Formatting & Offsets**:
  - `{date}`, `{date:format}` (e.g. `{date:MM/dd/yyyy}`, `{date:dddd, MMMM d, yyyy}`)
  - Relative date offsets: `{date:+1d}`, `{date:-1d:yyyy-MM-dd}`, `{tomorrow}`, `{yesterday}`
  - `{time}`, `{time:format}` (e.g. `{time:hh:mm tt}`, `{time:HH:mm}`), `{time:+1h}`
  - `{datetime}`, `{datetime:format}` (e.g. `{datetime:yyyy-MM-ddTHH:mm:ss}`)
- **Developer & System Tokens**:
  - `{guid}` / `{uuid}` (with `{guid:upper}`, `{guid:N}`, `{guid:B}`)
  - `{username}` / `{user}`, `{machine}` / `{computer}`
  - `{env:VAR_NAME}` for Windows environment variables
  - `{random:min,max}` or `{random:opt1,opt2}`
- **Context & Clipboard Tokens**:
  - `{active_window}` (target window title) and `{active_process}` (target executable)
  - `{clipboard}` (with modifiers: `{clipboard:trim}`, `{clipboard:upper}`, `{clipboard:urlencode}`)
  - `{cursor}` (caret positioning post-expansion)
- **Interactive Prompts with Defaults**:
  - `{text:Label|Default}`, `{multiline:Label|Default}`, `{choice:Label|Opt1=v1*,Opt2=v2}`, `{number:Label|min,max|default}`, `{date_picker:Label|Format}` (e.g. `{date_picker:Due Date|MM/dd/yyyy}`)

### 🛡 Process & Context Filters
- Restrict actions to run only within specific applications (or exclude them).
- High-contrast tag pill interface with inline double-click editing and file drag-and-drop support.

### ⚙ Enterprise-Grade Serilog Logging & Settings
- Dynamic runtime log level switching (`Verbose`, `Debug`, `Information`, `Warning`, `Error`, `Fatal`) with no restart required.
- Daily rolling file sink with configurable retention days (1–90 days).
- System tray management with quick "Snooze Hotkeys" toggle.

### 📦 Standalone Single-File Installer
- Packaged with Inno Setup into a clean, non-administrative `TriggerPointSetup.exe` (installs to `%LOCALAPPDATA%\Programs\TriggerPoint` without UAC prompts).

---

## Quick Start

### Installation
Download the latest `TriggerPointSetup.exe` from the [Releases](https://github.com/Rapscallion0/TriggerPoint/releases) page and run the installer.

### Building from Source

#### Prerequisites
- [.NET 9.0 SDK](https://dotnet.microsoft.com/download/dotnet/9.0) (x64)
- Windows 10 / 11

#### Build & Run
```powershell
# Clone the repository
git clone https://github.com/Rapscallion0/TriggerPoint.git
cd TriggerPoint

# Build the solution
dotnet build TriggerPoint.slnx

# Run all tests
dotnet test TriggerPoint.slnx

# Launch TriggerPoint
dotnet run --project src/TriggerPoint.UI
```

#### Package Installer (`TriggerPointSetup.exe`)
To package the app into a standalone installer:
```powershell
powershell -ExecutionPolicy Bypass -File build/package.ps1 -AppVersion "2.0.4"
```
The output installer will be produced at `artifacts/TriggerPointSetup.exe`.

---

## Solution Architecture

TriggerPoint adheres to clean separation of concerns:

```
TriggerPoint/
├── src/
│   ├── TriggerPoint.Core/            # Domain models, contracts, and core service interfaces
│   ├── TriggerPoint.Infrastructure/  # Win32 hooks, atomic JSON persistence, and Serilog logging
│   └── TriggerPoint.UI/              # Modern WPF UI, Tray icon, Hotkey controls, and Dark/Light themes
├── tests/
│   └── TriggerPoint.Tests/           # Unit test suite (xUnit, FluentAssertions)
├── installer/
│   └── TriggerPoint.iss              # Inno Setup installer specification
├── build/
│   └── package.ps1                   # Packaging & build automation script
├── artifacts/
│   └── TriggerPointSetup.exe         # Compiled release installer
└── assets/
    └── TriggerPoint.ico              # Multi-resolution application icon
```

---

## License

TriggerPoint is licensed under the [MIT License](LICENSE).
