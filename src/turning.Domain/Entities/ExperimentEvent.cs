using Turning.Domain.Common;
namespace Turning.Domain.Entities;
public sealed class ExperimentEvent : BaseEntity
{
    private ExperimentEvent(){}
    public Guid SessionId { get; private set; }
    public string Type { get; private set; } = string.Empty;
    public string PayloadJson { get; private set; } = "{}";
    public DateTime OccurredAtUtc { get; private set; }
    public DateTime ExpiresAtUtc { get; private set; }
    public static ExperimentEvent Create(Guid sid, string type, string payload, DateTime? occurredAtUtc = null)
    {
        var now = occurredAtUtc ?? DateTime.UtcNow;
        return new ExperimentEvent
        {
            Id = Guid.CreateVersion7(), SessionId = sid, Type = type, PayloadJson = payload,
            OccurredAtUtc = now, ExpiresAtUtc = now.AddDays(30), CreatedAt = now
        };
    }
}
