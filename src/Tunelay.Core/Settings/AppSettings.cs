namespace Tunelay.Core.Settings;

public enum ScreenCorner
{
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight,
}

public enum HotkeyAction
{
    /// <summary>The "interaction key": makes the player card clickable (and shows moods).</summary>
    ToggleMenu,
    Like,
    PlayPause,
    Next,
    Previous,
    VolumeUp,
    VolumeDown,
    SaveToMood,
    CycleMood,

    /// <summary>Makes the foreground (windowed) game borderless fullscreen, or restores it.</summary>
    Borderless,

    /// <summary>Toggles move mode: the card and the mood dock can be dragged anywhere.</summary>
    MoveMode,

    Shuffle,

    /// <summary>Cycles repeat: off → playlist → song.</summary>
    Repeat,
}

/// <summary>How much animation runs while a fullscreen app (a game) is in front.</summary>
public enum GameAnimationLevel
{
    Off,

    /// <summary>All animations at 30 fps.</summary>
    Light,

    /// <summary>All animations at 60 fps, same as the desktop.</summary>
    Full,
}

public sealed record PlaylistRef(string Id, string Name)
{
    public string Uri => $"spotify:playlist:{Id}";
}

/// <summary>A user-editable mood. <see cref="Color"/> is "#RRGGBB".</summary>
public sealed record MoodDefinition(string Id, string Name, string Color);

public sealed class AppSettings
{
    public string ClientId { get; set; } = "";

    /// <summary>Default anchor of the player card when it has never been moved.</summary>
    public ScreenCorner Corner { get; set; } = ScreenCorner.BottomRight;

    /// <summary>Where the user dragged the card (DIPs, virtual screen); null = <see cref="Corner"/>.</summary>
    public double? CardLeft { get; set; }

    /// <summary>The card's top edge, or its bottom edge when <see cref="CardAnchorBottom"/> (so it grows away from the screen edge).</summary>
    public double? CardAnchor { get; set; }

    public bool CardAnchorBottom { get; set; }

    /// <summary>Card opacity while it is display-only (click-through), 0–1.</summary>
    public double IdleOpacity { get; set; } = 0.6;

    /// <summary>Card opacity while the interaction key has it unlocked, 0–1.</summary>
    public double ActiveOpacity { get; set; } = 1.0;

    /// <summary>Short UI animations (master switch).</summary>
    public bool Animations { get; set; } = true;

    /// <summary>Animation level while a fullscreen app is in front.</summary>
    public GameAnimationLevel GameAnimations { get; set; } = GameAnimationLevel.Light;

    /// <summary>Shows the mood buttons as a widget pinned to the desktop (behind windows).</summary>
    public bool ShowMoodDock { get; set; } = true;

    /// <summary>Where the user dragged the mood dock (DIPs); null = default spot.</summary>
    public double? DockLeft { get; set; }

    public double? DockTop { get; set; }

    /// <summary>The user's moods, in display order. Ids of the built-ins match older settings files.</summary>
    public List<MoodDefinition> Moods { get; set; } = DefaultMoods();

    /// <summary>Id of the active mood.</summary>
    public string? ActiveMood { get; set; }

    /// <summary>Playlist per mood id.</summary>
    public Dictionary<string, PlaylistRef> MoodPlaylists { get; set; } = new();

    public Dictionary<HotkeyAction, string> Hotkeys { get; set; } = DefaultHotkeys();

    public MoodDefinition? FindMood(string? id) => id is null ? null : Moods.FirstOrDefault(m => m.Id == id);

    public static List<MoodDefinition> DefaultMoods() =>
    [
        new("Energetic", "Enerjik", "#FF6B35"),
        new("Calm", "Huzurlu", "#2EC4B6"),
        new("Focused", "Odaklanmış", "#4C7DF0"),
        new("Melancholic", "Melankolik", "#8E6CC9"),
        new("Night", "Gece", "#5D6FB0"),
    ];

    public static Dictionary<HotkeyAction, string> DefaultHotkeys() => new()
    {
        [HotkeyAction.ToggleMenu] = "Ctrl+Alt+Space",
        [HotkeyAction.Like] = "Ctrl+Alt+L",
        [HotkeyAction.PlayPause] = "Ctrl+Alt+P",
        [HotkeyAction.Next] = "Ctrl+Alt+Right",
        [HotkeyAction.Previous] = "Ctrl+Alt+Left",
        [HotkeyAction.VolumeUp] = "Ctrl+Alt+Up",
        [HotkeyAction.VolumeDown] = "Ctrl+Alt+Down",
        [HotkeyAction.SaveToMood] = "Ctrl+Alt+S",
        [HotkeyAction.CycleMood] = "Ctrl+Alt+M",
        [HotkeyAction.Borderless] = "Ctrl+Alt+B",
        [HotkeyAction.MoveMode] = "Ctrl+Alt+D",
        [HotkeyAction.Shuffle] = "Ctrl+Alt+K",
        [HotkeyAction.Repeat] = "Ctrl+Alt+R",
    };
}
