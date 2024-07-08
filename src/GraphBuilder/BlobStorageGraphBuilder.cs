using System;
using System.Threading.Tasks;
using Akka.Streams;
using Akka.Streams.Dsl;
using Arcane.Framework.Services.Base;
using Arcane.Framework.Sources.BlobStorage;
using Arcane.Stream.BlobStorage.Extensions;
using Arcane.Stream.BlobStorage.Models;
using Snd.Sdk.Storage.Base;
using Snd.Sdk.Tasks;

namespace Arcane.Stream.BlobStorage.GraphBuilder;

public class BlobStorageGraphBuilder : IStreamGraphBuilder<BlobStorageStreamContext>
{
    private readonly IBlobStorageService sourceBlobStorageService;
    private readonly IBlobStorageWriter targetBlobStorageService;

    public BlobStorageGraphBuilder(
        IBlobStorageService sourceBlobStorageService,
        IBlobStorageWriter targetBlobStorageService)
    {
        this.sourceBlobStorageService = sourceBlobStorageService;
        this.targetBlobStorageService = targetBlobStorageService;
    }

    public IRunnableGraph<(UniqueKillSwitch, Task)> BuildGraph(BlobStorageStreamContext context)
    {
        var source = BlobStorageSource.Create(
            context.SourcePath,
            context.Prefix,
            this.sourceBlobStorageService,
            context.ChangeCaptureInterval);
        return Source.FromGraph(source)
            .SelectAsync(context.ReadParallelism, b => this.GetBlobContentAsync(context.SourcePath, b))
            .SelectAsync(context.ReadParallelism, b => this.SaveBlobContentAsync(context.TargetPath, b))
            .ViaMaterialized(KillSwitches.Single<string>(), Keep.Right)
            .ToMaterialized(context.GetSink(this.sourceBlobStorageService), Keep.Both);
    }


    private Task<(string, BinaryData)> GetBlobContentAsync(string rootPath, string blobPath)
    {
        return this.sourceBlobStorageService
            .GetBlobContentAsync(rootPath, blobPath, data => data)
            .Map(d => (blobPath, d));
    }

    private Task<string> SaveBlobContentAsync(string targetPath, (string, BinaryData) writeRequest)
    {
        var (blobName, data) = writeRequest;
        return this.targetBlobStorageService
            .SaveBytesAsBlob(data, targetPath, blobName, overwrite: true)
            .Map(_ => blobName);
    }
}
