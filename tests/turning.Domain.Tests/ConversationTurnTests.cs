using FluentAssertions;
using Turning.Domain.Entities;
using Turning.Domain.Exceptions;
using Xunit;

namespace Turning.Domain.Tests;

/// <summary>
/// Pruebas de la entidad de turno conversacional.
/// </summary>
public class ConversationTurnTests
{
    private static ConversationTurn CreateTurn(int sequence = 1, ConversationActor sender = ConversationActor.Participant) =>
        ConversationTurn.Create(Guid.NewGuid(), sequence, sender, "Mensaje de prueba");

    [Fact]
    public void LinkOriginatingTurn_ShouldAssignTheOriginatingTurn()
    {
        var participantTurn = CreateTurn();
        var reply = CreateTurn(2, ConversationActor.Interlocutor);

        reply.LinkOriginatingTurn(participantTurn.Id);

        reply.OriginatingTurnId.Should().Be(participantTurn.Id);
    }

    [Fact]
    public void LinkOriginatingTurn_ShouldBeIdempotent_ForTheSameTurn()
    {
        var participantTurn = CreateTurn();
        var reply = CreateTurn(2, ConversationActor.Interlocutor);

        reply.LinkOriginatingTurn(participantTurn.Id);
        var act = () => reply.LinkOriginatingTurn(participantTurn.Id);

        act.Should().NotThrow();
    }

    [Fact]
    public void LinkOriginatingTurn_ShouldRejectReassignment()
    {
        var reply = CreateTurn(2, ConversationActor.Interlocutor);
        reply.LinkOriginatingTurn(Guid.NewGuid());

        var act = () => reply.LinkOriginatingTurn(Guid.NewGuid());

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void LinkOriginatingTurn_ShouldRejectSelfReference()
    {
        var reply = CreateTurn(2, ConversationActor.Interlocutor);

        var act = () => reply.LinkOriginatingTurn(reply.Id);

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void LinkOriginatingTurn_ShouldRejectEmptyGuid()
    {
        var reply = CreateTurn(2, ConversationActor.Interlocutor);

        var act = () => reply.LinkOriginatingTurn(Guid.Empty);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void RegisterConversationTurn_ShouldAutoActivateWithTheGivenDuration()
    {
        var session = ExperimentSession.Create(Guid.NewGuid(), ExperimentalCondition.Human);

        session.RegisterConversationTurn(TimeSpan.FromSeconds(900));

        session.Status.Should().Be(ExperimentSessionStatus.Active);
        (session.ExpiresAtUtc!.Value - session.ActivatedAtUtc!.Value).TotalSeconds.Should().BeApproximately(900, 1);
    }

    [Fact]
    public void RegisterConversationTurn_ShouldKeepDefaultDuration_WhenNoneIsGiven()
    {
        var session = ExperimentSession.Create(Guid.NewGuid(), ExperimentalCondition.Human);

        session.RegisterConversationTurn();

        (session.ExpiresAtUtc!.Value - session.ActivatedAtUtc!.Value).TotalSeconds
            .Should().BeApproximately(ExperimentSession.DefaultAutoActivationDuration.TotalSeconds, 1);
    }

    [Fact]
    public void MarkDegraded_ShouldFlagTheReading()
    {
        var reading = EmotionReading.Create(Guid.NewGuid(), EmotionLabel.Neutral, EmotionLabel.FallbackIntensity);

        reading.IsDegraded.Should().BeFalse();
        reading.MarkDegraded();
        reading.IsDegraded.Should().BeTrue();
    }

    [Theory]
    [InlineData("JOY", EmotionLabel.Joy)]
    [InlineData(" confusion ", EmotionLabel.Confusion)]
    [InlineData("FEAR", EmotionLabel.Fear)]
    [InlineData("disgust", EmotionLabel.Disgust)]
    [InlineData("platano", EmotionLabel.Neutral)]
    [InlineData(null, EmotionLabel.Neutral)]
    public void EmotionLabel_ShouldNormalizeToTheKnownVocabulary(string? input, string expected)
    {
        EmotionLabel.Normalize(input).Should().Be(expected);
    }

    [Fact]
    public void EmotionLabel_ShouldRecognizeTheSixLabelsPlusFearAndDisgustOfTheEmotionsApi()
    {
        EmotionLabel.All.Should().HaveCount(8);
        EmotionLabel.IsKnown("surprise").Should().BeTrue();
        EmotionLabel.IsKnown("fear").Should().BeTrue();
        EmotionLabel.IsKnown("disgust").Should().BeTrue();
        EmotionLabel.IsKnown("platano").Should().BeFalse();
    }
}
