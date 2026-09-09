# TriggerPoint Development Rules & AI Pair-Programming Policy

## 1. Build, Verification & Packaging Policy
- **Routine Verification:**
  - Verify changes using `dotnet build TriggerPoint.slnx` and `dotnet test TriggerPoint.slnx`.
  - Strive for zero warnings and zero errors.
- **No Automatic Release / Installer Packaging:**
  - Do **NOT** execute `build/package.ps1` or `dotnet publish -c Release` automatically at the end of a task or bug fix.
  - Only run packaging when the user explicitly requests it (e.g., *"Package it"*, *"Build the installer"*, or *"Create release"*).
- **End-of-Implementation Packaging Reminder:**
  - At the conclusion of each completed feature or implementation turn, include a brief footer/tip reminding the user that a Release build / installer (`artifacts/TriggerPointSetup.exe`) has not been updated yet, prompting them to say *"Package it"* if they want an updated setup executable.

## 2. Coding & UI Guidelines
- Maintain zero-bloat, lightweight startup performance.
- Preserve full Dark & Light mode theme adaptability using semantic tokens in `ThemeManager.cs` and dynamic resources.
- Global shortcuts must always avoid unhandled conflicts: when duplicating actions, ensure the cloned action clears its `Hotkey` to prevent immediate collisions.
- Keep all unit tests passing (`dotnet test`).
