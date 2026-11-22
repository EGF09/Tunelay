using System.Net;
using System.Text;
using Tunelay.Core.Spotify;

namespace Tunelay.Core.Tests;

public class PkceTests
{
    [Fact]
    public void Challenge_matches_rfc7636_example()
    {
        Assert.Equal(
            "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM",
            PkceAuth.ComputeChallenge("dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk"));
    }

    [Fact]
    public void Verifier_uses_unreserved_alphabet_and_valid_length()
    {
        var v = PkceAuth.GenerateVerifier();
        Assert.InRange(v.Length, 43, 128);
        Assert.Matches("^[A-Za-z0-9_-]+$", v);
        Assert.NotEqual(v, PkceAuth.GenerateVerifier());
    }

    [Fact]
    public void Authorize_url_carries_pkce_parameters()
    {
        var url = new Uri(PkceAuth.BuildAuthorizeUrl("client123", "chal", "st8"));
        var q = System.Web.HttpUtility.ParseQueryString(url.Query);
        Assert.Equal("client123", q["client_id"]);
        Assert.Equal("code", q["response_type"]);
        Assert.Equal("S256", q["code_challenge_method"]);
        Assert.Equal("chal", q["code_challenge"]);
        Assert.Equal("st8", q["state"]);
        Assert.Equal(PkceAuth.RedirectUri, q["redirect_uri"]);
        Assert.Contains("user-modify-playback-state", q["scope"]);
    }
}

public class TokenTests
{
    [Fact]
    public async Task Refresh_keeps_previous_refresh_token_when_omitted()
    {
        var http = new HttpClient(new StubHandler(_ => Json("""{"access_token":"new","expires_in":3600}""")));
        var set = await new TokenEndpointClient(http).RefreshAsync("cid", "old-refresh", default);
        Assert.Equal("new", set.AccessToken);
        Assert.Equal("old-refresh", set.RefreshToken);
    }

    [Fact]
    public async Task Refresh_failure_throws_auth_exception()
    {
        var http = new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("""{"error":"invalid_grant"}"""),
        }));
        await Assert.ThrowsAsync<SpotifyAuthException>(() => new TokenEndpointClient(http).RefreshAsync("cid", "r", default));
    }

    [Fact]
    public void Token_store_round_trips_through_protector()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".bin");
        var store = new TokenStore(path, new XorProtector());
        var tokens = new TokenSet("a", "r", DateTimeOffset.UtcNow.AddHours(1));
        store.Save(tokens);

        Assert.DoesNotContain("\"a\"", Encoding.UTF8.GetString(File.ReadAllBytes(path)));
        Assert.Equal(tokens, store.Load());

        store.Clear();
        Assert.Null(store.Load());
    }

    [Fact]
    public async Task Session_refreshes_expiring_token_and_persists_it()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".bin");
        var store = new TokenStore(path, new XorProtector());
        store.Save(new TokenSet("stale", "r1", DateTimeOffset.UtcNow.AddSeconds(10)));

        var calls = 0;
        var http = new HttpClient(new StubHandler(_ =>
        {
            calls++;
            return Json("""{"access_token":"fresh","refresh_token":"r2","expires_in":3600}""");
        }));
        var session = new SpotifyAuthSession(new TokenEndpointClient(http), store, () => "cid");

        Assert.Equal("fresh", await session.GetAccessTokenAsync(default));
        Assert.Equal("fresh", await session.GetAccessTokenAsync(default));
        Assert.Equal(1, calls);
        Assert.Equal("r2", store.Load()!.RefreshToken);
    }

    [Fact]
    public async Task Session_keeps_granted_scope_when_refresh_omits_it()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".bin");
        var store = new TokenStore(path, new XorProtector());
        store.Save(new TokenSet("stale", "r1", DateTimeOffset.UtcNow.AddSeconds(-1), "user-library-modify streaming"));
        var http = new HttpClient(new StubHandler(_ => Json("""{"access_token":"fresh","expires_in":3600}""")));
        var session = new SpotifyAuthSession(new TokenEndpointClient(http), store, () => "cid");

        await session.GetAccessTokenAsync(default);

        Assert.True(session.HasScope("user-library-modify"));
        Assert.False(session.HasScope("user-library"));
    }

    [Fact]
    public void Token_saved_by_older_version_loads_without_scope()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".bin");
        var json = Encoding.UTF8.GetBytes("""{"AccessToken":"a","RefreshToken":"r","ExpiresAt":"2030-01-01T00:00:00+00:00"}""");
        var protector = new XorProtector();
        File.WriteAllBytes(path, protector.Protect(json));

        var loaded = new TokenStore(path, protector).Load();

        Assert.Equal("r", loaded!.RefreshToken);
        Assert.Null(loaded.Scope);
        Assert.False(loaded.HasScope("user-library-modify"));
    }

    [Fact]
    public async Task Session_signs_out_when_refresh_is_rejected()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".bin");
        var store = new TokenStore(path, new XorProtector());
        store.Save(new TokenSet("stale", "revoked", DateTimeOffset.UtcNow.AddSeconds(-5)));
        var http = new HttpClient(new StubHandler(_ => Json("""{"error":"invalid_grant"}""", HttpStatusCode.BadRequest)));
        var session = new SpotifyAuthSession(new TokenEndpointClient(http), store, () => "cid");
        bool? signedIn = null;
        session.SignedInChanged += v => signedIn = v;

        Assert.Null(await session.GetAccessTokenAsync(default));
        Assert.False(session.IsSignedIn);
        Assert.False(signedIn);
        Assert.Null(store.Load());
    }

    internal static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
}

internal sealed class XorProtector : ITokenProtector
{
    public byte[] Protect(byte[] plain) => plain.Select(b => (byte)(b ^ 0x5A)).ToArray();

    public byte[] Unprotect(byte[] cipher) => Protect(cipher);
}

internal sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    public List<HttpRequestMessage> Requests { get; } = [];

    public List<string?> Bodies { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Requests.Add(request);
        Bodies.Add(request.Content is null ? null : await request.Content.ReadAsStringAsync(ct));
        return respond(request);
    }
}
