using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using Turning.API.Controllers;
using Turning.Application.Features.Surveys;
using Xunit;

namespace Turning.API.Tests;

public sealed class AdminSurveyResultsControllerTests
{
    [Fact]
    public void EndpointRequiresAnAdministratorOrResearcher()
    {
        var authorization = typeof(AdminSurveyResultsController).GetCustomAttributes(typeof(AuthorizeAttribute), true)
            .Cast<AuthorizeAttribute>().Single();

        authorization.Roles.Should().NotBeNullOrWhiteSpace();
        authorization.Roles!.Split(',').Select(role => role.Trim()).Should().BeEquivalentTo("Administrator", "Researcher");
        typeof(AdminSurveyResultsController).GetCustomAttributes(typeof(AllowAnonymousAttribute), true).Should().BeEmpty();
    }

    [Theory]
    [InlineData(0, 50)]
    [InlineData(1, 0)]
    [InlineData(1, 101)]
    public async Task InvalidPaginationReturnsBadRequest(int page, int size)
    {
        var service = Substitute.For<IAdminSurveyResultsService>();
        service.GetResultsAsync(page, size, null, Arg.Any<CancellationToken>())
            .Returns(Task.FromException<AdminSurveyResultsPageDto>(new ArgumentOutOfRangeException("page")));

        var result = await new AdminSurveyResultsController(service).Get(page, size);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task GetForwardsTheParticipantFilterAndReturnsThePage()
    {
        var service = Substitute.For<IAdminSurveyResultsService>();
        var participantId = Guid.NewGuid();
        var page = new AdminSurveyResultsPageDto([], 0, 2, 20);
        service.GetResultsAsync(2, 20, participantId, Arg.Any<CancellationToken>()).Returns(page);

        var result = await new AdminSurveyResultsController(service).Get(2, 20, participantId);

        result.Should().BeOfType<OkObjectResult>().Which.Value.Should().BeSameAs(page);
    }
}
