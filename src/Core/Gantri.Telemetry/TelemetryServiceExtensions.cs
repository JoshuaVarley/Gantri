using Gantri.Abstractions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Gantri.Telemetry;

public static class TelemetryServiceExtensions
{
    /// <summary>
    /// Registers the OpenTelemetry SDK with Gantri activity sources, meters,
    /// and log provider. OTLP endpoint/protocol can be set via YAML config
    /// or standard env vars (<c>OTEL_EXPORTER_OTLP_ENDPOINT</c>,
    /// <c>OTEL_EXPORTER_OTLP_PROTOCOL</c>).
    /// </summary>
    public static IServiceCollection AddGantriTelemetry(
        this IServiceCollection services, TelemetryOptions? options = null)
    {
        options ??= new TelemetryOptions();

        // Register TelemetryOptions so IOptions<TelemetryOptions> is available for DI consumers
        // (e.g. GantriAgentFactory reads EnableSensitiveData for .UseOpenTelemetry() config)
        services.AddSingleton<IOptions<TelemetryOptions>>(Options.Create(options));

        if (!options.Enabled)
            return services;

        services.AddOpenTelemetry()
            .ConfigureResource(resource =>
            {
                resource.AddService(
                    serviceName: options.ServiceName,
                    serviceVersion: options.ServiceVersion);

                if (options.ResourceAttributes.Count > 0)
                {
                    resource.AddAttributes(
                        options.ResourceAttributes.Select(kv =>
                            new KeyValuePair<string, object>(kv.Key, kv.Value)));
                }
            })
            .WithTracing(tracing =>
            {
                foreach (var sourceName in GantriActivitySources.AllSourceNames)
                    tracing.AddSource(sourceName);

                // Microsoft.Extensions.AI chat client telemetry (chat spans, token usage)
                // Wildcard prefix per Microsoft Agent Framework observability docs —
                // actual source names may be prefixed (e.g. Experimental.Microsoft.Extensions.AI)
                tracing.AddSource("*Microsoft.Extensions.AI");
                // Microsoft Agent Framework telemetry (invoke_agent, execute_tool spans)
                tracing.AddSource("*Microsoft.Extensions.Agents*");

                ConfigureTraceExporter(tracing, options.Traces);
            })
            .WithMetrics(metrics =>
            {
                metrics.AddMeter(GantriMeters.MeterName);

                // Microsoft Agent Framework metrics (operation duration, token usage histograms)
                metrics.AddMeter("*Microsoft.Agents.AI");
                // Agent Framework function invocation metrics
                metrics.AddMeter("*agent_framework*");

                ConfigureMetricExporter(metrics, options.Metrics);
            })
            .WithLogging(logging =>
            {
                ConfigureLogExporter(logging, options.Logs);
            }, loggerOptions =>
            {
                loggerOptions.IncludeFormattedMessage = true;
                loggerOptions.IncludeScopes = true;
                loggerOptions.ParseStateValues = true;
            });

        return services;
    }

    /// <summary>
    /// Forces the OTel TracerProvider and MeterProvider to initialize.
    /// In a host-based app (WebApplicationBuilder), the OTel IHostedService
    /// does this automatically. For raw ServiceCollection apps (like the CLI),
    /// call this after building the ServiceProvider so the providers register
    /// as listeners and start collecting traces/metrics.
    /// </summary>
    public static void EnsureProvidersInitialized(IServiceProvider serviceProvider)
    {
        serviceProvider.GetService<TracerProvider>();
        serviceProvider.GetService<MeterProvider>();
    }

    /// <summary>
    /// Returns true when the telemetry options route logs through OTLP
    /// (i.e. the console log provider is redundant).
    /// </summary>
    public static bool UsesOtlpLogExporter(TelemetryOptions? options)
    {
        return options is { Enabled: true }
            && options.Logs.Exporter.Equals("otlp", StringComparison.OrdinalIgnoreCase);
    }

    private static void ConfigureTraceExporter(TracerProviderBuilder builder, TraceOptions options)
    {
        switch (options.Exporter.ToLowerInvariant())
        {
            case "otlp":
                builder.AddOtlpExporter(otlp => ApplyOtlpOptions(otlp, options.Endpoint));
                break;
            case "console":
                builder.AddConsoleExporter();
                break;
        }
    }

    private static void ConfigureMetricExporter(MeterProviderBuilder builder, MetricExportOptions options)
    {
        switch (options.Exporter.ToLowerInvariant())
        {
            case "otlp":
                builder.AddOtlpExporter(otlp => ApplyOtlpOptions(otlp, options.Endpoint));
                break;
            case "console":
                builder.AddConsoleExporter();
                break;
        }
    }

    private static void ConfigureLogExporter(LoggerProviderBuilder builder, LogExportOptions options)
    {
        switch (options.Exporter.ToLowerInvariant())
        {
            case "otlp":
                builder.AddOtlpExporter(otlp => ApplyOtlpOptions(otlp, options.Endpoint));
                break;
            case "console":
                builder.AddConsoleExporter();
                break;
        }
    }

    /// <summary>
    /// Applies OTLP endpoint and protocol from YAML config, falling back to
    /// standard OTel env vars. This is necessary because the raw ServiceCollection
    /// used by the CLI does not bind IConfiguration from env vars, so the SDK's
    /// built-in env var reading may not work outside a host builder context.
    /// </summary>
    private static void ApplyOtlpOptions(OtlpExporterOptions otlp, string? configEndpoint)
    {
        // Protocol: config → env var → SDK default
        var envProtocol = Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_PROTOCOL");
        if (envProtocol is not null)
        {
            otlp.Protocol = envProtocol.ToLowerInvariant() switch
            {
                "grpc" => OtlpExportProtocol.Grpc,
                "http/protobuf" => OtlpExportProtocol.HttpProtobuf,
                _ => otlp.Protocol
            };
        }

        // Endpoint: config → env var → SDK default
        if (configEndpoint is not null)
        {
            otlp.Endpoint = new Uri(configEndpoint);
        }
        else
        {
            var envEndpoint = Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT");
            if (envEndpoint is not null)
                otlp.Endpoint = new Uri(envEndpoint);
        }
    }
}
