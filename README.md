# Jotiver C#/.NET AI Ticket Triage — runnable companion

Companion source for the 2026 Jotiver how-to:
`/blog/how-to-build-ai-ticket-triage-api-csharp-dotnet-openai-2026/`.

- `Triage.Core`: direct HTTP client for OpenAI Responses API structured JSON; finite schema; output validation and fail-closed processing.
- `Triage.Api`: local-development-only ASP.NET Core 10 Minimal API with a demo key, per-IP rate limit, bounded input and no auto-updates to any ticket system.
- `Triage.SmokeTests`: offline deterministic tests using mocked HTTP messages. No API key, network access, paid API calls or NuGet test package is required.

## Local run

Install .NET 10 SDK, then run:

```bash
dotnet run --project Triage.SmokeTests
dotnet build Triage.Api
```

To call the *real* OpenAI service, set `OPENAI_API_KEY`, `OPENAI_MODEL` (a model supporting Responses and Structured Outputs) and `TRIAGE_DEMO_KEY` as local environment variables or .NET user-secrets. Keep these on the **server**, never in HTML, browser JS, a repository, public logs or screenshots. Launch the API in Development with a local-only listener:

```bash
ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://127.0.0.1:5199 dotnet run --no-launch-profile --project Triage.Api
```

From a second terminal, replacing the placeholder with your own demo key:

```bash
curl -X POST http://127.0.0.1:5199/api/triage \
  -H "Content-Type: application/json" \
  -H "X-Demo-Key: REPLACE_WITH_LOCAL_DEMO_KEY" \
  -d '{"text":"My monthly invoice shows two charges, and I need help verifying the payment."}'
```

**Safety boundaries:** This is educational code, not an internet-ready support platform. The simple header key is not production authentication. Per-IP rate limits do not provide per-account cost limits. All tickets are untrusted; strip or pseudonymize PII before third-party processing, establish retention and contractual controls, and obtain consent or another valid legal basis if necessary. Avoid giving the LLM access to ticket writes, billing, emails, or account privileges. Add proper identity, per-tenant quotas, persistence, abuse monitoring and human approval before a real deployment. A schema restricts output shape, not factual correctness. `store:false` does not mean zero retention; check provider data controls.

**Evidence boundary:** CI builds and runs the offline deterministic smoke-test project. This is evidence for the C# logic under mocked provider responses, **not** evidence that a paid OpenAI API request was made or that model accuracy was benchmarked. Verify live integration separately with authorized synthetic tickets.
