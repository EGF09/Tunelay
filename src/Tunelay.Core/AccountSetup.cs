using System.Text.Json;
using System.Text.Json.Nodes;
using Tunelay.Core.Settings;
using Tunelay.Core.Spotify;

namespace Tunelay.Core;

/// <summary>Connecting and disconnecting the optional Spotify account.</summary>
public static class AccountSetup
{
    private const int ClientIdLength = 32;

    public static bool IsValidClientId(string value) => value.Length == ClientIdLength && value.All(char.IsAsciiHexDigit);

    /// <summary>People who used the app before (a Client ID or a session) go straight to the widget.</summary>
    public static bool NeedsWelcome(AppSettings settings, bool signedIn) =>
        !signedIn && !settings.WelcomeSeen && string.IsNullOrWhiteSpace(settings.ClientId);

    /// <summary>Signs in or renews the permissions. Returns the problem to show, or null when connected.</summary>
    public static async Task<string?> ConnectAsync(
        SpotifyAuthSession session, ISpotifyApi api, Action<string> openBrowser, CancellationToken ct)
    {
        try
        {
            await session.SignInAsync(openBrowser, ct).ConfigureAwait(false);
            return await VerifyAsync(session, api, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return "Giriş iptal edildi veya süresi doldu.";
        }
        catch (SpotifyAuthException ex)
        {
            return ex.Message;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return "Oturum bu bilgisayara kaydedilemedi.";
        }
        catch (Exception ex) when (SpotifyFailure.IsExpected(ex))
        {
            return SpotifyFailure.Describe(ex);
        }
    }

    /// <summary>
    /// An account outside the app's user list signs in fine and then gets 403 on every call, so it is
    /// signed out again and no-account mode keeps working. Other failures keep the session.
    /// </summary>
    internal static async Task<string?> VerifyAsync(SpotifyAuthSession session, ISpotifyApi api, CancellationToken ct)
    {
        try
        {
            await api.GetPlaybackAsync(ct).ConfigureAwait(false);
        }
        catch (SpotifyApiException ex) when (ex.Reason == SpotifyClient.NotRegisteredReason)
        {
            await session.SignOutAsync().ConfigureAwait(false);
            return ex.Message;
        }
        catch (Exception ex) when (SpotifyFailure.IsExpected(ex))
        {
            // Signed in; whatever this was shows up again on the next poll.
        }

        return null;
    }

    /// <summary>
    /// Deletes the Spotify data as the developer policy requires: tokens and playlist mappings, also in the
    /// old app's folder. Keeps the Client ID and everything else. False when some file couldn't be cleaned.
    /// </summary>
    /// <param name="root">The folder that holds both data folders (%AppData%).</param>
    public static async Task<bool> DisconnectAsync(SpotifyAuthSession session, SettingsStore settings, string root)
    {
        var legacy = Path.Combine(root, AppInfo.LegacyName);
        var clean = Try(() => settings.Update(s =>
        {
            s.MoodPlaylists = new();
            s.ActiveMood = null;
            s.WelcomeSeen = true;
        }));
        clean &= Try(() => new FileInfo(Path.Combine(legacy, "tokens.bin")).Delete());
        clean &= Try(() => ClearMappings(Path.Combine(legacy, "settings.json")));

        try
        {
            await session.SignOutAsync().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            clean = false;
        }

        return clean;
    }

    private static void ClearMappings(string path)
    {
        if (!File.Exists(path) || JsonNode.Parse(File.ReadAllText(path)) is not JsonObject saved)
        {
            return;
        }

        saved.Remove(nameof(AppSettings.MoodPlaylists));
        saved.Remove(nameof(AppSettings.ActiveMood));
        File.WriteAllText(path, saved.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    private static bool Try(Action action)
    {
        try
        {
            action();
            return true;
        }
        catch (DirectoryNotFoundException)
        {
            return true; // nothing there to clean
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return false;
        }
    }
}
