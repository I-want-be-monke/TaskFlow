using TaskFlow.Client.Ui;

namespace TaskFlow.IntegrationTests.Client;

public sealed class DateTimeMappingTests
{
    [Fact]
    public void DueDate_RoundTripsThroughApiUtcMapping()
    {
        var local = new DateTime(2026, 9, 24, 18, 30, 0, DateTimeKind.Unspecified);

        DateTimeOffset? api = DateTimeMapping.ToApi(local);
        DateTime? roundTrip = DateTimeMapping.ToLocal(api);

        Assert.NotNull(api);
        Assert.Equal(TimeSpan.Zero, api.Value.Offset);
        Assert.NotNull(roundTrip);
        Assert.Equal(local, roundTrip.Value);
    }
}
