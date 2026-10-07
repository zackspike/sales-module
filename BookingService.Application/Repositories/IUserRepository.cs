using BookingService.Domain;

namespace BookingService.Application.Repositories;

/// <summary>
/// Access to the users (buyers). Changes are persisted by <see cref="IUnitOfWork.SaveChangesAsync"/>.
/// </summary>
public interface IUserRepository
{
    Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken = default);

    void Add(User user);
}
