using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using TriggerPoint.Core.Models;

namespace TriggerPoint.Core.Services;

public static class WorkflowStepCompiler
{
    private static readonly Regex VariableRegex = new(@"\{(?<var>[a-zA-Z0-9_]+)\}", RegexOptions.Compiled);

    public static string CompileToJavaScript(IEnumerable<WorkflowStep>? steps)
    {
        if (steps == null) return string.Empty;

        var sb = new StringBuilder();
        sb.AppendLine("// TriggerPoint Automated Workflow Script");
        sb.AppendLine("// Generated from Visual Steps");
        sb.AppendLine();

        int stepIndex = 1;
        var definedVariables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var step in steps)
        {
            if (!step.IsEnabled)
            {
                sb.AppendLine($"// Step {stepIndex}: {step.Name} (Disabled)");
                stepIndex++;
                continue;
            }

            sb.AppendLine($"// Step {stepIndex}: {(string.IsNullOrWhiteSpace(step.Name) ? step.StepType.ToString() : step.Name)}");

            switch (step.StepType)
            {
                case WorkflowStepType.Prompt:
                    CompilePromptStep(step, sb, definedVariables);
                    break;

                case WorkflowStepType.OpenUrl:
                    CompileOpenUrlStep(step, sb);
                    break;

                case WorkflowStepType.EnsureDirectory:
                    CompileEnsureDirectoryStep(step, sb);
                    break;

                case WorkflowStepType.LaunchApp:
                    CompileLaunchAppStep(step, sb);
                    break;

                case WorkflowStepType.InjectSnippet:
                    CompileInjectSnippetStep(step, sb);
                    break;

                case WorkflowStepType.Delay:
                    sb.AppendLine($"await tp.delay({Math.Max(10, step.DelayMs)});");
                    break;

                case WorkflowStepType.RunScript:
                    if (!string.IsNullOrWhiteSpace(step.InlineScript))
                    {
                        sb.AppendLine(step.InlineScript.Trim());
                    }
                    break;

                case WorkflowStepType.ExecuteAction:
                    CompileExecuteActionStep(step, sb);
                    break;
            }

            sb.AppendLine();
            stepIndex++;
        }

        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// Compiles a single workflow step to its JavaScript equivalent without any
    /// file-level header comments. Suitable for use as an inline script body.
    /// </summary>
    public static string CompileSingleStep(WorkflowStep step)
    {
        if (step == null) return string.Empty;

        var sb = new StringBuilder();
        var definedVariables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        switch (step.StepType)
        {
            case WorkflowStepType.Prompt:
                CompilePromptStep(step, sb, definedVariables);
                break;

            case WorkflowStepType.OpenUrl:
                CompileOpenUrlStep(step, sb);
                break;

            case WorkflowStepType.EnsureDirectory:
                CompileEnsureDirectoryStep(step, sb);
                break;

            case WorkflowStepType.LaunchApp:
                CompileLaunchAppStep(step, sb);
                break;

            case WorkflowStepType.InjectSnippet:
                CompileInjectSnippetStep(step, sb);
                break;

            case WorkflowStepType.Delay:
                sb.AppendLine($"await tp.delay({Math.Max(10, step.DelayMs)});");
                break;

            case WorkflowStepType.RunScript:
                // Already a script — return its content as-is
                if (!string.IsNullOrWhiteSpace(step.InlineScript))
                    sb.AppendLine(step.InlineScript.Trim());
                break;

            case WorkflowStepType.ExecuteAction:
                CompileExecuteActionStep(step, sb);
                break;
        }

        return sb.ToString().TrimEnd();
    }

    private static void CompileExecuteActionStep(WorkflowStep step, StringBuilder sb)
    {
        if (step.TargetItemId.HasValue)
        {
            sb.AppendLine($"await tp.executeAction(\"{step.TargetItemId.Value}\");");
        }
        else
        {
            sb.AppendLine("// No target action selected");
        }
    }

