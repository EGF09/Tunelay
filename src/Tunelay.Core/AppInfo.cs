namespace Tunelay.Core;

/// <summary>The app's name and where its data lives.</summary>
public static class AppInfo
{
    public const string Name = "Tunelay";

    /// <summary>The name up to November 2026. Needed to find old data and an old running copy.</summary>
    public const string LegacyName = "Spotiwidget";

    private static readonly string[] DataFiles = ["settings.json", "tokens.bin"];

    /// <summary>
    /// Returns the data folder, copying the old one the first time. The copy is all or nothing:
    /// if it fails, the old folder is used for this run and the copy is retried on the next start.
    /// </summary>
    public static string ResolveDataDirectory(string root)
    {
        var current = Path.Combine(root, Name);
        var legacy = Path.Combine(root, LegacyName);

        // An existing new folder always wins, so a sign-out is never undone by a second copy.
        if (Directory.Exists(current) || !Directory.Exists(legacy))
        {
            return current;
        }

        var staging = Path.Combine(root, $"{Name}.migrating-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(staging);
            foreach (var file in DataFiles.Where(f => File.Exists(Path.Combine(legacy, f))))
            {
                File.Copy(Path.Combine(legacy, file), Path.Combine(staging, file));
            }

            Directory.Move(staging, current);
            return current;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            DiscardStaging(staging);
            return legacy;
        }
    }

    private static void DiscardStaging(string staging)
    {
        try
        {
            // Only ever the uniquely named folder created above, never user data.
            if (Directory.Exists(staging))
            {
                Directory.Delete(staging, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
