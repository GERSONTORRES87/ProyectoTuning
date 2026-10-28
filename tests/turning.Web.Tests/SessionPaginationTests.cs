using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using turning.Web.Auth;
using turning.Web.ExperimentSessions;
using Xunit;

namespace turning.Web.Tests;

public sealed class SessionPaginationTests
{
    [Fact]
    public async Task IncludesAnActiveSessionOnTheSecondPageAndAuthenticatesEveryRequest()
    {
        var firstPage = Enumerable.Range(1, 100).Select(index => Session(index, "Created")).ToList();
        var activeSession = Session(101, "Active");
        using var handler = new PagedHandler(
            Page(firstPage, total: 101, page: 1),
            Page([activeSession], total: 101, page: 2));
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://api.local/") };
        var authSession = new AuthSession();
        authSession.SetAccessToken("test-access-token");
        var client = new ExperimentSessionApiClient(httpClient, authSession);

        var result = await client.ListAllSessionsAsync();

        result.Items.Should().HaveCount(101);
        result.Total.Should().Be(101);
        result.Items.Last().Id.Should().Be(activeSession.Id);
        result.Items.Last().Status.Should().Be("Active");
        handler.Requests.Should().Equal(
            ("GET", "/api/results?page=1&pageSize=100", "Bearer test-access-token"),
            ("GET", "/api/results?page=2&pageSize=100", "Bearer test-access-token"));
    }

    [Fact]
    public async Task DoesNotRepeatSessionsWhenNewEntriesShiftThePageBoundary()
    {
        using var handler = new PagedHandler(
            Page(Enumerable.Range(1, 100).Select(index => Session(index, "Created")).ToList(), total: 101, page: 1),
            Page([Session(100, "Created"), Session(101, "Active")], total: 102, page: 2));
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://api.local/") };
        var client = new ExperimentSessionApiClient(httpClient, new AuthSession());

        var result = await client.ListAllSessionsAsync();

        result.Items.Should().HaveCount(101);
        result.Items.Select(session => session.Id).Should().OnlyHaveUniqueItems();
        result.Items.Last().Status.Should().Be("Active");
        result.Total.Should().Be(102);
        handler.Requests.Should().HaveCount(2);
    }

    [Fact]
    public async Task StopsWhenTheTotalIsExactlyOnePage()
    {
        using var handler = new PagedHandler(Page(
            Enumerable.Range(1, 100).Select(index => Session(index, "Created")).ToList(),
            total: 100, page: 1));
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://api.local/") };
        var client = new ExperimentSessionApiClient(httpClient, new AuthSession());

        var result = await client.ListAllSessionsAsync();

        result.Items.Should().HaveCount(100);
        result.Total.Should().Be(100);
        handler.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task AnEmptyFirstPageReturnsAnEmptyList()
    {
        using var handler = new PagedHandler(Page([], total: 0, page: 1));
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://api.local/") };
        var client = new ExperimentSessionApiClient(httpClient, new AuthSession());

        var result = await client.ListAllSessionsAsync();

        result.Items.Should().BeEmpty();
        result.Total.Should().Be(0);
        handler.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task AnEmptyLaterPageStopsPaginationWhenTheListChanges()
    {
        using var handler = new PagedHandler(
            Page(Enumerable.Range(1, 100).Select(index => Session(index, "Created")).ToList(), total: 250, page: 1),
            Page([], total: 250, page: 2));
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://api.local/") };
        var client = new ExperimentSessionApiClient(httpClient, new AuthSession());

        var result = await client.ListAllSessionsAsync();

        result.Items.Should().HaveCount(100);
        result.Total.Should().Be(250);
        handler.Requests.Should().HaveCount(2);
    }

    [Fact]
    public async Task AnErrorOnALaterPageIsReportedInsteadOfReturningIncompleteStatistics()
    {
        using var handler = new PagedHandler(
            Page(Enumerable.Range(1, 100).Select(index => Session(index, "Created")).ToList(), total: 101, page: 1),
            new HttpResponseMessage(HttpStatusCode.Forbidden)
            {
                Content = JsonContent.Create(new { message = "Acceso denegado." })
            });
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://api.local/") };
        var client = new ExperimentSessionApiClient(httpClient, new AuthSession());

        var act = () => client.ListAllSessionsAsync();

        var failure = await act.Should().ThrowAsync<ApiException>();
        failure.Which.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        failure.Which.Message.Should().Be("Acceso denegado.");
    }

    private static HttpResponseMessage Page(List<ExperimentSessionSnapshotDto> items, int total, int page) =>
        new(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new { items, total, page, pageSize = 100 })
        };

    private static ExperimentSessionSnapshotDto Session(int index, string status) => new()
    {
        Id = Guid.Parse($"00000000-0000-0000-0000-{index:000000000000}"),
        SessionCode = $"EXP-{index:00000000}",
        Condition = "AI",
        Status = status,
        AvatarState = "Neutral",
        ConversationTurnCount = status == "Active" ? 1 : 0,
        EmotionSampleCount = status == "Active" ? 1 : 0,
        CreatedAtUtc = new DateTime(2026, 10, 27, 15, 0, 0, DateTimeKind.Utc),
        ConversationStage = "ready",
        EmotionStage = "ready",
        AvatarStage = "ready"
    };

    private sealed class PagedHandler(params HttpResponseMessage[] responses) : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses = new(responses);
        public List<(string Method, string Path, string Authorization)> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add((request.Method.Method, request.RequestUri!.PathAndQuery, request.Headers.Authorization?.ToString() ?? string.Empty));
            if (_responses.Count == 0)
            {
                throw new InvalidOperationException("The client requested a page beyond the end of the result set.");
            }

            return Task.FromResult(_responses.Dequeue());
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                foreach (var response in _responses) response.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
