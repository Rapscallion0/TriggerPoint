using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using TriggerPoint.Core.Models;

namespace TriggerPoint.Core.Services;

/// <summary>
/// Provides utility methods to inspect and cascade variable name renames across workflow steps.
/// </summary>
public static class WorkflowVariableCascadeHelper
{
    public static int CountVariableReferences(IEnumerable<WorkflowStep>? steps, string variableName, WorkflowStep? skipStepDefinition = null)
    {
        if (steps == null || string.IsNullOrWhiteSpace(variableName)) return 0;
        int count = 0;
        string token = $"{{{variableName}}}";

        foreach (var step in steps)
        {
            count += CountInStep(step, variableName, token, skipStepDefinition);
            if (step.ThenSteps != null && step.ThenSteps.Count > 0)
            {
                count += CountVariableReferences(step.ThenSteps, variableName, skipStepDefinition);
            }
            if (step.ElseSteps != null && step.ElseSteps.Count > 0)
            {
                count += CountVariableReferences(step.ElseSteps, variableName, skipStepDefinition);
            }
        }

        return count;
    }

    private static int CountInStep(WorkflowStep step, string varName, string token, WorkflowStep? skipStepDefinition)
    {
        int count = 0;

        count += CountOccurrences(step.Url, token);
        count += CountOccurrences(step.Command, token);
        count += CountOccurrences(step.Arguments, token);
        count += CountOccurrences(step.WorkingDirectory, token);
        count += CountOccurrences(step.DirectoryPath, token);
        count += CountOccurrences(step.SnippetTemplate, token);
        count += CountOccurrences(step.DialogTitle, token);
        count += CountOccurrences(step.DialogMessage, token);
        count += CountOccurrences(step.SetVariableValue, token);
        count += CountOccurrences(step.ConditionLeft, token);
        count += CountOccurrences(step.ConditionRight, token);
        count += CountOccurrences(step.InlineScript, token);
        count += CountOccurrences(step.PromptTitle, token);
        count += CountOccurrences(step.PromptSubtitle, token);
        count += CountOccurrences(step.PromptDefaultValue, token);

        bool isSkipStep = skipStepDefinition != null && step.Id == skipStepDefinition.Id;

        if (!isSkipStep)
        {
            if (step.StepType == WorkflowStepType.SetVariable && string.Equals(step.SetVariableName, varName, StringComparison.OrdinalIgnoreCase))
            {
                count++;
            }

            if (step.StepType == WorkflowStepType.Prompt)
            {
                if (string.Equals(step.VariableName, varName, StringComparison.OrdinalIgnoreCase))
                {
                    count++;
                }
                if (step.PromptFields != null)
                {
                    foreach (var field in step.PromptFields)
                    {
                        if (string.Equals(field.VariableName, varName, StringComparison.OrdinalIgnoreCase))
                        {
                            count++;
                        }
                    }
                }
            }
        }

        if (step.PromptFields != null)
        {
            foreach (var field in step.PromptFields)
            {
                count += CountOccurrences(field.DefaultValue, token);
                count += CountOccurrences(field.Label, token);
                count += CountOccurrences(field.Choices, token);
            }
        }

        return count;
    }

    public static int ReplaceVariableReferences(IEnumerable<WorkflowStep>? steps, string oldName, string newName, WorkflowStep? skipStepDefinition = null)
    {
        if (steps == null || string.IsNullOrWhiteSpace(oldName) || string.IsNullOrWhiteSpace(newName)) return 0;
        int replacements = 0;
        string oldToken = $"{{{oldName}}}";
        string newToken = $"{{{newName}}}";

        foreach (var step in steps)
        {
            replacements += ReplaceInStep(step, oldName, newName, oldToken, newToken, skipStepDefinition);
            if (step.ThenSteps != null && step.ThenSteps.Count > 0)
            {
                replacements += ReplaceVariableReferences(step.ThenSteps, oldName, newName, skipStepDefinition);
            }
            if (step.ElseSteps != null && step.ElseSteps.Count > 0)
            {
                replacements += ReplaceVariableReferences(step.ElseSteps, oldName, newName, skipStepDefinition);
            }
        }

        return replacements;
    }

