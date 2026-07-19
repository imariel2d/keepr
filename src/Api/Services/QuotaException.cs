namespace Keepr.Api.Services;

/// <summary>Thrown when an operation would push a user over their storage quota.</summary>
public class QuotaExceededException(long requested, long remaining)
    : Exception($"Upload of {requested} bytes exceeds remaining quota of {remaining} bytes.")
{
    public long Requested { get; } = requested;
    public long Remaining { get; } = remaining;
}
