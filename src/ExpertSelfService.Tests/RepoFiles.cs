namespace ExpertSelfService.Tests;

/// <summary>Finds files in the repo, wherever the tests run from.</summary>
internal static class RepoFiles
{
    public static string Root { get; } = FindRoot();

    public static string App => Path.Combine(Root, "src", "ExpertSelfService");

    public static string Read(params string[] parts) =>
        File.ReadAllText(Path.Combine([Root, .. parts]));

    private static string FindRoot()
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (folder is not null && !File.Exists(Path.Combine(folder.FullName, "global.json")))
        {
            folder = folder.Parent;
        }

        return folder?.FullName ?? throw new InvalidOperationException("global.json not found");
    }
}
