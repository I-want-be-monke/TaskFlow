using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using TaskFlow.Domain.Projects;
using TaskFlow.Domain.Tags;
using TaskFlow.Domain.TaskTags;
using TaskFlow.Domain.Tasks;
using TaskFlow.Infrastructure.Identity;
using TaskFlow.Infrastructure.Persistence.Interceptors;

namespace TaskFlow.Infrastructure.Persistence;

public sealed class TaskFlowDbContext
    : IdentityUserContext<ApplicationUser, Guid>, IDataProtectionKeyContext
{
    public TaskFlowDbContext(DbContextOptions<TaskFlowDbContext> options)
        : base(options)
    {
    }

    public DbSet<Project> Projects => Set<Project>();

    public DbSet<TaskItem> TaskItems => Set<TaskItem>();

    public DbSet<Tag> Tags => Set<Tag>();

    public DbSet<TaskTag> TaskTags => Set<TaskTag>();

    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.AddInterceptors(VersionConcurrencyInterceptor.Instance);
        base.OnConfiguring(optionsBuilder);
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(typeof(TaskFlowDbContext).Assembly);
    }
}
