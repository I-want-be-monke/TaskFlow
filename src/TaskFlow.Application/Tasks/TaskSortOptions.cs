namespace TaskFlow.Application.Tasks;

public static class TaskSortOptions
{
    public const string CreatedAtAscending = "createdAt:asc";
    public const string CreatedAtDescending = "createdAt:desc";
    public const string DueAtAscending = "dueAt:asc";
    public const string DueAtDescending = "dueAt:desc";
    public const string PriorityAscending = "priority:asc";
    public const string PriorityDescending = "priority:desc";
    public const string TitleAscending = "title:asc";
    public const string TitleDescending = "title:desc";

    public static bool IsAllowed(string? value) => value is
        CreatedAtAscending or
        CreatedAtDescending or
        DueAtAscending or
        DueAtDescending or
        PriorityAscending or
        PriorityDescending or
        TitleAscending or
        TitleDescending;
}
