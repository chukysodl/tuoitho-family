using System.Diagnostics;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text.Json;
using System.Text.Json.Nodes;
using TuoiTho.Core.Policy;

namespace TuoiTho.BrowserHost;

public sealed record BrowserDeploymentResult(string ExtensionId, bool GeneratedNewKey);

/// <summary>
/// Creates and validates the staged browser-extension identity. The RSA public key stays only in
/// the staged manifest; its Chrome extension ID is also the trusted native-messaging origin.
/// </summary>
public static class BrowserDeploymentBootstrapper
{
    private static readonly JsonSerializerOptions IndentedJson = new() { WriteIndented = true };

    public static BrowserDeploymentResult Create(
        string extensionDirectory,
        string configurationPath,
        BrowserControlConfiguration? firstInstallConfiguration = null)
    {
        var manifestPath = RequireManifest(extensionDirectory);
        var existingConfiguration = TryLoadConfiguration(configurationPath);
        var key = TryReadManifestKey(manifestPath);
        var generatedNewKey = false;

        if (key is null)
        {
            // An existing trusted config means an update must retain its staged key. Generating a
            // replacement here would silently change the extension identity and native origin.
            if (existingConfiguration is not null)
                throw new InvalidOperationException("The staged extension key is missing. Run M4-BROWSER-REPAIR.cmd; do not create a new identity during update.");

            key = GeneratePublicKey();
            WriteManifestKey(manifestPath, key);
            generatedNewKey = true;
        }

        var extensionId = GetExtensionIdFromKey(key);
        if (existingConfiguration is not null && !string.Equals(existingConfiguration.ExtensionId, extensionId, StringComparison.Ordinal))
            throw new InvalidOperationException("The staged extension identity does not match browser-control.json. Run M4-BROWSER-REPAIR.cmd.");

        var configuration = (existingConfiguration ?? firstInstallConfiguration ?? CreateFirstInstallConfiguration(extensionId)) with
        {
            ExtensionId = extensionId
        };
        configuration.Validate();
        WriteConfiguration(configurationPath, configuration);
        WriteRuntimeConfig(extensionDirectory, configuration);
        return new BrowserDeploymentResult(extensionId, generatedNewKey);
    }

    /// <summary>Creates one replacement identity only when a staged key was genuinely lost.</summary>
    public static BrowserDeploymentResult Repair(
        string extensionDirectory,
        string configurationPath,
        BrowserControlConfiguration? firstInstallConfiguration = null)
    {
        var manifestPath = RequireManifest(extensionDirectory);
        var key = TryReadManifestKey(manifestPath);
        var generatedNewKey = key is null;
        key ??= GeneratePublicKey();
        WriteManifestKey(manifestPath, key);

        var extensionId = GetExtensionIdFromKey(key);
        var existingConfiguration = TryLoadConfiguration(configurationPath);
        var configuration = (existingConfiguration ?? firstInstallConfiguration ?? CreateFirstInstallConfiguration(extensionId)) with
        {
            ExtensionId = extensionId
        };
        configuration.Validate();
        WriteConfiguration(configurationPath, configuration);
        WriteRuntimeConfig(extensionDirectory, configuration);
        return new BrowserDeploymentResult(extensionId, generatedNewKey);
    }

    /// <summary>Copies only a previously trusted staged key into a freshly copied source manifest.</summary>
    public static string PreserveKeyDuringUpdate(string priorManifestPath, string stagedManifestPath)
    {
        var key = TryReadManifestKey(priorManifestPath)
            ?? throw new InvalidOperationException("The prior staged extension key is missing. Use M4-BROWSER-REPAIR.cmd.");
        WriteManifestKey(stagedManifestPath, key);
        return GetExtensionIdFromKey(key);
    }

    public static string GetExtensionId(string manifestPath)
    {
        var key = TryReadManifestKey(manifestPath)
            ?? throw new InvalidOperationException("The staged extension manifest has no valid key.");
        return GetExtensionIdFromKey(key);
    }

    private static string RequireManifest(string extensionDirectory)
    {
        var manifestPath = Path.Combine(extensionDirectory, "manifest.json");
        if (!File.Exists(manifestPath))
            throw new InvalidOperationException("The staged browser extension manifest is unavailable.");
        return manifestPath;
    }

    private static BrowserControlConfiguration? TryLoadConfiguration(string configurationPath) =>
        File.Exists(configurationPath) ? BrowserControlConfiguration.Load(configurationPath) : null;

    private static BrowserControlConfiguration CreateFirstInstallConfiguration(string extensionId)
    {
        var sid = WindowsIdentity.GetCurrent().User?.Value
            ?? throw new InvalidOperationException("The current Windows user SID is unavailable.");
        return new BrowserControlConfiguration(extensionId, "m1-child", Process.GetCurrentProcess().SessionId, sid, TestMode: true);
    }

    private static string? TryReadManifestKey(string manifestPath)
    {
        var manifest = JsonNode.Parse(File.ReadAllText(manifestPath))?.AsObject()
            ?? throw new InvalidOperationException("The staged browser extension manifest is invalid.");
        var key = manifest["key"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(key)) return null;

        try
        {
            using var rsa = RSA.Create();
            rsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(key), out _);
            return key;
        }
        catch (Exception exception) when (exception is FormatException or CryptographicException)
        {
            throw new InvalidOperationException("The staged browser extension key is invalid.", exception);
        }
    }

    private static string GeneratePublicKey()
    {
        using var rsa = RSA.Create(2048);
        return Convert.ToBase64String(rsa.ExportSubjectPublicKeyInfo());
    }

    private static void WriteManifestKey(string manifestPath, string key)
    {
        var manifest = JsonNode.Parse(File.ReadAllText(manifestPath))?.AsObject()
            ?? throw new InvalidOperationException("The staged browser extension manifest is invalid.");
        manifest["key"] = key;
        File.WriteAllText(manifestPath, manifest.ToJsonString(IndentedJson));
    }

    private static void WriteConfiguration(string configurationPath, BrowserControlConfiguration configuration)
    {
        var directory = Path.GetDirectoryName(configurationPath);
        if (string.IsNullOrWhiteSpace(directory))
            throw new InvalidOperationException("The browser control configuration path is invalid.");
        Directory.CreateDirectory(directory);
        File.WriteAllText(configurationPath, JsonSerializer.Serialize(configuration, IndentedJson));
    }

    private static void WriteRuntimeConfig(string extensionDirectory, BrowserControlConfiguration configuration) =>
        File.WriteAllText(Path.Combine(extensionDirectory, "m4-runtime-config.js"),
            $"export const TEST_MODE = {configuration.TestMode.ToString().ToLowerInvariant()};{Environment.NewLine}");

    private static string GetExtensionIdFromKey(string key) =>
        ToChromeExtensionId(SHA256.HashData(Convert.FromBase64String(key)));

    private static string ToChromeExtensionId(byte[] keyHash)
    {
        if (keyHash.Length < 16) throw new InvalidOperationException("The extension key hash is invalid.");
        var characters = new char[32];
        for (var index = 0; index < 16; index++)
        {
            characters[index * 2] = (char)('a' + (keyHash[index] >> 4));
            characters[index * 2 + 1] = (char)('a' + (keyHash[index] & 0x0f));
        }
        return new string(characters);
    }
}