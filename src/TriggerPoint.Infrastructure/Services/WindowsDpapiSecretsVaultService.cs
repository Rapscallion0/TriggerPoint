using System;
using System.Security.Cryptography;
using System.Text;
using Serilog;
using TriggerPoint.Core.Contracts;

namespace TriggerPoint.Infrastructure.Services;

public class WindowsDpapiSecretsVaultService : ISecretsVaultService
{
    private static readonly ILogger Logger = Log.ForContext<WindowsDpapiSecretsVaultService>();
    public const string DpapiPrefix = "vault:dpapi:";
    public const string B64FallbackPrefix = "vault:b64:";
    private static readonly byte[] Entropy = "TriggerPoint_Vault_Entropy_v1"u8.ToArray();

    public string Protect(string plainText)
    {
        if (string.IsNullOrWhiteSpace(plainText))
        {
            return plainText ?? string.Empty;
        }

        if (IsProtected(plainText))
        {
            return plainText;
        }

        try
        {
            var plainBytes = Encoding.UTF8.GetBytes(plainText);
            var cipherBytes = ProtectedData.Protect(plainBytes, Entropy, DataProtectionScope.CurrentUser);
            return DpapiPrefix + Convert.ToBase64String(cipherBytes);
        }
        catch (PlatformNotSupportedException)
        {
            Logger.Warning("DPAPI not supported on this platform. Falling back to encoded format.");
            return B64FallbackPrefix + Convert.ToBase64String(Encoding.UTF8.GetBytes(plainText));
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to encrypt secret via DPAPI.");
            return B64FallbackPrefix + Convert.ToBase64String(Encoding.UTF8.GetBytes(plainText));
        }
    }

    public string Unprotect(string cipherText)
    {
        if (string.IsNullOrEmpty(cipherText))
        {
            return string.Empty;
        }

        if (!IsProtected(cipherText))
        {
            return cipherText;
        }

        if (cipherText.StartsWith(DpapiPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var base64 = cipherText[DpapiPrefix.Length..];
            try
            {
                var cipherBytes = Convert.FromBase64String(base64);
                var plainBytes = ProtectedData.Unprotect(cipherBytes, Entropy, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(plainBytes);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to decrypt DPAPI secret payload.");
                return string.Empty;
            }
        }

        if (cipherText.StartsWith(B64FallbackPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var base64 = cipherText[B64FallbackPrefix.Length..];
            try
            {
                var plainBytes = Convert.FromBase64String(base64);
                return Encoding.UTF8.GetString(plainBytes);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to decode fallback secret payload.");
                return string.Empty;
            }
        }

        return cipherText;
    }

    public bool IsProtected(string? value)
    {
        if (string.IsNullOrEmpty(value)) return false;
        return value.StartsWith(DpapiPrefix, StringComparison.OrdinalIgnoreCase) ||
               value.StartsWith(B64FallbackPrefix, StringComparison.OrdinalIgnoreCase) ||
               value.StartsWith("vault:", StringComparison.OrdinalIgnoreCase);
    }
}
