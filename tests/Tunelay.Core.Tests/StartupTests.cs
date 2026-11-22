using System.Net;
using Tunelay.Core.Spotify;

namespace Tunelay.Core.Tests;

// Never touches the registry: the store is in memory.
public class StartupSwitchTests
{
    private const string Exe = @"C:\Apps\Tunelay\Tunelay.exe";
    private const string Command = "\"" + Exe + "\" --background";

    private sealed class FakeStore : IStartupStore
    {
        public string? Command { get; set; }

        public byte[]? Approval { get; set; }

        public Exception? Failure { get; init; }

        public string? ReadCommand() => Failure is null ? Command : throw Failure;

        public byte[]? ReadApproval() => Approval;

        public void WriteCommand(string command) => Command = Failure is null ? command : throw Failure;

        public void DeleteCommand() => Command = Failure is null ? null : throw Failure;
    }

    [Fact]
    public void Off_by_default()
    {
        Assert.Equal(StartupState.Off, new StartupSwitch(new FakeStore(), Exe).State);
    }

    [Fact]
    public void Turning_on_registers_a_quoted_background_start()
    {
        var store = new FakeStore();
        var startup = new StartupSwitch(store, Exe);

        Assert.Null(startup.SetEnabled(true));

        Assert.Equal(Command, store.Command);
        Assert.Equal(StartupState.On, startup.State);
    }

    [Fact]
    public void Turning_off_removes_it()
    {
        var store = new FakeStore { Command = Command };
        var startup = new StartupSwitch(store, Exe);

        Assert.Null(startup.SetEnabled(false));

        Assert.Null(store.Command);
        Assert.Equal(StartupState.Off, startup.State);
    }

    [Theory]
    [InlineData(2, StartupState.On)]
    [InlineData(6, StartupState.On)]
    [InlineData(3, StartupState.BlockedByWindows)]
    [InlineData(7, StartupState.BlockedByWindows)]
    public void Follows_what_task_manager_says(byte first, StartupState expected)
    {
        var store = new FakeStore { Command = Command, Approval = [first, 0, 0, 0] };

        Assert.Equal(expected, new StartupSwitch(store, Exe).State);
    }

    [Fact]
    public void Never_overrides_task_manager()
    {
        var store = new FakeStore { Approval = [3, 0, 0, 0] };
        var startup = new StartupSwitch(store, Exe);

        startup.SetEnabled(true);

        Assert.Equal([3, 0, 0, 0], store.Approval);
        Assert.Equal(Command, store.Command); // listed in Task Manager, so it can be re-enabled there
        Assert.Equal(StartupState.BlockedByWindows, startup.State);
        Assert.Contains("Görev Yöneticisi", startup.Hint);
    }

    [Fact]
    public void Moved_app_is_shown_and_fixed_by_turning_on()
    {
        var store = new FakeStore { Command = "\"C:\\Old\\Tunelay.exe\" --background" };
        var startup = new StartupSwitch(store, Exe);
        Assert.Equal(StartupState.OtherLocation, startup.State);

        startup.SetEnabled(true);

        Assert.Equal(Command, store.Command);
    }

    [Theory]
    [InlineData(@"C:\Code\Tunelay\src\Tunelay.App\bin\Debug\net10.0\Tunelay.exe", true)]
    [InlineData(@"C:\Code\Tunelay\publish\Tunelay.exe", false)]
    public void Warns_when_running_from_a_build_folder(string exe, bool warns)
    {
        var startup = new StartupSwitch(new FakeStore(), exe);

        Assert.Equal(warns, startup.Hint.Contains("geliştirme klasör"));
    }

    [Fact]
    public void Too_long_a_path_is_refused()
    {
        var store = new FakeStore();

        Assert.NotNull(new StartupSwitch(store, @"C:\" + new string('a', 260) + @"\Tunelay.exe").SetEnabled(true));
        Assert.Null(store.Command);
    }

    [Fact]
    public void A_refused_registry_is_reported_not_thrown()
    {
        var startup = new StartupSwitch(new FakeStore { Failure = new UnauthorizedAccessException() }, Exe);

        Assert.Equal(StartupState.Off, startup.State);
        Assert.NotNull(startup.SetEnabled(true));
    }

    [Theory]
    [InlineData("--background", true)]
    [InlineData("--BACKGROUND", true)]
    [InlineData("--other", false)]
    public void Recognizes_a_background_start(string argument, bool expected)
    {
        Assert.Equal(expected, StartupSwitch.IsBackgroundStart([argument]));
    }
}

public class QuietStartTests
{
    private static async Task<List<string>> ErrorsAfterPollingAsync(bool quietStart, Func<int, HttpResponseMessage> respond)
    {
        var calls = 0;
        var client = new SpotifyClient(new HttpClient(new StubHandler(_ => respond(Interlocked.Increment(ref calls)))));
        var errors = new List<string>();
        var playback = new PlaybackService(client, () => true, quietStart);
        playback.Error += errors.Add;

        playback.Start();
        for (var i = 0; i < 100 && Volatile.Read(ref calls) < 4; i++)
        {
            playback.RefreshSoon();
            await Task.Delay(20);
        }

        await playback.DisposeAsync();
        return errors;
    }

    [Fact]
    public async Task Background_start_without_a_network_stays_silent()
    {
        Assert.Empty(await ErrorsAfterPollingAsync(true, _ => throw new HttpRequestException("offline")));
    }

    [Fact]
    public async Task Once_online_a_lost_connection_is_reported_again()
    {
        var errors = await ErrorsAfterPollingAsync(true, call => call == 2
            ? new HttpResponseMessage(HttpStatusCode.NoContent)
            : throw new HttpRequestException("offline"));

        Assert.Single(errors);
    }

    [Fact]
    public async Task Background_start_still_reports_real_problems()
    {
        Assert.Single(await ErrorsAfterPollingAsync(true, _ => new HttpResponseMessage(HttpStatusCode.Forbidden)));
    }

    [Fact]
    public async Task Normal_start_reports_being_offline()
    {
        Assert.Single(await ErrorsAfterPollingAsync(false, _ => throw new HttpRequestException("offline")));
    }
}
