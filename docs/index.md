# TriggerPoint User Guide & Documentation ⚡

Welcome to the official end-user documentation for **TriggerPoint**, the high-performance hotkey daemon, cursor menu launcher, dynamic snippet expander, and workflow automation platform for Windows 10 and Windows 11.

---

## Executive Summary

TriggerPoint is engineered to eliminate repetitive desktop friction without introducing system bloat. It runs quietly in the Windows notification area (system tray) with near-zero idle resource utilization, zero cloud telemetry, and microsecond response times.

Whether launching frequently used executables, pasting dynamic text templates with live date and clipboard calculations, summoning cursor-anchored quick-key menus, or executing multi-step automation sequences powered by conditional branching and JavaScript, TriggerPoint centralizes desktop productivity into a unified, responsive interface.

![TriggerPoint Main Interface Overview](images/index-hero-overview.png)
<!-- SCREENSHOT REQUIRED: Main Settings Window in default Dark theme showing the action sidebar tree with populated items (Folders, Shell Apps, Rich Snippets, Workflows), active action details panel on the right, top action toolbar (Save, Revert, Test, Duplicate, Delete, Add), and bottom status bar. -->

---

## Documentation Architecture

This user guide is divided into modular, task-oriented topics. Explore each section below:

| Documentation Section | Description & Focus |
| :--- | :--- |
| [**Getting Started**](getting-started.md) | System requirements, installation (Per-User vs All-Users), first-time startup, tray icon orientation, and initial default configuration. |
| [**Interface Overview**](interface-overview.md) | In-depth breakdown of the main configuration window, sidebar tree hierarchy, action toolbar, status indicators, HUD overlays, and window crosshair targeting. |
| [**Core Workflows**](core-workflows.md) | Comprehensive guides for daily tasks: launching applications, creating rich and plain text snippets, dynamic token substitution, Spotlight Command Palette, quick calculator, cursor menus, and application-specific process filtering. |
| [**Workflows & Automation**](workflows-and-scripts.md) | Advanced multi-step visual workflows, conditional branching (`IfCondition`), drag-and-drop reordering, DPAPI encrypted secrets vault, Jint JavaScript engine integration, and workflow debugging. |
| [**Application Configuration**](configuration.md) | Complete reference for themes, global hotkeys, toast notifications, window placement, backup and automatic failover recovery, Serilog runtime logging, and the integrated GitHub update checker. |
| [**Troubleshooting & Diagnostics**](troubleshooting.md) | Resolving internal and system hotkey conflicts, missing executable target validation, script runtime error diagnosis, log inspection, and configuration disaster recovery. |
| [**Screenshot Checklist**](screenshot-checklist.md) | Master inventory of all required visual artifacts, capture states, sample data, and control focus requirements across the documentation suite. |

---

## Quick Navigation by User Task

Find immediate answers based on what you are trying to accomplish:

### 🚀 "I want to set up my first shortcut to open an app or document"
- Review [Getting Started](getting-started.md#first-launch--quick-start) for initial hotkey registration.
- Follow [Creating an App & Command Action](core-workflows.md#1-creating-app--command-actions) to configure file paths, arguments, and multi-monitor targeting.

### 📝 "I want to paste rich or formatted text snippets with today's date"
- See [Creating Rich Text Snippets](core-workflows.md#2-plain-text--rich-text-snippets) to explore the visual formatting ribbon and Paper Canvas view.
- Consult the [Dynamic Snippet Tokens Reference](core-workflows.md#dynamic-tokens--placeholder-syntax) for date offsets, clipboard transforms, and interactive prompt tokens.

### 🔍 "I want to search all my shortcuts or compute a quick math calculation"
- Learn about the [Spotlight Command Palette](core-workflows.md#3-spotlight-command-palette--quick-calculator) (`Alt+Space`).
- Read how to evaluate math formulas on the fly in [Instant Math Calculator](core-workflows.md#instant-math-calculator).

![Command Palette and Calculator](images/index-command-palette.png)
<!-- SCREENSHOT REQUIRED: Command Palette window floating over the desktop with the search box populated with "=45 * 1.15", showing the calculated result preview card below and "Press Enter to copy result" instruction badge. -->

### 🔀 "I want to build an automation workflow that checks a file or asks for input"
- Read [Visual Step Builder](workflows-and-scripts.md#visual-step-builder) to chain prompts, delays, file checks, and snippets.
- Master [Conditional Branching (`IfCondition`)](workflows-and-scripts.md#conditional-branching-ifcondition) to create `THEN` and `ELSE` logical execution branches.
- Store sensitive API keys safely using the [Windows DPAPI Secrets Vault](workflows-and-scripts.md#workflow-variables--hardware-backed-secrets-vault).

### ⚠ "My hotkey has a yellow warning or TriggerPoint says there is a conflict"
- Review [Conflict Resolution Guide](troubleshooting.md#1-hotkey-conflicts--collision-handling) to understand the difference between internal TriggerPoint collisions and external Windows system hotkey reservations.
- Fix missing files with [Broken Executable Target Warnings](troubleshooting.md#2-broken-executable-target-warnings).

---

## Technical Specifications

- **Target OS**: Windows 10 (version 1809 / build 17763 or newer) and Windows 11 (all editions, x64).
- **Runtime**: Native .NET 9.0 Desktop Runtime (WPF, XAML, C# 13).
- **Execution Architecture**: Native Win32 hooks (`RegisterHotKey`, low-level mouse hooks, Win32 window handles).
- **Storage Model**: Atomic transactional JSON serialization (`%LOCALAPPDATA%\TriggerPoint\config\triggers.json`) with automated rolling `.bak` snapshots.
- **Packaging**: Single-file standalone installer compiled with Inno Setup. Supports seamless background in-app updates via GitHub Releases API.
