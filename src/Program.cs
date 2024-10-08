﻿using System;
using System.Collections.Generic;
using System.Text.Json;
using Arcane.Framework.Contracts;
using Arcane.Framework.Providers.Hosting;
using Arcane.Stream.BlobStorage.Exceptions;
using Arcane.Stream.BlobStorage.Extensions;
using Arcane.Stream.BlobStorage.GraphBuilder;
using Arcane.Stream.BlobStorage.Models;
using Microsoft.Extensions.Hosting;
using Serilog;
using SnD.Sdk.Extensions.Environment.Hosting;
using Snd.Sdk.Logs.Providers;
using Snd.Sdk.Metrics.Configurations;
using Snd.Sdk.Metrics.Providers;

Log.Logger = DefaultLoggingProvider.CreateBootstrapLogger(nameof(Arcane));

int exitCode;
try
{
    exitCode = await Host.CreateDefaultBuilder(args)
        .AddDatadogLogging((_, _, configuration) =>
        {
            var loggingProperties = EnvironmentExtensions.GetDomainEnvironmentVariable("LOGGING_PROPERTIES");
            if (!string.IsNullOrEmpty(loggingProperties))
            {
                var properties = JsonSerializer.Deserialize<Dictionary<string, string>>(loggingProperties);
                foreach (var (key, value) in properties)
                {
                    configuration.Enrich.WithProperty(key, value);
                }
            }
            configuration.WriteTo.Console();
        })
        .ConfigureRequiredServices(services
            => services.AddStreamGraphBuilder<BlobStorageGraphBuilder, BlobStorageStreamContext>())
        .ConfigureAdditionalServices((services, context) =>
        {
            services.TryAddAmazonS3Client(StorageType.SOURCE, StorageType.SOURCE.GetScopedCredentials());
            services.TryAddAmazonS3Client(StorageType.TARGET, StorageType.TARGET.GetScopedCredentials());
            services.AddDatadogMetrics(configuration: DatadogConfiguration.UnixDomainSocket(context.ApplicationName));
            services.AddSourceListService();
            services.AddSourceReader();
            services.AddTargetWriter();
            services.AddSourceWriter();
        })
        .Build()
        .RunStream<BlobStorageStreamContext>(Log.Logger);
}
catch (ConfigurationException ex)
{
    Log.Fatal(ex, "Invalid configuration provided, exiting");
    return StreamExitCodes.NO_RETRY;
}
catch (Exception ex)
{
    Log.Fatal(ex, "Host terminated unexpectedly");
    return ExitCodes.FATAL;
}
finally
{
    await Log.CloseAndFlushAsync();
}

return exitCode;