    private static int ReplaceInStep(WorkflowStep step, string oldName, string newName, string oldToken, string newToken, WorkflowStep? skipStepDefinition)
    {
        int count = 0;

        step.Url = ReplaceString(step.Url, oldToken, newToken, ref count);
        step.Command = ReplaceString(step.Command, oldToken, newToken, ref count);
        step.Arguments = ReplaceString(step.Arguments, oldToken, newToken, ref count);
        step.WorkingDirectory = ReplaceString(step.WorkingDirectory, oldToken, newToken, ref count);
        step.DirectoryPath = ReplaceString(step.DirectoryPath, oldToken, newToken, ref count);
        step.SnippetTemplate = ReplaceString(step.SnippetTemplate, oldToken, newToken, ref count);
        step.DialogTitle = ReplaceString(step.DialogTitle, oldToken, newToken, ref count);
        step.DialogMessage = ReplaceString(step.DialogMessage, oldToken, newToken, ref count);
        step.SetVariableValue = ReplaceString(step.SetVariableValue, oldToken, newToken, ref count);
        step.ConditionLeft = ReplaceString(step.ConditionLeft, oldToken, newToken, ref count);
        step.ConditionRight = ReplaceString(step.ConditionRight, oldToken, newToken, ref count);
        step.InlineScript = ReplaceString(step.InlineScript, oldToken, newToken, ref count);
        step.PromptTitle = ReplaceString(step.PromptTitle, oldToken, newToken, ref count);
        step.PromptSubtitle = ReplaceString(step.PromptSubtitle, oldToken, newToken, ref count);
        step.PromptDefaultValue = ReplaceString(step.PromptDefaultValue, oldToken, newToken, ref count);

        bool isSkipStep = skipStepDefinition != null && step.Id == skipStepDefinition.Id;

        if (!isSkipStep)
        {
            if (step.StepType == WorkflowStepType.SetVariable && string.Equals(step.SetVariableName, oldName, StringComparison.OrdinalIgnoreCase))
            {
                step.SetVariableName = newName;
                count++;
            }

            if (step.StepType == WorkflowStepType.Prompt)
            {
                if (string.Equals(step.VariableName, oldName, StringComparison.OrdinalIgnoreCase))
                {
                    step.VariableName = newName;
                    count++;
                }
                if (step.PromptFields != null)
                {
                    foreach (var field in step.PromptFields)
                    {
                        if (string.Equals(field.VariableName, oldName, StringComparison.OrdinalIgnoreCase))
                        {
                            field.VariableName = newName;
                            count++;
                        }
                    }
                }
            }
        }

        if (step.PromptFields != null)
        {
            foreach (var field in step.PromptFields)
            {
                field.DefaultValue = ReplaceString(field.DefaultValue, oldToken, newToken, ref count);
                field.Label = ReplaceString(field.Label, oldToken, newToken, ref count);
                field.Choices = ReplaceString(field.Choices, oldToken, newToken, ref count);
            }
        }

        return count;
    }

    private static int CountOccurrences(string? source, string token)
    {
        if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(token)) return 0;
        int count = 0;
        int idx = 0;
        while ((idx = source.IndexOf(token, idx, StringComparison.OrdinalIgnoreCase)) != -1)
        {
            count++;
            idx += token.Length;
        }
        return count;
    }

    private static string ReplaceString(string? source, string oldToken, string newToken, ref int count)
    {
        if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(oldToken)) return source ?? string.Empty;
        int occurrences = CountOccurrences(source, oldToken);
        if (occurrences == 0) return source;
        count += occurrences;
        return Regex.Replace(source, Regex.Escape(oldToken), newToken, RegexOptions.IgnoreCase);
    }
}
