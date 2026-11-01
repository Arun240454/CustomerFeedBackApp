using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

// Basic load test: customers POST feedback while managers GET the store summary.
//   dotnet run --project tests/CustomerFeedback.LoadTest -c Release -- [options]
// Options (all optional): --identity URL --feedback URL --analytics URL --seconds N --customers N --managers N
//   --customer-email E --manager-email E --password P --max-error-rate 0.01 --p95-ms 1000
// Passwords can also be supplied via the LOADTEST_PASSWORD environment variable.

var options = Options.Parse(args);
using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

var customerToken = await LoginAsync(options.IdentityUrl, options.CustomerEmail, options.Password);
var managerToken = await LoginAsync(options.IdentityUrl, options.ManagerEmail, options.Password);
Console.WriteLine($"Running {options.Seconds}s: {options.Customers} submitting customers, {options.Managers} summary-reading managers");

using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(options.Seconds));
var submit = new Scenario("POST /api/feedback");
var summary = new Scenario("GET  /api/analytics/stores/1/summary");

var rng = new Random(42);
var workers = Enumerable.Range(0, options.Customers).Select(_ => Task.Run(() => Loop(submit, cts.Token, async () =>
{
    var rating = (byte)(1 + rng.Next(5));
    var body = new
    {
        storeId = 1, overallRating = rating, isAnonymous = false, comments = "Load test: the queue was long but staff were helpful",
        items = new[] { new { categoryId = 2, rating } },
    };
    using var request = new HttpRequestMessage(HttpMethod.Post, $"{options.FeedbackUrl}/api/feedback") { Content = JsonContent.Create(body) };
    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", customerToken);
    return await http.SendAsync(request, cts.Token);
}))).Concat(Enumerable.Range(0, options.Managers).Select(_ => Task.Run(() => Loop(summary, cts.Token, async () =>
{
    using var request = new HttpRequestMessage(HttpMethod.Get, $"{options.AnalyticsUrl}/api/analytics/stores/1/summary?days=30");
    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", managerToken);
    return await http.SendAsync(request, cts.Token);
}))));

await Task.WhenAll(workers);

var failed = false;
foreach (var scenario in new[] { submit, summary })
{
    failed |= scenario.Report(options);
}

return failed ? 1 : 0;

async Task<string> LoginAsync(string identityUrl, string email, string password)
{
    var response = await http.PostAsJsonAsync($"{identityUrl}/api/auth/login", new { email, password });
    if (!response.IsSuccessStatusCode)
    {
        throw new InvalidOperationException($"Login failed for {email}: {(int)response.StatusCode}. Is the identity service running with seeded demo data?");
    }

    using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    return doc.RootElement.GetProperty("accessToken").GetString()!;
}

static async Task Loop(Scenario scenario, CancellationToken ct, Func<Task<HttpResponseMessage>> call)
{
    while (!ct.IsCancellationRequested)
    {
        var started = Stopwatch.GetTimestamp();
        try
        {
            using var response = await call();
            scenario.Record(Stopwatch.GetElapsedTime(started), response.IsSuccessStatusCode);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            break;
        }
        catch (HttpRequestException)
        {
            scenario.Record(Stopwatch.GetElapsedTime(started), false);
        }
    }
}

internal sealed class Scenario(string name)
{
    private readonly List<double> _latenciesMs = [];
    private int _errors;
    private readonly Lock _gate = new();

    public void Record(TimeSpan elapsed, bool ok)
    {
        lock (_gate)
        {
            _latenciesMs.Add(elapsed.TotalMilliseconds);
            if (!ok)
            {
                _errors++;
            }
        }
    }

    /// <returns>true when a threshold was breached.</returns>
    public bool Report(Options options)
    {
        var sorted = _latenciesMs.Order().ToArray();
        if (sorted.Length == 0)
        {
            Console.WriteLine($"{name}: no requests completed");
            return true;
        }

        double Percentile(double p) => sorted[Math.Min(sorted.Length - 1, (int)Math.Ceiling(p * sorted.Length) - 1)];
        var errorRate = (double)_errors / sorted.Length;
        var p95 = Percentile(0.95);
        Console.WriteLine($"{name}\n  requests={sorted.Length} rps={sorted.Length / (double)options.Seconds:F1} errors={_errors} ({errorRate:P2})" +
                          $"\n  avg={sorted.Average():F1}ms p50={Percentile(0.5):F1}ms p95={p95:F1}ms p99={Percentile(0.99):F1}ms max={sorted[^1]:F1}ms");

        var breached = errorRate > options.MaxErrorRate || p95 > options.P95Ms;
        if (breached)
        {
            Console.WriteLine($"  THRESHOLD BREACHED (max error rate {options.MaxErrorRate:P1}, p95 {options.P95Ms}ms)");
        }

        return breached;
    }
}

internal sealed record Options(
    string IdentityUrl, string FeedbackUrl, string AnalyticsUrl, int Seconds, int Customers, int Managers,
    string CustomerEmail, string ManagerEmail, string Password, double MaxErrorRate, int P95Ms)
{
    public static Options Parse(string[] args)
    {
        string Get(string key, string fallback)
        {
            var index = Array.IndexOf(args, key);
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : fallback;
        }

        return new Options(
            Get("--identity", "http://localhost:5101"),
            Get("--feedback", "http://localhost:5102"),
            Get("--analytics", "http://localhost:5103"),
            int.Parse(Get("--seconds", "20"), System.Globalization.CultureInfo.InvariantCulture),
            int.Parse(Get("--customers", "10"), System.Globalization.CultureInfo.InvariantCulture),
            int.Parse(Get("--managers", "5"), System.Globalization.CultureInfo.InvariantCulture),
            Get("--customer-email", "asha@example.com"),
            Get("--manager-email", "manager.delhi@example.com"),
            Get("--password", Environment.GetEnvironmentVariable("LOADTEST_PASSWORD") ?? "Passw0rd!Demo1"),
            double.Parse(Get("--max-error-rate", "0.01"), System.Globalization.CultureInfo.InvariantCulture),
            int.Parse(Get("--p95-ms", "1000"), System.Globalization.CultureInfo.InvariantCulture));
    }
}
