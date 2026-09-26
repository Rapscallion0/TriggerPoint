using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Serilog;
using TriggerPoint.Core.Contracts;

namespace TriggerPoint.Infrastructure.Services;

public class PortableAesSecretsVaultService : ISecretsVaultService
{
    private static readonly ILogger Logger = Log.ForContext<PortableAesSecretsVaultService>();

    public const string AesGcmPrefix = "vault:aes-gcm:";
    public const string DpapiPrefix = "vault:dpapi:";
    public const string B64FallbackPrefix = "vault:b64:";
    private const int NonceSizeBytes = 12;
    private const int TagSizeBytes = 16;
    private const int KeySizeBytes = 32;

    private readonly byte[] _key;
    private readonly WindowsDpapiSecretsVaultService _dpapiFallback = new();

    public PortableAesSecretsVaultService(string? keyFilePath = null)
    {
        _key = LoadOrCreateKey(keyFilePath);
    }

    private static byte[] LoadOrCreateKey(string? keyFilePath)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(keyFilePath) && File.Exists(keyFilePath))
            {
                var existingB64 = File.ReadAllText(keyFilePath, Encoding.UTF8).Trim();
                var keyBytes = Convert.FromBase64String(existingB64);
                if (keyBytes.Length == KeySizeBytes)
                {
                    return keyBytes;
                }
            }

            var newKey = RandomNumberGenerator.GetBytes(KeySizeBytes);

            if (!string.IsNullOrWhiteSpace(keyFilePath))
            {
                var dir = Path.GetDirectoryName(keyFilePath);
                if (!string.IsNullOrWhiteSpace(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                File.WriteAllText(keyFilePath, Convert.ToBase64String(newKey), Encoding.UTF8);
            }

            return newKey;
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "Failed to persist vault key to {Path}. Using in-memory fallback key.", keyFilePath);
            return RandomNumberGenerator.GetBytes(KeySizeBytes);
        }
    }

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
            var nonce = RandomNumberGenerator.GetBytes(NonceSizeBytes);
            var tag = new byte[TagSizeBytes];
            var cipherBytes = new byte[plainBytes.Length];

            using var aes = new AesGcm(_key, TagSizeBytes);
            aes.Encrypt(nonce, plainBytes, cipherBytes, tag);

            // Payload: nonce (12) + tag (16) + cipherBytes (N)
            var combined = new byte[NonceSizeBytes + TagSizeBytes + cipherBytes.Length];
            Buffer.BlockCopy(nonce, 0, combined, 0, NonceSizeBytes);
            Buffer.BlockCopy(tag, 0, combined, NonceSizeBytes, TagSizeBytes);
            Buffer.BlockCopy(cipherBytes, 0, combined, NonceSizeBytes + TagSizeBytes, cipherBytes.Length);

            return AesGcmPrefix + Convert.ToBase64String(combined);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to encrypt secret via AES-GCM. Falling back to base64 format.");
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

        if (cipherText.StartsWith(AesGcmPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var base64 = cipherText[AesGcmPrefix.Length..];
            try
            {
                var combined = Convert.FromBase64String(base64);
                if (combined.Length < NonceSizeBytes + TagSizeBytes)
                {
                    Logger.Warning("Malformed AES-GCM secret payload (too short).");
                    return string.Empty;
                }

                var nonce = new byte[NonceSizeBytes];
                var tag = new byte[TagSizeBytes];
                var cipherLength = combined.Length - NonceSizeBytes - TagSizeBytes;
                var cipherBytes = new byte[cipherLength];
                var plainBytes = new byte[cipherLength];

                Buffer.BlockCopy(combined, 0, nonce, 0, NonceSizeBytes);
                Buffer.BlockCopy(combined, NonceSizeBytes, tag, 0, TagSizeBytes);
                Buffer.BlockCopy(combined, NonceSizeBytes + TagSizeBytes, cipherBytes, 0, cipherLength);

                using var aes = new AesGcm(_key, TagSizeBytes);
                aes.Decrypt(nonce, cipherBytes, tag, plainBytes);

                return Encoding.UTF8.GetString(plainBytes);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to decrypt AES-GCM secret payload.");
                return string.Empty;
            }
        }

        if (cipherText.StartsWith(DpapiPrefix, StringComparison.OrdinalIgnoreCase))
        {
            // Attempt DPAPI decryption fallback (e.g. if config was copied from an installed machine)
            var dpapiResult = _dpapiFallback.Unprotect(cipherText);
            if (!string.IsNullOrEmpty(dpapiResult))
            {
                return dpapiResult;
            }

            Logger.Warning("DPAPI secret could not be decrypted on this host.");
            return string.Empty;
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
        return value.StartsWith(AesGcmPrefix, StringComparison.OrdinalIgnoreCase) ||
               value.StartsWith(DpapiPrefix, StringComparison.OrdinalIgnoreCase) ||
               value.StartsWith(B64FallbackPrefix, StringComparison.OrdinalIgnoreCase) ||
               value.StartsWith("vault:", StringComparison.OrdinalIgnoreCase);
    }
}
