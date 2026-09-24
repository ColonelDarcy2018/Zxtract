using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.IO;

namespace ExtractUtil.App.Services;

public sealed class LocalPasswordSettings
{
    public IReadOnlyList<string> CustomInferenceRules { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> SavedPasswords { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Stores user password helpers under LocalAppData. Password values are protected
/// with Windows DPAPI for the current user before they are written to JSON.
/// </summary>
public sealed class LocalPasswordSettingsStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("ExtractUtil.PasswordLibrary.v1");
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };
    private readonly string _legacySettingsPath;

    public LocalPasswordSettingsStore()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        SettingsPath = Path.Combine(appData, "Zxtract", "password-settings.json");
        _legacySettingsPath = Path.Combine(appData, "ExtractUtil", "password-settings.json");
    }

    public string SettingsPath { get; }

    public LocalPasswordSettings Load()
    {
        var sourcePath = File.Exists(SettingsPath) ? SettingsPath : _legacySettingsPath;
        if (!File.Exists(sourcePath))
        {
            return new LocalPasswordSettings();
        }

        try
        {
            var json = File.ReadAllText(sourcePath, Encoding.UTF8);
            var document = JsonSerializer.Deserialize<StoredSettings>(json, _jsonOptions) ?? new StoredSettings();
            var passwords = new List<string>();
            foreach (var protectedValue in document.ProtectedPasswords)
            {
                try
                {
                    var encrypted = Convert.FromBase64String(protectedValue);
                    var plain = ProtectedData.Unprotect(encrypted, Entropy, DataProtectionScope.CurrentUser);
                    var value = Encoding.UTF8.GetString(plain);
                    if (!string.IsNullOrWhiteSpace(value) && !passwords.Contains(value, StringComparer.Ordinal))
                    {
                        passwords.Add(value);
                    }
                }
                catch (Exception) when (protectedValue.Length > 0)
                {
                    // Ignore a damaged or no-longer-decryptable entry while keeping the rest usable.
                }
            }

            return new LocalPasswordSettings
            {
                CustomInferenceRules = document.CustomInferenceRules
                    .Where(rule => !string.IsNullOrWhiteSpace(rule))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                SavedPasswords = passwords
            };
        }
        catch (JsonException)
        {
            return new LocalPasswordSettings();
        }
        catch (IOException)
        {
            return new LocalPasswordSettings();
        }
    }

    public void Save(IEnumerable<string> customInferenceRules, IEnumerable<string> passwords)
    {
        var stored = new StoredSettings
        {
            CustomInferenceRules = customInferenceRules
                .Where(rule => !string.IsNullOrWhiteSpace(rule))
                .Select(rule => rule.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            ProtectedPasswords = passwords
                .Where(password => !string.IsNullOrWhiteSpace(password))
                .Distinct(StringComparer.Ordinal)
                .Select(Protect)
                .ToList()
        };

        var directory = Path.GetDirectoryName(SettingsPath)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = SettingsPath + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(stored, _jsonOptions), Encoding.UTF8);
        File.Move(temporaryPath, SettingsPath, true);
    }

    private static string Protect(string value)
    {
        var plain = Encoding.UTF8.GetBytes(value);
        var encrypted = ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(encrypted);
    }

    private sealed class StoredSettings
    {
        public int SchemaVersion { get; init; } = 1;
        public List<string> CustomInferenceRules { get; init; } = new();
        public List<string> ProtectedPasswords { get; init; } = new();
    }
}
