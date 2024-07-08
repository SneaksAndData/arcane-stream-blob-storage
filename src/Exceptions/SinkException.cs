using System;

namespace Arcane.Stream.BlobStorage.Exceptions;

/// <summary>
/// An exception thrown when a sink operation fails
/// </summary>
public class SinkException : Exception
{
    public SinkException(string message): base(message)
    {
    }
}
