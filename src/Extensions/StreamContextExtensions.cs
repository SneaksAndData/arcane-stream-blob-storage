using System;
using System.Threading.Tasks;
using Akka.Streams.Dsl;
using Arcane.Stream.BlobStorage.Models;
using Snd.Sdk.Storage.Models.Base;

namespace Arcane.Stream.BlobStorage.Extensions;

public static class StreamContextExtensions
{
    public static Sink<(IStoragePath, string), Task> GetSink(this BlobStorageStreamContext context, Func<(IStoragePath, string), Task > action)
    {
        return Sink.ForEachAsync<(IStoragePath, string)>(context.DeleteParallelism, async deleteRequest =>
        {
            await action(deleteRequest);
        }).MapMaterializedValue(v => (Task)v);
    }
}
