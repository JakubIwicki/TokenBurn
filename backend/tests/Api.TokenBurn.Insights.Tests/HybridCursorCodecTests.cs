using System.Globalization;
using System.Text;
using FluentAssertions;
using TokenBurn.Common.Pagination;

namespace Api.TokenBurn.Insights.Tests;

[Collection("culture")]
public sealed class HybridCursorCodecTests
{
    private const string Id = "01234567-89ab-cdef-0123-456789abcdef";

    [Fact]
    public void RoundTrips_ScoreAndId()
    {
        const double score = 0.032522;

        string encoded = HybridCursorCodec.Encode(score, Id);

        HybridCursorCodec.TryParse(encoded, out HybridCursorPosition position).Should().BeTrue();
        position.Score.Should().Be(score);
        position.Id.Should().Be(Id);
    }

    [Fact]
    public void RoundTrips_UnderCommaDecimalCulture()
    {
        using var culture = new CultureScope(new CultureInfo("fr-FR"));
        const double score = 1.5;

        string encoded = HybridCursorCodec.Encode(score, Id);

        HybridCursorCodec.TryParse(encoded, out HybridCursorPosition position).Should().BeTrue();
        position.Score.Should().Be(score);
        position.Id.Should().Be(Id);
    }

    [Fact]
    public void ReturnsFalse_WhenCursorIsNull()
    {
        HybridCursorCodec.TryParse(null, out _).Should().BeFalse();
    }

    [Fact]
    public void ReturnsFalse_WhenCursorIsNotBase64()
    {
        HybridCursorCodec.TryParse("not-base64!!", out _).Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("12.5")]
    [InlineData("12.5|")]
    [InlineData("abc|id")]
    public void ReturnsFalse_WhenCursorIsMalformed(string raw)
    {
        string encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(raw));

        HybridCursorCodec.TryParse(encoded, out _).Should().BeFalse();
    }

    [Theory]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("-Infinity")]
    public void ReturnsFalse_WhenScoreIsNotFinite(string score)
    {
        string encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{score}|{Id}"));

        HybridCursorCodec.TryParse(encoded, out _).Should().BeFalse();
    }

    private sealed class CultureScope : IDisposable
    {
        private readonly CultureInfo _previous = CultureInfo.CurrentCulture;

        public CultureScope(CultureInfo culture)
        {
            CultureInfo.CurrentCulture = culture;
        }

        public void Dispose() => CultureInfo.CurrentCulture = _previous;
    }
}
