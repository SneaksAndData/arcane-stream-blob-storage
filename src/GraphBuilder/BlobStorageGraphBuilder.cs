using System;
using System.Threading.Tasks;
using Akka.Streams;
using Akka.Streams.Dsl;
using Arcane.Framework.Services.Base;
using Arcane.Framework.Sources.BlobStorage;
using Arcane.Stream.BlobStorage.Exceptions;
using Arcane.Stream.BlobStorage.Extensions;
using Arcane.Stream.BlobStorage.Models;
using Microsoft.Extensions.DependencyInjection;
using Snd.Sdk.Storage.Base;
using Snd.Sdk.Storage.Models.Base;
using Snd.Sdk.Storage.Models.BlobPath;
using Snd.Sdk.Tasks;

namespace Arcane.Stream.BlobStorage.GraphBuilder;

public class BlobStorageGraphBuilder : IStreamGraphBuilder<BlobStorageStreamContext>
{
    private readonly IBlobStorageListService sourceBlobStorageService;
    private readonly IBlobStorageWriter targetBlobStorageService;
    private readonly IBlobStorageWriter sourceBlobStorageWriter;
    private readonly IBlobStorageReader sourceBlobStorageReader;

    public BlobStorageGraphBuilder(
        IBlobStorageListService sourceBlobStorageService,
        IBlobStorageReader sourceBlobStorageReader,
        [FromKeyedServices(StorageType.SOURCE)] IBlobStorageWriter sourceBlobStorageWriter,
        [FromKeyedServices(StorageType.TARGET)] IBlobStorageWriter targetBlobStorageService)
    {
        this.sourceBlobStorageService = sourceBlobStorageService;
        this.sourceBlobStorageReader = sourceBlobStorageReader;
        this.sourceBlobStorageWriter = sourceBlobStorageWriter;
        this.targetBlobStorageService = targetBlobStorageService;
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

        var parsedSourcePath = new AmazonS3StoragePath(context.SourcePath);
        var parsedTargetPath = new AmazonS3StoragePath(context.TargetPath);
        var source = BlobStorageSource.Create(
            parsedSourcePath.Bucket,
            parsedSourcePath.ObjectKey,
            this.sourceBlobStorageService,
            context.ChangeCaptureInterval);
        return Source.FromGraph(source)
            .Throttle(context.ElementsPerSecond, TimeSpan.FromSeconds(1), context.RequestThrottleBurst, ThrottleMode.Shaping)
            .SelectAsync(context.ReadParallelism, b => this.GetBlobContentAsync(parsedSourcePath, b))
            .SelectAsync(context.WriteParallelism, b => this.SaveBlobContentAsync(parsedTargetPath, b))
            .ViaMaterialized(KillSwitches.Single<(IStoragePath, string)>(), Keep.Right)
            .ToMaterialized(context.GetSink(this.sourceBlobStorageWriter), Keep.Both);
    }


    private Task<(IStoragePath, string, BinaryData)> GetBlobContentAsync(IStoragePath rootPath, string blobPath)
    {
        return this.sourceBlobStorageReader
            .GetBlobContentAsync(rootPath.ObjectKey, blobPath, data => data)
            .Map(d => (rootPath, blobPath, d));
    }

    private Task<(IStoragePath, string)> SaveBlobContentAsync(IStoragePath targetPath, (IStoragePath, string, BinaryData) writeRequest)
    {
        var (rootPath, blobName, data) = writeRequest;
        var targetFullPath = $"{targetPath.ObjectKey}/{rootPath.ObjectKey}".Replace("//", "/");
        return this.targetBlobStorageService
            .SaveBytesAsBlob(data, targetFullPath, blobName, overwrite: true)
            .Map(_ => (rootPath, blobName));
    }
}
