using Microsoft.AspNetCore.Identity;

namespace TaskFlow.Infrastructure.Identity;

public sealed class ApplicationUser : IdentityUser<Guid>
{
    private ApplicationUser()
    {
    }

    public ApplicationUser(Guid id, string userName, DateTimeOffset createdAt)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Identifier must not be empty.", nameof(id));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(userName);

        Id = id;
        UserName = userName;
        CreatedAt = createdAt;
    }

    public DateTimeOffset CreatedAt { get; private set; }
}
