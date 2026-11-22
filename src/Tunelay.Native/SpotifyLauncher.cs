using System.Diagnostics;
using System.IO;
using Microsoft.Win32;
using Tunelay.Core.Spotify;

namespace Tunelay.Native;

/// <summary>Opens Spotify minimized, without giving it focus: Store alias, desktop exe, then the spotify: link.</summary>
public sealed class SpotifyLauncher : ISpotifyLauncher
{
    private const int ShowMinimizedNoActivate = 7; // SW_SHOWMINNOACTIVE

    public bool IsRunning
    {
        get
        {
            var processes = Process.GetProcessesByName("Spotify");
            foreach (var process in processes)
            {
                process.Dispose();
            }

            return processes.Length > 0;
        }
    }

    public bool TryLaunch() => Candidates().Any(c => Open(c.Target, c.Arguments));

    internal static IEnumerable<(string Target, string? Arguments)> Candidates()
    {
        var store = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\WindowsApps\Spotify.exe");
        var desktop = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Spotify\Spotify.exe");
        foreach (var exe in new[] { store, desktop }.Where(File.Exists))
        {
            yield return (exe, "--minimized");
        }

        // Only when something handles the link, otherwise Windows shows a "pick an app" dialog over the game.
        if (CanOpenLinks)
        {
            yield return ("spotify:", null);
        }
    }

    /// <summary>Whether something on this PC handles spotify: links.</summary>
    public static bool CanOpenLinks
    {
        get
        {
            using var protocol = Registry.ClassesRoot.OpenSubKey("spotify");
            return protocol is not null;
        }
    }

    // ShellExecute reports success with a value above 32.
    private static bool Open(string target, string? arguments) =>
        Win32.ShellExecute(0, "open", target, arguments, null, ShowMinimizedNoActivate) > 32;
}
