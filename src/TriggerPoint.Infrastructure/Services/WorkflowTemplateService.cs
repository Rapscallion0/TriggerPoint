using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Serilog;
using TriggerPoint.Core.Contracts;
using TriggerPoint.Core.Models;

namespace TriggerPoint.Infrastructure.Services;

public class WorkflowTemplateService : IWorkflowTemplateService
{
    private readonly string _templatesDirectory;
    private readonly object _lock = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: true) }
    };

    public string TemplatesDirectory => _templatesDirectory;

    public WorkflowTemplateService(string? customTemplatesDirectory = null)
    {
        _templatesDirectory = customTemplatesDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "TriggerPoint", "templates", "workflows");
    }

    public void EnsureDefaultTemplates()
    {
        lock (_lock)
        {
            try
            {
                if (!Directory.Exists(_templatesDirectory))
                {
                    Directory.CreateDirectory(_templatesDirectory);
                }

                var existingFiles = Directory.GetFiles(_templatesDirectory, "*.json");
                if (existingFiles.Length == 0)
                {
                    Log.Information("Workflow templates folder is empty. Seeding default built-in templates to: {Path}", _templatesDirectory);
                    var defaults = WorkflowPresets.GetAll();
                    foreach (var preset in defaults)
                    {
                        var safeId = GenerateSlug(string.IsNullOrWhiteSpace(preset.Id) ? preset.Title : preset.Id);
                        var targetPath = Path.Combine(_templatesDirectory, $"{safeId}.json");
                        var json = JsonSerializer.Serialize(preset, JsonOptions);
                        File.WriteAllText(targetPath, json);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to ensure default workflow templates in: {Path}", _templatesDirectory);
            }
        }
    }

    public IReadOnlyList<WorkflowPreset> GetAllTemplates()
    {
        EnsureDefaultTemplates();

        var templates = new List<WorkflowPreset>();

        lock (_lock)
        {
            if (!Directory.Exists(_templatesDirectory))
            {
                return WorkflowPresets.GetAll();
            }

            var files = Directory.GetFiles(_templatesDirectory, "*.json", SearchOption.AllDirectories);
            foreach (var file in files)
            {
                try
                {
                    var content = File.ReadAllText(file);
                    var preset = JsonSerializer.Deserialize<WorkflowPreset>(content, JsonOptions);
                    if (preset != null)
                    {
                        if (string.IsNullOrWhiteSpace(preset.Id))
                        {
                            preset.Id = Path.GetFileNameWithoutExtension(file);
                        }

                        if (string.IsNullOrWhiteSpace(preset.Category))
                        {
                            preset.Category = "General";
                        }

                        if (string.IsNullOrWhiteSpace(preset.Title))
                        {
                            preset.Title = preset.Id;
                        }

                        preset.Steps ??= [];
                        templates.Add(preset);
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Failed to deserialize workflow template file: {FilePath}", file);
                }
            }
        }

        if (templates.Count == 0)
        {
            return WorkflowPresets.GetAll();
        }

        // Sort: Standard categories first, then alphabetically by category, then by title
        return templates
            .OrderBy(t => GetCategorySortOrder(t.Category))
            .ThenBy(t => t.Category, StringComparer.OrdinalIgnoreCase)
            .ThenBy(t => t.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public IReadOnlyList<string> GetCategories()
    {
        var templates = GetAllTemplates();
        var categories = templates
            .Select(t => t.Category?.Trim() ?? string.Empty)
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(GetCategorySortOrder)
            .ThenBy(c => c, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (!categories.Contains(WorkflowPresets.CategoryGeneral, StringComparer.OrdinalIgnoreCase))
        {
            categories.Insert(0, WorkflowPresets.CategoryGeneral);
        }

        if (!categories.Contains(WorkflowPresets.CategoryDeveloper, StringComparer.OrdinalIgnoreCase))
        {
            categories.Add(WorkflowPresets.CategoryDeveloper);
        }

        return categories;
    }

    public WorkflowPreset? GetTemplateById(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        return GetAllTemplates().FirstOrDefault(t => t.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
    }

    public bool SaveTemplate(WorkflowPreset template)
    {
        if (template == null) throw new ArgumentNullException(nameof(template));

        lock (_lock)
        {
            try
            {
                if (!Directory.Exists(_templatesDirectory))
                {
                    Directory.CreateDirectory(_templatesDirectory);
                }

                if (string.IsNullOrWhiteSpace(template.Title))
                {
                    template.Title = "Untitled Template";
                }

                if (string.IsNullOrWhiteSpace(template.Category))
                {
                    template.Category = "General";
                }

                if (string.IsNullOrWhiteSpace(template.Id))
                {
                    template.Id = GenerateSlug(template.Title);
                }

                template.Steps ??= [];

                var targetPath = Path.Combine(_templatesDirectory, $"{template.Id}.json");
                var json = JsonSerializer.Serialize(template, JsonOptions);
                File.WriteAllText(targetPath, json);
                Log.Information("Saved workflow template '{Title}' (Category: '{Category}') to: {Path}", 
                    template.Title, template.Category, targetPath);
                return true;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to save workflow template: {Title}", template.Title);
                return false;
            }
        }
    }

    public bool DeleteTemplate(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return false;

        lock (_lock)
        {
            try
            {
                if (!Directory.Exists(_templatesDirectory)) return false;

                var directPath = Path.Combine(_templatesDirectory, $"{id}.json");
                if (File.Exists(directPath))
                {
                    File.Delete(directPath);
                    Log.Information("Deleted workflow template file: {Path}", directPath);
                    return true;
                }

                // Search all subdirectories if any
                var files = Directory.GetFiles(_templatesDirectory, "*.json", SearchOption.AllDirectories);
                foreach (var file in files)
                {
                    if (Path.GetFileNameWithoutExtension(file).Equals(id, StringComparison.OrdinalIgnoreCase))
                    {
                        File.Delete(file);
                        Log.Information("Deleted workflow template file: {Path}", file);
                        return true;
                    }

                    try
                    {
                        var content = File.ReadAllText(file);
                        var preset = JsonSerializer.Deserialize<WorkflowPreset>(content, JsonOptions);
                        if (preset != null && preset.Id.Equals(id, StringComparison.OrdinalIgnoreCase))
                        {
                            File.Delete(file);
                            Log.Information("Deleted workflow template file: {Path}", file);
                            return true;
                            }
                    }
                    catch { }
                }

                return false;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to delete workflow template: {Id}", id);
                return false;
            }
        }
    }

    private static int GetCategorySortOrder(string category)
    {
        if (string.Equals(category, WorkflowPresets.CategoryGeneral, StringComparison.OrdinalIgnoreCase)) return 0;
        if (string.Equals(category, WorkflowPresets.CategoryDeveloper, StringComparison.OrdinalIgnoreCase)) return 1;
        return 2;
    }

    private static string GenerateSlug(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return Guid.NewGuid().ToString("N")[..8];
        var slug = Regex.Replace(text.ToLowerInvariant().Trim(), @"[^a-z0-9_\-]+", "_");
        slug = slug.Trim('_');
        return string.IsNullOrWhiteSpace(slug) ? Guid.NewGuid().ToString("N")[..8] : slug;
    }
}
