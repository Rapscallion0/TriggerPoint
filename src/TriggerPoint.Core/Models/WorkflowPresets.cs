using System;
using System.Collections.Generic;

namespace TriggerPoint.Core.Models;

public sealed record WorkflowPreset
{
    public string Id { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<WorkflowStep> Steps { get; set; } = [];

    public WorkflowPreset() { }

    public WorkflowPreset(string id, string category, string title, string description, List<WorkflowStep> steps)
    {
        Id = id;
        Category = category;
        Title = title;
        Description = description;
        Steps = steps;
    }

    public WorkflowPreset DeepClone()
    {
        return new WorkflowPreset
        {
            Id = Id,
            Category = Category,
            Title = Title,
            Description = Description,
            Steps = Steps?.ConvertAll(s => s.Clone()) ?? []
        };
    }
}

public static class WorkflowPresets
{
    public const string CategoryGeneral = "General & Everyday Productivity";
    public const string CategoryDeveloper = "Developer & Advanced Workspaces";

    public static IReadOnlyList<WorkflowPreset> GetAll()
    {
        return
        [
            // General Category
            new WorkflowPreset(
                "web_search",
                CategoryGeneral,
                "Google / Web Search",
                "Prompt for a search term and open results in your default browser",
                [
                    new WorkflowStep
                    {
                        StepType = WorkflowStepType.Prompt,
                        Name = "Prompt Search Query",
                        VariableName = "query",
                        PromptLabel = "Search Query",
                        PromptDefaultValue = string.Empty,
                        OnError = StepErrorPolicy.StopWorkflow
                    },
                    new WorkflowStep
                    {
                        StepType = WorkflowStepType.OpenUrl,
                        Name = "Open Google Search",
                        Url = "https://www.google.com/search?q={query}",
                        OnError = StepErrorPolicy.StopWorkflow
                    }
                ]),

            new WorkflowPreset(
                "morning_routine",
                CategoryGeneral,
                "Morning Workstation Routine",
                "Open your webmail, company calendar, and dashboard simultaneously",
                [
                    new WorkflowStep
                    {
                        StepType = WorkflowStepType.OpenUrl,
                        Name = "Open Email",
                        Url = "https://mail.google.com",
                        OnError = StepErrorPolicy.Continue
                    },
                    new WorkflowStep
                    {
                        StepType = WorkflowStepType.OpenUrl,
                        Name = "Open Calendar",
                        Url = "https://calendar.google.com",
                        OnError = StepErrorPolicy.Continue
                    },
                    new WorkflowStep
                    {
                        StepType = WorkflowStepType.OpenUrl,
                        Name = "Open News / Portal",
                        Url = "https://news.ycombinator.com",
                        OnError = StepErrorPolicy.Continue
                    }
                ]),

            new WorkflowPreset(
                "quick_email",
                CategoryGeneral,
                "Quick Email Composer",
                "Prompt for recipient and subject, then open your default Windows mail client",
                [
                    new WorkflowStep
                    {
                        StepType = WorkflowStepType.Prompt,
                        Name = "Prompt Recipient",
                        VariableName = "recipient",
                        PromptLabel = "Recipient Email Address",
                        PromptDefaultValue = string.Empty,
                        OnError = StepErrorPolicy.StopWorkflow
                    },
                    new WorkflowStep
                    {
                        StepType = WorkflowStepType.Prompt,
                        Name = "Prompt Subject",
                        VariableName = "subject",
                        PromptLabel = "Email Subject",
                        PromptDefaultValue = string.Empty,
                        OnError = StepErrorPolicy.StopWorkflow
                    },
                    new WorkflowStep
                    {
                        StepType = WorkflowStepType.OpenUrl,
                        Name = "Open Mail Client",
                        Url = "mailto:{recipient}?subject={subject}",
                        OnError = StepErrorPolicy.StopWorkflow
                    }
                ]),

            new WorkflowPreset(
                "client_folder",
                CategoryGeneral,
                "Client / Project Folder Finder",
                "Prompt for client name, ensure the folder exists, and reveal in Windows Explorer",
                [
                    new WorkflowStep
                    {
                        StepType = WorkflowStepType.Prompt,
                        Name = "Prompt Client Name",
                        VariableName = "client",
                        PromptLabel = "Client or Account Name",
                        PromptDefaultValue = string.Empty,
                        OnError = StepErrorPolicy.StopWorkflow
                    },
                    new WorkflowStep
                    {
                        StepType = WorkflowStepType.EnsureDirectory,
                        Name = "Ensure Client Folder",
                        DirectoryPath = @"%USERPROFILE%\Documents\Clients\{client}",
                        DirectoryMissingPolicy = DirectoryMissingPolicy.PromptToCreate,
                        OpenInExplorer = true,
                        OnError = StepErrorPolicy.StopWorkflow
                    }
                ]),

            new WorkflowPreset(
                "desk_tools",
                CategoryGeneral,
                "Desk Tools (Calculator + Scratchpad)",
                "Open Windows Calculator alongside a quick scratchpad notes file",
                [
                    new WorkflowStep
                    {
                        StepType = WorkflowStepType.LaunchApp,
                        Name = "Launch Calculator",
                        Command = "calc.exe",
                        OnError = StepErrorPolicy.Continue
                    },
                    new WorkflowStep
                    {
                        StepType = WorkflowStepType.EnsureDirectory,
                        Name = "Ensure Notes Directory",
                        DirectoryPath = @"%USERPROFILE%\Documents\Notes",
                        DirectoryMissingPolicy = DirectoryMissingPolicy.CreateSilently,
                        OnError = StepErrorPolicy.Continue
                    },
                    new WorkflowStep
                    {
                        StepType = WorkflowStepType.LaunchApp,
                        Name = "Open Scratchpad",
                        Command = "notepad.exe",
                        Arguments = @"%USERPROFILE%\Documents\Notes\scratchpad.txt",
                        OnError = StepErrorPolicy.Continue
                    }
                ]),

            // Developer & Advanced Category
            new WorkflowPreset(
                "ticket_workspace",
                CategoryDeveloper,
                "Ticket Workspace (Zendesk / Jira + Folder + Editor)",
                "Prompt for ticket ID, open web ticket, verify/create local folder, and launch code editor",
                [
                    new WorkflowStep
                    {
                        StepType = WorkflowStepType.Prompt,
                        Name = "Prompt Ticket Number",
                        VariableName = "ticket",
                        PromptLabel = "Ticket / Issue Number",
                        PromptDefaultValue = string.Empty,
                        OnError = StepErrorPolicy.StopWorkflow
                    },
                    new WorkflowStep
                    {
                        StepType = WorkflowStepType.OpenUrl,
                        Name = "Open Ticket in Browser",
                        Url = "https://locsoftware.zendesk.com/agent/tickets/{ticket}",
                        OnError = StepErrorPolicy.Continue
                    },
                    new WorkflowStep
                    {
                        StepType = WorkflowStepType.EnsureDirectory,
                        Name = "Ensure Ticket Folder",
                        DirectoryPath = @"C:\Tickets\{ticket}",
                        DirectoryMissingPolicy = DirectoryMissingPolicy.PromptToCreate,
                        OpenInExplorer = false,
                        OnError = StepErrorPolicy.StopWorkflow
                    },
                    new WorkflowStep
                    {
                        StepType = WorkflowStepType.LaunchApp,
                        Name = "Open in Sublime / Editor",
                        Command = "subl.exe",
                        Arguments = @"C:\Tickets\{ticket}",
                        OnError = StepErrorPolicy.Continue
                    }
                ]),

            new WorkflowPreset(
                "git_feature",
                CategoryDeveloper,
                "Git Feature Workspace",
                "Prompt for feature branch name, ensure project directory, and launch terminal & IDE",
                [
                    new WorkflowStep
                    {
                        StepType = WorkflowStepType.Prompt,
                        Name = "Prompt Branch Name",
                        VariableName = "branch",
                        PromptLabel = "Feature Branch Name (e.g. feat-auth)",
                        PromptDefaultValue = string.Empty,
                        OnError = StepErrorPolicy.StopWorkflow
                    },
                    new WorkflowStep
                    {
                        StepType = WorkflowStepType.EnsureDirectory,
                        Name = "Ensure Project Directory",
                        DirectoryPath = @"%USERPROFILE%\Projects\{branch}",
                        DirectoryMissingPolicy = DirectoryMissingPolicy.PromptToCreate,
                        OpenInExplorer = false,
                        OnError = StepErrorPolicy.StopWorkflow
                    },
                    new WorkflowStep
                    {
                        StepType = WorkflowStepType.LaunchApp,
                        Name = "Open Code / IDE",
                        Command = "code",
                        Arguments = @"%USERPROFILE%\Projects\{branch}",
                        OnError = StepErrorPolicy.Continue
                    }
                ]),

            new WorkflowPreset(
                "developer_search",
                CategoryDeveloper,
                "Developer Multi-Search / Triage",
                "Prompt for an error or concept and search Google, StackOverflow, and GitHub simultaneously",
                [
                    new WorkflowStep
                    {
                        StepType = WorkflowStepType.Prompt,
                        Name = "Prompt Error Query",
                        VariableName = "error",
                        PromptLabel = "Error Message or Technical Term",
                        PromptDefaultValue = string.Empty,
                        OnError = StepErrorPolicy.StopWorkflow
                    },
                    new WorkflowStep
                    {
                        StepType = WorkflowStepType.OpenUrl,
                        Name = "Google Search",
                        Url = "https://www.google.com/search?q={error}",
                        OnError = StepErrorPolicy.Continue
                    },
                    new WorkflowStep
                    {
                        StepType = WorkflowStepType.OpenUrl,
                        Name = "StackOverflow Search",
                        Url = "https://stackoverflow.com/search?q={error}",
                        OnError = StepErrorPolicy.Continue
                    },
                    new WorkflowStep
                    {
                        StepType = WorkflowStepType.OpenUrl,
                        Name = "GitHub Search",
                        Url = "https://github.com/search?q={error}&type=issues",
                        OnError = StepErrorPolicy.Continue
                    }
                ]),

            new WorkflowPreset(
                "local_dev_server",
                CategoryDeveloper,
                "Local Dev Server Launcher",
                "Prompt for port number, launch development server in terminal, and open in browser",
                [
                    new WorkflowStep
                    {
                        StepType = WorkflowStepType.Prompt,
                        Name = "Prompt Port Number",
                        VariableName = "port",
                        PromptLabel = "Localhost Port",
                        PromptDefaultValue = "3000",
                        OnError = StepErrorPolicy.StopWorkflow
                    },
                    new WorkflowStep
                    {
                        StepType = WorkflowStepType.LaunchApp,
                        Name = "Launch Terminal",
                        Command = "wt.exe",
                        Arguments = "powershell -NoExit -Command \"npm run dev\"",
                        OnError = StepErrorPolicy.Continue
                    },
                    new WorkflowStep
                    {
                        StepType = WorkflowStepType.Delay,
                        Name = "Wait for Server Startup",
                        DelayMs = 1500,
                        OnError = StepErrorPolicy.Continue
                    },
                    new WorkflowStep
                    {
                        StepType = WorkflowStepType.OpenUrl,
                        Name = "Open Localhost in Browser",
                        Url = "http://localhost:{port}",
                        OnError = StepErrorPolicy.Continue
                    }
                ]),

            new WorkflowPreset(
                "ssh_session",
                CategoryDeveloper,
                "Remote Server / SSH Session",
                "Prompt for server hostname or IP and connect in Windows Terminal",
                [
                    new WorkflowStep
                    {
                        StepType = WorkflowStepType.Prompt,
                        Name = "Prompt Server Host",
                        VariableName = "host",
                        PromptLabel = "Remote Host / IP (or alias)",
                        PromptDefaultValue = string.Empty,
                        OnError = StepErrorPolicy.StopWorkflow
                    },
                    new WorkflowStep
                    {
                        StepType = WorkflowStepType.LaunchApp,
                        Name = "Launch SSH in Terminal",
                        Command = "wt.exe",
                        Arguments = "ssh {host}",
                        OnError = StepErrorPolicy.StopWorkflow
                    }
                ])
        ];
    }
}
