using System.IO.Compression;

namespace TaskFlow.DevCli;

internal sealed class SubmissionPackCommands(RepositoryContext repository)
{
    private static readonly HashSet<string> ExcludedDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git",
        ".vs",
        ".idea",
        ".vscode",
        ".local",
        "bin",
        "obj",
        "artifacts",
        "TestResults",
        "coverage",
        "node_modules",
    };

    private readonly RepositoryContext _repository = repository ?? throw new ArgumentNullException(nameof(repository));

    public int Create(string outputPath)
    {
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            throw new ArgumentException("Output path cannot be empty.", nameof(outputPath));
        }

        string destination = Path.IsPathRooted(outputPath)
            ? Path.GetFullPath(outputPath)
            : _repository.Resolve(outputPath);

        Directory.CreateDirectory(Path.GetDirectoryName(destination) ?? _repository.Root);
        if (File.Exists(destination))
        {
            File.Delete(destination);
        }

        using var archive = ZipFile.Open(destination, ZipArchiveMode.Create);
        foreach (string file in EnumerateCandidateFiles())
        {
            if (!ShouldInclude(file, destination))
            {
                continue;
            }

            string entryName = Path.GetRelativePath(_repository.Root, file).Replace('\\', '/');
            archive.CreateEntryFromFile(file, entryName, CompressionLevel.Optimal);
        }

        Console.WriteLine($"Clean submission archive: {destination}");
        return 0;
    }

    private IEnumerable<string> EnumerateCandidateFiles()
    {
        var pendingDirectories = new Stack<string>();
        pendingDirectories.Push(_repository.Root);

        while (pendingDirectories.TryPop(out string? directory))
        {
            foreach (string file in Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly))
            {
                yield return file;
            }

            foreach (string childDirectory in Directory.EnumerateDirectories(directory, "*", SearchOption.TopDirectoryOnly))
            {
                if (!ExcludedDirectoryNames.Contains(Path.GetFileName(childDirectory)))
                {
                    pendingDirectories.Push(childDirectory);
                }
            }
        }
    }

    internal bool ShouldInclude(string filePath, string destinationPath)
    {
        string fullPath = Path.GetFullPath(filePath);
        if (string.Equals(fullPath, Path.GetFullPath(destinationPath), StringComparisonForPaths()))
        {
            return false;
        }

        string relativePath = Path.GetRelativePath(_repository.Root, fullPath);
        string[] segments = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (segments.Any(ExcludedDirectoryNames.Contains))
        {
            return false;
        }

        string fileName = Path.GetFileName(relativePath);
        if (fileName.Equals(".env.compose.local", StringComparison.OrdinalIgnoreCase) ||
            fileName.Equals(".env", StringComparison.OrdinalIgnoreCase) ||
            (fileName.StartsWith(".env.", StringComparison.OrdinalIgnoreCase) &&
             !fileName.Equals(".env.example", StringComparison.OrdinalIgnoreCase) &&
             !fileName.Equals(".env.compose.example", StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        return !fileName.EndsWith(".user", StringComparison.OrdinalIgnoreCase) &&
               !fileName.EndsWith(".suo", StringComparison.OrdinalIgnoreCase) &&
               !fileName.EndsWith(".log", StringComparison.OrdinalIgnoreCase) &&
               !fileName.EndsWith(".db", StringComparison.OrdinalIgnoreCase) &&
               !fileName.EndsWith(".db-shm", StringComparison.OrdinalIgnoreCase) &&
               !fileName.EndsWith(".db-wal", StringComparison.OrdinalIgnoreCase) &&
               !fileName.EndsWith(".coverage", StringComparison.OrdinalIgnoreCase) &&
               !fileName.EndsWith(".coveragexml", StringComparison.OrdinalIgnoreCase) &&
               !fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) &&
               !fileName.EndsWith(".7z", StringComparison.OrdinalIgnoreCase) &&
               !fileName.EndsWith(".rar", StringComparison.OrdinalIgnoreCase) &&
               !fileName.EndsWith(".Local.json", StringComparison.OrdinalIgnoreCase) &&
               !fileName.Equals("secrets.json", StringComparison.OrdinalIgnoreCase) &&
               !fileName.Equals(".DS_Store", StringComparison.OrdinalIgnoreCase) &&
               !fileName.Equals("Thumbs.db", StringComparison.OrdinalIgnoreCase);
    }

    private static StringComparison StringComparisonForPaths() =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
}
