# TriggerPoint ⚡

**Precision shortcuts. Instant menus. Zero bloat.**  
*High-performance hotkey daemon, cursor menu launcher & dynamic snippet expander for Windows.*

[![.NET](https://img.shields.io/badge/.NET-9.0--windows-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![Platform](https://img.shields.io/badge/Platform-Windows%2010%20%2F%2011%20x64-0078D6?logo=windows)](https://microsoft.com/windows)
[![Version](https://img.shields.io/badge/Version-v2.1.0-blue.svg)](https://github.com/Rapscallion0/TriggerPoint/releases/tag/v2.1.0)
[![License: MIT](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)
[![Tests](https://img.shields.io/badge/Tests-526%20Passed%20(100%25)-brightgreen)]()

---

## Overview

**TriggerPoint** is a fast, lightweight Windows productivity tool that puts your most frequent actions at your fingertips. Launch apps, run scripts, execute multi-step workflows, paste dynamic snippets, and open custom menus anywhere on your screen using simple system-wide shortcuts.

Designed to stay out of your way, TriggerPoint runs quietly in your system tray with near-zero memory footprint, zero telemetry, and lightning-fast response times.

---

## What's New in v2.1.0 🚀

- **Standalone Portable Edition**:
  - Run TriggerPoint anywhere with zero installation, zero host registry pollution, and zero leftover files.
  - Automatically activates Portable Mode when a local `data/` directory or `portable.dat` sentinel is detected (or via `--portable` / `--data-dir <path>` switches).
  - **Hardware-Independent Secrets Vault**: Replaces machine-tied DPAPI with a self-contained AES-256-GCM encrypted vault (`vault.key`) stored in `data/`, allowing secrets and API keys to move securely across different PCs.
  - **Host Integration Reconciliation**: Proactively monitors for USB drive removal and session logoff, automatically scrubbing temporary Explorer context menu registrations. Includes a standalone `cleanup-host-integration.bat` emergency utility for clean ejection.
- **Interactive First-Run Onboarding & Live Theme Selection**:
  - **Inno Setup Installer**: Added an **"Initial Preferences"** wizard page allowing users to select their preferred appearance theme (*System Default*, *Dark*, *Light*) and choose whether to install starter content before copying files. Upgrades automatically preserve existing user preferences.
  - **Portable Edition**: Introduces a borderless **`FirstRunSetupWindow`** on clean launch featuring real-time live theme preview switching and starter content selection before launching background listeners.
- **Starter Pack & Default Shortcuts Reorganization**:
  - All sample items are now unified under a single top-level **`Starter Pack & Examples`** folder, allowing new users to explore capabilities or completely wipe all sample content with a single click.
  - Includes showcase folders:
    - **`Quick Launcher (Cursor Menu)`** (<kbd>Ctrl</kbd> + <kbd>Alt</kbd> + <kbd>Space</kbd>): Windows Calculator (`1`), Notepad Scratchpad (`2`), and Google Web Search (`3`).
    - **`Text Snippets & Templates`**: Dynamic Timestamp (<kbd>Ctrl</kbd> + <kbd>Alt</kbd> + <kbd>D</kbd>), Git Conventional Commit with interactive prompt modal, Meeting Notes template, and Markdown Code Block clipboard wrapper.
    - **`Automated Workflows`**: Morning Workspace Setup (sequential URL launch, delay, Notepad scratchpad, and toast alert) and Open Temp Directory.
    - **`Keystroke Automation (Macro)`**: Duplicate Line Down (<kbd>Shift</kbd> + <kbd>Alt</kbd> + <kbd>Down</kbd>).
  - **Ergonomic Default Hotkey**: Command Palette default global shortcut changed from `Alt+Space` to **`Ctrl+Shift+Space`**, eliminating collision with the standard Windows window menu.
- **Test Suite Expansion**:
  - Test suite expanded to **526 unit and UI integration tests** (100% passing) covering portable path routing, AES encryption roundtrips, initial setup persistence, and startup reconciliation.

---

## Key Features

### 🔄 Multi-Step Workflows & Script Automation
- **Visual Step Builder**: Chain actions visually without writing code:
  - **Prompt User**: Collect dynamic inputs via single-line text, multiline text, numbers, dropdown choices, or date pickers.
  - **Open URL**: Open web pages in default or specific browsers with custom browser profiles and private window support.
  - **Ensure Directory**: Check folder existence with silent creation, error handling, or interactive create prompts.
  - **Launch App**: Launch executables with custom arguments, working directories, Run as Admin, and multi-monitor display targeting.
  - **Inject Snippet**: Type or paste dynamic template expansions directly into target applications.
  - **Delay**: Configurable pauses in milliseconds.
  - **Execute Action**: Trigger other TriggerPoint actions or open nested folder cursor menus.
  - **Inline JavaScript**: Embed custom script snippets right inside the visual sequence.
- **Full JavaScript Engine (Jint)**:
  - Robust script execution powered by Jint with full access to native `tp` APIs (`tp.prompt()`, `tp.openUrl()`, `tp.launch()`, `tp.fs`, `tp.delay()`, `tp.injectSnippet()`, `tp.executeAction()`, `tp.vars`).
  - Contextual mode switching with safe 3-option re-compile confirmations and per-step script conversion.
  - Dual AvalonEdit code editors with JavaScript syntax highlighting, line numbers, and dark/light theme adaptability.

### ⚡ Precision Global Hotkeys & Conflict Detection
- Fast Win32 `RegisterHotKey` global shortcut registration.
- **Two-Tiered Conflict Detection**: Warns immediately if a shortcut is claimed by another TriggerPoint action or an external system process/Windows utility.
- Interactive hotkey recorder with live visual modifier combination builder.

### 🔍 Spotlight-Style Command Palette & Quick Calculator
- Summon a fast, floating search palette anywhere with a single global shortcut (`Ctrl+Shift+Space` or custom hotkey).
- Fuzzy search across all actions and folders with autocomplete suggestions, sort modes (Alphabetical, Frequency, or Custom Tree Order), and folder breadcrumb paths (`📁 Tools › Development › Edit Hosts`).
- **Instant Math Calculator**: Type arithmetic, percentages, parentheses, powers, and scientific functions directly into the search bar (`=45*1.2`, `15% of $800`, `sqrt(256)`) for real-time calculation and instant clipboard copying.

### ⌨ Shortcut Cheat Sheet & Chord HUD
- **Shortcut Cheat Sheet HUD (`Ctrl+Shift+/`)**: Instantly view all shortcuts across all folders in a sleek, non-intrusive floating HUD with real-time filtering.
- **Chord Indicator HUD**: Clean visual HUD for multi-key chords (`Ctrl+K, ...`), showing pending leaders and next valid keystrokes.

### 📋 Floating Cursor Menus & Folder Launchers
- Launch grouped actions directly under your mouse cursor with custom accelerator keys.
- Organize shortcuts into folders and hierarchical launcher menus.
- **Sequential Quick-Keys (`1–9`, `A–Z`)**: Auto-numbers popup menus up to 35 direct access keys with `Off`, `Smart Fill`, and `Strict Positional` modes.

### 📝 Dynamic Snippets, Plain Text & Rich Text Expansion
- **Rich Text & Plain Text Modes**: Create rich text snippets with bold, italics, underline, strikethrough, custom text & highlight colors, font size presets, bulleted/numbered lists, and alignments.
- **Theme-Agnostic Engine**: Unstyled text exports as host-neutral `\cf0` in RTF and clean color-free HTML, ensuring pasted text perfectly matches the receiving application's dark or light theme.
- **True WYSIWYG Paper Canvas**: Editor defaults to a paper white document canvas with synchronized live preview.
- Send keystrokes or clipboard-injected templates directly into the focused window.
- **Top-Aligned Multiline Editing**: Clean top vertical alignment across snippet boxes, multiline inputs, and prompt dialogs.
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

### 🔒 Dual Secrets Vault Architecture (Windows DPAPI & Portable AES-256)
- Secure sensitive tokens, API keys, and passwords within workflow variables at rest.
- **Installed Mode**: Leverages hardware-backed Windows Data Protection API (`vault:dpapi:...`).
- **Portable Mode**: Uses a standalone, machine-independent AES-256-GCM encrypted vault (`vault.key`) stored in `data/`, allowing encrypted credentials to travel across PCs.

### 💼 Standalone Portable Edition (Zero Host Pollution)
- Run directly from thumb drives, network shares, or temporary folders without installation.
- Self-contained storage for all configurations, actions, backups, and logs in the local `data/` directory.
- Proactively cleans up Explorer context menu registrations upon USB ejection or session exit, and includes an emergency `cleanup-host-integration.bat` utility.

### 🎯 Window & Browser Target Crosshair Tool
- Drag an interactive crosshair target onto any open application window to automatically extract its executable path and process name.
- **Active Browser Tab URL Filter**: Detects when targeting Chromium/Firefox browsers and prompts to capture and filter on the current URL pattern.

### 🛡 Process & Context Filters
- Restrict actions to run only within specific applications (or exclude them).
- High-contrast tag pill interface with inline double-click editing and file drag-and-drop support.

### ⚙ Enterprise-Grade Serilog Logging & Settings
- Dynamic runtime log level switching (`Verbose`, `Debug`, `Information`, `Warning`, `Error`, `Fatal`) with no restart required.
- Daily rolling file sink with configurable retention days (1–90 days).
- System tray management with quick "Snooze Hotkeys" toggle.

### 🔄 In-App Update Checker & Auto-Updater
- **Integrated GitHub Releases Engine**: Automatically queries GitHub for newly published releases and release notes.
- **Configurable Cadence**: Check on application startup, daily, weekly, or manual only.
- **Full Markdown Release Notes**: Read complete changelogs, version numbers, and file sizes directly in the update dialog.
- **One-Click In-App Upgrade**: Download and verify the setup installer with real-time progress, automated SHA-256 hash checks, silent installation, and automatic application restart.
- **Contextual Actions**: Trigger on-demand checks from the System Tray menu or Application Settings at any time.

### 📦 Dual-Scope Single-File Installer
- Packaged with Inno Setup into a clean `TriggerPointSetup.exe`.
- Supports standard **Per-User** install (installs to `%LOCALAPPDATA%\Programs\TriggerPoint` without UAC prompts) or **All-Users** machine-wide installation.
- Features an **Initial Preferences** setup page to select theme and starter content during installation.

---

## Quick Start

### Installation & Deployment

#### Option A: Inno Setup Installer (Recommended)
Download `TriggerPointSetup.exe` from the [Releases](https://github.com/Rapscallion0/TriggerPoint/releases) page and run the installer. Choose between per-user (no admin rights needed) or machine-wide installation.

#### Option B: Standalone Portable Edition (Zero Installation)
Download `TriggerPoint-v{version}-Portable-win-x64.zip` from [Releases](https://github.com/Rapscallion0/TriggerPoint/releases), extract to any directory or USB drive, and launch `TriggerPoint.exe`.

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

# Run all unit tests
dotnet test TriggerPoint.slnx

# Launch TriggerPoint
dotnet run --project src/TriggerPoint.UI
```

#### Package Installer & Portable Archive
To package the app into both the Inno Setup installer and the standalone portable zip:
```powershell
powershell -ExecutionPolicy Bypass -File build/package.ps1 -AppVersion "2.1.0"
```
The output artifacts will be produced in the `artifacts/` directory:
- `artifacts/TriggerPointSetup.exe`
- `artifacts/TriggerPoint-v2.1.0-Portable-win-x64.zip`

---

## Solution Architecture

TriggerPoint adheres to clean separation of concerns:

```
TriggerPoint/
├── src/
│   ├── TriggerPoint.Core/            # Domain models, workflow compiler, and service contracts
│   ├── TriggerPoint.Infrastructure/  # Win32 hooks, atomic JSON persistence, and Serilog logging
│   └── TriggerPoint.UI/              # Modern WPF UI, Tray daemon, Hotkey recorder, and Themes
├── tests/
│   └── TriggerPoint.Tests/           # Unit & UI test suite (526 tests: xUnit, FluentAssertions)
├── installer/
│   └── TriggerPoint.iss              # Inno Setup dual-scope installer specification
├── build/
│   ├── package.ps1                   # Packaging & Inno Setup automation script
│   └── set-version.ps1               # Centralized version synchronization script
├── artifacts/
│   ├── TriggerPointSetup.exe         # Compiled release installer
│   └── TriggerPoint-*-Portable-*.zip # Standalone portable package
└── assets/
    ├── TriggerPoint.ico              # Multi-resolution dark theme application icon
    └── TriggerPoint.Light.ico        # Multi-resolution light theme application icon
```

---

## License

TriggerPoint is licensed under the [MIT License](LICENSE).
