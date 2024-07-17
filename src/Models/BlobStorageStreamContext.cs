using System;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using Arcane.Framework.Configuration;
using Arcane.Framework.Services.Base;

namespace Arcane.Stream.BlobStorage.Models;

[ExcludeFromCodeCoverage(Justification = "Model")]
public class BlobStorageStreamContext : IStreamContext, IStreamContextWriter
{
    /// <summary>
    /// Source blob path. Should be in Proteus format.
    /// </summary>
    public string SourcePath { get; init; }

    /// <summary>
    /// Target blob path. Should be in Proteus format.
    /// </summary>
    public string TargetPath { get; init; }

    /// <summary>
    /// Parallelism for read operations (include listing blobs).
    /// </summary>
    public int ReadParallelism { get; init; }

    /// <summary>
    /// Parallelism for write operations.
    /// </summary>
    public int WriteParallelism { get; init; }

    /// <summary>
    /// Parallelism for delete operations.
    /// </summary>
    public int DeleteParallelism { get; init; }

    /// <summary>
    /// How often to check for changes in the source blob storage.
    /// </summary>
    [JsonConverter(typeof(SecondsToTimeSpanConverter))]
    [JsonPropertyName("changeCaptureIntervalSeconds")]
    public TimeSpan ChangeCaptureInterval { get; init; }

    /// <summary>
    /// Maximum allowed burst before throttling kicks in.
    /// </summary>
    public int RequestThrottleBurst { get; init; }

    /// <summary>
    /// Maximum allowed elements per second
    /// </summary>
    public int ElementsPerSecond { get; init; }

    /// <inheritdoc cref="IStreamContext.StreamId"/>>
    public string StreamId { get; private set; }

    /// <inheritdoc cref="IStreamContext.IsBackfilling"/>>
    public bool IsBackfilling { get; private set; }

    /// <inheritdoc cref="IStreamContext.StreamKind"/>>
    public string StreamKind { get; private set; }

    /// <inheritdoc cref="IStreamContextWriter.SetStreamId"/>>
    public void SetStreamId(string streamId)
    {
        this.StreamId = streamId;
    }

    /// <inheritdoc cref="IStreamContextWriter.SetBackfilling"/>>
    public void SetBackfilling(bool isRunningInBackfillMode)
    {
        this.IsBackfilling = isRunningInBackfillMode;
    }

    /// <inheritdoc cref="IStreamContextWriter.SetStreamKind"/>>
    public void SetStreamKind(string streamKind)
    {
        this.StreamKind = streamKind;
    }
}
