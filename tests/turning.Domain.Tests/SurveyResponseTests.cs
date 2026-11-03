using FluentAssertions;
using Turning.Domain.Entities;
using Xunit;

namespace Turning.Domain.Tests;

public class SurveyResponseTests
{
    [Fact]
    public void Submit_PersistsTypedAnswersAndFinishesInOneCall()
    {
        var definition = CreateDefinition();
        var ownerId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var response = SurveyResponse.Create(sessionId, ownerId, definition.Id);

        response.Submit(definition, new Dictionary<string, string> { ["interlocutor"] = "Unsure", ["empathy"] = "4", ["comments"] = " Me escucharon. " });

        response.OwnerUserId.Should().Be(ownerId);
        response.SessionId.Should().Be(sessionId);
        response.SubmittedAtUtc.Should().NotBeNull();
        response.Answers.Should().HaveCount(3);
        response.Answers.Should().Contain(a => a.SurveyQuestionId == definition.Questions.Single(q => q.Code == "comments").Id && a.Value == "Me escucharon.");
        Action repeat = () => response.Submit(definition, new Dictionary<string, string>());
        repeat.Should().Throw<InvalidOperationException>().WithMessage("ALREADY_SUBMITTED");
    }

    [Theory]
    [InlineData("0")]
    [InlineData("6")]
    [InlineData("3.5")]
    [InlineData("NaN")]
    public void Submit_RejectsInvalidScaleWithoutChangingResponse(string value)
    {
        var definition = CreateDefinition();
        var response = SurveyResponse.Create(Guid.NewGuid(), Guid.NewGuid(), definition.Id);

        Action submit = () => response.Submit(definition, new Dictionary<string, string> { ["interlocutor"] = "AI", ["empathy"] = value });

        submit.Should().Throw<ArgumentException>().WithMessage("SURVEY_INVALID_SCALE");
        response.SubmittedAtUtc.Should().BeNull();
        response.Answers.Should().BeEmpty();
    }

    [Theory]
    [InlineData("unknown", "SURVEY_UNKNOWN_QUESTION")]
    [InlineData("interlocutor", "SURVEY_INVALID_CHOICE")]
    public void Submit_RejectsUnknownQuestionOrChoice(string code, string error)
    {
        var definition = CreateDefinition();
        var response = SurveyResponse.Create(Guid.NewGuid(), Guid.NewGuid(), definition.Id);
        Action submit = () => response.Submit(definition, new Dictionary<string, string> { [code] = "Other" });
        submit.Should().Throw<ArgumentException>().WithMessage(error);
        response.Answers.Should().BeEmpty();
    }

    [Fact]
    public void Submit_RejectsMissingRequiredAnswerAndDuplicateQuestionAliases()
    {
        var definition = CreateDefinition();
        var response = SurveyResponse.Create(Guid.NewGuid(), Guid.NewGuid(), definition.Id);
        Action missing = () => response.Submit(definition, null);
        missing.Should().Throw<ArgumentException>().WithMessage("SURVEY_REQUIRED_ANSWER");

        Action duplicate = () => response.Submit(definition, new Dictionary<string, string>
        {
            ["interlocutor"] = "Human", [definition.Questions[0].Id.ToString()] = "AI"
        });
        duplicate.Should().Throw<ArgumentException>().WithMessage("SURVEY_DUPLICATE_ANSWER");
        response.SubmittedAtUtc.Should().BeNull();
    }

    [Fact]
    public void Submit_RejectsAnAnswerFromAnotherDefinition()
    {
        var definition = CreateDefinition();
        var response = SurveyResponse.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        Action submit = () => response.Submit(definition, null);
        submit.Should().Throw<ArgumentException>().WithMessage("SURVEY_NOT_FOUND");
    }

    private static SurveyDefinition CreateDefinition()
    {
        var definition = SurveyDefinition.Create("test-v1", "Encuesta de prueba");
        definition.AddQuestion("interlocutor", "¿Con quién conversaste?", "choice", true, 1);
        definition.AddQuestion("empathy", "Empatía de 1 a 5", "scale", true, 2);
        definition.AddQuestion("comments", "Comentario", "text", false, 3);
        return definition;
    }
}
