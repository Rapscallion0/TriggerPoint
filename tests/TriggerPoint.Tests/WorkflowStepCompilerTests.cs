using System;
using System.Collections.Generic;
using TriggerPoint.Core.Models;
using TriggerPoint.Core.Services;
using Xunit;

namespace TriggerPoint.Tests;

public class WorkflowStepCompilerTests
{
    [Fact]
    public void CompileToJavaScript_GeneratesScriptForPromptAndUrl()
    {
        var steps = new List<WorkflowStep>
        {
            new()
            {
                StepType = WorkflowStepType.Prompt,
                Name = "Prompt Ticket",
                VariableName = "ticket",
                PromptLabel = "Ticket Number",
                PromptDefaultValue = "123",
                OnError = StepErrorPolicy.StopWorkflow
            },
            new()
            {
                StepType = WorkflowStepType.OpenUrl,
                Name = "Open Zendesk",
                Url = "https://example.com/tickets/{ticket}",
                OnError = StepErrorPolicy.StopWorkflow
            }
        };

        var js = WorkflowStepCompiler.CompileToJavaScript(steps);

        Assert.Contains("const ticket = await tp.prompt(\"Ticket Number\", { default: \"123\" });", js);
        Assert.Contains("tp.vars.ticket = ticket;", js);
        Assert.Contains("if (!ticket) return;", js);
        Assert.Contains("tp.openUrl(`https://example.com/tickets/${tp.vars.ticket ?? '{ticket}'}`);", js);
    }

    [Fact]
    public void CompileToJavaScript_GeneratesDirectoryCheckWithConfirmation()
    {
        var steps = new List<WorkflowStep>
        {
            new()
            {
                StepType = WorkflowStepType.EnsureDirectory,
                Name = "Ensure Folder",
                DirectoryPath = @"D:\Tickets\{ticket}",
                DirectoryMissingPolicy = DirectoryMissingPolicy.PromptToCreate,
                OpenInExplorer = true,
                OnError = StepErrorPolicy.StopWorkflow
            }
        };

        var js = WorkflowStepCompiler.CompileToJavaScript(steps);

        Assert.Contains("tp.fs.exists(", js);
        Assert.Contains("await tp.confirm(", js);
        Assert.Contains("tp.fs.createDirectory(", js);
        Assert.Contains("tp.fs.openInExplorer(", js);
    }

    [Fact]
    public void CompileToJavaScript_GeneratesLaunchAppAndSnippet()
    {
        var steps = new List<WorkflowStep>
        {
            new()
            {
                StepType = WorkflowStepType.LaunchApp,
                Name = "Launch Sublime",
                Command = "subl.exe",
                Arguments = "D:\\Tickets\\{ticket}",
                RunAsAdmin = true,
                OnError = StepErrorPolicy.Continue
            },
            new()
            {
                StepType = WorkflowStepType.InjectSnippet,
                Name = "Inject Notes",
                SnippetTemplate = "Working on ticket #{ticket}",
                OnError = StepErrorPolicy.Continue
            }
        };

        var js = WorkflowStepCompiler.CompileToJavaScript(steps);

        Assert.Contains("tp.launch(`subl.exe`, `D:\\\\Tickets\\\\${tp.vars.ticket ?? '{ticket}'}`, ``, true);", js);
        Assert.Contains("await tp.injectSnippet(`Working on ticket #${tp.vars.ticket ?? '{ticket}'}`);", js);
    }

    [Fact]
    public void CompileToJavaScript_AllBuiltInPresetsCompileSuccessfully()
    {
        var presets = WorkflowPresets.GetAll();
        Assert.NotEmpty(presets);

        foreach (var preset in presets)
        {
            var js = WorkflowStepCompiler.CompileToJavaScript(preset.Steps);
            Assert.False(string.IsNullOrWhiteSpace(js), $"Preset '{preset.Title}' failed to compile.");
            Assert.Contains("// TriggerPoint Automated Workflow Script", js);
        }
    }

    [Fact]
    public void CompileToJavaScript_GeneratesExecuteAction()
    {
        var targetId = Guid.NewGuid();
        var steps = new List<WorkflowStep>
        {
            new()
            {
                StepType = WorkflowStepType.ExecuteAction,
                Name = "Call Another Action",
                TargetItemId = targetId
            }
        };

        var js = WorkflowStepCompiler.CompileToJavaScript(steps);
        Assert.Contains($"await tp.executeAction(\"{targetId}\");", js);
    }

