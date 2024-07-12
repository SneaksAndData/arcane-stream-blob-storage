using System;
using Snd.Sdk.Storage.Models.Base;

namespace Arcane.Stream.BlobStorage.Exceptions;

/// <summary>
/// An exception thrown when an intermediate processing operation fails
/// </summary>
public class ProcessingException : Exception
{
    public ProcessingException(IStoragePath path, string blobName)
        : base($"Failed to download {path.Join(blobName).ToHdfsPath()}")
    {
    }
}
