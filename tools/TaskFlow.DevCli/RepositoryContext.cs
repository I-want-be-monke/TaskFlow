namespace TaskFlow.DevCli;

internal sealed class RepositoryContext
{
    private RepositoryContext(string root)
    {
        Root = root;
    }

    public string Root { get; }

    public string Resolve(string relativePath) => Path.GetFullPath(Path.Combine(Root, relativePath));

    internal static RepositoryContext ForRoot(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        return new RepositoryContext(Path.GetFullPath(root));
    }

    public static RepositoryContext Discover()
    {
        foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
        {
            var directory = new DirectoryInfo(start);
            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "TaskFlow.sln")) &&
                    File.Exists(Path.Combine(directory.FullName, "docker-compose.yml")))
                {
                    return new RepositoryContext(directory.FullName);
                }

                directory = directory.Parent;
            }
        }

        throw new InvalidOperationException(
            "Не удалось найти корень TaskFlow. Запустите команду из каталога проекта или его подпапки.");
    }
}
