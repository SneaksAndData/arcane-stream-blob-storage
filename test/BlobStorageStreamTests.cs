using System;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Streams;
using Arcane.Stream.BlobStorage.GraphBuilder;
using Arcane.Stream.BlobStorage.Models;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Snd.Sdk.Storage.Base;
using Snd.Sdk.Storage.Models;
using Xunit;

namespace Arcane.Stream.BlobStorage.Tests;

public class BlobStorageStreamTests
{
    // Akka service and test helpers
    private readonly ActorSystem actorSystem = ActorSystem.Create(nameof(BlobStorageStreamTests));

    // Mocks
    private readonly Mock<IBlobStorageService> blobStorageServiceMock = new();
    private readonly TaskCompletionSource tcs = new();
    private readonly CancellationTokenSource cts = new();

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
            TargetPath = "s3a://target-bucket/target/",
            ChangeCaptureInterval = TimeSpan.FromSeconds(1),
            ElementsPerSecond = 1000,
            RequestThrottleBurst = 100
        };

        this.blobStorageServiceMock.Setup(s 
            => s.RemoveBlob(It.IsAny<string>(), It.IsAny<string>()))
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
            .Returns(new StoredBlob[] { new StoredBlob { Name = "name" } });

        await task;

        this.blobStorageServiceMock.Verify(s =>
            s.SaveBytesAsBlob(It.IsAny<BinaryData>(),"target/prefix/to/blobs", "name", true));
        this.blobStorageServiceMock.Verify(s => s.ListBlobsAsEnumerable("prefix/to/blobs"));
        this.blobStorageServiceMock.Verify(s
            => s.RemoveBlob("prefix/to/blobs", "name"));
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
            .Returns(new StoredBlob[] { new StoredBlob { Name = "name" } });
        
        await Assert.ThrowsAnyAsync<AggregateException>( async () => await task);
    }


    private ServiceProvider CreateServiceProvider()
    {
        return new ServiceCollection()
            .AddSingleton<IMaterializer>(this.actorSystem.Materializer())
            .AddSingleton(this.actorSystem)
            .AddSingleton(this.blobStorageServiceMock.Object)
            .AddSingleton<IBlobStorageReader>(this.blobStorageServiceMock.Object)
            .AddSingleton<IBlobStorageWriter>(this.blobStorageServiceMock.Object)
            .AddSingleton<BlobStorageGraphBuilder>()
            .BuildServiceProvider();
    }
}
