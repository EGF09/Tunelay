namespace Tunelay.Core.Tests;

public sealed class DataMigrationTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("tunelay-").FullName;

    private string Legacy => Path.Combine(_root, AppInfo.LegacyName);

    private string Current => Path.Combine(_root, AppInfo.Name);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private void SeedLegacy()
    {
        Directory.CreateDirectory(Legacy);
        File.WriteAllText(Path.Combine(Legacy, "settings.json"), """{"ClientId":"cid"}""");
        File.WriteAllBytes(Path.Combine(Legacy, "tokens.bin"), [1, 2, 3]);
    }

    [Fact]
    public void Copies_settings_and_session_and_leaves_the_old_folder_untouched()
    {
        SeedLegacy();

        Assert.Equal(Current, AppInfo.ResolveDataDirectory(_root));

        foreach (var name in new[] { "settings.json", "tokens.bin" })
        {
            Assert.Equal(File.ReadAllBytes(Path.Combine(Legacy, name)), File.ReadAllBytes(Path.Combine(Current, name)));
        }
    }

    [Fact]
    public void A_later_sign_out_is_not_undone_by_copying_again()
    {
        SeedLegacy();
        AppInfo.ResolveDataDirectory(_root);
        File.Delete(Path.Combine(Current, "tokens.bin"));

        AppInfo.ResolveDataDirectory(_root);

        Assert.False(File.Exists(Path.Combine(Current, "tokens.bin")));
    }

    [Fact]
    public void Never_overwrites_existing_new_data()
    {
        SeedLegacy();
        Directory.CreateDirectory(Current);
        File.WriteAllText(Path.Combine(Current, "settings.json"), "new");

        AppInfo.ResolveDataDirectory(_root);

        Assert.Equal("new", File.ReadAllText(Path.Combine(Current, "settings.json")));
    }

    [Fact]
    public void A_fresh_install_uses_the_new_folder()
    {
        Assert.Equal(Current, AppInfo.ResolveDataDirectory(_root));
        Assert.False(Directory.Exists(Legacy));
    }

    [Fact]
    public void A_failed_copy_keeps_using_the_old_folder_and_retries_next_time()
    {
        SeedLegacy();
        using (new FileStream(Path.Combine(Legacy, "tokens.bin"), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.Equal(Legacy, AppInfo.ResolveDataDirectory(_root));
            Assert.False(Directory.Exists(Current), "A half-copied folder would block the retry.");
        }

        Assert.Equal(Current, AppInfo.ResolveDataDirectory(_root));
        Assert.True(File.Exists(Path.Combine(Current, "tokens.bin")));
    }
}
