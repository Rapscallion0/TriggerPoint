using System;
using System.Collections.Generic;
using TriggerPoint.Core.Models;
using TriggerPoint.Infrastructure.Services;
using TriggerPoint.UI.Views;
using Xunit;

namespace TriggerPoint.Tests;

public class WindowsServiceTests
{
    [Fact]
    public void WindowsServiceItem_StatusProperties_ReflectCorrectBadgesAndText()
    {
        var running = new WindowsServiceItem
        {
            ServiceName = "wuauserv",
            DisplayName = "Windows Update",
            Status = WindowsServiceStatus.Running,
            StartType = "Manual"
        };

        Assert.Equal("Running", running.StatusText);
        Assert.Equal("🟢", running.StatusBadgeEmoji);

        var stopped = new WindowsServiceItem
        {
            ServiceName = "Spooler",
            DisplayName = "Print Spooler",
            Status = WindowsServiceStatus.Stopped,
            StartType = "Automatic"
        };

        Assert.Equal("Stopped", stopped.StatusText);
        Assert.Equal("⚪", stopped.StatusBadgeEmoji);

        var paused = new WindowsServiceItem
        {
            ServiceName = "TestService",
            DisplayName = "Test Service",
            Status = WindowsServiceStatus.Paused
        };

        Assert.Equal("Paused", paused.StatusText);
        Assert.Equal("🟡", paused.StatusBadgeEmoji);

        var starting = new WindowsServiceItem
        {
            ServiceName = "TestStarting",
            DisplayName = "Test Starting",
            Status = WindowsServiceStatus.StartPending
        };

        Assert.Equal("Starting...", starting.StatusText);
        Assert.Equal("🔄", starting.StatusBadgeEmoji);
    }

    [Fact]
    public void WindowsServiceManager_IsRunningElevated_ReturnsWithoutThrowing()
    {
        // Must succeed without throwing regardless of process elevation state
        bool elevated = WindowsServiceManager.IsRunningElevated();
        Assert.True(elevated || !elevated);
    }

    [Fact]
    public void PaletteItemViewModel_FromWindowsServiceItem_PopulatesPropertiesAccurately()
    {
        var service = new WindowsServiceItem
        {
            ServiceName = "EventLog",
            DisplayName = "Windows Event Log",
            Status = WindowsServiceStatus.Running,
            StartType = "Automatic"
        };

        var vm = new PaletteItemViewModel(service, [0, 1, 2], 150.0);

        Assert.True(vm.IsServiceItem);
        Assert.NotNull(vm.ServiceItem);
        Assert.Equal("Windows Event Log", vm.Name);
        Assert.Equal("RUNNING", vm.TypeText);
        Assert.Equal("🟢", vm.IconSymbol);
        Assert.Contains("EventLog • Automatic • Running", vm.SecondaryDetail);
        Assert.Contains("Status: Running", vm.DetailPreviewText);
        Assert.NotNull(vm.MatchResult);
        Assert.Equal(150.0, vm.MatchResult!.Score);
    }

    [Fact]
    public void ActionPayload_And_WorkflowStep_ServiceProperties_DefaultAndCloneProperly()
    {
        // 1. ActionPayload defaults & clone
        var payload = new ActionPayload
        {
            ServiceName = "Spooler",
            ServiceOperation = ServiceOperation.Restart,
            ServiceTimeoutSeconds = 45,
            ServiceRunAsAdmin = true
        };

        var clonedPayload = payload.Clone();
        Assert.Equal("Spooler", clonedPayload.ServiceName);
        Assert.Equal(ServiceOperation.Restart, clonedPayload.ServiceOperation);
        Assert.Equal(45, clonedPayload.ServiceTimeoutSeconds);
        Assert.True(clonedPayload.ServiceRunAsAdmin);

        // 2. WorkflowStep defaults & clone
        var step = new WorkflowStep
        {
            StepType = WorkflowStepType.Service,
            ServiceName = "wuauserv",
            ServiceOperation = ServiceOperation.Stop,
            ServiceWaitForCompletion = true,
            ServiceTimeoutSeconds = 60,
            ServiceRunAsAdmin = false
        };

        var clonedStep = step.Clone();
        Assert.Equal(WorkflowStepType.Service, clonedStep.StepType);
        Assert.Equal("wuauserv", clonedStep.ServiceName);
        Assert.Equal(ServiceOperation.Stop, clonedStep.ServiceOperation);
        Assert.True(clonedStep.ServiceWaitForCompletion);
        Assert.Equal(60, clonedStep.ServiceTimeoutSeconds);
        Assert.False(clonedStep.ServiceRunAsAdmin);
    }