    private static void CompilePromptStep(WorkflowStep step, StringBuilder sb, HashSet<string> definedVariables)
    {
        var fields = (step.PromptFields != null && step.PromptFields.Count > 0)
            ? step.PromptFields
            : [
                new WorkflowPromptField
                {
                    VariableName = step.VariableName,
                    Label = step.PromptLabel,
                    DefaultValue = step.PromptDefaultValue,
                    Type = step.PromptType,
                    Choices = step.PromptChoices
                }
            ];

        foreach (var field in fields)
        {
            var rawVar = string.IsNullOrWhiteSpace(field.VariableName) ? "input" : field.VariableName.Trim();
            var safeVar = SanitizeIdentifier(rawVar);
            var label = EscapeJsString(string.IsNullOrWhiteSpace(field.Label) ? "Enter value" : field.Label);
            var defaultVal = EscapeJsString(field.DefaultValue);

            string promptCall;
            if (field.Type == TokenType.PromptNumber)
            {
                var minPart = field.MinNumber.HasValue ? $", min: {field.MinNumber.Value}" : "";
                var maxPart = field.MaxNumber.HasValue ? $", max: {field.MaxNumber.Value}" : "";
                var defPart = !string.IsNullOrEmpty(defaultVal) ? $", default: \"{defaultVal}\"" : "";
                promptCall = $"await tp.prompt(\"{label}\", {{ type: \"number\"{minPart}{maxPart}{defPart} }})";
            }
            else if (field.Type == TokenType.PromptDatePicker)
            {
                var fmt = EscapeJsString(string.IsNullOrWhiteSpace(field.DateFormat) ? "yyyy-MM-dd" : field.DateFormat);
                var defPart = !string.IsNullOrEmpty(defaultVal) ? $", default: \"{defaultVal}\"" : "";
                promptCall = $"await tp.prompt(\"{label}\", {{ type: \"date\", dateFormat: \"{fmt}\"{defPart} }})";
            }
            else if (field.Type == TokenType.PromptMultiline)
            {
                var defPart = !string.IsNullOrEmpty(defaultVal) ? $", default: \"{defaultVal}\"" : "";
                promptCall = $"await tp.prompt(\"{label}\", {{ type: \"multiline\"{defPart} }})";
            }
            else if (!string.IsNullOrWhiteSpace(field.Choices) || field.Type == TokenType.PromptChoice)
            {
                var escapedChoices = EscapeJsString(field.Choices ?? string.Empty);
                var defPart = !string.IsNullOrEmpty(defaultVal) ? $", default: \"{defaultVal}\"" : "";
                promptCall = $"await tp.prompt(\"{label}\", {{ choices: \"{escapedChoices}\"{defPart} }})";
            }
            else if (!string.IsNullOrEmpty(defaultVal))
            {
                promptCall = $"await tp.prompt(\"{label}\", {{ default: \"{defaultVal}\" }})";
            }
            else
            {
                promptCall = $"await tp.prompt(\"{label}\")";
            }

            if (definedVariables.Contains(safeVar))
            {
                sb.AppendLine($"{safeVar} = {promptCall};");
            }
            else
            {
                sb.AppendLine($"const {safeVar} = {promptCall};");
                definedVariables.Add(safeVar);
            }

            sb.AppendLine($"tp.vars.{safeVar} = {safeVar};");

            if (step.OnError == StepErrorPolicy.StopWorkflow)
            {
                sb.AppendLine($"if (!{safeVar}) return; // Stop workflow if cancelled or empty");
            }
        }
    }

    private static void CompileOpenUrlStep(WorkflowStep step, StringBuilder sb)
    {
        var template = ConvertPlaceholdersToTemplateLiteral(step.Url);
        var browser = EscapeJsString(step.BrowserTarget ?? string.Empty);
        var profile = EscapeJsString(step.BrowserProfile ?? string.Empty);
        var newWin = step.OpenInNewWindow ? "true" : "false";

        if (step.OpenInNewWindow || !string.IsNullOrWhiteSpace(step.BrowserProfile))
        {
            sb.AppendLine($"tp.openUrl(`{template}`, \"{browser}\", \"{profile}\", {newWin});");
        }
        else if (!string.IsNullOrWhiteSpace(step.BrowserTarget))
        {
            sb.AppendLine($"tp.openUrl(`{template}`, \"{browser}\");");
        }
        else
        {
            sb.AppendLine($"tp.openUrl(`{template}`);");
        }
    }

