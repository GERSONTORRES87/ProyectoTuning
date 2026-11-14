using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Turning.Application.Interfaces;
using Turning.Domain.Entities;
using Turning.Infrastructure.AI.Guard;
using Xunit;

namespace Turning.Infrastructure.Tests;

public class RagConversationGuardTests
{
    // Base mínima: los "embeddings" falsos son ejes, así la similitud es 1 con su propio ejemplo y 0 con los demás.
    private const string Knowledge = """
        {
          "version": "test",
          "blockRules": [
            { "id": "crisis", "examples": ["quiero morir"], "keywords": ["suicidarme"], "response": "Respuesta de crisis." }
          ],
          "topicGuides": [
            { "id": "duelo", "examples": ["murio mi abuela"], "guidance": "Valida la perdida." }
          ],
          "emotionGuides": [
            { "emotion": "sadness", "guidance": "Responde con calma." }
          ]
        }
        """;

    private static readonly Dictionary<string, float[]> Axes = new()
    {
        ["quiero morir"] = [1, 0, 0],
        ["murio mi abuela"] = [0, 1, 0],
        ["ya no quiero vivir mas"] = [0.95f, 0.05f, 0],
        ["se murio mi abuela ayer"] = [0, 0.9f, 0.1f],
        ["hola, como estas"] = [0, 0, 1]
    };

    private static RagConversationGuard Create(IEmbeddingClient embeddings) =>
        new(GuardKnowledge.Parse(Knowledge), embeddings,
            Options.Create(new GuardOptions { BlockThreshold = 0.8, GuideThreshold = 0.5 }),
            TimeProvider.System, NullLogger<RagConversationGuard>.Instance);

    [Fact]
    public async Task BlocksWithTheApprovedResponse_WhenTheMessageIsCloseToARule()
    {
        var guard = Create(new FakeEmbeddings());
        await guard.WarmUpAsync();

        var decision = await guard.EvaluateAsync("ya no quiero vivir mas", EmotionLabel.Sadness);

        decision.Mode.Should().Be(GuardMode.Blocked);
        decision.RuleId.Should().Be("crisis");
        decision.Response.Should().Be("Respuesta de crisis.");
    }

    [Fact]
    public async Task RetrievesTopicAndEmotionGuidance_WhenNothingBlocks()
    {
        var guard = Create(new FakeEmbeddings());
        await guard.WarmUpAsync();

        var decision = await guard.EvaluateAsync("se murio mi abuela ayer", EmotionLabel.Sadness);

        decision.Mode.Should().Be(GuardMode.Context);
        decision.Guidance.Should().Equal("Responde con calma.", "Valida la perdida.");
        decision.Trace.Should().Be("context:emotion:sadness,duelo");
    }

    [Fact]
    public async Task OnlyAddsTheEmotionGuide_WhenNoTopicIsSimilar()
    {
        var guard = Create(new FakeEmbeddings());
        await guard.WarmUpAsync();

        var decision = await guard.EvaluateAsync("hola, como estas", EmotionLabel.Sadness);

        decision.Guidance.Should().Equal("Responde con calma.");
    }

    [Fact]
    public async Task FallsBackToKeywords_WhenEmbeddingsFail()
    {
        var guard = Create(new FakeEmbeddings { Fail = true });
        await guard.WarmUpAsync();

        var blocked = await guard.EvaluateAsync("Pienso en SUICIDARME", null);
        var passed = await guard.EvaluateAsync("hola, como estas", EmotionLabel.Sadness);

        blocked.Mode.Should().Be(GuardMode.Blocked);
        blocked.RuleId.Should().Be("crisis");
        passed.Mode.Should().Be(GuardMode.Keywords);
        passed.Guidance.Should().Equal("Responde con calma.");
    }

    [Fact]
    public async Task UsesOnlyKeywords_WhenEmbeddingsAreNotConfigured()
    {
        var embeddings = new FakeEmbeddings { Configured = false };
        var guard = Create(embeddings);

        var decision = await guard.EvaluateAsync("ya no quiero vivir mas", null);

        decision.Mode.Should().Be(GuardMode.Keywords);
        embeddings.Calls.Should().Be(0);
    }

    [Fact]
    public void TheBundledKnowledgeLoads()
    {
        var knowledge = GuardKnowledge.LoadEmbedded();

        knowledge.BlockRules.Should().Contain(rule => rule.Id == "crisis");
        knowledge.EmotionGuides.Select(guide => guide.Emotion).Should().BeEquivalentTo(EmotionLabel.All);
    }

    [Fact]
    public void ARuleWithoutResponseFailsNamingIt()
    {
        var act = () => GuardKnowledge.Parse("""{ "version": "x", "blockRules": [ { "id": "sin-respuesta", "keywords": ["algo"] } ] }""");

        act.Should().Throw<InvalidOperationException>().WithMessage("*sin-respuesta*");
    }

    private sealed class FakeEmbeddings : IEmbeddingClient
    {
        public bool Configured { get; init; } = true;
        public bool Fail { get; init; }
        public int Calls { get; private set; }
        public bool IsConfigured => Configured;

        public Task<float[][]> EmbedAsync(IReadOnlyList<string> inputs, int timeoutMs, CancellationToken cancellationToken)
        {
            Calls++;
            if (Fail) throw new HttpRequestException("ollama caído");
            return Task.FromResult(inputs.Select(input => Axes[input]).ToArray());
        }
    }
}
