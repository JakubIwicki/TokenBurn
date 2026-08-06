using Microsoft.EntityFrameworkCore;
using TokenBurn.Processor.Domain;
using TokenBurn.Processor.Persistence;
using TokenBurn.Processor.Tests.Bases;

namespace TokenBurn.Processor.Tests.Persistence;

public sealed class DocumentsUpserterTests : TelemetryHandlerTestBase
{
    private static readonly DateTimeOffset IndexedAt = new(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Upserts_NewDocument_ReturnsAppliedTrueWithStoredId()
    {
        SearchDocument document = SearchDocument.Create("/tmp/a.txt", "a.txt", "documents", "hash-alpha", IndexedAt);

        (long storedId, bool applied) = await CreateSut().UpsertAsync(document, CancellationToken.None);

        applied.Should().BeTrue();
        storedId.Should().BeGreaterThan(0);
        SearchDocument stored = await Context.SearchDocuments.AsNoTracking().SingleAsync();
        stored.StoredId.Should().Be(storedId);
        stored.Uri.Should().Be("/tmp/a.txt");
        stored.ContentHash.Should().Be("hash-alpha");
        stored.IndexedAt.Should().Be(IndexedAt);
    }

    [Fact]
    public async Task Upserts_DuplicateContentHash_ReturnsSameStoredIdWithAppliedFalse()
    {
        DocumentsUpserter sut = CreateSut();
        SearchDocument first = SearchDocument.Create("/tmp/a.txt", "a.txt", "documents", "hash-alpha", IndexedAt);
        SearchDocument replay = SearchDocument.Create("/tmp/copy.txt", "copy.txt", "documents", "hash-alpha", IndexedAt);
        (long firstId, bool firstApplied) = await sut.UpsertAsync(first, CancellationToken.None);

        (long storedId, bool applied) = await sut.UpsertAsync(replay, CancellationToken.None);

        firstApplied.Should().BeTrue();
        applied.Should().BeFalse();
        storedId.Should().Be(firstId);
        (await Context.SearchDocuments.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task FindsStoredId_ByContentHash_ReturnsNull_WhenMissing()
    {
        long? storedId = await CreateSut().FindStoredIdAsync("hash-missing", CancellationToken.None);

        storedId.Should().BeNull();
    }

    [Fact]
    public async Task FindsStoredId_ByContentHash_ReturnsExistingId()
    {
        DocumentsUpserter sut = CreateSut();
        await sut.UpsertAsync(SearchDocument.Create("/tmp/a.txt", "a.txt", "documents", "hash-alpha", IndexedAt), CancellationToken.None);

        long? storedId = await sut.FindStoredIdAsync("hash-alpha", CancellationToken.None);

        storedId.Should().Be((await Context.SearchDocuments.SingleAsync()).StoredId);
    }

    [Fact]
    public async Task Upserts_DistinctContentHashes_ReturnDistinctStoredIds()
    {
        DocumentsUpserter sut = CreateSut();

        (long firstId, bool firstApplied) = await sut.UpsertAsync(
            SearchDocument.Create("/tmp/a.txt", "a.txt", "documents", "hash-alpha", IndexedAt), CancellationToken.None);
        (long secondId, bool secondApplied) = await sut.UpsertAsync(
            SearchDocument.Create("/tmp/b.txt", "b.txt", "documents", "hash-beta", IndexedAt), CancellationToken.None);

        firstApplied.Should().BeTrue();
        secondApplied.Should().BeTrue();
        secondId.Should().NotBe(firstId);
        (await Context.SearchDocuments.CountAsync()).Should().Be(2);
    }

    private DocumentsUpserter CreateSut() => new(Context);
}
