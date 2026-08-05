namespace TokenBurn.Contracts;

/// <summary>
///     Canonical run-status vocabulary shared by every normalized envelope. Same
///     member names as the domain's <c>RunStatus</c> so the mapper is a straight
///     switch; the two enums stay distinct because Contracts is the transport
///     vocabulary and Domain owns persistence state.
/// </summary>
public enum RunStatus
{
    Running,
    Completed,
    Failed,
    Cancelled,
    Unknown
}
