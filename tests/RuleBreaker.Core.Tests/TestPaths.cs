namespace RuleBreaker.Core.Tests;

using RuleBreaker.Core.Content;
using RuleBreaker.Core.Model;

/// <summary>Locates repository data from the test binaries so content tests read the real library.</summary>
internal static class TestPaths
{
    public static string RepoRoot { get; } = FindRepoRoot();

    public static string RulesDirectory => Path.Combine(RepoRoot, "data", "rules");

    public static IReadOnlyList<RuleDefinition> LoadLibrary(out IReadOnlyList<string> errors)
    {
        var files = Directory.GetFiles(RulesDirectory, "*.json").OrderBy(f => f, StringComparer.Ordinal).ToArray();
        return RuleLibraryLoader.LoadAll(files, out errors);
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "data", "rules")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate the repository's data/rules directory.");
    }
}
