using System.IO;
using System.Text.Json;

namespace ExtractUtil.App.Themes;

// Appearance preferences are deliberately separate from extraction and password settings.
public sealed class AppearanceSettingsStore
{
    public AppearanceSettingsStore(string? path = null) => SettingsPath = path ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Zxtract", "appearance.json");

    public string SettingsPath { get; }

    public string? LoadThemeId()
    {
        try
        {
            return JsonSerializer.Deserialize<Settings>(File.ReadAllText(SettingsPath))?.ThemeId;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    public bool TrySave(string themeId)
    {
        var temporaryPath = SettingsPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(SettingsPath))!);
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(new Settings { ThemeId = themeId }));
            File.Move(temporaryPath, SettingsPath, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
        finally
        {
            try { File.Delete(temporaryPath); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    private sealed class Settings
    {
        public string? ThemeId { get; set; }
    }
}
