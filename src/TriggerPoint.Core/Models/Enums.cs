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
    Folder = 2,
    Workflow = 3
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
    ExecuteAction = 7
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
