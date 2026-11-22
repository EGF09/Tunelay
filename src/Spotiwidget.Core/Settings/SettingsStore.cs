using System.Text.Json;
using System.Text.Json.Serialization;

namespace Spotiwidget.Core.Settings;

/// <summary>Loads and saves <see cref="AppSettings"/> as JSON. A missing or corrupt file yields defaults.</summary>
public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _path;
    private readonly object _gate = new();

    public SettingsStore(string path)
    {
        _path = path;
        Current = Load();
    }

    public static string DefaultDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Spotiwidget");

    public AppSettings Current { get; private set; }

    public event Action<AppSettings>? Saved;

    public AppSettings Load()
    {
        lock (_gate)
        {
            try
            {
                if (!File.Exists(_path))
                {
                    return Current = new AppSettings();
                }

                var loaded = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path), Options) ?? new AppSettings();
                // Fill hotkeys added in newer versions.
                foreach (var (action, combo) in AppSettings.DefaultHotkeys())
                {
                    loaded.Hotkeys.TryAdd(action, combo);
                }

                loaded.Moods ??= AppSettings.DefaultMoods();
                loaded.MoodPlaylists ??= new();
                loaded.IdleOpacity = Math.Clamp(loaded.IdleOpacity, 0, 1);
                loaded.ActiveOpacity = Math.Clamp(loaded.ActiveOpacity, 0.1, 1);

                return Current = loaded;
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                return Current = new AppSettings();
            }
        }
    }

    public void Save(AppSettings settings)
    {
        lock (_gate)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temp = _path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(settings, Options));
            File.Move(temp, _path, overwrite: true);
            Current = settings;
        }

        Saved?.Invoke(settings);
    }

    public void Update(Action<AppSettings> change)
    {
        change(Current);
        Save(Current);
    }
}
