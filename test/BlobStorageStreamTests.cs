using System;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Streams;
using Arcane.Stream.BlobStorage.Extensions;
using Arcane.Stream.BlobStorage.GraphBuilder;
using Arcane.Stream.BlobStorage.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Snd.Sdk.Helpers;
using Snd.Sdk.Metrics.Base;
using Snd.Sdk.Storage.Base;
using Snd.Sdk.Storage.Models;
using Snd.Sdk.Storage.Models.BlobPath;
using Xunit;

namespace Arcane.Stream.BlobStorage.Tests;

public class BlobStorageStreamTests
{
    // Akka service and test helpers
    private readonly ActorSystem actorSystem = ActorSystem.Create(nameof(BlobStorageStreamTests));

    // Mocks
    private readonly Mock<IBlobStorageService<AmazonS3StoragePath>> blobStorageServiceMock = new();

    [Fact]
    public async Task TestCanStreamBlobs()
    {
        var builder = this.CreateServiceProvider().GetRequiredService<BlobStorageGraphBuilder>();
        var context = new BlobStorageStreamContext
        {
            ReadParallelism = 1,
            WriteParallelism = 1,
            DeleteParallelism = 1,
            SourcePath = "s3a://source-bucket/prefix/to/blobs",
            TargetPath = "s3a://target-bucket/prefix/to/blobs",
            ChangeCaptureInterval = TimeSpan.FromSeconds(1),
            ElementsPerSecond = 1000,
            RequestThrottleBurst = 100,
        };

        context.SetStreamKind(nameof(this.TestCanStreamBlobs));
        this.blobStorageServiceMock.Setup(s
            => s.RemoveBlob(It.IsAny<AmazonS3StoragePath>()))
            .ReturnsAsync(true);
        var graph = builder.BuildGraph(context);
        var callCount = 0;

        var (killSwitch, task) = graph.Run(this.actorSystem.Materializer());
        this.blobStorageServiceMock
            .Setup(s => s.ListBlobsAsEnumerable(It.IsAny<string>()))
            .Callback(() =>
            {
                if (callCount > 3)
                {
                    killSwitch.Shutdown();
                }

                callCount++;
            })
            .Returns(new[] { new StoredBlob { Name = "prefix/to/blobs/name" } });
        this.blobStorageServiceMock
            .Setup(s => s.GetBlobContentAsync(It.IsAny<AmazonS3StoragePath>(), 
                It.IsAny<Func<BinaryData, BinaryData>>()))
            .ReturnsAsync(new BinaryData([1, 2, 3]));

        await task;

        this.blobStorageServiceMock.Verify(s =>
            s.SaveBytesAsBlob( It.IsAny<BinaryData>(), "s3a://target-bucket/prefix/to/blobs/name".AsAmazonS3Path(), true));
        
        this.blobStorageServiceMock.Verify(s =>
            s.ListBlobsAsEnumerable("s3a://source-bucket/prefix/to/blobs"));
        
        this.blobStorageServiceMock.Verify(s
            => s.RemoveBlob("s3a://source-bucket/prefix/to/blobs/name".AsAmazonS3Path()));
    }

    [Fact]
    public async Task TestFailsIfCannotDeleteBlob()
    {
        var builder = this.CreateServiceProvider().GetRequiredService<BlobStorageGraphBuilder>();
        var context = new BlobStorageStreamContext
        {
            ReadParallelism = 1,
            WriteParallelism = 1,
            DeleteParallelism = 1,
            SourcePath = "s3a://prefix/to/blobs",
            TargetPath = "s3a://target/",
            ChangeCaptureInterval = TimeSpan.FromSeconds(1),
            ElementsPerSecond = 1000,
            RequestThrottleBurst = 100
        };

        context.SetStreamKind(nameof(this.TestFailsIfCannotDeleteBlob));
        var graph = builder.BuildGraph(context);
        var callCount = 0;

        var (killSwitch, task) = graph.Run(this.actorSystem.Materializer());
        this.blobStorageServiceMock
            .Setup(s => s.ListBlobsAsEnumerable(It.IsAny<string>()))
            .Callback(() =>
            {
                if (callCount > 3)
                {
                    killSwitch.Shutdown();
                }

                callCount++;
            })
            .Returns(new[] { new StoredBlob { Name = "name" } });

        await Assert.ThrowsAnyAsync<AggregateException>(async () => await task);
    }


    private ServiceProvider CreateServiceProvider()
    {
        return new ServiceCollection()
            .AddSingleton<IMaterializer>(this.actorSystem.Materializer())
            .AddSingleton(this.actorSystem)
            .AddSingleton<IBlobStorageListService>(this.blobStorageServiceMock.Object)
            .AddSingleton<IBlobStorageReader<AmazonS3StoragePath>>(this.blobStorageServiceMock.Object)
            .AddKeyedSingleton<IBlobStorageWriter<AmazonS3StoragePath>>(StorageType.SOURCE, this.blobStorageServiceMock.Object)
            .AddKeyedSingleton<IBlobStorageWriter<AmazonS3StoragePath>>(StorageType.TARGET, this.blobStorageServiceMock.Object)
            .AddSingleton(new Mock<ILogger<BlobStorageGraphBuilder>>().Object)
            .AddSingleton(new Mock<MetricsService>().Object)
            .AddSingleton<BlobStorageGraphBuilder>()
            .BuildServiceProvider();
    }
}