    [Fact]
    public void CompileToJavaScript_GeneratesScriptForPromptNumberAndDatePicker()
    {
        var steps = new List<WorkflowStep>
        {
            new()
            {
                StepType = WorkflowStepType.Prompt,
                Name = "Input Details",
                PromptFields =
                [
                    new WorkflowPromptField
                    {
                        VariableName = "count",
                        Label = "Quantity",
                        Type = TokenType.PromptNumber,
                        MinNumber = 1,
                        MaxNumber = 100,
                        DefaultValue = "5"
                    },
                    new WorkflowPromptField
                    {
                        VariableName = "startDate",
                        Label = "Start Date",
                        Type = TokenType.PromptDatePicker,
                        DateFormat = "MM/dd/yyyy",
                        DefaultValue = "01/01/2026"
                    }
                ]
            }
        };

        var js = WorkflowStepCompiler.CompileToJavaScript(steps);
        Assert.Contains("const count = await tp.prompt(\"Quantity\", { type: \"number\", min: 1, max: 100, default: \"5\" });", js);
        Assert.Contains("const startDate = await tp.prompt(\"Start Date\", { type: \"date\", dateFormat: \"MM/dd/yyyy\", default: \"01/01/2026\" });", js);
        Assert.Contains("tp.vars.count = count;", js);
        Assert.Contains("tp.vars.startDate = startDate;", js);
    }

    [Fact]
    public void CompileToJavaScript_GeneratesOpenUrl_WithNewWindow()
    {
        var steps = new List<WorkflowStep>
        {
            new()
            {
                StepType = WorkflowStepType.OpenUrl,
                Url = "https://example.com",
                OpenInNewWindow = true
            }
        };

        var js = WorkflowStepCompiler.CompileToJavaScript(steps);
        Assert.Contains("tp.openUrl(`https://example.com`, \"\", \"\", true);", js);
    }

    [Fact]
    public void CompileToJavaScript_GeneratesLaunchApp_WithTargetDisplay()
    {
        var steps = new List<WorkflowStep>
        {
            new()
            {
                StepType = WorkflowStepType.LaunchApp,
                Command = "notepad.exe",
                Arguments = "file.txt",
                TargetDisplay = "display:2"
            }
        };

        var js = WorkflowStepCompiler.CompileToJavaScript(steps);
        Assert.Contains("tp.launch(`notepad.exe`, `file.txt`, ``, false, \"display:2\");", js);
    }

    [Fact]
    public void WorkflowStep_Clone_PreservesNewProperties()
    {
        var original = new WorkflowStep
        {
            StepType = WorkflowStepType.LaunchApp,
            Command = "calc.exe",
            TargetDisplay = "cursor",
            OpenInNewWindow = true
        };

        var clone = original.Clone();

        Assert.Equal(original.TargetDisplay, clone.TargetDisplay);
        Assert.Equal(original.OpenInNewWindow, clone.OpenInNewWindow);
    }

    [Fact]
    public void CompileSingleStep_NullStep_ReturnsEmptyString()
    {
        var js = WorkflowStepCompiler.CompileSingleStep(null!);
        Assert.Equal(string.Empty, js);
    }

    [Fact]
    public void CompileSingleStep_Prompt_GeneratesPromptWithoutHeader()
    {
        var step = new WorkflowStep
        {
            StepType = WorkflowStepType.Prompt,
            Name = "Ask Name",
            VariableName = "userName",
            PromptLabel = "Your Name",
            PromptDefaultValue = "Alice"
        };

        var js = WorkflowStepCompiler.CompileSingleStep(step);

        Assert.DoesNotContain("// TriggerPoint Workflow Script", js);
        Assert.Contains("const userName = await tp.prompt(\"Your Name\", { default: \"Alice\" });", js);
        Assert.Contains("tp.vars.userName = userName;", js);
    }

    [Fact]
    public void CompileSingleStep_OpenUrl_GeneratesUrlCall()
    {
        var step = new WorkflowStep
        {
            StepType = WorkflowStepType.OpenUrl,
            Url = "https://example.com/test"
        };

        var js = WorkflowStepCompiler.CompileSingleStep(step);

        Assert.DoesNotContain("// TriggerPoint Workflow Script", js);
        Assert.Equal("tp.openUrl(`https://example.com/test`);", js);
    }

    [Fact]
    public void CompileSingleStep_EnsureDirectory_GeneratesDirectoryCheck()
    {
        var step = new WorkflowStep
        {
            StepType = WorkflowStepType.EnsureDirectory,
            DirectoryPath = @"C:\Projects",
            DirectoryMissingPolicy = DirectoryMissingPolicy.CreateSilently
        };

        var js = WorkflowStepCompiler.CompileSingleStep(step);

        Assert.DoesNotContain("// TriggerPoint Workflow Script", js);
        Assert.Contains("tp.fs.createDirectory(", js);
    }

