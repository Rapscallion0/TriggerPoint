using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TriggerPoint.Core.Models;
using TriggerPoint.Infrastructure.Services;
using Xunit;

namespace TriggerPoint.Tests;

public class WorkflowTemplateServiceTests : IDisposable
{
    private readonly string _testDir;

    public WorkflowTemplateServiceTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "TriggerPoint_TemplateTest_" + Guid.NewGuid());
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testDir))
            {
                Directory.Delete(_testDir, true);
            }
        }
        catch { }
    }

    [Fact]
    public void EnsureDefaultTemplates_SeedsBuiltInPresetsToFolder()
    {
        var service = new WorkflowTemplateService(_testDir);
        service.EnsureDefaultTemplates();

        var files = Directory.GetFiles(_testDir, "*.json");
        Assert.NotEmpty(files);

        var templates = service.GetAllTemplates();
        Assert.NotEmpty(templates);
        Assert.Contains(templates, t => t.Title == "Google / Web Search");
    }

    [Fact]
    public void SaveTemplate_WritesValidJsonAndAppearsInCategories()
    {
        var service = new WorkflowTemplateService(_testDir);
        var newTemplate = new WorkflowPreset
        {
            Id = "test_devops_workflow",
            Category = "DevOps & Cloud",
            Title = "Deploy Local Containers",
            Description = "Spins up docker compose environment",
            Steps =
            [
                new WorkflowStep
                {
                    StepType = WorkflowStepType.Prompt,
                    Name = "Prompt Environment",
                    VariableName = "env",
                    PromptLabel = "Target Environment"
                },
                new WorkflowStep
                {
                    StepType = WorkflowStepType.OpenUrl,
                    Name = "Open Dashboard",
                    Url = "http://localhost:8080/{env}"
                }
            ]
        };

        bool saved = service.SaveTemplate(newTemplate);
        Assert.True(saved);

        var categories = service.GetCategories();
        Assert.Contains("DevOps & Cloud", categories);

        var fetched = service.GetTemplateById("test_devops_workflow");
        Assert.NotNull(fetched);
        Assert.Equal("Deploy Local Containers", fetched.Title);
        Assert.Equal("DevOps & Cloud", fetched.Category);
        Assert.Equal(2, fetched.Steps.Count);
    }

    [Fact]
    public void UpdateTemplate_ChangesCategoryAndReflectsInGetCategories()
    {
        var service = new WorkflowTemplateService(_testDir);
        var template = new WorkflowPreset
        {
            Id = "change_category_template",
            Category = "Initial Category",
            Title = "Category Test",
            Description = "Testing category update",
            Steps = [new WorkflowStep { StepType = WorkflowStepType.Delay, DelayMs = 500 }]
        };

        service.SaveTemplate(template);
        Assert.Contains("Initial Category", service.GetCategories());

        // Update category
        template.Category = "Updated Category";
        service.SaveTemplate(template);

        var updated = service.GetTemplateById("change_category_template");
        Assert.NotNull(updated);
        Assert.Equal("Updated Category", updated.Category);
        Assert.Contains("Updated Category", service.GetCategories());
    }

    [Fact]
    public void DeleteTemplate_RemovesJsonFile()
    {
        var service = new WorkflowTemplateService(_testDir);
        var template = new WorkflowPreset
        {
            Id = "temp_to_delete",
            Category = "Testing",
            Title = "Temporary Template",
            Description = "To be deleted",
            Steps = []
        };

        service.SaveTemplate(template);
        Assert.NotNull(service.GetTemplateById("temp_to_delete"));

        bool deleted = service.DeleteTemplate("temp_to_delete");
        Assert.True(deleted);

        Assert.Null(service.GetTemplateById("temp_to_delete"));
        Assert.False(File.Exists(Path.Combine(_testDir, "temp_to_delete.json")));
    }

    [Fact]
    public void GetAllTemplates_ResilientToMalformedJson()
    {
        var service = new WorkflowTemplateService(_testDir);
        service.EnsureDefaultTemplates();

        // Write a corrupt JSON file
        File.WriteAllText(Path.Combine(_testDir, "corrupted_template.json"), "{ invalid json !!");

        // Should not throw and still return valid templates
        var templates = service.GetAllTemplates();
        Assert.NotEmpty(templates);
        Assert.DoesNotContain(templates, t => t.Id == "corrupted_template");
    }
}
