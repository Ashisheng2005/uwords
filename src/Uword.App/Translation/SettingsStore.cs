using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Uword.App.Translation;

public sealed class SettingsStore
{
    private static readonly string Folder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Uword");
    private static readonly string ConfigPath = Path.Combine(Folder, "translation.json");
    private static readonly string KeyPath = Path.Combine(Folder, "api-key.dat");

    public TranslationSettings Load()
    {
        var settings = File.Exists(ConfigPath)
            ? JsonSerializer.Deserialize<TranslationSettings>(File.ReadAllText(ConfigPath)) ?? new()
            : new TranslationSettings();
        if (File.Exists(KeyPath))
            settings.ApiKey = Encoding.UTF8.GetString(ProtectedData.Unprotect(
                File.ReadAllBytes(KeyPath), null, DataProtectionScope.CurrentUser));
        return settings;
    }

    public void Save(TranslationSettings settings)
    {
        settings.Validate();
        Directory.CreateDirectory(Folder);
        string temporary = ConfigPath + ".tmp";
        string temporaryKey = KeyPath + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
            byte[] encrypted = ProtectedData.Protect(Encoding.UTF8.GetBytes(settings.ApiKey), null,
                DataProtectionScope.CurrentUser);
            File.WriteAllBytes(temporaryKey, encrypted);
            File.Move(temporaryKey, KeyPath, true);
            File.Move(temporary, ConfigPath, true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
            if (File.Exists(temporaryKey)) File.Delete(temporaryKey);
        }
    }
}
