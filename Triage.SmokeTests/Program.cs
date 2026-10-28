using System.Net;
using System.Text;
using System.Text.Json;
using Triage.Core;

int passed = 0;
async Task Test(string name, Func<Task> run)
{
    await run();
    Console.WriteLine("PASS " + name);
    passed++;
}
static HttpClient Client(Func<HttpRequestMessage, HttpResponseMessage> handler) =>
    new(new FakeHandler(handler)) { BaseAddress = new Uri("https://api.openai.com/") };

static HttpResponseMessage Reply(string status, string text, bool refusal = false)
{
    var part = refusal ? new { type = "refusal", refusal = "Unable to comply" } :
                          (object)new { type = "output_text", text };
    var json = JsonSerializer.Serialize(new { status, output = new[] {
        new { type = "message", content = new object[] { part } } } });
    return new HttpResponseMessage(HttpStatusCode.OK)
    { Content = new StringContent(json, Encoding.UTF8, "application/json") };
}
const string Ticket = "Invoice shows a duplicate charge for my monthly subscription.";
const string Normal = """{"category":"billing","priority":"normal","needsHumanReview":false,"reason":"Invoice question"}""";
await Test("required fields, strict schema and privacy are sent", async () =>
{
    using var client = Client(request =>
    {
        if (request.RequestUri?.AbsolutePath != "/v1/responses") throw new Exception("Wrong endpoint");
        if (request.Headers.Authorization?.Scheme != "Bearer") throw new Exception("Missing Authorization");
        var body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
        using var json = JsonDocument.Parse(body);
        var root = json.RootElement;
        if (root.GetProperty("store").GetBoolean()) throw new Exception("Stored response!");
        var fmt = root.GetProperty("text").GetProperty("format");
        if (fmt.GetProperty("type").GetString() != "json_schema" ||
            !fmt.GetProperty("strict").GetBoolean()) throw new Exception("Not strict schema");
        var schema = fmt.GetProperty("schema");
        if (!schema.GetProperty("required").EnumerateArray().Any(x => x.GetString() == "needsHumanReview") ||
            schema.GetProperty("additionalProperties").GetBoolean()) throw new Exception("Schema wrong");
        return Reply("completed", Normal);
    });
    var result = await TriageEngine.ClassifyAsync(client, "example-key", "example-model", Ticket);
    if (result.Category != "billing" || result.NeedsHumanReview) throw new Exception("Wrong result");
});
await Test("high priority is always reviewed", async () =>
{
    using var client = Client(_ => Reply("completed",
        """{"category":"technical","priority":"high","needsHumanReview":false,"reason":"Service outage"}"""));
    var result = await TriageEngine.ClassifyAsync(client,"key","model",Ticket);
    if (!result.NeedsHumanReview) throw new Exception("High priority escaped review");
});
await Test("ambiguous category is always reviewed", async () =>
{
    using var client = Client(_ => Reply("completed",
        """{"category":"other","priority":"low","needsHumanReview":false,"reason":"No category"}"""));
    var result = await TriageEngine.ClassifyAsync(client,"key","model",Ticket);
    if (!result.NeedsHumanReview) throw new Exception("Ambiguous escaped review");
});
await Test("empty, short and oversized inputs blocked", async () =>
{
    if (TriageEngine.ValidInput("short") || TriageEngine.ValidInput(new string('a',4001)) ||
        TriageEngine.ValidInput(null) || !TriageEngine.ValidInput(Ticket)) throw new Exception("Input bounds wrong");
    await Task.CompletedTask;
});
await Test("model refusal fails closed", async () =>
{
    using var client = Client(_ => Reply("completed","",true));
    try { await TriageEngine.ClassifyAsync(client,"key","model",Ticket); throw new Exception("Refusal accepted"); }
    catch (TriageException) { }
});
await Test("incomplete model response fails closed", async () =>
{
    using var client = Client(_ => Reply("incomplete",Normal));
    try { await TriageEngine.ClassifyAsync(client,"key","model",Ticket); throw new Exception("Incomplete accepted"); }
    catch (TriageException) { }
});
await Test("rate limiting is surfaced as unavailable", async () =>
{
    using var client = Client(_ => new HttpResponseMessage(HttpStatusCode.TooManyRequests));
    try { await TriageEngine.ClassifyAsync(client,"key","model",Ticket); throw new Exception("429 accepted"); }
    catch (TriageException) { }
});
await Test("invalid enums never reach the caller", async () =>
{
    using var client = Client(_ => Reply("completed",
        """{"category":"admin","priority":"normal","needsHumanReview":false,"reason":"Wrong enum"}"""));
    try { await TriageEngine.ClassifyAsync(client,"key","model",Ticket); throw new Exception("Invalid enum accepted"); }
    catch (TriageException) { }
});
await Test("refusal or output_text is not assumed to be first item", async () =>
{
    using var client = Client(_ =>
    {
        var body = """{"status":"completed","output":[{"type":"reasoning"},{"type":"message","content":[{"type":"output_text","text":"{\"category\":\"billing\",\"priority\":\"normal\",\"needsHumanReview\":false,\"reason\":\"Invoice question\"}"}]}]}""";
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body,Encoding.UTF8,"application/json") };
    });
    var result = await TriageEngine.ClassifyAsync(client,"key","model",Ticket);
    if (result.Category != "billing") throw new Exception("Failed to find output item");
});
Console.WriteLine($"PASS {passed} offline deterministic smoke tests.");

sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        => Task.FromResult(responder(request));
}
