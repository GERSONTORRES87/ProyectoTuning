using FluentAssertions;
using Turning.Domain.Entities;
using Xunit;

namespace Turning.Domain.Tests;

public class AvatarExpressionTests
{
    private static EmotionReading Reading(string emotion, double score) =>
        EmotionReading.Create(Guid.NewGuid(), emotion, score, "video", "test");

    [Fact]
    public void FromReading_MarksADegradedNeutralReadingAsFallback()
    {
        var reading = Reading(EmotionLabel.Neutral, EmotionLabel.FallbackIntensity);
        reading.MarkDegraded();

        var expression = AvatarExpression.FromReading(reading);

        // Sin esto el neutral de respaldo (0.3) se mostraba como una medicion del 30 %.
        expression.ExpressionName.Should().Be("Neutral");
        expression.IsFallback.Should().BeTrue();
    }

    [Fact]
    public void FromReading_KeepsAValidNeutralReadingAsMeasured()
    {
        var expression = AvatarExpression.FromReading(Reading(EmotionLabel.Neutral, 0.6));

        expression.IsFallback.Should().BeFalse();
        expression.Intensity.Should().Be(0.6);
    }

    [Fact]
    public void FromReading_StillFlagsAnEmotionWithoutItsOwnExpression()
    {
        // Comportamiento previo: "confusion" no tiene expresion y cae en Neutral.
        var expression = AvatarExpression.FromReading(Reading("confusion", 0.8));

        expression.ExpressionName.Should().Be("Neutral");
        expression.IsFallback.Should().BeTrue();
    }

    [Fact]
    public void FromReading_LeavesAMeasuredKnownEmotionUnflagged()
    {
        var expression = AvatarExpression.FromReading(Reading(EmotionLabel.Joy, 0.7));

        expression.ExpressionName.Should().Be("Joy");
        expression.IsFallback.Should().BeFalse();
    }
}
