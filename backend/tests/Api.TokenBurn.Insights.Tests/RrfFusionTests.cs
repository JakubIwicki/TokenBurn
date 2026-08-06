using Api.TokenBurn.Insights.Features.Search;
using FluentAssertions;

namespace Api.TokenBurn.Insights.Tests;

public sealed class RrfFusionTests
{
    private const int DefaultK = 60;

    [Fact]
    public void Fuses_SingleLeg_InLegOrder()
    {
        IReadOnlyList<RrfFusedHit> fused = RrfFusion.Fuse([["a", "b", "c"]], DefaultK);

        fused.Select(hit => hit.Id).Should().Equal("a", "b", "c");
        fused[0].Score.Should().BeApproximately(1.0 / 61, 1e-12);
        fused[1].Score.Should().BeApproximately(1.0 / 62, 1e-12);
        fused[2].Score.Should().BeApproximately(1.0 / 63, 1e-12);
    }

    [Fact]
    public void Fuses_TwoOverlappingLegs_WithHandComputedScores()
    {
        IReadOnlyList<RrfFusedHit> fused = RrfFusion.Fuse(
        [
            ["a", "b", "c"],
            ["c", "b"]
        ], DefaultK);

        fused.Select(hit => hit.Id).Should().Equal("c", "b", "a");
        fused[0].Score.Should().BeApproximately(1.0 / 61 + 1.0 / 63, 1e-12);
        fused[1].Score.Should().BeApproximately(2.0 / 62, 1e-12);
        fused[2].Score.Should().BeApproximately(1.0 / 61, 1e-12);
    }

    [Fact]
    public void Fuses_DisjointLegs_WithIndependentContributions()
    {
        IReadOnlyList<RrfFusedHit> fused = RrfFusion.Fuse(
        [
            ["a", "b"],
            ["c"]
        ], DefaultK);

        fused.Select(hit => hit.Id).Should().Equal("c", "a", "b");
        fused.Single(hit => hit.Id == "a").Score.Should().BeApproximately(1.0 / 61, 1e-12);
        fused.Single(hit => hit.Id == "b").Score.Should().BeApproximately(1.0 / 62, 1e-12);
        fused.Single(hit => hit.Id == "c").Score.Should().BeApproximately(1.0 / 61, 1e-12);
    }

    [Fact]
    public void TiesBreak_HigherIdFirst_WhenScoresEqual()
    {
        IReadOnlyList<RrfFusedHit> fused = RrfFusion.Fuse(
        [
            ["b", "a"],
            ["a", "b"]
        ], DefaultK);

        fused.Select(hit => hit.Id).Should().Equal("b", "a");
        fused[0].Score.Should().Be(fused[1].Score);
    }

    [Fact]
    public void IsDeterministic_ForEqualInputs()
    {
        IReadOnlyList<IReadOnlyList<string>> legs = [["a", "b", "c"], ["c", "b"]];

        IReadOnlyList<RrfFusedHit> first = RrfFusion.Fuse(legs, DefaultK);
        IReadOnlyList<RrfFusedHit> second = RrfFusion.Fuse(legs, DefaultK);

        first.Select(hit => hit.Id).Should().Equal(second.Select(hit => hit.Id));
        first.Select(hit => hit.Score).Should().Equal(second.Select(hit => hit.Score));
    }
}
