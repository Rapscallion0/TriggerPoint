using System;
using System.IO;
using TriggerPoint.Infrastructure.Services;
using Xunit;

namespace TriggerPoint.Tests;

public class PortableAesSecretsVaultServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _keyFile;

    public PortableAesSecretsVaultServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "VaultTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _keyFile = Path.Combine(_tempDir, "vault.key");
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, true);
            }
        }
        catch
        {
            // Ignore test cleanup errors
        }
    }

    [Fact]
    public void ProtectAndUnprotect_RoundTrip_Succeeds()
    {
        var vault = new PortableAesSecretsVaultService(_keyFile);
        string secret = "MySuperSecretApiKey_12345!@#$%^&*()";

        string protectedText = vault.Protect(secret);

        Assert.StartsWith(PortableAesSecretsVaultService.AesGcmPrefix, protectedText);
        Assert.True(vault.IsProtected(protectedText));

        string decrypted = vault.Unprotect(protectedText);
        Assert.Equal(secret, decrypted);
    }

    [Fact]
    public void KeyPersistence_AllowsDecryptionAcrossInstances()
    {
        var vault1 = new PortableAesSecretsVaultService(_keyFile);
        string secret = "PasswordSharedAcrossInstances";
        string cipherText = vault1.Protect(secret);

        // Second instance loading the same keyfile
        var vault2 = new PortableAesSecretsVaultService(_keyFile);
        string decrypted = vault2.Unprotect(cipherText);

        Assert.Equal(secret, decrypted);
    }

    [Fact]
    public void DifferentKeys_FailDecryptionGracefully()
    {
        var vault1 = new PortableAesSecretsVaultService(_keyFile);
        string secret = "ConfidentialData";
        string cipherText = vault1.Protect(secret);

        // Different key file
        string keyFile2 = Path.Combine(_tempDir, "other_vault.key");
        var vault2 = new PortableAesSecretsVaultService(keyFile2);

        string decrypted = vault2.Unprotect(cipherText);
        Assert.Empty(decrypted);
    }

    [Fact]
    public void EmptyOrNull_ReturnsUnchanged()
    {
        var vault = new PortableAesSecretsVaultService(_keyFile);

        Assert.Equal("", vault.Protect(""));
        Assert.Equal("", vault.Unprotect(""));
    }
}
