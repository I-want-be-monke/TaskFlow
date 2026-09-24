using TaskFlow.DbMigrator;

using var shutdown = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    shutdown.Cancel();
};

return await DbMigratorApplication.RunFromEnvironmentAsync(shutdown.Token);
