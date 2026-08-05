namespace TokenBurn.Processor.Features.Imports;

public sealed record ImportCommandRequest(string Source, string Path, DateTimeOffset? Since);
