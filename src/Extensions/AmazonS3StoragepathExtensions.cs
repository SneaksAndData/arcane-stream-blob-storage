using System.Collections.Generic;
using Arcane.Framework.Services.Base;
using Snd.Sdk.Helpers;
using Snd.Sdk.Storage.Models.BlobPath;

namespace Arcane.Stream.BlobStorage.Extensions;

public static class AmazonS3StoragePathExtensions
{
    /// <summary>
    /// Converts the Amazon S3 storage path to a dictionary of metrics tags.
    /// </summary>
    /// <param name="path">Path in Snd.Sdk format.</param>
    /// <param name="context">Stream context.</param>
    /// <returns>Metrics tags as sorted dictionary.</returns>
    public static SortedDictionary<string, string> ToMetricsTags(this AmazonS3StoragePath path, IStreamContext context)
    {
        return new SortedDictionary<string, string>
        {
            { "bucket", path.Bucket },
            { "key", path.ObjectKey },
            { "arcane.sneaksanddata.com/kind", CodeExtensions.CamelCaseToSnakeCase(context.StreamKind) },
            { "arcane.sneaksanddata.com/stream_id", context.StreamId }
        };
    }
}