    [Fact]
    public void CompileSingleStep_LaunchApp_GeneratesLaunch()
    {
        var step = new WorkflowStep
        {
            StepType = WorkflowStepType.LaunchApp,
            Command = "notepad.exe",
            Arguments = "notes.txt",
            TargetDisplay = "cursor"
        };

        var js = WorkflowStepCompiler.CompileSingleStep(step);

        Assert.DoesNotContain("// TriggerPoint Workflow Script", js);
        Assert.Equal("tp.launch(`notepad.exe`, `notes.txt`, ``, false, \"cursor\");", js);
    }

    [Fact]
    public void CompileSingleStep_InjectSnippet_GeneratesInjectSnippet()
    {
        var step = new WorkflowStep
        {
            StepType = WorkflowStepType.InjectSnippet,
            SnippetTemplate = "Hello {name}!"
        };

        var js = WorkflowStepCompiler.CompileSingleStep(step);

        Assert.DoesNotContain("// TriggerPoint Workflow Script", js);
        Assert.Equal("await tp.injectSnippet(`Hello ${tp.vars.name ?? '{name}'}!`);", js);
    }

    [Fact]
    public void CompileSingleStep_Delay_GeneratesDelay()
    {
        var step = new WorkflowStep
        {
            StepType = WorkflowStepType.Delay,
            DelayMs = 250
        };

        var js = WorkflowStepCompiler.CompileSingleStep(step);

        Assert.DoesNotContain("// TriggerPoint Workflow Script", js);
        Assert.Equal("await tp.delay(250);", js);
    }

    [Fact]
    public void CompileSingleStep_RunScript_ReturnsExistingInlineScript()
    {
        var step = new WorkflowStep
        {
            StepType = WorkflowStepType.RunScript,
            InlineScript = "  await tp.delay(100);  "
        };

        var js = WorkflowStepCompiler.CompileSingleStep(step);

        Assert.DoesNotContain("// TriggerPoint Workflow Script", js);
        Assert.Equal("await tp.delay(100);", js);
    }

    [Fact]
    public void CompileSingleStep_ExecuteAction_GeneratesExecuteAction()
    {
        var targetId = Guid.NewGuid();
        var step = new WorkflowStep
        {
            StepType = WorkflowStepType.ExecuteAction,
            TargetItemId = targetId
        };

        var js = WorkflowStepCompiler.CompileSingleStep(step);

        Assert.DoesNotContain("// TriggerPoint Workflow Script", js);
        Assert.Equal($"await tp.executeAction(\"{targetId}\");", js);
    }

    [Fact]
    public void CompileSingleStep_EnsureDirectory_ExportsVariable()
    {
        var step = new WorkflowStep
        {
            StepType = WorkflowStepType.EnsureDirectory,
            DirectoryPath = @"C:\Projects\Work",
            VariableName = "myFolder"
        };

        var js = WorkflowStepCompiler.CompileSingleStep(step);

        Assert.Contains("tp.vars.myFolder = folderPath_", js);
    }

    [Fact]
    public void CompileSingleStep_Dialog_GeneratesConfirmAndExportsResult()
    {
        var step = new WorkflowStep
        {
            StepType = WorkflowStepType.Dialog,
            DialogTitle = "Save Changes",
            DialogMessage = "Do you want to save?",
            DialogButtons = WorkflowDialogButtons.YesNo,
            VariableName = "userChoice",
            OnError = StepErrorPolicy.StopWorkflow
        };

        var js = WorkflowStepCompiler.CompileSingleStep(step);

        Assert.Contains("await tp.confirm(`Do you want to save?`, \"Save Changes\", \"Yes\", \"No\")", js);
        Assert.Contains("tp.vars.userChoice = dlg_", js);
        Assert.Contains("return; // Cancelled at dialog", js);
    }

    [Fact]
    public void CompileSingleStep_Macro_GeneratesRunMacro()
    {
        var step = new WorkflowStep
        {
            StepType = WorkflowStepType.Macro,
            Macro = new MacroPayload
            {
                Events = [
                    new MacroEvent { Type = MacroEventType.KeyDown, KeyCode = 13, KeyName = "Enter" },
                    new MacroEvent { Type = MacroEventType.KeyUp, KeyCode = 13, KeyName = "Enter" }
                ]
            }
        };

        var js = WorkflowStepCompiler.CompileSingleStep(step);

        Assert.Contains("await tp.runMacro(", js);
        Assert.Contains("Enter", js);
    }
}
