namespace turning.Web.ExperimentSessions;

public sealed class ResultsAnalysisReport
{
    private ResultsAnalysisReport() { }

    public IReadOnlyList<ExperimentSessionSnapshotDto> Sessions { get; private init; } = [];
    public IReadOnlyList<SessionResultDto> Details { get; private init; } = [];
    public IReadOnlyDictionary<string, int> EmotionCounts { get; private init; } = new Dictionary<string, int>();
    public IReadOnlyDictionary<string, int> StatusCounts { get; private init; } = new Dictionary<string, int>();
    public int TotalSessions => Sessions.Count;
    public int TotalMessages { get; private init; }
    public int TotalReadings { get; private init; }
    public int DegradedReadings { get; private init; }
    public string? PredominantEmotion { get; private init; }

    public static ResultsAnalysisReport Create(IEnumerable<ExperimentSessionSnapshotDto> sessions,
        IEnumerable<SessionResultDto> details, string? status = null, string? condition = null)
    {
        var selectedSessions = sessions
            .DistinctBy(session => session.Id)
            .Where(session => Matches(session.Status, status) && Matches(session.Condition, condition))
            .OrderByDescending(session => session.CreatedAtUtc)
            .ToArray();
        var selectedIds = selectedSessions.Select(session => session.Id).ToHashSet();
        var detailsBySession = details
            .Where(detail => selectedIds.Contains(detail.Session.Id))
            .DistinctBy(detail => detail.Session.Id)
            .ToDictionary(detail => detail.Session.Id);
        var selectedDetails = selectedSessions
            .Where(session => detailsBySession.ContainsKey(session.Id))
            .Select(session => detailsBySession[session.Id])
            .ToArray();
        var readings = selectedDetails.SelectMany(detail => detail.EmotionReadings).ToArray();
        var emotions = readings
            .Where(reading => !string.IsNullOrWhiteSpace(reading.Emotion))
            .GroupBy(reading => reading.Emotion.Trim().ToLowerInvariant(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);

        return new ResultsAnalysisReport
        {
            Sessions = selectedSessions,
            Details = selectedDetails,
            EmotionCounts = emotions,
            StatusCounts = selectedSessions
                .GroupBy(session => session.Status.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase),
            TotalMessages = selectedSessions.Sum(session => session.ConversationTurnCount),
            TotalReadings = readings.Length,
            DegradedReadings = readings.Count(reading => reading.IsDegraded),
            PredominantEmotion = emotions.OrderByDescending(pair => pair.Value)
                .ThenBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .Select(pair => pair.Key)
                .FirstOrDefault()
        };
    }

    private static bool Matches(string value, string? filter) =>
        string.IsNullOrWhiteSpace(filter) || string.Equals(value.Trim(), filter.Trim(), StringComparison.OrdinalIgnoreCase);
}
