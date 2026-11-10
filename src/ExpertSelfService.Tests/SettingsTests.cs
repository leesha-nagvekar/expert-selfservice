using System.Text;
using ExpertSelfService.Services;
using Microsoft.Extensions.Configuration;

namespace ExpertSelfService.Tests;

/// <summary>Settings from the environment and .env (CLAUDE.md ground rule 4).</summary>
public sealed class SettingsTests : IDisposable
{
    private const string Key = AppFactory.CodeKey;

    // Variable names only these tests use, because the environment is shared by every test.
    private const string Quoted = "ESS_TEST_QUOTED";
    private const string Preset = "ESS_TEST_PRESET";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "expert-self-service-settings", Guid.NewGuid().ToString("N"));

    public SettingsTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(Quoted, null);
        Environment.SetEnvironmentVariable(Preset, null);
        Directory.Delete(_root, recursive: true);
    }

    private static IConfiguration Config(string? databasePath, string? codeKey) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DATABASE_PATH"] = databasePath,
                ["CODE_KEY"] = codeKey,
            })
            .Build();

    private string CreateFile(params string[] parts)
    {
        var path = Path.Combine([_root, .. parts]);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "");
        return path;
    }

    [Fact]
    public void Env_file_with_bom_and_quotes_is_read_and_the_environment_wins()
    {
        var envFile = Path.Combine(_root, ".env");
        // Notepad and PowerShell may start the file with a BOM. The first line must still be read.
        File.WriteAllText(envFile, $"{Quoted}=\"a value with spaces\"\n{Preset}=from-file\n", new UTF8Encoding(true));
        Environment.SetEnvironmentVariable(Preset, "from-environment");

        AppSettings.LoadEnvFile(envFile);

        Assert.Equal("a value with spaces", Environment.GetEnvironmentVariable(Quoted));
        Assert.Equal("from-environment", Environment.GetEnvironmentVariable(Preset));
    }

    [Fact]
    public void A_missing_env_file_is_not_an_error()
    {
        AppSettings.LoadEnvFile(Path.Combine(_root, "no-such.env"));
    }

    [Fact]
    public void A_relative_database_path_is_read_from_the_project_folder()
    {
        var expected = CreateFile("other", "x.db");

        var settings = AppSettings.Load(Config("other/x.db", Key), _root);

        Assert.Equal(Path.GetFullPath(expected), settings.DatabasePath);
        Assert.Equal(Key, settings.CodeKey);
    }

    [Fact]
    public void Without_database_path_the_default_is_the_same_as_seed_py()
    {
        var expected = CreateFile("data", "expert_register.db");

        var settings = AppSettings.Load(Config(null, Key), _root);

        Assert.Equal(Path.GetFullPath(expected), settings.DatabasePath);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("too-short-key")]
    public void A_missing_or_short_code_key_is_refused(string? codeKey)
    {
        CreateFile("data", "expert_register.db");

        var error = Assert.Throws<InvalidOperationException>(() => AppSettings.Load(Config(null, codeKey), _root));

        Assert.Contains("CODE_KEY", error.Message);
    }

    [Fact]
    public void A_missing_database_is_refused_with_the_reset_command()
    {
        var error = Assert.Throws<InvalidOperationException>(() => AppSettings.Load(Config(null, Key), _root));

        Assert.Contains("seed.py --reset", error.Message);
    }

    [Fact]
    public void The_app_does_not_start_without_a_code_key()
    {
        using var app = AppFactory.With(codeKey: "");

        var error = Assert.ThrowsAny<Exception>(() => app.CreateClient());

        Assert.Contains("CODE_KEY", error.ToString());
    }

    [Fact]
    public void The_app_does_not_start_without_a_database()
    {
        using var app = AppFactory.With(createDatabase: false);

        var error = Assert.ThrowsAny<Exception>(() => app.CreateClient());

        Assert.Contains("seed.py --reset", error.ToString());
        Assert.False(File.Exists(app.DatabasePath)); // no empty database is created
    }
}
