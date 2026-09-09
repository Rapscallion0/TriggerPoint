using System;

namespace TriggerPoint.Core.Models;

[Flags]
public enum ModifierKeys
{
    None = 0,
    Alt = 1,
    Control = 2,
    Shift = 4,
    Windows = 8
}

public enum PresentationMode
{
    Direct = 0,
    CursorMenu = 1,
    CommandPalette = 2
}

public enum ActionType
{
    Shell = 0,
    Snippet = 1,
    Folder = 2
}

public enum HotkeyConflictType
{
    None = 0,
    Internal = 1,
    External = 2
}
