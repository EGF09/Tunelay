namespace Tunelay.Core.Spotify;

/// <summary>Starts the Spotify desktop app (Windows implementation in Tunelay.Native).</summary>
public interface ISpotifyLauncher
{
    bool IsRunning { get; }

    /// <summary>Starts it minimized, without activating it. False when it isn't installed.</summary>
    bool TryLaunch();
}
