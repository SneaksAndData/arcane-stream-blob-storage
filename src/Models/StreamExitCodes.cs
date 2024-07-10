namespace Arcane.Stream.BlobStorage.Models;

public class StreamExitCodes
{
    /// <summary>
    /// Exit code that should fail the stream job immediately without retrying.
    /// </summary>
    public const int NO_RETRY = 3;
}
