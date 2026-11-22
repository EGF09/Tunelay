using System.Text;
using System.Text.Json;

namespace Tunelay.Core.Spotify;

/// <param name="Scope">Space-separated scopes Spotify granted; null for tokens saved by older versions.</param>
public sealed record TokenSet(string AccessToken, string RefreshToken, DateTimeOffset ExpiresAt, string? Scope = null)
{
    public bool ExpiresWithin(TimeSpan margin, DateTimeOffset now) => ExpiresAt - now <= margin;

    public bool HasScope(string scope) =>
        Scope?.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains(scope) == true;
}

/// <summary>Encrypts bytes at rest (DPAPI on Windows; a fake in tests).</summary>
public interface ITokenProtector
{
    byte[] Protect(byte[] plain);

    byte[] Unprotect(byte[] cipher);
}

/// <summary>Persists the token set encrypted with an <see cref="ITokenProtector"/>.</summary>
public sealed class TokenStore(string path, ITokenProtector protector)
{
    public void Save(TokenSet tokens)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var json = JsonSerializer.SerializeToUtf8Bytes(tokens);
        File.WriteAllBytes(path, protector.Protect(json));
    }

    public TokenSet? Load()
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            var json = protector.Unprotect(File.ReadAllBytes(path));
            return JsonSerializer.Deserialize<TokenSet>(json);
        }
        catch (Exception ex) when (ex is JsonException or IOException or System.Security.Cryptography.CryptographicException)
        {
            return null;
        }
    }

    public void Clear()
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}

/// <summary>Supplies bearer tokens to the API handler.</summary>
public interface IAccessTokenProvider
{
    Task<string?> GetAccessTokenAsync(CancellationToken ct);

    /// <summary>Forces a refresh after a 401. Returns null when no session exists.</summary>
    Task<string?> RefreshAccessTokenAsync(CancellationToken ct);
}

internal static class Base64Url
{
    public static string Encode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static byte[] Utf8(string s) => Encoding.UTF8.GetBytes(s);
}
