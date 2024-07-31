using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.Streams.Supervision;
using Akka.Util;
using Akka.Util.Extensions;
using Arcane.Framework.Services.Base;
using Arcane.Framework.Sources.BlobStorage;
using Arcane.Stream.BlobStorage.Contracts;
using Arcane.Stream.BlobStorage.Exceptions;
using Arcane.Stream.BlobStorage.Extensions;
using Arcane.Stream.BlobStorage.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Snd.Sdk.ActorProviders;
using Snd.Sdk.Metrics.Base;
using Snd.Sdk.Storage.Base;
using Snd.Sdk.Storage.Models.BlobPath;
using Snd.Sdk.Tasks;

namespace Arcane.Stream.BlobStorage.GraphBuilder;

public class BlobStorageGraphBuilder: IStreamGraphBuilder<BlobStorageStreamContext>
{
    private readonly IBlobStorageListService sourceBlobListStorageService;
    private readonly IBlobStorageWriter<AmazonS3StoragePath> targetBlobStorageService;
    private readonly IBlobStorageWriter<AmazonS3StoragePath> sourceBlobStorageWriter;
    private readonly IBlobStorageReader<AmazonS3StoragePath> sourceBlobStorageReader;
    private readonly ILogger<BlobStorageGraphBuilder> logger;
    private readonly MetricsService metricsService;

    private SortedDictionary<string, string> sourceDimensions;
    private SortedDictionary<string, string> targetDimensions;

    public BlobStorageGraphBuilder(
        IBlobStorageListService sourceBlobStorageService,
        IBlobStorageReader<AmazonS3StoragePath> sourceBlobStorageReader,
        [FromKeyedServices(StorageType.SOURCE)] IBlobStorageWriter<AmazonS3StoragePath> sourceBlobStorageWriter,
        [FromKeyedServices(StorageType.TARGET)] IBlobStorageWriter<AmazonS3StoragePath> targetBlobStorageService,
        MetricsService metricsService,
        ILogger<BlobStorageGraphBuilder> logger)
    {
        this.sourceBlobListStorageService = sourceBlobStorageService;
        this.sourceBlobStorageReader = sourceBlobStorageReader;
        this.sourceBlobStorageWriter = sourceBlobStorageWriter;
        this.targetBlobStorageService = targetBlobStorageService;
        this.logger = logger;
        this.metricsService = metricsService;
    }

    public IRunnableGraph<(UniqueKillSwitch, Task)> BuildGraph(BlobStorageStreamContext context)
    {
        if (!AmazonS3StoragePath.IsAmazonS3Path(context.TargetPath))
        {
            throw new ConfigurationException("Target path is invalid, only Amazon S3 paths are supported");
        }

        if (context.IsBackfilling)
        {
            throw new ConfigurationException("Backfilling is not supported for this stream type");
        }

        var parsedSourcePath = GetPath(context.SourcePath);
        this.sourceDimensions = parsedSourcePath.ToMetricsTags(context);

        var parsedTargetPath = GetPath(context.TargetPath);
        this.targetDimensions = parsedTargetPath.ToMetricsTags(context);

        var source = BlobStorageSource.Create(
            context.SourcePath,
            this.sourceBlobListStorageService,
            context.ChangeCaptureInterval);

        return Source.FromGraph(source)
            .Throttle(context.ElementsPerSecond, TimeSpan.FromSeconds(1), context.RequestThrottleBurst, ThrottleMode.Shaping)
            .Select(document =>
            {
                this.metricsService.Increment(DeclaredMetrics.OBJECTS_INCOMING, this.sourceDimensions);
                return document;
            })
            .SelectAsync(context.ReadParallelism, b => this.GetBlobContentAsync(parsedSourcePath, b))
            .CollectOption()
            .SelectAsync(context.WriteParallelism, b => this.SaveBlobContentAsync(parsedTargetPath, b))
            .ViaMaterialized(KillSwitches.Single<AmazonS3StoragePath>(), Keep.Right)
            .ToMaterialized(GetSink(context, this.RemoveSource), Keep.Both)
            .WithAttributes(ActorAttributes.CreateSupervisionStrategy(DecideOnFailure));
    }

    private Task<Option<(AmazonS3StoragePath, string, BinaryData)>> GetBlobContentAsync(AmazonS3StoragePath rootPath, string blobPath)
    {
        var fullPath = new AmazonS3StoragePath(rootPath.Bucket, blobPath);
        this.logger.LogDebug("Reading blob content from {Bucket}, {Key}", fullPath.Bucket, fullPath.ObjectKey);
        return this.sourceBlobStorageReader
            .GetBlobContentAsync(fullPath, data => data)
            .Map(data =>
            {
                if (data == null)
                {
                    return Option<(AmazonS3StoragePath, string, BinaryData)>.None;
                }
                this.metricsService.Count(DeclaredMetrics.OBJECTS_SIZE, data.ToMemory().Length, this.sourceDimensions);
                return (rootPath, blobPath, data).AsOption();
            });
    }

    private Task<AmazonS3StoragePath> SaveBlobContentAsync(AmazonS3StoragePath targetRoot, (AmazonS3StoragePath, string, BinaryData) writeRequest)
    {
        var (sourceRoot, sourceKey, data) = writeRequest;
        
        var targetKey = sourceKey.TrimStart(targetRoot.ObjectKey.ToArray());
        var finalTargetPath = targetRoot.Join(targetKey);
        this.logger.LogInformation("Saving blob content to {BlobPath}", finalTargetPath);
        this.metricsService.Increment(DeclaredMetrics.OBJECTS_OUTGOING, this.targetDimensions);
        return this.targetBlobStorageService
            .SaveBytesAsBlob(data, finalTargetPath, overwrite: true)
            .Map(_ => new AmazonS3StoragePath(sourceRoot.Bucket, sourceKey));
    }

    private async Task RemoveSource(AmazonS3StoragePath pathToDelete)
    {
        this.logger.LogDebug("Removing blob content from {BlobPath}", pathToDelete);
        this.metricsService.Increment(DeclaredMetrics.OBJECTS_DELETED, this.sourceDimensions);
        var success = await this.sourceBlobStorageWriter.RemoveBlob(pathToDelete);
        if (!success)
        {
            throw new SinkException($"Failed to remove blob {pathToDelete}");
        }
    }

    private static Directive DecideOnFailure(Exception ex)
    {
        return ex switch
        {
            _ => Directive.Stop
        };
    }
    
    private static AmazonS3StoragePath GetPath(string path)
    {
        if (!AmazonS3StoragePath.IsAmazonS3Path(path))
        {
            throw new ConfigurationException("Source path is invalid, only Amazon S3 paths are supported");
        }

        return new AmazonS3StoragePath(path);
    }
    
    private static Sink<AmazonS3StoragePath, Task> GetSink(BlobStorageStreamContext context, Func<AmazonS3StoragePath, Task> action)
    {
        return Sink.ForEachAsync<AmazonS3StoragePath>(context.DeleteParallelism, async deleteRequest =>
        {
            await action(deleteRequest);
        }).MapMaterializedValue(v => (Task)v);
    }
}
