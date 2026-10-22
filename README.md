# TriggerPoint ⚡

**Precision shortcuts. Instant menus. Zero bloat.**  
*High-performance hotkey daemon, cursor menu launcher & dynamic snippet expander for Windows.*

[![.NET](https://img.shields.io/badge/.NET-9.0--windows-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![Platform](https://img.shields.io/badge/Platform-Windows%2010%20%2F%2011%20x64-0078D6?logo=windows)](https://microsoft.com/windows)
[![Version](https://img.shields.io/badge/Version-v2.2.7-blue.svg)](https://github.com/Rapscallion0/TriggerPoint/releases/tag/v2.2.7)
[![License: MIT](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)
[![Tests](https://img.shields.io/badge/Tests-857%20Passed%20(100%25)-brightgreen)]()

---

## Overview

**TriggerPoint** is a fast, lightweight Windows productivity tool that puts your most frequent actions at your fingertips. Launch apps, run scripts, execute multi-step workflows, paste dynamic snippets, and open custom menus anywhere on your screen using simple system-wide shortcuts.

Designed to stay out of your way, TriggerPoint runs quietly in your system tray with near-zero memory footprint, zero telemetry, and lightning-fast response times.

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
- **Unified 'All' Search**: Typing a search query under the 'All' filter simultaneously searches actions, clipboard clips, and Windows services, automatically grouping results under clean category headers (`ACTIONS`, `CLIPBOARD HISTORY`, `WINDOWS SERVICES`) with configurable inclusion toggles and customizable result limits in Settings.
- **Advanced Search Operators & Syntax**:
  - **Implicit AND**: Space-separated terms match items containing all words (e.g. `rocky mavericks`).
  - **Logical OR (`OR`, `|`)**: Matches either search condition (e.g. `invoice OR receipt`, `report | memo`).
  - **Exclusion / NOT (`-term`, `NOT term`)**: Rejects items containing unwanted words (e.g. `financial -draft`, `memo NOT archive`).
  - **Exact Phrase (`"phrase"`)**: Matches contiguous word sequences enclosed in double quotes (e.g. `"due date"`).
  - **Prefix Anchor (`^term`)**: Matches items starting with the specified term (e.g. `^calc`).
- **Smart Context Excerpts & Search Highlighting**: When searching through long text or clipboard history, relevant match excerpts are dynamically generated on secondary lines and highlighted in both the search list and bottom preview bar with theme-aware accent colors.
- **Instant Math Calculator**: Type arithmetic, percentages, parentheses, powers, and scientific functions directly into the search bar (`=45*1.2`, `15% of $800`, `sqrt(256)`) for real-time calculation and instant clipboard copying.

### ⌨ Shortcut Cheat Sheet & Chord HUD
- **Shortcut Cheat Sheet HUD (`Ctrl+Shift+/`)**: Instantly view all shortcuts across all folders in a sleek, non-intrusive floating HUD with real-time filtering.
- **Chord Indicator HUD**: Clean visual HUD for multi-key chords (`Ctrl+K, ...`), showing pending leaders and next valid keystrokes.

### 📋 Floating Cursor Menus & Folder Launchers
- Launch grouped actions directly under your mouse cursor with custom accelerator keys.
- Organize shortcuts into folders and hierarchical launcher menus.
- **Sequential Quick-Keys (`1–9`, `A–Z`)**: Auto-numbers popup menus up to 35 direct access keys with `Off`, `Smart Fill`, and `Strict Positional` modes.

### 📋 Smart Clipboard History & Quick Paste (`Ctrl+Shift+V`)
- **System-Wide Win32 Clipboard Monitor**: Captures clipboard updates in real time with high performance and zero system latency.
- **Rich Multi-Format Support**: Preserves Plain Text, Rich Text (RTF), HTML formatting, and Images (PNG/BMP) with thumbnail previews.
- **Instant Search & Direct Paste**: Filter clippings in real time; press `Enter` to automatically paste the selected clip directly into the previous active application, or `Ctrl+C` to copy back to clipboard.
- **Pinned Favorites**: Star/pin frequently needed templates, code snippets, or account IDs to prevent them from rolling off history.
- **Privacy & Password Manager Protection**:
  - Automatically respects standard Windows clipboard security flags (`Clipboard Viewer Ignore`, `CanIncludeInClipboardHistory = 0`, `ExcludeClipboardContentFromMonitorProcessing`).
  - Automatically suppresses clipboard capture from popular password managers (1Password, Bitwarden, KeePass, KeePassXC, LastPass, Dashlane, Enpass).
- **Flexible Retention & Storage Controls**: Configurable history capacity (5–500 items, default 50), retention duration (1–365 days), and maximum item size (1–50 MB) persisted in atomic JSON storage.

### ⚡ Inline Abbreviation Expander / Live Text Expander
- **Instant As-You-Type Expansion**: Automatically expands trigger abbreviations across any Windows application (e.g. typing `;email` or `/sig` instantly replaces with your dynamic template or rich snippet).
- **Low-Level Precision Hooking**: Seamlessly intercepts keystrokes, calculates replacement length, and performs atomic backspace and insertion with automatic caret placement (`{cursor}`).
- **Case-Sensitivity Control**: Toggle between strict case-sensitive triggers or case-insensitive matching.
- **Safe Environment Guardrails**:
  - Automatically excludes remote desktop and terminal sessions by default (`mstsc.exe`, `vmconnect.exe`, `putty.exe`) with customizable process lists.
  - Automatically suppresses expansion during full-screen games to avoid accidental activation.
- **Audio & Visual Feedback**: Optional subtle audible cues (clicks, pops, muted beeps) and visual indicators upon successful expansion.

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
- **Integrated GitHub Releases Engine**: Automatically queries GitHub for newly published releases, metadata, and full Markdown release notes.
- **Intelligent Startup Notifications**:
  - Prompts with the full interactive `UpdateAvailableDialog` upon manual application launch.
  - Keeps notifications unobtrusive during Windows autostart (`--autostart`) with interactive, persistent toasts containing a direct "Review & Update" action.
  - Displays a clean update banner at the top of the Command Palette when an update is pending.
- **Configurable Cadence**: Check on application startup, daily, weekly, or manual only.
- **One-Click In-App Upgrade**: Download and verify the setup installer with real-time progress, automated SHA-256 hash checks, silent installation, and automatic application restart.
- **Instant Tray Feedback**: Trigger on-demand checks from the System Tray menu or Application Settings with immediate progress HUD feedback.

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
powershell -ExecutionPolicy Bypass -File build/package.ps1 -AppVersion "2.2.7"
```
The output artifacts will be produced in the `artifacts/` directory:
- `artifacts/TriggerPointSetup.exe`
- `artifacts/TriggerPoint-v2.2.6-Portable-win-x64.zip`

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
│   └── TriggerPoint.Tests/           # Unit & UI test suite (839 tests: xUnit, FluentAssertions)
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
