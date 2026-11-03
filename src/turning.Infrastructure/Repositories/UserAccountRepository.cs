using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Turning.Application.Interfaces;
using Turning.Domain.Entities;
using Turning.Infrastructure.Persistence;
using TurningApplicationException = Turning.Application.Exceptions.ApplicationException;

namespace Turning.Infrastructure.Repositories;

/// <summary>
/// Repositorio EF Core para cuentas de usuario.
/// </summary>
public sealed class UserAccountRepository : IUserAccountRepository
{
    private readonly TurningDbContext _dbContext;

    /// <summary>
    /// Constructor del repositorio de usuarios.
    /// </summary>
    public UserAccountRepository(TurningDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <inheritdoc />
    public Task<UserAccount?> GetByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        var normalizedEmail = UserAccount.NormalizeEmail(email);

        return _dbContext.UserAccounts
            .FirstOrDefaultAsync(user => user.NormalizedEmail == normalizedEmail && !user.IsDeleted, cancellationToken);
    }

    /// <inheritdoc />
    public Task<UserAccount?> GetByIdAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        return _dbContext.UserAccounts
            .FirstOrDefaultAsync(user => user.Id == userId && !user.IsDeleted, cancellationToken);
    }

    /// <inheritdoc />
    public Task<bool> ExistsByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        var normalizedEmail = UserAccount.NormalizeEmail(email);

        // Sin filtro IsDeleted: el índice único de NormalizedEmail también reserva el correo de cuentas eliminadas.
        return _dbContext.UserAccounts
            .AnyAsync(user => user.NormalizedEmail == normalizedEmail, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<(IReadOnlyList<UserAccount> Items, int Total)> SearchAsync(
        string? query, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var accounts = _dbContext.UserAccounts.AsNoTracking().Where(user => !user.IsDeleted);
        if (!string.IsNullOrWhiteSpace(query))
        {
            // NormalizedEmail ya está en mayúsculas; la intercalación de SQL Server no distingue mayúsculas en FullName.
            var text = query.Trim();
            var upper = text.ToUpperInvariant();
            accounts = accounts.Where(user => user.NormalizedEmail.Contains(upper) || user.FullName.Contains(text));
        }

        var total = await accounts.CountAsync(cancellationToken);
        var items = await accounts
            .OrderBy(user => user.NormalizedEmail)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, total);
    }

    /// <inheritdoc />
    public async Task AddAsync(UserAccount userAccount, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(userAccount);

        await _dbContext.UserAccounts.AddAsync(userAccount, cancellationToken);
    }

    /// <inheritdoc />
    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (
            ex.InnerException is SqlException { Number: 2601 or 2627 }
            && ex.Entries.Any(entry => entry.Entity is UserAccount && entry.State == EntityState.Added))
        {
            throw new TurningApplicationException("Ya existe un usuario registrado con ese correo.", "AUTH_USER_ALREADY_EXISTS");
        }
    }
}