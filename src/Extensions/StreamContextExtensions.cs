using System;
using System.Threading.Tasks;
using Akka.Streams.Dsl;
using Arcane.Stream.BlobStorage.Models;
using Snd.Sdk.Storage.Base;

namespace Arcane.Stream.BlobStorage.Extensions;

public static class StreamContextExtensions
{
    public static Sink<string, Task> GetSink(this BlobStorageStreamContext context, IBlobStorageService blobStorageService)
    {
        return Sink.ForEachAsync<string>(context.ReadParallelism, async sourceBlobName =>
        {
            var res = await blobStorageService.RemoveBlob(context.SourcePath, sourceBlobName);
            if (!res)
            {
                throw new Exception($"Failed to remove blob {sourceBlobName} from {context.SourcePath}");
            }
        }).MapMaterializedValue(v => (Task)v);
    }
}
