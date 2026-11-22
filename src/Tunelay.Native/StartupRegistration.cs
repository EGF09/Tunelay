using Microsoft.Win32;
using Tunelay.Core;

namespace Tunelay.Native;

/// <summary>The current user's Run key; needs no administrator rights.</summary>
public sealed class StartupRegistration : IStartupStore
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

    public string? ReadCommand()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(AppInfo.Name) as string;
    }

    public byte[]? ReadApproval()
    {
        using var key = Registry.CurrentUser.OpenSubKey(ApprovedKey);
        return key?.GetValue(AppInfo.Name) as byte[];
    }

    public void WriteCommand(string command)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        key.SetValue(AppInfo.Name, command, RegistryValueKind.String);
    }

    public void DeleteCommand()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        key?.DeleteValue(AppInfo.Name, throwOnMissingValue: false);
    }
}
