// -----------------------------------------------------------------------------
// Splunk + OpenTelemetry correlation (ILogger and traces)
// -----------------------------------------------------------------------------
// Instrumentation is automatic — no manual ActivitySource spans:
// - AddAspNetCoreInstrumentation(): one "server" span per incoming HTTP request.
// - AddHttpClientInstrumentation(): child spans for each outgoing HttpClient call.
//
// The /work endpoint triggers two same-process HTTP GETs to this app (loopback).
// In Splunk Observability you should see one trace: inbound GET /work plus two
// HTTP GET client spans (e.g. /healthz and /), all produced by the agents.
//
// Trace ↔ log correlation:
// - While handling a request, Activity.Current is the ASP.NET server span (and
//   nested client spans during awaited HttpClient calls). ILogger emits
//   OpenTelemetry log records with trace_id / span_id when the integration is on.
// - In Splunk Cloud (HEC logs), use the same trace_id to align with Observability traces.
//
// References:
// - https://docs.splunk.com/observability/en/gdi/get-data-in/application/otel-dotnet/instrumentation/instrument-dotnet-application.html
// - https://opentelemetry.io/docs/languages/dotnet/logs/correlation/
//
// Container env (Azure / local): OTEL_SERVICE_NAME, OTEL_RESOURCE_ATTRIBUTES,
// OTEL_EXPORTER_OTLP_ENDPOINT (sidecar collector on 127.0.0.1:4317).
// -----------------------------------------------------------------------------

using System.Diagnostics;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

internal static class Program
{
    private sealed class WorkLog
    {
    }

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

        // ILogger → OTLP logs, with trace/span IDs when there is an active Activity.
        builder.Logging.AddOpenTelemetry(options =>
        {
            options.SetResourceBuilder(resourceBuilder);
            options.IncludeFormattedMessage = true;
            options.IncludeScopes = true;
            options.AddOtlpExporter();
        });

        // Automatic tracing only (no custom ActivitySource).
        builder.Services.AddHttpClient();

        builder.Services.AddOpenTelemetry()
            .ConfigureResource(r => r
                .AddService(serviceName: serviceName, serviceVersion: serviceVersion)
                .AddAttributes(new KeyValuePair<string, object>[]
                {
                    new("deployment.environment", deploymentEnvironment),
                }))
            .WithTracing(tracing => tracing
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddOtlpExporter())
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddRuntimeInstrumentation()
                .AddOtlpExporter());

        var app = builder.Build();

        var loopbackBase = LoopbackBaseUri();

        app.MapGet("/", () => Results.Ok(new
        {
            service = serviceName,
            deployment_environment = deploymentEnvironment,
            endpoints = new[] { "/healthz", "/work" },
        }));

        app.MapGet("/healthz", () => Results.Ok(new { status = "healthy" }));

        // Two automatic HttpClient spans (children of the incoming /work span).
        app.MapGet("/work", async Task<IResult> (
            IHttpClientFactory httpFactory,
            ILogger<WorkLog> logger,
            CancellationToken ct) =>
        {
            logger.LogInformation(
                "work: start — trace_id on this log comes from automatic ASP.NET Core instrumentation");

            var client = httpFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(30);

            using var r1 = await client.GetAsync(new Uri(loopbackBase, "/healthz"), ct);
            logger.LogInformation(
                "work: after automatic HTTP client call to /healthz, status={Status}",
                (int)r1.StatusCode);

            using var r2 = await client.GetAsync(new Uri(loopbackBase, "/"), ct);
            logger.LogInformation(
                "work: after automatic HTTP client call to /, status={Status}",
                (int)r2.StatusCode);

            var traceId = Activity.Current?.TraceId.ToString();
            logger.LogInformation("work: done. trace_id={TraceId}", traceId);

            return Results.Ok(new
            {
                status = "ok",
                trace_id = traceId,
                hint = "Observability: one inbound span + two HTTP client spans; logs share trace_id.",
            });
        });

        app.Run();
    }

    /// <summary>Use loopback to this process (Dockerfile sets ASPNETCORE_URLS=http://+:8080).</summary>
    private static Uri LoopbackBaseUri()
    {
        var raw = Environment.GetEnvironmentVariable("ASPNETCORE_URLS") ?? "http://127.0.0.1:8080";
        var first = raw.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault()
            ?? "http://127.0.0.1:8080";
        first = first.Replace("+", "127.0.0.1", StringComparison.Ordinal);
        if (!Uri.TryCreate(first, UriKind.Absolute, out var uri))
        {
            uri = new Uri("http://127.0.0.1:8080");
        }

        return new Uri($"{uri.Scheme}://{uri.Host}:{uri.Port}");
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
