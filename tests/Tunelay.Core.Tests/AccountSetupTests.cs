using System.Net;
using System.Net.Sockets;
using Tunelay.Core.Settings;
using Tunelay.Core.Spotify;

namespace Tunelay.Core.Tests;

/// <summary>Connecting and disconnecting the optional account. Only temporary folders are touched.</summary>
public sealed class AccountSetupTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "tunelay-account-" + Guid.NewGuid().ToString("N"));

    private sealed class PlainProtector : ITokenProtector
    {
        public byte[] Protect(byte[] plain) => plain;

        public byte[] Unprotect(byte[] cipher) => cipher;
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true); // created by this test
        }
    }

    private string DataFile(string folder, string file) => Path.Combine(_root, folder, file);

    private SpotifyAuthSession SignedInSession()
    {
        var store = new TokenStore(DataFile(AppInfo.Name, "tokens.bin"), new PlainProtector());
        store.Save(new TokenSet("access", "refresh", DateTimeOffset.UtcNow.AddHours(1)));
        var http = new HttpClient(new StubHandler(_ => throw new InvalidOperationException("No network in this test.")));
        return new SpotifyAuthSession(new TokenEndpointClient(http), store, () => "id");
    }

    private static SpotifyClient ClientAnswering(Func<HttpResponseMessage> answer) =>
        new(new HttpClient(new StubHandler(_ => answer())) { BaseAddress = SpotifyClient.BaseAddress });

    [Theory]
    [InlineData("0123456789abcdef0123456789abcdef", true)]
    [InlineData("0123456789ABCDEF0123456789ABCDEF", true)]
    [InlineData("0123456789abcdef", false)]
    [InlineData("http://127.0.0.1:5543/callback", false)]
    [InlineData("", false)]
    public void Client_id_is_32_hex_characters(string value, bool valid) =>
        Assert.Equal(valid, AccountSetup.IsValidClientId(value));

    [Fact]
    public void Only_a_new_user_sees_the_welcome()
    {
        Assert.True(AccountSetup.NeedsWelcome(new AppSettings(), signedIn: false));
        Assert.False(AccountSetup.NeedsWelcome(new AppSettings(), signedIn: true));
        Assert.False(AccountSetup.NeedsWelcome(new AppSettings { ClientId = "old" }, signedIn: false));
        Assert.False(AccountSetup.NeedsWelcome(new AppSettings { WelcomeSeen = true }, signedIn: false));
    }

    [Fact]
    public async Task Disconnect_deletes_spotify_data_in_both_folders_and_keeps_the_rest()
    {
        var session = SignedInSession();
        var settings = new SettingsStore(DataFile(AppInfo.Name, "settings.json"));
        settings.Update(s =>
        {
            s.ClientId = "mine";
            s.IdleOpacity = 0.3;
            s.ActiveMood = "Calm";
            s.MoodPlaylists["Calm"] = new PlaylistRef("p1", "Quiet");
        });
        Directory.CreateDirectory(Path.Combine(_root, AppInfo.LegacyName));
        File.WriteAllText(DataFile(AppInfo.LegacyName, "tokens.bin"), "secret");
        File.WriteAllText(DataFile(AppInfo.LegacyName, "settings.json"),
            """{"ClientId":"mine","ActiveMood":"Calm","MoodPlaylists":{"Calm":{"Id":"p1","Name":"Quiet"}}}""");

        Assert.True(await AccountSetup.DisconnectAsync(session, settings, _root));

        Assert.False(session.IsSignedIn);
        Assert.False(File.Exists(DataFile(AppInfo.Name, "tokens.bin")));
        Assert.False(File.Exists(DataFile(AppInfo.LegacyName, "tokens.bin")));

        var saved = new SettingsStore(DataFile(AppInfo.Name, "settings.json")).Current;
        Assert.Empty(saved.MoodPlaylists);
        Assert.Null(saved.ActiveMood);
        Assert.Equal("mine", saved.ClientId);
        Assert.Equal(0.3, saved.IdleOpacity);
        Assert.Equal(5, saved.Moods.Count);

        var legacy = File.ReadAllText(DataFile(AppInfo.LegacyName, "settings.json"));
        Assert.DoesNotContain("Quiet", legacy);
        Assert.DoesNotContain("Calm", legacy);
        Assert.Contains("mine", legacy);
    }

    [Fact]
    public async Task An_unreadable_old_file_still_signs_out_and_is_reported()
    {
        var session = SignedInSession();
        Directory.CreateDirectory(Path.Combine(_root, AppInfo.LegacyName));
        File.WriteAllText(DataFile(AppInfo.LegacyName, "settings.json"), "{ broken");

        Assert.False(await AccountSetup.DisconnectAsync(session, new SettingsStore(DataFile(AppInfo.Name, "settings.json")), _root));
        Assert.False(session.IsSignedIn);
    }

    [Fact]
    public async Task Disconnect_works_without_an_old_folder()
    {
        Assert.True(await AccountSetup.DisconnectAsync(
            SignedInSession(), new SettingsStore(DataFile(AppInfo.Name, "settings.json")), _root));
        Assert.False(Directory.Exists(Path.Combine(_root, AppInfo.LegacyName)));
    }

    [Theory]
    [InlineData("""{"error":{"status":403,"message":"User not registered in the Developer Dashboard"}}""")]
    [InlineData("Check settings on developer.spotify.com/dashboard, the user may not be registered.")]
    public async Task An_account_outside_the_user_list_is_signed_out_with_a_clear_reason(string body)
    {
        var session = SignedInSession();
        var api = ClientAnswering(() => TokenTests.Json(body, HttpStatusCode.Forbidden));

        var error = await AccountSetup.VerifyAsync(session, api, default);

        Assert.Contains("kullanıcı listesinde değil", error);
        Assert.False(session.IsSignedIn);
    }

    [Fact]
    public async Task A_spotify_outage_while_verifying_keeps_the_new_session()
    {
        var session = SignedInSession();
        var api = ClientAnswering(() => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

        Assert.Null(await AccountSetup.VerifyAsync(session, api, default));
        Assert.True(session.IsSignedIn);
    }

    [Fact]
    public void A_taken_port_is_explained()
    {
        var taken = new TcpListener(IPAddress.Loopback, 0);
        taken.Start();
        try
        {
            var port = ((IPEndPoint)taken.LocalEndpoint).Port;
            var ex = Assert.Throws<SpotifyAuthException>(() => LoopbackLogin.EnsurePortFree(port));
            Assert.Contains(port.ToString(), ex.Message);
        }
        finally
        {
            taken.Stop();
        }
    }

    [Fact]
    public async Task Signing_out_forgets_what_was_playing()
    {
        var signedIn = true;
        var api = ClientAnswering(() => TokenTests.Json(
            """{"is_playing":true,"shuffle_state":false,"item":{"id":"t","uri":"spotify:track:t","name":"Song","duration_ms":200000,"artists":[]}}"""));
        await using var playback = new PlaybackService(api, () => signedIn);
        playback.Start();
        await Until(() => playback.Current is not null);

        signedIn = false;
        playback.RefreshSoon();

        await Until(() => playback.Current is null);
    }

    private static async Task Until(Func<bool> condition)
    {
        for (var i = 0; i < 100 && !condition(); i++)
        {
            await Task.Delay(20);
        }

        Assert.True(condition());
    }
}
