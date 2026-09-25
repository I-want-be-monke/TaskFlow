using TaskFlow.DevCli;

namespace TaskFlow.DevCli.Tests;

public sealed class CliArgumentsTests
{
    [Fact]
    public void Parse_PackOutput_PreservesPositionalPath()
    {
        CliArguments parsed = CliArguments.Parse(["pack", "artifacts/custom.zip"]);

        Assert.Equal("pack", parsed.Command);
        Assert.Equal("artifacts/custom.zip", parsed.SinglePositionalOrDefault("unused.zip", "pack"));
    }

    [Fact]
    public void Parse_UnknownOption_Throws()
    {
        Assert.Throws<ArgumentException>(() => CliArguments.Parse(["up", "--unknown"]));
    }
}
