using System;
using Amazon.S3;
using Arcane.Stream.BlobStorage.Exceptions;
using Arcane.Stream.BlobStorage.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using SnD.Sdk.Extensions.Environment.Hosting;
using Snd.Sdk.Storage.Amazon;
using Snd.Sdk.Storage.Base;
using Snd.Sdk.Storage.Models.BlobPath;
using Snd.Sdk.Storage.Providers.Configurations;

namespace Arcane.Stream.BlobStorage.Extensions;

/// <summary>
/// Used to identify the storage type in the configuration and DI container
/// </summary>
public enum StorageType
{
    /// <summary>
    /// The source service type
    /// </summary>
    SOURCE,

    /// <summary>
    /// The target service type
    /// </summary>
    TARGET
}

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddSourceListService(this IServiceCollection services)
    {
        services.TryAddSingleton<IBlobStorageListService>(sp =>
        {
            var context = sp.GetRequiredService<BlobStorageStreamContext>();
            if (AmazonS3StoragePath.IsAmazonS3Path(context.SourcePath))
            {
                services.TryAddAmazonS3Client(StorageType.SOURCE, GetScopedCredentials(StorageType.SOURCE));
                var client = sp.GetRequiredKeyedService<IAmazonS3>(StorageType.SOURCE);
                var logger = sp.GetRequiredService<ILogger<AmazonBlobStorageService>>();
                return new AmazonBlobStorageService(client, logger);
            }

            throw new ConfigurationException("Source path is invalid, only Amazon S3 paths are supported");
        });
        return services;
    }

    public static IServiceCollection AddSourceReader(this IServiceCollection services)
    {
        services.TryAddSingleton<IBlobStorageReader>(sp =>
        {
            var context = sp.GetRequiredService<BlobStorageStreamContext>();
            if (AmazonS3StoragePath.IsAmazonS3Path(context.SourcePath))
            {
                services.TryAddAmazonS3Client(StorageType.SOURCE, GetScopedCredentials(StorageType.SOURCE));
                var client = sp.GetRequiredKeyedService<IAmazonS3>(StorageType.SOURCE);
                var logger = sp.GetRequiredService<ILogger<AmazonBlobStorageService>>();
                return new AmazonBlobStorageService(client, logger);
            }

            throw new ConfigurationException("Source path is invalid, only Amazon S3 paths are supported");
        });
        return services;
    }

    public static IServiceCollection AddTargetWriter(this IServiceCollection services)
    {
        services.TryAddKeyedSingleton<IBlobStorageWriter>(StorageType.TARGET, (sp, key) =>
        {
            var context = sp.GetRequiredService<BlobStorageStreamContext>();
            if (AmazonS3StoragePath.IsAmazonS3Path(context.SourcePath))
            {
                services.TryAddAmazonS3Client(StorageType.TARGET, GetScopedCredentials(key));
                var client = sp.GetRequiredKeyedService<IAmazonS3>(StorageType.TARGET);
                var logger = sp.GetRequiredService<ILogger<AmazonBlobStorageService>>();
                return new AmazonBlobStorageService(client, logger);
            }

            throw new ConfigurationException("Source path is invalid, only Amazon S3 paths are supported");
        });
        return services;
    }

    public static IServiceCollection AddSourceWriter(this IServiceCollection services)
    {
        services.TryAddKeyedSingleton<IBlobStorageWriter>(StorageType.SOURCE, (sp, key) =>
        {
            var context = sp.GetRequiredService<BlobStorageStreamContext>();
            if (AmazonS3StoragePath.IsAmazonS3Path(context.SourcePath))
            {
                services.TryAddAmazonS3Client(StorageType.SOURCE, GetScopedCredentials(key));
                var client = sp.GetRequiredKeyedService<IAmazonS3>(StorageType.SOURCE);
                var logger = sp.GetRequiredService<ILogger<AmazonBlobStorageService>>();
                return new AmazonBlobStorageService(client, logger);
            }

            throw new ConfigurationException("Source path is invalid, only Amazon S3 paths are supported");
        });
        return services;
    }

    private static AmazonStorageConfiguration GetScopedCredentials(object scope) => new()
    {
        AccessKey = EnvironmentExtensions.GetDomainEnvironmentVariable($"{scope}.AWS_ACCESS_KEY_ID"),
        SecretKey = EnvironmentExtensions.GetDomainEnvironmentVariable($"{scope}.AWS_SECRET_ACCESS_KEY"),
        ServiceUrl =
            string.IsNullOrEmpty(EnvironmentExtensions.GetDomainEnvironmentVariable($"{scope}.AWS_ENDPOINT_URL"))
                ? null
                : new Uri(EnvironmentExtensions.GetDomainEnvironmentVariable($"{scope}.AWS_ENDPOINT_URL"))
    };

    private static void TryAddAmazonS3Client(this IServiceCollection services, StorageType storageType,
        AmazonStorageConfiguration config)
    {
        if (config == null || IsEmpty(config))
        {
            services.TryAddKeyedSingleton<IAmazonS3, AmazonS3Client>(storageType);
            return;
        }

        var clientConfig = new AmazonS3Config
        {
            UseHttp = config.UseHttp,
            ForcePathStyle = true,
            ServiceURL = config.ServiceUrl.ToString(),
            UseAccelerateEndpoint = false,
        };
        var client = new AmazonS3Client(config.AccessKey, config.SecretKey, clientConfig);
        services.TryAddKeyedSingleton<IAmazonS3>(storageType, client);
    }

    private static bool IsEmpty(AmazonStorageConfiguration config) =>
        string.IsNullOrEmpty(config.AccessKey) || string.IsNullOrEmpty(config.SecretKey) || config.ServiceUrl == null;
}
