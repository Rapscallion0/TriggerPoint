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

public enum FolderAutoNumberMode
{
    Off = 0,
    SmartFill = 1,
    StrictPositional = 2
}

public enum ActionType
{
    Shell = 0,
    Snippet = 1,
    Folder = 2,
    Workflow = 3,
    Macro = 4
}

public enum WorkflowMode
{
    Visual = 0,
    Script = 1
}

public enum WorkflowStepType
{
    Prompt = 0,
    OpenUrl = 1,
    LaunchApp = 2,
    EnsureDirectory = 3,
    InjectSnippet = 4,
    Delay = 5,
    RunScript = 6,
    ExecuteAction = 7,
    Dialog = 8,
    Macro = 9
}

public enum WorkflowDialogButtons
{
    Ok = 0,
    OkCancel = 1,
    YesNo = 2,
    YesNoCancel = 3
}

public enum WorkflowDialogIcon
{
    Information = 0,
    Question = 1,
    Warning = 2,
    Error = 3
}

public enum MacroEventType
{
    KeyDown = 0,
    KeyUp = 1,
    MouseMove = 2,
    MouseDown = 3,
    MouseUp = 4,
    Delay = 5
}

public enum MacroMouseButton
{
    Left = 0,
    Right = 1,
    Middle = 2
}

public enum StepErrorPolicy
{
    StopWorkflow = 0,
    Continue = 1
}

public enum DirectoryMissingPolicy
{
    PromptToCreate = 0,
    CreateSilently = 1,
    Fail = 2
}

public enum HotkeyConflictType
{
    None = 0,
    Internal = 1,
    External = 2
}

public enum CommandPaletteSortMode
{
    Smart = 0,
    Alphabetical = 1,
    MostFrequent = 2,
    Recent = 3,
    ActionTree = 4
}

public enum CommandPaletteFilterType
{
    All = 0,
    App = 1,
    Snippet = 2,
    Workflow = 3,
    Folder = 4,
    Macro = 5
}
