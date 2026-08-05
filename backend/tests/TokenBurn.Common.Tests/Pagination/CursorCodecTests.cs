using TokenBurn.Common.Pagination;

namespace TokenBurn.Common.Tests.Pagination;

public sealed class CursorCodecTests
{
    [Fact]
    public void RoundTrips_StartedAtAndId()
    {
        var startedAt = new DateTimeOffset(2026, 7, 24, 21, 57, 51, 995, TimeSpan.Zero);
        Guid id = Guid.NewGuid();

        string cursor = CursorCodec.Encode(startedAt, id);
        bool decoded = CursorCodec.TryDecode(cursor, out DateTimeOffset? decodedStartedAt, out Guid decodedId);

        decoded.Should().BeTrue();
        decodedStartedAt.Should().Be(startedAt);
        decodedId.Should().Be(id);
    }

    [Fact]
    public void RoundTrips_NullStartedAt()
    {
        Guid id = Guid.NewGuid();

        string cursor = CursorCodec.Encode(null, id);
        bool decoded = CursorCodec.TryDecode(cursor, out DateTimeOffset? decodedStartedAt, out Guid decodedId);

        decoded.Should().BeTrue();
        decodedStartedAt.Should().BeNull();
        decodedId.Should().Be(id);
    }

    [Fact]
    public void PreservesOffsetPrecision()
    {
        var startedAt = new DateTimeOffset(2026, 8, 1, 12, 30, 0, 0, TimeSpan.FromHours(8));
        Guid id = Guid.NewGuid();

        string cursor = CursorCodec.Encode(startedAt, id);
        CursorCodec.TryDecode(cursor, out DateTimeOffset? decoded, out Guid decodedId);

        decoded.Should().Be(startedAt);
        decodedId.Should().Be(id);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-base64!!")]
    [InlineData("aGVsbG8=")] // "hello" — no pipe separator
    public void Rejects_Garbage(string cursor)
    {
        CursorCodec.TryDecode(cursor, out _, out _).Should().BeFalse();
    }
}
