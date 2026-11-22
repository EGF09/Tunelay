namespace Spotiwidget.Core.Spotify;

// Shapes mirror the Spotify Web API (snake_case is mapped by the serializer options).

public sealed record PlaybackState(
    bool IsPlaying,
    bool ShuffleState,
    int? ProgressMs,
    Device? Device,
    Track? Item);

public sealed record Device(string? Id, string Name, bool IsActive, int? VolumePercent);

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

internal sealed record Paging<T>(IReadOnlyList<T> Items, string? Next);

internal sealed record DeviceList(IReadOnlyList<Device> Devices);

internal sealed record ErrorEnvelope(ErrorBody? Error);

internal sealed record ErrorBody(int Status, string? Message, string? Reason);
