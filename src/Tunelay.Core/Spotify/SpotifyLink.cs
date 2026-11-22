using System.Text.RegularExpressions;

namespace Tunelay.Core.Spotify;

/// <summary>The link back to Spotify that its policy asks for next to a song's name and cover.</summary>
public static partial class SpotifyLink
{
    /// <param name="uri">The playing item's Spotify URI. Unknown without an account.</param>
    /// <param name="appInstalled">Whether something handles spotify: links on this PC.</param>
    public static string For(string? uri, bool appInstalled)
    {
        // Handed to the Windows shell, so only well-formed Spotify ids get through.
        var match = Playable().Match(uri ?? "");
        if (!match.Success)
        {
            return appInstalled ? "spotify:" : "https://open.spotify.com/";
        }

        return appInstalled
            ? match.Value
            : $"https://open.spotify.com/{match.Groups[1].Value}/{match.Groups[2].Value}";
    }

    [GeneratedRegex(@"\Aspotify:(track|episode):([A-Za-z0-9]{1,64})\z")]
    private static partial Regex Playable();
}
