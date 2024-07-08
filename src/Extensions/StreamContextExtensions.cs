using System.Threading.Tasks;
using Akka.Streams.Dsl;
using Arcane.Stream.BlobStorage.Exceptions;
using Arcane.Stream.BlobStorage.Models;
using Snd.Sdk.Storage.Base;
using Snd.Sdk.Storage.Models.Base;

namespace Arcane.Stream.BlobStorage.Extensions;

public static class StreamContextExtensions
{
    public static Sink<(IStoragePath, string), Task> GetSink(this BlobStorageStreamContext context, IBlobStorageService blobStorageService)
    {
        return Sink.ForEachAsync<(IStoragePath, string)>(context.DeleteParallelism, async deleteRequest =>
        {
            var (sourceRoot, sourceBlobName) = deleteRequest;
            var res = await blobStorageService.RemoveBlob(sourceRoot.ObjectKey, sourceBlobName);
            if (!res)
            {
                throw new SinkException($"Failed to remove blob {sourceBlobName} from {sourceRoot}");
            }
        }).MapMaterializedValue(v => (Task)v);
    }
}
