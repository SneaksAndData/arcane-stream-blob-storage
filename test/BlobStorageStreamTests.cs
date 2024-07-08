using System;
using System.Runtime.CompilerServices;
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
    public async Task BlobStorageGraphTest()
    {
        var builder = this.CreateServiceProvider().GetRequiredService<BlobStorageGraphBuilder>();
        var context = new BlobStorageStreamContext
        {
            ReadParallelism = 1,
            Prefix = "prefix",
            ChangeCaptureInterval = TimeSpan.FromSeconds(1)
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

        this.blobStorageServiceMock.Verify(s
            => s.RemoveBlob(It.IsAny<string>(), It.IsAny<string>()));
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
