using FluentAssertions;
using turning.Web.ExperimentSessions;
using Xunit;

namespace turning.Web.Tests;

public sealed class ResultsAnalysisReportTests
{
    [Fact]
    public void AggregatesActualReadingsAndMessagesAcrossSessionsInNewestFirstOrder()
    {
        var older = Session(1, "Completed", "Human", messages: 2);
        var newer = Session(2, "Active", "AI", messages: 4);
        var olderDetail = Detail(older, Reading("joy", score: 0.9), Reading("sadness", score: 0.7, degraded: true));
        var newerDetail = Detail(newer, Reading("joy", score: 0.2));

        var report = ResultsAnalysisReport.Create([older, newer], [olderDetail, newerDetail]);

        report.Sessions.Select(session => session.Id).Should().Equal(newer.Id, older.Id);
        report.Details.Select(detail => detail.Session.Id).Should().Equal(newer.Id, older.Id);
        report.TotalSessions.Should().Be(2);
        report.TotalMessages.Should().Be(6);
        report.TotalReadings.Should().Be(3);
        report.DegradedReadings.Should().Be(1);
        report.EmotionCounts.Should().BeEquivalentTo(new Dictionary<string, int> { ["joy"] = 2, ["sadness"] = 1 });
        report.StatusCounts.Should().BeEquivalentTo(new Dictionary<string, int> { ["Active"] = 1, ["Completed"] = 1 });
        report.PredominantEmotion.Should().Be("joy");
    }

    [Fact]
    public void StatusAndConditionFiltersExcludeReadingsFromOtherAndUnknownSessions()
    {
        var activeAi = Session(1, "Active", "AI", messages: 3);
        var completedAi = Session(2, "Completed", "AI", messages: 6);
        var activeHuman = Session(3, "Active", "Human", messages: 9);
        var unknown = Session(4, "Active", "AI", messages: 12);

        var report = ResultsAnalysisReport.Create([activeAi, completedAi, activeHuman],
            [Detail(activeAi, Reading("joy")), Detail(completedAi, Reading("anger")),
             Detail(activeHuman, Reading("sadness")), Detail(unknown, Reading("confusion"))],
            status: " active ", condition: " ai ");

        report.Sessions.Select(session => session.Id).Should().Equal(activeAi.Id);
        report.Details.Select(detail => detail.Session.Id).Should().Equal(activeAi.Id);
        report.TotalSessions.Should().Be(1);
        report.TotalMessages.Should().Be(3);
        report.TotalReadings.Should().Be(1);
        report.EmotionCounts.Should().BeEquivalentTo(new Dictionary<string, int> { ["joy"] = 1 });
        report.StatusCounts.Should().BeEquivalentTo(new Dictionary<string, int> { ["Active"] = 1 });
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    [InlineData("  ", "  ")]
    public void MissingFiltersIncludeEverySession(string? status, string? condition)
    {
        var completed = Session(1, "Completed", "Human", messages: 1);
        var active = Session(2, "Active", "AI", messages: 2);

        var report = ResultsAnalysisReport.Create([completed, active],
            [Detail(completed, Reading("joy")), Detail(active, Reading("neutral"))], status, condition);

        report.TotalSessions.Should().Be(2);
        report.TotalMessages.Should().Be(3);
        report.TotalReadings.Should().Be(2);
    }

    [Fact]
    public void UnassignedConditionsAreExcludedFromAConditionFilter()
    {
        var unassigned = new ExperimentSessionSnapshotDto
        {
            Id = Guid.NewGuid(),
            Status = "Created",
            Condition = null
        };
        var assigned = Session(1, "Active", "AI", messages: 1);

        var report = ResultsAnalysisReport.Create([unassigned, assigned], [], condition: "AI");

        report.Sessions.Should().ContainSingle().Which.Id.Should().Be(assigned.Id);
        ResultsAnalysisReport.Create([unassigned, assigned], []).TotalSessions.Should().Be(2);
    }

    [Fact]
    public void EmotionNamesAreTrimmedAndGroupedWithoutCaseSensitiveDuplicates()
    {
        var first = Session(1, "Active", "AI", messages: 1);
        var second = Session(2, "active", "AI", messages: 1);

        var report = ResultsAnalysisReport.Create([first, second],
            [Detail(first, Reading(" Joy "), Reading("JOY")), Detail(second, Reading("joy"), Reading("Sadness"))]);

        report.EmotionCounts.Should().BeEquivalentTo(new Dictionary<string, int> { ["joy"] = 3, ["sadness"] = 1 });
        report.StatusCounts.Should().ContainSingle().Which.Value.Should().Be(2);
        report.TotalReadings.Should().Be(4);
        report.PredominantEmotion.Should().Be("joy");
    }

    [Fact]
    public void EmptyDataHasZeroCountsAndNoPredominantEmotion()
    {
        var report = ResultsAnalysisReport.Create([], []);

        report.Sessions.Should().BeEmpty();
        report.Details.Should().BeEmpty();
        report.EmotionCounts.Should().BeEmpty();
        report.StatusCounts.Should().BeEmpty();
        report.TotalSessions.Should().Be(0);
        report.TotalMessages.Should().Be(0);
        report.TotalReadings.Should().Be(0);
        report.DegradedReadings.Should().Be(0);
        report.PredominantEmotion.Should().BeNull();
    }

    [Fact]
    public void SnapshotCountsWithoutActualReadingsDoNotInventEmotionalMeasurements()
    {
        var session = Session(1, "Created", "AI", messages: 4, advertisedReadings: 9);

        var report = ResultsAnalysisReport.Create([session], []);

        report.TotalSessions.Should().Be(1);
        report.TotalMessages.Should().Be(4);
        report.TotalReadings.Should().Be(0);
        report.EmotionCounts.Should().BeEmpty();
        report.PredominantEmotion.Should().BeNull();
    }

    [Fact]
    public void DuplicateSessionsAndDetailsAreOnlyCountedOnce()
    {
        var session = Session(1, "Completed", "Human", messages: 2);
        var detail = Detail(session, Reading("neutral", degraded: true));

        var report = ResultsAnalysisReport.Create([session, session], [detail, detail]);

        report.TotalSessions.Should().Be(1);
        report.Details.Should().ContainSingle();
        report.TotalMessages.Should().Be(2);
        report.TotalReadings.Should().Be(1);
        report.DegradedReadings.Should().Be(1);
    }

    private static ExperimentSessionSnapshotDto Session(int index, string status, string condition, int messages, int advertisedReadings = 0) => new()
    {
        Id = Guid.Parse($"00000000-0000-0000-0000-{index:000000000000}"),
        SessionCode = $"EXP-{index:00000000}",
        Condition = condition,
        Status = status,
        AvatarState = "neutral",
        ConversationTurnCount = messages,
        EmotionSampleCount = advertisedReadings,
        LastDetectedEmotion = "confusion",
        CreatedAtUtc = new DateTime(2026, 10, 27, 12, index, 0, DateTimeKind.Utc),
        ConversationStage = "available",
        EmotionStage = "available",
        AvatarStage = "available"
    };

    private static SessionResultDto Detail(ExperimentSessionSnapshotDto session, params EmotionReadingDto[] readings) =>
        new() { Session = session, EmotionReadings = [.. readings] };

    private static EmotionReadingDto Reading(string emotion, double score = 0.5, bool degraded = false) => new()
    {
        Emotion = emotion,
        Score = score,
        IsDegraded = degraded,
        CapturedAtUtc = new DateTime(2026, 10, 27, 16, 0, 0, DateTimeKind.Utc)
    };
}
