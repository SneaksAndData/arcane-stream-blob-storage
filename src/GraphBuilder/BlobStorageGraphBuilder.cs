using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.Streams.Supervision;
using Arcane.Framework.Services.Base;
using Arcane.Framework.Sources.BlobStorage;
using Arcane.Stream.BlobStorage.Contracts;
using Arcane.Stream.BlobStorage.Exceptions;
using Arcane.Stream.BlobStorage.Extensions;
using Arcane.Stream.BlobStorage.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Snd.Sdk.Metrics.Base;
using Snd.Sdk.Storage.Base;
using Snd.Sdk.Storage.Models.Base;
using Snd.Sdk.Storage.Models.BlobPath;
using Snd.Sdk.Tasks;

namespace Arcane.Stream.BlobStorage.GraphBuilder;

public class BlobStorageGraphBuilder : IStreamGraphBuilder<BlobStorageStreamContext>
{
    private readonly IBlobStorageListService sourceBlobListStorageService;
    private readonly IBlobStorageWriter targetBlobStorageService;
    private readonly IBlobStorageWriter sourceBlobStorageWriter;
    private readonly IBlobStorageReader sourceBlobStorageReader;
    private readonly ILogger<BlobStorageGraphBuilder> logger;
    private readonly MetricsService metricsService;
    
    private SortedDictionary<string, string> sourceDimensions;
    private SortedDictionary<string, string> targetDimensions;

    public BlobStorageGraphBuilder(
        IBlobStorageListService sourceBlobStorageService,
        IBlobStorageReader sourceBlobStorageReader,
        [FromKeyedServices(StorageType.SOURCE)] IBlobStorageWriter sourceBlobStorageWriter,
        [FromKeyedServices(StorageType.TARGET)] IBlobStorageWriter targetBlobStorageService,
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
        if (!AmazonS3StoragePath.IsAmazonS3Path(context.SourcePath))
        {
            throw new ConfigurationException("Source path is invalid, only Amazon S3 paths are supported");
        }

        if (!AmazonS3StoragePath.IsAmazonS3Path(context.TargetPath))
        {
            throw new ConfigurationException("Target path is invalid, only Amazon S3 paths are supported");
        }
        
        if (context.IsBackfilling)
        {
            throw new ConfigurationException("Backfilling is not supported for this stream type");
        }

        var parsedSourcePath = new AmazonS3StoragePath(context.SourcePath);
        this.sourceDimensions = parsedSourcePath.ToMetricsTags();
        
        var parsedTargetPath = new AmazonS3StoragePath(context.TargetPath);
        this.targetDimensions= parsedTargetPath.ToMetricsTags();
        
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
            .SelectAsync(context.WriteParallelism, b => this.SaveBlobContentAsync(parsedTargetPath, b))
            .ViaMaterialized(KillSwitches.Single<(IStoragePath, string)>(), Keep.Right)
            .ToMaterialized(context.GetSink(this.RemoveSource), Keep.Both)
            .WithAttributes(ActorAttributes.CreateSupervisionStrategy(DecideOnFailure));
    }

    private Task<(IStoragePath, string, BinaryData)> GetBlobContentAsync(IStoragePath rootPath, string blobPath)
    {
        this.logger.LogDebug("Reading blob content from {BlobPath}", rootPath.Join(blobPath).ToHdfsPath());
        return this.sourceBlobStorageReader
            .GetBlobContentAsync(rootPath.ToHdfsPath(), blobPath, data => data)
            .Map(data =>
            {
                if (data == null)
                {
                    throw new ProcessingException(rootPath, blobPath);
                }
                this.metricsService.Count(DeclaredMetrics.OBJECTS_SIZE, data.ToMemory().Length, this.sourceDimensions);
                return (rootPath, blobPath, data);
            });
    }

    private Task<(IStoragePath, string)> SaveBlobContentAsync(IStoragePath targetPath, (IStoragePath, string, BinaryData) writeRequest)
    {
        var (rootPath, blobName, data) = writeRequest;
        this.logger.LogDebug("Saving blob content to {BlobPath}", targetPath.Join(blobName).ToHdfsPath());
        this.metricsService.Increment(DeclaredMetrics.OBJECTS_OUTGOING, this.targetDimensions);
        return this.targetBlobStorageService
            .SaveBytesAsBlob(data, targetPath.ToHdfsPath(), blobName, overwrite: true)
            .Map(_ => (rootPath, blobName));
    }

    private async Task RemoveSource((IStoragePath, string) deleteRequest)
    {
        var (sourceRoot, sourceBlobName) = deleteRequest;
        this.logger.LogDebug("Removing blob content from {BlobPath}", sourceRoot.Join(sourceBlobName).ToHdfsPath());
        this.metricsService.Increment(DeclaredMetrics.OBJECTS_DELETED, this.sourceDimensions);
        var success = await this.sourceBlobStorageWriter.RemoveBlob(sourceRoot.ToHdfsPath(), sourceBlobName);
        if (!success)
        {
            throw new SinkException($"Failed to remove blob {sourceBlobName} from {sourceRoot}");
        }
    }
    
    private static Directive DecideOnFailure(Exception ex)
    {
        return ex switch
        {
            ProcessingException => Directive.Resume,
            _ => Directive.Stop
        };
    }
}
