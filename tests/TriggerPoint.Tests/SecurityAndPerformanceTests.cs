using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using TriggerPoint.Core.Contracts;
using TriggerPoint.Core.Models;
using TriggerPoint.Core.Services;
using TriggerPoint.Infrastructure.Persistence;
using TriggerPoint.Infrastructure.Services;
using Xunit;

namespace TriggerPoint.Tests;

public class SecurityAndPerformanceTests : IDisposable
{
    private readonly string _testDir;

    public SecurityAndPerformanceTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "TriggerPoint_SecPerfTest_" + Guid.NewGuid());
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
    public void SecretsVault_RoundTrip_EncryptsAndDecryptsSuccessfully()
    {
        var vault = new WindowsDpapiSecretsVaultService();
        const string secretValue = "MySuperSecretApiKey_12345!@#$";

        string protectedText = vault.Protect(secretValue);

        Assert.NotEqual(secretValue, protectedText);
        Assert.True(vault.IsProtected(protectedText));
        Assert.StartsWith("vault:dpapi:", protectedText);

        string unprotectedText = vault.Unprotect(protectedText);
        Assert.Equal(secretValue, unprotectedText);
    }

    [Fact]
    public void SecretsVault_EmptyAndWhitespace_HandlesGracefully()
    {
        var vault = new WindowsDpapiSecretsVaultService();

        Assert.Equal(string.Empty, vault.Protect(string.Empty));
        Assert.Equal("   ", vault.Protect("   "));
        Assert.Equal(string.Empty, vault.Unprotect(string.Empty));
        Assert.Equal("   ", vault.Unprotect("   "));
        Assert.False(vault.IsProtected("random_plaintext"));
    }

    [Fact]
    public void SecretsVault_AlreadyProtectedString_DoesNotDoubleEncrypt()
    {
        var vault = new WindowsDpapiSecretsVaultService();
        string protectedOnce = vault.Protect("test_token");
        string protectedTwice = vault.Protect(protectedOnce);

        Assert.Equal(protectedOnce, protectedTwice);
    }

    [Theory]
    [InlineData("https://google.com")]
    [InlineData("http://localhost:5000/api")]
    [InlineData("mailto:support@triggerpoint.app")]
    [InlineData("ftp://files.example.com")]
    [InlineData("customapp://open?id=123")]
    public void ProtocolValidator_AllowsSafeSchemes(string url)
    {
        bool isSafe = ProtocolValidator.IsSafeUrl(url, out string? reason);
        Assert.True(isSafe, reason);
        Assert.Null(reason);
    }

    [Theory]
    [InlineData("javascript:alert(document.cookie)")]
    [InlineData("JAVASCRIPT:alert(1)")]
    [InlineData("vbscript:MsgBox(1)")]
    [InlineData("data:text/html;base64,PHNjcmlwdD5hbGVydCgxKTwvc2NyaXB0Pg==")]
    [InlineData("ms-appinstaller:?source=https://bad.com/payload.appxbundle")]
    [InlineData("ms-msdt:/id PCWDiagnostic")]
    [InlineData("search-ms:query=test")]
    [InlineData("shell:AppsFolder")]
    [InlineData("shell:::{20D04FE0-3AEA-1069-A2D8-08002B30309D}")]
    public void ProtocolValidator_BlocksHazardousSchemes(string url)
    {
        bool isSafe = ProtocolValidator.IsSafeUrl(url, out string? reason);
        Assert.False(isSafe);
        Assert.NotNull(reason);
    }

    [Fact]
    public async Task JsonConfigRepository_EncryptsSecretsAtRest_AndDecryptsInMemory()
    {
        var vault = new WindowsDpapiSecretsVaultService();
        var repo = new JsonConfigRepository(_testDir, vault);

        const string rawSecretToken = "github_pat_11AAAAAAABBBBBB";

        var item = new TriggerItem
        {
            Name = "Secure Workflow",
            ActionType = ActionType.Workflow,
            Payload = new ActionPayload
            {
                WorkflowVariables =
                [
                    new WorkflowVariableDefinition
                    {
                        Name = "GITHUB_TOKEN",
                        Value = rawSecretToken,
                        IsSecret = true
                    },
                    new WorkflowVariableDefinition
                    {
                        Name = "PUBLIC_ENDPOINT",
                        Value = "https://api.github.com",
                        IsSecret = false
                    }
                ]
            }
        };

        await repo.SaveAsync([item]);

        // 1. Verify JSON file on disk has encrypted ciphertext for GITHUB_TOKEN
        string diskJson = await File.ReadAllTextAsync(repo.ConfigFilePath);
        Assert.DoesNotContain(rawSecretToken, diskJson);
        Assert.Contains("vault:dpapi:", diskJson);
        Assert.Contains("https://api.github.com", diskJson);

        // 2. Verify LoadAsync automatically decrypts the secret in memory
        var loadedItems = await repo.LoadAsync();
        var loadedItem = Assert.Single(loadedItems, x => x.Name == "Secure Workflow");
        var secretVar = Assert.Single(loadedItem.Payload.WorkflowVariables, x => x.Name == "GITHUB_TOKEN");
        Assert.True(secretVar.IsSecret);
        Assert.Equal(rawSecretToken, secretVar.Value);

        var publicVar = Assert.Single(loadedItem.Payload.WorkflowVariables, x => x.Name == "PUBLIC_ENDPOINT");
        Assert.False(publicVar.IsSecret);
        Assert.Equal("https://api.github.com", publicVar.Value);
    }

    [Fact]
    public void DwmHelper_ApplyBackdrop_SafeOnZeroHandle()
    {
        bool result = TriggerPoint.Infrastructure.Win32.DwmHelper.ApplyBackdrop(
            IntPtr.Zero,
            TriggerPoint.Infrastructure.Win32.BackdropType.Acrylic,
            isDarkMode: true);

        Assert.False(result);
    }
}

