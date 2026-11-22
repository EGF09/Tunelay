using System.Security;

namespace Tunelay.Core;

/// <summary>Where Windows keeps the start-up entry.</summary>
public interface IStartupStore
{
    string? ReadCommand();

    /// <summary>Task Manager's own on/off mark. Read only: the user's choice there is never overridden.</summary>
    byte[]? ReadApproval();

    void WriteCommand(string command);

    void DeleteCommand();
}

public enum StartupState
{
    Off,
    On,

    /// <summary>Registered, but switched off in Task Manager.</summary>
    BlockedByWindows,

    /// <summary>Registered for another copy or an old folder.</summary>
    OtherLocation,
}

/// <summary>"Start with Windows". Windows' own entry is the only truth, so nothing is kept in the settings file.</summary>
public sealed class StartupSwitch(IStartupStore store, string exePath)
{
    public const string BackgroundArgument = "--background";

    private const int MaxCommandLength = 260; // Windows ignores longer Run commands

    private string Command => $"\"{exePath}\" {BackgroundArgument}";

    public static bool IsBackgroundStart(IEnumerable<string> args) =>
        args.Contains(BackgroundArgument, StringComparer.OrdinalIgnoreCase);

    public StartupState State
    {
        get
        {
            try
            {
                if (store.ReadCommand() is not { } registered)
                {
                    return StartupState.Off;
                }

                // An odd first byte is how Task Manager marks "disabled".
                if (store.ReadApproval() is [var first, ..] && first % 2 == 1)
                {
                    return StartupState.BlockedByWindows;
                }

                return string.Equals(registered, Command, StringComparison.OrdinalIgnoreCase)
                    ? StartupState.On
                    : StartupState.OtherLocation;
            }
            catch (Exception ex) when (IsAccessFailure(ex))
            {
                return StartupState.Off;
            }
        }
    }

    /// <summary>What the user should know about the current state; empty when there is nothing to say.</summary>
    public string Hint => State switch
    {
        StartupState.BlockedByWindows => "Görev Yöneticisi'nde kapatılmış. Açmak için Görev Yöneticisi → Başlangıç → Tunelay → Etkinleştir.",
        StartupState.OtherLocation => "Kayıt başka bir konumu gösteriyor. Açarsanız bu kopyaya güncellenir.",
        _ when RunsFromBuildFolder => "Uygulama geliştirme klasöründen (bin) çalışıyor. Klasör temizlenir veya taşınırsa açılışta başlamaz. Kalıcı kullanım için yayımlanmış exe'den açın.",
        _ => "",
    };

    private bool RunsFromBuildFolder => exePath.Contains(@"\bin\", StringComparison.OrdinalIgnoreCase);

    /// <summary>Null on success, otherwise what went wrong.</summary>
    public string? SetEnabled(bool on)
    {
        if (on && Command.Length > MaxCommandLength)
        {
            return "Uygulamanın klasör yolu Windows başlangıcı için çok uzun.";
        }

        try
        {
            if (on)
            {
                store.WriteCommand(Command);
            }
            else
            {
                store.DeleteCommand();
            }

            return null;
        }
        catch (Exception ex) when (IsAccessFailure(ex))
        {
            return "Windows başlangıç ayarı değiştirilemedi.";
        }
    }

    private static bool IsAccessFailure(Exception ex) => ex is IOException or UnauthorizedAccessException or SecurityException;
}
