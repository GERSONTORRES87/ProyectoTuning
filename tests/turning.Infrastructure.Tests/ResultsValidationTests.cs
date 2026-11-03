using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Turning.Infrastructure.Persistence;
using Turning.Infrastructure.Services;
using Xunit;

namespace Turning.Infrastructure.Tests;

public class ResultsValidationTests
{
    [Theory]
    [InlineData(0, 50)]
    [InlineData(1, 0)]
    [InlineData(1, 101)]
    [InlineData(int.MaxValue, 100)]
    public async Task List_RejectsInvalidPaginationBeforeAccessingDatabase(int page, int pageSize)
    {
        await using var db = CreateDisconnectedContext();
        var service = new ResultsService(db);
        Func<Task> query = () => service.ListAsync(null, null, null, page, pageSize, default);
        await query.Should().ThrowAsync<ArgumentException>().WithMessage("INVALID_PAGINATION");
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("1")]
    [InlineData("3")]
    public async Task List_RejectsUnknownOrNumericConditions(string condition)
    {
        await using var db = CreateDisconnectedContext();
        var service = new ResultsService(db);
        Func<Task> query = () => service.ListAsync(null, null, condition, 1, 50, default);
        await query.Should().ThrowAsync<ArgumentException>().WithMessage("INVALID_CONDITION");
    }

    [Fact]
    public async Task List_RejectsInvertedDateRange()
    {
        await using var db = CreateDisconnectedContext();
        var service = new ResultsService(db);
        Func<Task> query = () => service.ListAsync(DateTime.UtcNow, DateTime.UtcNow.AddDays(-1), null, 1, 50, default);
        await query.Should().ThrowAsync<ArgumentException>().WithMessage("INVALID_DATE_RANGE");
    }

    private static TurningDbContext CreateDisconnectedContext() => new(new DbContextOptionsBuilder<TurningDbContext>()
        .UseSqlServer("Server=not-used;Database=not-used;Trusted_Connection=True;TrustServerCertificate=True").Options);
}