    [Fact]
    public void PaletteItemViewModel_FromDedicatedServiceAction_PopulatesPropertiesAccurately()
    {
        var action = new TriggerItem
        {
            Id = Guid.NewGuid(),
            Name = "Restart Print Spooler",
            ActionType = ActionType.Service,
            Payload = new ActionPayload
            {
                ServiceName = "Spooler",
                ServiceOperation = ServiceOperation.Restart,
                ServiceTimeoutSeconds = 30,
                ServiceRunAsAdmin = true
            }
        };

        var vm = new PaletteItemViewModel(action, null, null, WindowsServiceStatus.Stopped);

        Assert.False(vm.IsServiceItem);
        Assert.Equal("Restart Print Spooler", vm.Name);
        Assert.Equal("STOPPED", vm.TypeText);
        Assert.Equal("⚪", vm.IconSymbol);
        Assert.Equal("RESTART • Spooler • Stopped", vm.SecondaryDetail);
        Assert.Contains("Restart 'Spooler'", vm.DetailPreviewText);
        Assert.Contains("Timeout: 30s", vm.DetailPreviewText);
        Assert.Contains("[Elevated/Admin]", vm.DetailPreviewText);
        Assert.Equal(System.Windows.Visibility.Visible, vm.AdminBadgeVisibility);

        var vmRunning = new PaletteItemViewModel(action, null, null, WindowsServiceStatus.Running);
        Assert.Equal("RUNNING", vmRunning.TypeText);
        Assert.Equal("🟢", vmRunning.IconSymbol);
        Assert.Equal("RESTART • Spooler • Running", vmRunning.SecondaryDetail);
    }

    [Fact]
    public void WorkflowStepCompiler_CompilesServiceStepToScriptCorrectly()
    {
        var step = new WorkflowStep
        {
            StepType = WorkflowStepType.Service,
            ServiceName = "wuauserv",
            ServiceOperation = ServiceOperation.Toggle,
            ServiceWaitForCompletion = true,
            ServiceTimeoutSeconds = 25,
            ServiceRunAsAdmin = true
        };

        string js = TriggerPoint.Core.Services.WorkflowStepCompiler.CompileToJavaScript(new[] { step });

        Assert.Contains("tp.shell.executeService", js);
        Assert.Contains("wuauserv", js);
        Assert.Contains("'toggle'", js);
        Assert.Contains("true", js); // runAsAdmin
        Assert.Contains("25", js); // timeoutSeconds
    }

