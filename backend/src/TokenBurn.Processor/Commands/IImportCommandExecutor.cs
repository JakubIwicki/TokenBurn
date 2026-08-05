using TokenBurn.Processor.Domain;

namespace TokenBurn.Processor.Commands;

public interface IImportCommandExecutor
{
    string CommandType { get; }

    Task ExecuteAsync(ImportCommand command, Func<string, CancellationToken, Task> updateProgress, CancellationToken ct);
}
