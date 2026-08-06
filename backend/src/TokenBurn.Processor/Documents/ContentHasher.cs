using System.Security.Cryptography;
using System.Text;

namespace TokenBurn.Processor.Documents;

/// <summary>
///     SHA256 hex over the UTF-8 bytes of a text — the dedupe identity for search documents
///     and their chunks. Determinism is the contract: the same text must produce the same hash
///     on every process and every replay, because <c>search.documents.content_hash</c> is the
///     unique key that makes a re-import of identical content a no-op.
/// </summary>
public static class ContentHasher
{
    public static string Compute(string text)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
}
