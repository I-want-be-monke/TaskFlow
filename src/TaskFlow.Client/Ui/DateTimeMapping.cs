namespace TaskFlow.Client.Ui;

public static class DateTimeMapping
{
    public static DateTimeOffset? ToApi(DateTime? localValue)
    {
        if (localValue is null)
        {
            return null;
        }

        DateTime local = DateTime.SpecifyKind(localValue.Value, DateTimeKind.Local);
        return new DateTimeOffset(local).ToUniversalTime();
    }

    public static DateTime? ToLocal(DateTimeOffset? apiValue) =>
        apiValue?.ToLocalTime().DateTime;
}
