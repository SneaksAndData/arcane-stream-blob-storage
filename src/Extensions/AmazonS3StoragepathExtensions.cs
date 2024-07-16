using System.Collections.Generic;
using Snd.Sdk.Storage.Models.BlobPath;

namespace Arcane.Stream.BlobStorage.Extensions;

public static class AmazonS3StoragePathExtensions
{
    /// <summary>
    /// Converts the Amazon S3 storage path to a dictionary of metrics tags
    /// </summary>
    /// <param name="path">Path</param>
    /// <returns>Metrics tags</returns>
   public static SortedDictionary<string, string> ToMetricsTags(this AmazonS3StoragePath path)
   {
       return new SortedDictionary<string, string>
       {
           { "bucket", path.Bucket },
           { "key", path.ObjectKey }
       };
   }
}
