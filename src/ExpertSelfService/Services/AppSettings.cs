namespace ExpertSelfService.Services;

/// <summary>
/// Settings from the environment and .env (CLAUDE.md ground rule 4: no secrets in code or Git).
/// The app refuses to start without a code key or a database.
/// </summary>
public sealed record AppSettings(string DatabasePath, string CodeKey)
{
    /// <summary>Same default as settings.py, so seed.py and the app use the same file.</summary>
    public const string DefaultDatabasePath = "data/expert_register.db";

    /// <summary>A short key could be guessed, and then every code hash could be reversed.</summary>
    public const int MinimumKeyLength = 32;

    /// <summary>The repo root: the first folder upwards that holds global.json.</summary>
    public static string FindProjectRoot(string start)
    {
        var folder = new DirectoryInfo(start);
        while (folder is not null && !File.Exists(Path.Combine(folder.FullName, "global.json")))
        {
            folder = folder.Parent;
        }

        return folder?.FullName
            ?? throw new InvalidOperationException($"global.json not found above {start}. Run the app from the repo.");
    }

    /// <summary>
    /// Puts the values from .env into the environment. A variable that is already set wins
    /// (as in settings.py). A missing file is fine: the values may come from the environment.
    /// </summary>
    public static void LoadEnvFile(string path)
    {
        if (File.Exists(path))
        {
            DotNetEnv.Env.NoClobber().Load(path);
        }
    }

    public static AppSettings Load(IConfiguration configuration, string root)
    {
        var key = configuration["CODE_KEY"]?.Trim();
        if (string.IsNullOrEmpty(key) || key.Length < MinimumKeyLength)
        {
            throw new InvalidOperationException(
                $"CODE_KEY in .env is missing or shorter than {MinimumKeyLength} characters. See Install in README.md.");
        }

        var configured = configuration["DATABASE_PATH"];
        var relative = string.IsNullOrWhiteSpace(configured) ? DefaultDatabasePath : configured.Trim();
        // A relative path is read from the project folder, never from where the app was started.
        var databasePath = Path.GetFullPath(Path.Combine(root, relative));
        if (!File.Exists(databasePath))
        {
            throw new InvalidOperationException(
                $"No database at {databasePath}. Build it with: .venv\\Scripts\\python.exe seed.py --reset");
        }

        return new AppSettings(databasePath, key);
    }
}
