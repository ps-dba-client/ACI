// -----------------------------------------------------------------------------
// Splunk + OpenTelemetry correlation (ILogger and traces)
// -----------------------------------------------------------------------------
// This sample follows Splunk's OpenTelemetry guidance for .NET: instrument the app
// with the OpenTelemetry SDK, export OTLP to the Splunk OpenTelemetry Collector
// running as a sidecar in the same Container App (shared network namespace), and
// let the collector forward signals to Splunk Platform (HEC).
//
// Trace ↔ log correlation (what your teams should know):
// - ASP.NET Core creates a root "server" span (Activity) per HTTP request.
// - When you call ILogger inside that request (or inside a child Activity you create),
//   the OpenTelemetry logging integration attaches OpenTelemetry.BlobTraceId,
//   OpenTelemetry.BlobSpanId, and related fields to the LogRecord (OTLP logs).
// - In Splunk Search, use the trace_id / trace ID field from log events to join with
//   trace data ingested for the same request (same trace_id).
//
// References (client documentation):
// - Splunk:  https://docs.splunk.com/observability/en/gdi/get-data-in/application/otel-dotnet/instrumentation/instrument-dotnet-application.html
// - OTel .NET logging / correlation:
//   https://opentelemetry.io/docs/languages/dotnet/logs/correlation/
//
// Environment variables used in this lab (set in Azure Container Apps):
// - OTEL_SERVICE_NAME           → Splunk "service.name" resource attribute
// - OTEL_RESOURCE_ATTRIBUTES    → e.g. deployment.environment=lab
// - OTEL_EXPORTER_OTLP_ENDPOINT → points at the sidecar collector (127.0.0.1:4317)
// -----------------------------------------------------------------------------

using System.Diagnostics;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

internal static class Program
{
    /// <summary>Logger category type (avoids ILogger&lt;Program&gt; when Program is static).</summary>
    private sealed class WorkLog
    {
    }

    /// <summary>Name passed to <see cref="ActivitySource"/> — must match AddSource(...) below.</summary>
    private const string LabActivitySourceName = "AcaOtelLab.Lab";

    private static readonly ActivitySource LabActivity = new(LabActivitySourceName);

    private static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        var serviceName = builder.Configuration["Otel:ServiceName"]
            ?? Environment.GetEnvironmentVariable("OTEL_SERVICE_NAME")
            ?? "aca-dotnet-otel-lab";

        var deploymentEnvironment = builder.Configuration["Otel:DeploymentEnvironment"]
            ?? ParseDeploymentEnvironmentFromOtelResourceAttributes()
            ?? "lab";

        var serviceVersion = typeof(Program).Assembly.GetName().Version?.ToString() ?? "1.0.0";

        var resourceBuilder = ResourceBuilder.CreateDefault()
            .AddService(serviceName: serviceName, serviceVersion: serviceVersion)
            .AddAttributes(new KeyValuePair<string, object>[]
            {
                new("deployment.environment", deploymentEnvironment),
            });

        // Logging: OpenTelemetry exporter enriches log records with trace context when Activity.Current is set.
        builder.Logging.AddOpenTelemetry(options =>
        {
            options.SetResourceBuilder(resourceBuilder);
            options.IncludeFormattedMessage = true;
            options.IncludeScopes = true;
            options.AddOtlpExporter();
        });

        builder.Services.AddOpenTelemetry()
            .ConfigureResource(r => r
                .AddService(serviceName: serviceName, serviceVersion: serviceVersion)
                .AddAttributes(new KeyValuePair<string, object>[]
                {
                    new("deployment.environment", deploymentEnvironment),
                }))
            .WithTracing(tracing => tracing
                .AddSource(LabActivitySourceName)
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddOtlpExporter())
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddRuntimeInstrumentation()
                .AddOtlpExporter());

        var app = builder.Build();

        app.MapGet("/", () => Results.Ok(new
        {
            service = serviceName,
            deployment_environment = deploymentEnvironment,
            endpoints = new[] { "/healthz", "/work" },
        }));

        app.MapGet("/healthz", () => Results.Ok(new { status = "healthy" }));

        // Produces a trace with multiple spans: ASP.NET inbound + lab.workflow + two children.
        app.MapGet("/work", async Task<IResult> (ILogger<WorkLog> logger, CancellationToken ct) =>
        {
            logger.LogInformation(
                "Lab /work: start. Splunk tip — filter logs and traces on trace_id once you have one ID.");

            using var workflow = LabActivity.StartActivity("lab.workflow");
            workflow?.SetTag("lab.scenario", "multi-span-demo");

            logger.LogInformation("Lab /work: inside lab.workflow span — log record should carry the same trace_id.");

            await Task.Delay(Random.Shared.Next(8, 40), ct);

            using (var fetch = LabActivity.StartActivity("lab.fetch-details"))
            {
                fetch?.SetTag("lab.child", "1");
                logger.LogInformation("Lab /work: inner span lab.fetch-details");
                await Task.Delay(Random.Shared.Next(8, 40), ct);
            }

            using (var persist = LabActivity.StartActivity("lab.persist-result"))
            {
                persist?.SetTag("lab.child", "2");
                logger.LogInformation("Lab /work: inner span lab.persist-result");
                await Task.Delay(Random.Shared.Next(8, 40), ct);
            }

            var traceId = Activity.Current?.TraceId.ToString() ?? workflow?.TraceId.ToString();
            logger.LogInformation("Lab /work: complete. trace_id={TraceId}", traceId);

            return Results.Ok(new
            {
                status = "ok",
                trace_id = traceId,
                hint = "Search Splunk logs for this trace_id and open the matching trace.",
            });
        });

        app.Run();
    }

    private static string? ParseDeploymentEnvironmentFromOtelResourceAttributes()
    {
        var raw = Environment.GetEnvironmentVariable("OTEL_RESOURCE_ATTRIBUTES");
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        foreach (var part in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var kv = part.Split('=', 2, StringSplitOptions.TrimEntries);
            if (kv.Length == 2 && kv[0].Equals("deployment.environment", StringComparison.OrdinalIgnoreCase))
            {
                return kv[1];
            }
        }

        return null;
    }
}