    private static void EnsureApplicationAndThemeResources()
    {
        if (System.Windows.Application.Current == null)
        {
            new System.Windows.Application();
        }

        if (!System.Windows.Application.Current!.Resources.MergedDictionaries.Any(d => d.Source?.OriginalString?.Contains("ThemeResources.xaml") == true))
        {
            System.Windows.Application.Current!.Resources.MergedDictionaries.Add(new System.Windows.ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/TriggerPoint;component/Theme/ThemeResources.xaml", UriKind.Absolute)
            });
        }
    }

    [Fact]
    public void SearchableServicePickerControl_InitializeAndSelect_UpdatesSelectionState()
    {
        Exception? caughtEx = null;
        var thread = new System.Threading.Thread(() =>
        {
            try
            {
                EnsureApplicationAndThemeResources();

                var services = new List<WindowsServiceItem>
                {
                    new() { ServiceName = "spooler", DisplayName = "Print Spooler", Status = WindowsServiceStatus.Running, StartType = "Automatic" },
                    new() { ServiceName = "wuauserv", DisplayName = "Windows Update", Status = WindowsServiceStatus.Stopped, StartType = "Manual" },
                    new() { ServiceName = "docker", DisplayName = "Docker Desktop Service", Status = WindowsServiceStatus.Running, StartType = "Automatic" }
                };

                var picker = new TriggerPoint.UI.Controls.SearchableServicePickerControl();
                picker.InitializeServices(services);

                // Initial state empty
                Assert.Empty(picker.SelectedServiceName);
                Assert.Null(picker.SelectedServiceItem);

                // Set service by name
                picker.SetSelectedService("spooler");
                Assert.Equal("spooler", picker.SelectedServiceName);
                Assert.NotNull(picker.SelectedServiceItem);
                Assert.Equal("Print Spooler", picker.SelectedServiceDisplayName);
                Assert.Equal(WindowsServiceStatus.Running, picker.SelectedServiceItem!.Status);

                // Fallback custom service
                picker.SetSelectedService("MyCustomService");
                Assert.Equal("MyCustomService", picker.SelectedServiceName);
                Assert.Equal("MyCustomService", picker.SelectedServiceDisplayName);
            }
            catch (Exception ex)
            {
                caughtEx = ex;
            }
        });

        thread.SetApartmentState(System.Threading.ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (caughtEx != null) throw caughtEx;
    }

    [Fact]
    public void WorkflowStep_ServiceStep_PropertiesAndDefaultConfig_InitializedCorrectly()
    {
        var step = new WorkflowStep
        {
            StepType = WorkflowStepType.Service,
            ServiceName = "spooler",
            ServiceOperation = ServiceOperation.Restart,
            ServiceTimeoutSeconds = 45,
            ServiceWaitForCompletion = true,
            ServiceRunAsAdmin = true
        };

        Assert.Equal(WorkflowStepType.Service, step.StepType);
        Assert.Equal("spooler", step.ServiceName);
        Assert.Equal(ServiceOperation.Restart, step.ServiceOperation);
        Assert.Equal(45, step.ServiceTimeoutSeconds);
        Assert.True(step.ServiceWaitForCompletion);
        Assert.True(step.ServiceRunAsAdmin);
    }

    [Fact]
    public void AppSettings_RunAsAdminAtStartup_Serialization_DefaultsAndRoundTrips()
    {
        var settings = new AppSettings();
        Assert.False(settings.RunAsAdminAtStartup);

        settings.RunAsAdminAtStartup = true;
        string json = System.Text.Json.JsonSerializer.Serialize(settings);
        var restored = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(json);

        Assert.NotNull(restored);
        Assert.True(restored!.RunAsAdminAtStartup);
    }

    [Theory]
    [InlineData(WindowsServiceStatus.Running, "RUNNING", "🟢")]
    [InlineData(WindowsServiceStatus.Stopped, "STOPPED", "⚪")]
    [InlineData(WindowsServiceStatus.Paused, "PAUSED", "🟡")]
    public void PaletteItemViewModel_FromActionService_DisplaysLiveStatusAndColors(
        WindowsServiceStatus status,
        string expectedTypeText,
        string expectedEmoji)
    {
        var action = new TriggerItem
        {
            Id = Guid.NewGuid(),
            Name = "Control Test Service",
            ActionType = ActionType.Service,
            Payload = new ActionPayload
            {
                ServiceName = "TestSvc",
                ServiceOperation = ServiceOperation.Toggle
            }
        };

        var vm = new PaletteItemViewModel(action, null, null, status);

        Assert.Equal(expectedTypeText, vm.TypeText);
        Assert.Equal(expectedEmoji, vm.IconSymbol);
        Assert.Contains(expectedTypeText, vm.SecondaryDetail.ToUpperInvariant());
    }

    [Fact]
    public void ServiceProgressHudWindow_CanInstantiateAndReportSuccessOnStaThread()
    {
        Exception? caughtEx = null;
        var thread = new System.Threading.Thread(() =>
        {
            try
            {
                var hud = new ServiceProgressHudWindow("Test Title", "Test Message", () => { }, "Cancel");
                hud.ReportSuccess("Service stopped successfully.");
                hud.Dismiss();
            }
            catch (Exception ex)
            {
                caughtEx = ex;
            }
        });

        thread.SetApartmentState(System.Threading.ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (caughtEx != null) throw caughtEx;
    }
}
