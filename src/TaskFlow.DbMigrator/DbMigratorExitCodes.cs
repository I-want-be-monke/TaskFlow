namespace TaskFlow.DbMigrator;

public static class DbMigratorExitCodes
{
    public const int Success = 0;
    public const int ConfigurationError = 2;
    public const int LockTimeout = 3;
    public const int MigrationFailed = 4;
    public const int Cancelled = 130;
}
