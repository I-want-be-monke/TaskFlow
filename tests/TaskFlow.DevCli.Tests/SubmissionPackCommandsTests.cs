using TaskFlow.DevCli;

namespace TaskFlow.DevCli.Tests;

public sealed class SubmissionPackCommandsTests
{
    [Theory]
    [InlineData("src/TaskFlow.Domain/Project.cs", true)]
    [InlineData("src/TaskFlow.Domain/bin/Release/output.dll", false)]
    [InlineData("src/TaskFlow.Domain/obj/project.assets.json", false)]
    [InlineData(".env.compose.local", false)]
    [InlineData(".env.compose.example", true)]
    [InlineData("TestResults/results.trx", false)]
    [InlineData("node_modules/package/index.js", false)]
    [InlineData(".DS_Store", false)]
    [InlineData("Thumbs.db", false)]
    public void ShouldInclude_FiltersBuildOutputAndSecrets(string relativePath, bool expected)
    {
        using var directory = new TemporaryDirectory();
        var repository = RepositoryContext.ForRoot(directory.Path);
        var pack = new SubmissionPackCommands(repository);
        string file = Path.Combine(directory.Path, relativePath.Replace('/', Path.DirectorySeparatorChar));
        string destination = Path.Combine(directory.Path, "submission.zip");

        Assert.Equal(expected, pack.ShouldInclude(file, destination));
    }
}