    private static void CompileEnsureDirectoryStep(WorkflowStep step, StringBuilder sb)
    {
        var template = ConvertPlaceholdersToTemplateLiteral(step.DirectoryPath);
        var folderVar = "folderPath_" + Math.Abs(step.Id.GetHashCode() % 10000);

        sb.AppendLine($"const {folderVar} = `{template}`;");

        if (step.DirectoryMissingPolicy == DirectoryMissingPolicy.PromptToCreate)
        {
            sb.AppendLine($"if (!tp.fs.exists({folderVar})) {{");
            sb.AppendLine($"    const shouldCreate = await tp.confirm(`Folder \"${{{folderVar}}}\" does not exist. Create it?`, \"Folder Missing\");");
            sb.AppendLine($"    if (shouldCreate) {{");
            sb.AppendLine($"        tp.fs.createDirectory({folderVar});");
            sb.AppendLine($"    }} else {{");
            if (step.OnError == StepErrorPolicy.StopWorkflow)
            {
                sb.AppendLine($"        return; // Cancelled folder creation");
            }
            else
            {
                sb.AppendLine($"        // Continuing despite missing folder");
            }
            sb.AppendLine($"    }}");
            sb.AppendLine($"}}");
        }
        else if (step.DirectoryMissingPolicy == DirectoryMissingPolicy.CreateSilently)
        {
            sb.AppendLine($"if (!tp.fs.exists({folderVar})) {{");
            sb.AppendLine($"    tp.fs.createDirectory({folderVar});");
            sb.AppendLine($"}}");
        }
        else if (step.DirectoryMissingPolicy == DirectoryMissingPolicy.Fail)
        {
            sb.AppendLine($"if (!tp.fs.exists({folderVar})) {{");
            sb.AppendLine($"    tp.alert(`Folder \"${{{folderVar}}}\" does not exist!`, \"Error\");");
            if (step.OnError == StepErrorPolicy.StopWorkflow)
            {
                sb.AppendLine($"    return;");
            }
            sb.AppendLine($"}}");
        }

        if (step.OpenInExplorer)
        {
            sb.AppendLine($"tp.fs.openInExplorer({folderVar});");
        }
    }

    private static void CompileLaunchAppStep(WorkflowStep step, StringBuilder sb)
    {
        var cmd = ConvertPlaceholdersToTemplateLiteral(step.Command);
        var args = ConvertPlaceholdersToTemplateLiteral(step.Arguments);
        var workDir = ConvertPlaceholdersToTemplateLiteral(step.WorkingDirectory);
        var runAsAdmin = step.RunAsAdmin ? "true" : "false";
        var targetDisplay = EscapeJsString(step.TargetDisplay ?? string.Empty);

        if (!string.IsNullOrWhiteSpace(step.TargetDisplay))
        {
            sb.AppendLine($"tp.launch(`{cmd}`, `{args}`, `{workDir}`, {runAsAdmin}, \"{targetDisplay}\");");
        }
        else if (step.RunAsAdmin || !string.IsNullOrWhiteSpace(step.WorkingDirectory) || !string.IsNullOrWhiteSpace(step.Arguments))
        {
            sb.AppendLine($"tp.launch(`{cmd}`, `{args}`, `{workDir}`, {runAsAdmin});");
        }
        else
        {
            sb.AppendLine($"tp.launch(`{cmd}`);");
        }
    }

    private static void CompileInjectSnippetStep(WorkflowStep step, StringBuilder sb)
    {
        var template = ConvertPlaceholdersToTemplateLiteral(step.SnippetTemplate);
        sb.AppendLine($"await tp.injectSnippet(`{template}`);");
    }

    public static string ConvertPlaceholdersToTemplateLiteral(string? input)
    {
        if (string.IsNullOrEmpty(input)) return string.Empty;

        // Escape backticks and backslashes for JS template literal
        var escaped = input
            .Replace("\\", "\\\\")
            .Replace("`", "\\`")
            .Replace("${", "\\${");

        // Convert {varName} into ${tp.vars.varName ?? ''}
        return VariableRegex.Replace(escaped, match =>
        {
            var varName = match.Groups["var"].Value;
            var safe = SanitizeIdentifier(varName);
            return $"${{tp.vars.{safe} ?? '{match.Value}'}}";
        });
    }

    public static string SanitizeIdentifier(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "input";
        var cleaned = Regex.Replace(name, @"[^a-zA-Z0-9_]", "_");
        if (char.IsDigit(cleaned[0])) cleaned = "_" + cleaned;
        return cleaned;
    }

    private static string EscapeJsString(string? text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        return text
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\r", "")
            .Replace("\n", "\\n");
    }
}
