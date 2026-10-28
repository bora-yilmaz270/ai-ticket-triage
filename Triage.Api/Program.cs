using System.Threading.RateLimiting;
using Triage.Core;

// This sample is intentionally local-only. Protect a public endpoint with real
// identity, per-tenant quotas, cost limits and abuse controls before deployment.
var builder = WebApplication.CreateBuilder(args);
if (!builder.Environment.IsDevelopment())
    throw new InvalidOperationException("This demonstration only runs in Development.");

builder.Services.AddHttpClient("openai", http =>
{
    http.BaseAddress = new Uri("https://api.openai.com/");
    http.Timeout = TimeSpan.FromSeconds(25);
});
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("triage", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 8,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
});
var app = builder.Build();
app.UseRateLimiter();

app.MapPost("/api/triage", async (
    TriageInput input, HttpContext context, IHttpClientFactory factory,
    IConfiguration configuration, CancellationToken cancellationToken) =>
{
    // Placeholder local demo key. Never use this check as production identity.
    var expected = configuration["TRIAGE_DEMO_KEY"];
    if (string.IsNullOrWhiteSpace(expected) ||
        context.Request.Headers["X-Demo-Key"].ToString() != expected)
        return Results.Unauthorized();

    if (!TriageEngine.ValidInput(input.Text))
        return Results.BadRequest(new { error = "Ticket must contain 20-4000 characters." });

    var apiKey = configuration["OPENAI_API_KEY"];
    var model = configuration["OPENAI_MODEL"];
    if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(model))
        return Results.Problem("Missing server-side AI configuration.", statusCode: 503);

    try
    {
        using var http = factory.CreateClient("openai");
        var decision = await TriageEngine.ClassifyAsync(
            http, apiKey, model, input.Text!, cancellationToken);
        return Results.Ok(decision);
    }
    catch (TriageException)
    {
        return Results.Problem("AI triage unavailable. Send ticket to human review.", statusCode: 503);
    }
    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
    {
        return Results.Problem("Upstream AI request timed out.", statusCode: 504);
    }
    // Caller cancellations should propagate rather than being converted to success.
}).RequireRateLimiting("triage");

app.Run();
public sealed record TriageInput(string? Text);
