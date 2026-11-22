namespace Tunelay.Core.Spotify;

// Shapes mirror the Spotify Web API (snake_case is mapped by the serializer options).

/// <param name="RepeatState">Spotify's "off", "context" (repeat playlist) or "track".</param>
public sealed record PlaybackState(
    bool IsPlaying,
    bool ShuffleState,
    int? ProgressMs,
    Device? Device,
    Track? Item,
    string? RepeatState = null);

public static class RepeatMode
{
    public const string Off = "off";
    public const string Context = "context";
    public const string Track = "track";

    /// <summary>off → context → track → off, like Spotify's own button.</summary>
    public static string Next(string? current) => current switch
    {
        Context => Track,
        Track => Off,
        _ => Context,
    };
}

/// <param name="Type">Spotify's device type, e.g. "Computer" or "Smartphone".</param>
public sealed record Device(string? Id, string Name, bool IsActive, int? VolumePercent, string? Type = null);

public sealed record Track(string Id, string Uri, string Name, int DurationMs, IReadOnlyList<Artist> Artists, Album? Album)
{
    public string ArtistNames => string.Join(", ", Artists.Select(a => a.Name));

    public string? ImageUrl => Album?.Images is { Count: > 0 } images
        ? images.OrderBy(i => Math.Abs((i.Width ?? 300) - 300)).First().Url
        : null;
}

public sealed record Artist(string Name);

public sealed record Album(string Name, IReadOnlyList<Image> Images);

public sealed record Image(string Url, int? Width, int? Height);

public sealed record SimplePlaylist(string Id, string Name);

/// <param name="Items">Spotify returns it only to the playlist's owner and collaborators.</param>
internal sealed record PlaylistAccess(UserRef? Owner, bool? Collaborative, ItemsInfo? Items);

internal sealed record UserRef(string? Id);

internal sealed record ItemsInfo(int? Total);

internal sealed record Paging<T>(IReadOnlyList<T> Items, string? Next);

internal sealed record DeviceList(IReadOnlyList<Device> Devices);

internal sealed record ErrorEnvelope(ErrorBody? Error);

internal sealed record ErrorBody(int Status, string? Message, string? Reason);
