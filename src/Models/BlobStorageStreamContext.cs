using System;
using Arcane.Framework.Services.Base;

namespace Arcane.Stream.BlobStorage.Models;

public class BlobStorageStreamContext : IStreamContext, IStreamContextWriter
{
    /// <inheritdoc cref="IStreamContext.StreamId"/>>
    public string StreamId { get; private set; }

    /// <inheritdoc cref="IStreamContext.IsBackfilling"/>>
    public bool IsBackfilling { get; private set; }

    /// <inheritdoc cref="IStreamContext.StreamKind"/>>
    public string StreamKind { get; private set; }

    public string SourcePath { get; set; }
    public string TargetPath { get; set; }
    public int ReadParallelism { get; set; }
    public string Prefix { get; set; }
    public TimeSpan ChangeCaptureInterval { get; set; }

    public void SetStreamId(string streamId)
    {
        this.StreamId = streamId;
    }

    public void SetBackfilling(bool isRunningInBackfillMode)
    {
        this.IsBackfilling = isRunningInBackfillMode;
    }

    public void SetStreamKind(string streamKind)
    {
        this.StreamKind = streamKind;
    }
}
