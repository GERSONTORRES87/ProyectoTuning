using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Turning.Domain.Common;
using Turning.Domain.Entities;
using Turning.Infrastructure.Persistence;
using Turning.Infrastructure.Repositories;
using Xunit;
using TurningApplicationException = Turning.Application.Exceptions.ApplicationException;

namespace Turning.Infrastructure.Tests;

/// <summary>
/// Pruebas contra SQL Server LocalDB de la unicidad de correo en <see cref="UserAccountRepository"/>.
/// </summary>
public class UserAccountRepositoryDuplicateEmailTests : IDisposable
{
    private readonly DbContextOptions<TurningDbContext> _options;
    private readonly TurningDbContext _dbContext;

    public UserAccountRepositoryDuplicateEmailTests()
    {
        var databaseName = $"TurningTests_{Guid.NewGuid():N}";
        var connectionString =
            $"Server=(localdb)\\MSSQLLocalDB;Database={databaseName};Trusted_Connection=True;TrustServerCertificate=True";

        _options = new DbContextOptionsBuilder<TurningDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        _dbContext = new TurningDbContext(_options);
        _dbContext.Database.EnsureCreated();
    }

    [Fact]
    public async Task ExistsByEmailAsync_ShouldKeepEmailReservedForSoftDeletedUser()
    {
        var repository = new UserAccountRepository(_dbContext);
        var user = UserAccount.Create("deleted@example.com", "Deleted User", "hash-value", UserRoles.Participant);
        await repository.AddAsync(user);
        await repository.SaveChangesAsync();
        await _dbContext.UserAccounts
            .Where(u => u.Id == user.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(u => u.IsDeleted, true));

        var exists = await repository.ExistsByEmailAsync("DELETED@example.com");
        var found = await repository.GetByEmailAsync("deleted@example.com");

        exists.Should().BeTrue();
        found.Should().BeNull();
    }

    [Fact]
    public async Task SaveChangesAsync_ShouldThrowUserAlreadyExists_WhenEmailBelongsToSoftDeletedUser()
    {
        var first = UserAccount.Create("reserved@example.com", "First", "hash-value", UserRoles.Participant);
        _dbContext.UserAccounts.Add(first);
        await _dbContext.SaveChangesAsync();
        await _dbContext.UserAccounts
            .Where(u => u.Id == first.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(u => u.IsDeleted, true));

        await using var otherContext = new TurningDbContext(_options);
        var repository = new UserAccountRepository(otherContext);
        await repository.AddAsync(UserAccount.Create("reserved@example.com", "Second", "hash-value", UserRoles.Participant));

        var act = () => repository.SaveChangesAsync();

        var exception = await act.Should().ThrowAsync<TurningApplicationException>();
        exception.Which.Code.Should().Be("AUTH_USER_ALREADY_EXISTS");
    }

    [Fact]
    public async Task SaveChangesAsync_ShouldThrowUserAlreadyExists_WhenConcurrentRegistrationWinsRace()
    {
        await using var contextA = new TurningDbContext(_options);
        await using var contextB = new TurningDbContext(_options);
        var repositoryA = new UserAccountRepository(contextA);
        var repositoryB = new UserAccountRepository(contextB);

        // Ambos prechecks ocurren antes de cualquier guardado.
        (await repositoryA.ExistsByEmailAsync("race@example.com")).Should().BeFalse();
        (await repositoryB.ExistsByEmailAsync("race@example.com")).Should().BeFalse();

        await repositoryA.AddAsync(UserAccount.Create("race@example.com", "Racer A", "hash-value", UserRoles.Participant));
        await repositoryB.AddAsync(UserAccount.Create("race@example.com", "Racer B", "hash-value", UserRoles.Participant));
        await repositoryA.SaveChangesAsync();

        var act = () => repositoryB.SaveChangesAsync();

        var exception = await act.Should().ThrowAsync<TurningApplicationException>();
        exception.Which.Code.Should().Be("AUTH_USER_ALREADY_EXISTS");
    }

    public void Dispose()
    {
        _dbContext.Database.EnsureDeleted();
        _dbContext.Dispose();
    }
}
