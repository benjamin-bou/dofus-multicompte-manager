using System.Text.Json;
using System.Text.Json.Serialization;

namespace DofusMulticompte.Core;

/// <summary>Lit et écrit les réglages en JSON.</summary>
public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly object _lock = new();

    public string FilePath { get; }

    public SettingsStore(string filePath) => FilePath = filePath;

    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DofusMulticompte", "settings.json");

    public AppSettings Load()
    {
        lock (_lock)
        {
            try
            {
                if (!File.Exists(FilePath)) return new AppSettings();
                var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Options) ?? new AppSettings();
                // Le dictionnaire désérialisé est sensible à la casse : on le reconstruit.
                settings.Characters = new Dictionary<string, CharacterProfile>(settings.Characters ?? [], StringComparer.OrdinalIgnoreCase);
                settings.Hotkeys ??= new HotkeySettings();
                settings.Hotkeys.ReplicateClick ??= "";
                settings.CharacterBar ??= new CharacterBarSettings();
                if (settings.ProcessNames is null || settings.ProcessNames.Count == 0) settings.ProcessNames = ["Dofus"];
                return settings;
            }
            catch (JsonException)
            {
                // Fichier corrompu : on le met de côté plutôt que de planter.
                try { File.Copy(FilePath, FilePath + ".corrompu", overwrite: true); } catch (IOException) { }
                return new AppSettings();
            }
        }
    }

    public void Save(AppSettings settings)
    {
        lock (_lock)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(settings, Options));
            File.Move(tmp, FilePath, overwrite: true);
        }
    }
}
