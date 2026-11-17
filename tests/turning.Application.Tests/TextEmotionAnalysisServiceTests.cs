using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Turning.Application.Features.Emotions;
using Turning.Application.Interfaces;
using Turning.Domain.Emotions;
using Turning.Domain.Entities;
using Xunit;

namespace Turning.Application.Tests;

public class TextEmotionAnalysisServiceTests
{
    private readonly ITextEmotionClassifierPort _classifier = Substitute.For<ITextEmotionClassifierPort>();
    private readonly TextEmotionAnalysisService _service;

    public TextEmotionAnalysisServiceTests()
    {
        // Léxico vacío: si el servicio cae al léxico, el resultado es el neutral de fallback con esta versión.
        var lexicon = Substitute.For<IEmotionLexicon>();
        lexicon.Version.Returns("lexicon-test");
        lexicon.MultiWordTerms.Returns([]);
        _service = new TextEmotionAnalysisService(_classifier, new TextEmotionAnalyzer(lexicon), NullLogger<TextEmotionAnalysisService>.Instance);
    }

    [Fact]
    public async Task UsesTheClassifierWithIntensityAndPolarityFromItsDistribution()
    {
        _classifier.ClassifyAsync("tengo miedo", Arg.Any<CancellationToken>()).Returns(new TextEmotionClassification(
            EmotionLabel.Fear, 0.7,
            new Dictionary<string, double> { [EmotionLabel.Fear] = 0.7, [EmotionLabel.Neutral] = 0.2, [EmotionLabel.Joy] = 0.1 },
            "robertuito@abc"));

        var result = await _service.AnalyzeAsync("tengo miedo");

        result.EmotionId.Should().Be(EmotionLabel.Fear);
        result.Confidence.Should().Be(0.7);
        result.Intensity.Should().BeApproximately(0.8, 1e-9);
        result.Polarity.Should().BeApproximately(-0.6, 1e-9);
        result.Source.Should().Be(TextEmotionDto.SourceTextAnalysis);
        result.FallbackUsed.Should().BeFalse();
        result.ModelVersion.Should().Be("robertuito@abc");
    }

    [Fact]
    public async Task FallsBackToTheLexiconWhenTheClassifierIsUnavailable()
    {
        _classifier.ClassifyAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((TextEmotionClassification?)null);

        var result = await _service.AnalyzeAsync("hola");

        result.ModelVersion.Should().Be("lexicon-test");
    }

    [Fact]
    public async Task FallsBackToTheLexiconWhenTheClassifierThrows()
    {
        _classifier.ClassifyAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).ThrowsAsync(new HttpRequestException("caído"));

        var result = await _service.AnalyzeAsync("hola");

        result.ModelVersion.Should().Be("lexicon-test");
        result.EmotionId.Should().Be(EmotionLabel.Neutral);
    }

    [Fact]
    public async Task DoesNotCallTheClassifierForEmptyText()
    {
        await _service.AnalyzeAsync("   ");

        await _classifier.DidNotReceiveWithAnyArgs().ClassifyAsync(default!, default);
    }

    [Fact]
    public async Task PropagatesTheCallersCancellation()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        _classifier.ClassifyAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).ThrowsAsync(new OperationCanceledException());

        var act = () => _service.AnalyzeAsync("hola", cancelled.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
