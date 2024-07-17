namespace Arcane.Stream.BlobStorage.Contracts;

/// <summary>
/// Metrics specific to the BlobStorage stream plugin.
/// </summary>
public class DeclaredMetrics
{
    /// <summary>Number of objects found by streamer in the source bucket.</summary>
    public const string OBJECTS_INCOMING = "objects.incoming";

    /// <summary>Number of objects written by streamer into the target bucket.</summary>
    public const string OBJECTS_OUTGOING = "objects.outgoing";

    /// <summary>Number objects size processed by the streamer.</summary>
    public const string OBJECTS_SIZE = "objects.size";

    /// <summary>Number of objects deleted by the streamer in the source bucket.</summary>
    public const string OBJECTS_DELETED = "objects.deleted";
}
