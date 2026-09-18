# TriggerPoint ⚡

**Precision shortcuts. Instant menus. Zero bloat.**  
*High-performance hotkey daemon, cursor menu launcher & dynamic snippet expander for Windows.*

[![.NET](https://img.shields.io/badge/.NET-9.0--windows-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![Platform](https://img.shields.io/badge/Platform-Windows%2010%20%2F%2011%20x64-0078D6?logo=windows)](https://microsoft.com/windows)
[![Version](https://img.shields.io/badge/Version-v2.0.9-blue.svg)](https://github.com/Rapscallion0/TriggerPoint/releases/tag/v2.0.9)
[![License: MIT](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)
[![Tests](https://img.shields.io/badge/Tests-383%20Passed%20(100%25)-brightgreen)]()

---

## Overview

**TriggerPoint** is a fast, lightweight Windows productivity tool that puts your most frequent actions at your fingertips. Launch apps, run scripts, execute multi-step workflows, paste dynamic snippets, and open custom menus anywhere on your screen using simple system-wide shortcuts.

Designed to stay out of your way, TriggerPoint runs quietly in your system tray with near-zero memory footprint, zero telemetry, and lightning-fast response times.

---

## What's New in v2.0.9 🚀

- **Rich Text Snippets & Visual Formatting Ribbon**:
  - Full support for Rich Text snippets with dual Plain Text and Rich Text editor switching.
  - Visual formatting ribbon featuring font size presets (`9pt` to `18pt`), Bold (`Ctrl+B`), Italic (`Ctrl+I`), Underline (`Ctrl+U`), Strikethrough, Text Color with custom color picker flyout, Text Highlight, Bulleted Lists, Numbered Lists, Left/Center/Right alignments, and Clear Formatting (`Ctrl+\`).
  - **Theme-Agnostic Export**: Unstyled text automatically exports as neutral `\cf0` in RTF and clean color-neutral HTML. Snippets pasted into destination apps (Word, Outlook, Google Docs, Slack, browsers) seamlessly match the receiving application's typography and dark/light themes without baking in TriggerPoint's dark mode colors.
  - **Paper Canvas View**: Rich text editor defaults to a crisp `#FFFFFF` document paper canvas with `#1E293B` text and synchronized live expansion preview, with a single-click ribbon toggle to switch to theme background if desired.
  - **Comprehensive Clear Formatting**: Cleanly removes all styles, flattens bulleted and numbered lists into regular paragraphs, and resets alignment to left across selected text or the entire document.
- **Spotlight Quick Calculator in Command Palette**:
  - Type math expressions directly into the Command Palette (e.g., `=2+2`, `50*1.12`, `(120+45)*0.8`, `sqrt(144)`, `15% of 240`) for instant evaluation.
  - Press Enter to copy the calculated result directly to your clipboard or inject it into your active application.
- **Shortcut Cheat Sheet HUD (`Ctrl+Shift+/`)**:
  - Floating high-contrast shortcut HUD displays all registered shortcuts, leaders, and actions grouped by folder or context.
  - Instant real-time filtering, keyboard navigation, and direct trigger execution.
- **Chorded Hotkey Indicator HUD**:
  - Clean floating indicator HUD when typing multi-key shortcut chords (e.g., `Ctrl+K, ...`), displaying pending chords and available secondary key options.
- **Windows DPAPI Encrypted Secrets Vault**:
  - Securely store sensitive tokens, passwords, and API keys within workflow variables with hardware-tied Windows Data Protection API (DPAPI) encryption at rest (`vault:dpapi:...`).
- **Seamless GitHub Application Updates**:
  - Integrated GitHub update engine with automatic background checks, release notes preview dialog, and one-click installer downloads.
- **Floating HUD Transparency Polish**:
  - Eliminated legacy DWM non-client frame artifacts (grey box borders) on transparent layered HUD windows, delivering pixel-perfect rounded corners and soft floating drop shadows across Windows 10 and Windows 11.

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
- Summon a fast, floating search palette anywhere with a single global shortcut (`Ctrl+Space` or custom hotkey).
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

### 🔒 Hardware-Backed Secrets Vault (DPAPI)
- Secure sensitive tokens, API keys, and passwords within workflow variables using Windows DPAPI encryption at rest.

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

### 📦 Dual-Scope Single-File Installer
- Packaged with Inno Setup into a clean `TriggerPointSetup.exe`.
- Supports standard **Per-User** install (installs to `%LOCALAPPDATA%\Programs\TriggerPoint` without UAC prompts) or **All-Users** machine-wide installation.

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

# Run all unit tests
dotnet test TriggerPoint.slnx

# Launch TriggerPoint
dotnet run --project src/TriggerPoint.UI
```

#### Package Installer (`TriggerPointSetup.exe`)
To package the app into a standalone installer:
```powershell
powershell -ExecutionPolicy Bypass -File build/package.ps1 -AppVersion "2.0.9"
```
The output installer will be produced at `artifacts/TriggerPointSetup.exe`.

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
│   └── TriggerPoint.Tests/           # Unit test suite (186 tests: xUnit, FluentAssertions)
├── installer/
│   └── TriggerPoint.iss              # Inno Setup dual-scope installer specification
├── build/
│   ├── package.ps1                   # Packaging & Inno Setup automation script
│   └── set-version.ps1               # Centralized version synchronization script
├── artifacts/
│   └── TriggerPointSetup.exe         # Compiled release installer
└── assets/
    ├── TriggerPoint.ico              # Multi-resolution dark theme application icon
    └── TriggerPoint.Light.ico        # Multi-resolution light theme application icon
```

---

## License

TriggerPoint is licensed under the [MIT License](LICENSE).
