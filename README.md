# TriggerPoint ⚡

**Precision shortcuts. Instant menus. Zero bloat.**  
*High-performance hotkey daemon, cursor menu launcher & dynamic snippet expander for Windows.*

[![.NET](https://img.shields.io/badge/.NET-9.0--windows-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![Platform](https://img.shields.io/badge/Platform-Windows%2010%20%2F%2011%20x64-0078D6?logo=windows)](https://microsoft.com/windows)
[![License: MIT](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)
[![Tests](https://img.shields.io/badge/Tests-36%20Passed%20(100%25)-brightgreen)]()

---

## Overview

**TriggerPoint** is a lightweight, zero-bloat Windows automation daemon that pairs low-latency global hotkeys with cursor-anchored launcher menus, dynamic text expansion snippets, and fine-grained process/URL contextual execution rules.

Built with native Win32 input hooks and WPF, TriggerPoint uses minimal system resources while providing keyboard-first efficiency.

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
- Built-in dynamic token chips:
  - `{clip}`: Current clipboard text
  - `{date}` / `{time}` / `{datetime}`: Localized timestamps
  - `{guid}`: Fresh unique identifier
  - `{selection}`: Active text selection

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
powershell -ExecutionPolicy Bypass -File build/package.ps1
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
└── assets/
    └── TriggerPoint.ico              # Multi-resolution application icon
```

---

## License

TriggerPoint is licensed under the [MIT License](LICENSE).
