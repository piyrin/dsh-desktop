using System;
using System.Diagnostics;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

internal enum ReadinessState
{
    NotReady,
    Ready,
    PortConflict
}

internal sealed class ReadinessResult
{
    internal ReadinessState State { get; private set; }
    internal TimeSpan Elapsed { get; private set; }
    internal string Detail { get; private set; }

    internal ReadinessResult(ReadinessState state, TimeSpan elapsed, string detail)
    {
        State = state;
        Elapsed = elapsed;
        Detail = detail;
    }
}

internal static class DshReadiness
{
    private const string TimeoutDetail = "DSH did not become ready within 30 seconds.";
    private static readonly HttpClient Client = CreateClient();

    internal static ReadinessState Classify(int? statusCode, string body, Exception error)
    {
        if (error != null || !statusCode.HasValue)
            return ReadinessState.NotReady;

        if (statusCode.Value < 200 || statusCode.Value >= 300)
            return ReadinessState.PortConflict;

        if (!String.IsNullOrEmpty(body) && body.IndexOf("<title>DeepSeek Harness</title>", StringComparison.OrdinalIgnoreCase) >= 0)
            return ReadinessState.Ready;

        return ReadinessState.PortConflict;
    }

    internal static async Task<ReadinessResult> WaitAsync(Uri uri, TimeSpan timeout, CancellationToken token)
    {
        if (uri == null) throw new ArgumentNullException("uri");
        if (timeout < TimeSpan.Zero) throw new ArgumentOutOfRangeException("timeout");

        Stopwatch stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < timeout && !token.IsCancellationRequested)
        {
            int? statusCode = null;
            string body = null;
            Exception error = null;
            try
            {
                using (HttpResponseMessage response = await Client.GetAsync(uri, token).ConfigureAwait(false))
                {
                    statusCode = (int)response.StatusCode;
                    body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                if (token.IsCancellationRequested)
                    break;
                error = new TimeoutException("DSH readiness request timed out.");
            }
            catch (Exception exception)
            {
                error = exception;
            }

            ReadinessState state = Classify(statusCode, body, error);
            if (state != ReadinessState.NotReady)
                return new ReadinessResult(state, stopwatch.Elapsed, state == ReadinessState.Ready ? "DSH HTTP endpoint is ready." : "The DSH port is occupied by another application.");

            TimeSpan remaining = timeout - stopwatch.Elapsed;
            if (remaining <= TimeSpan.Zero)
                break;
            TimeSpan delay = remaining < TimeSpan.FromMilliseconds(75) ? remaining : TimeSpan.FromMilliseconds(75);
            try
            {
                await Task.Delay(delay, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        return new ReadinessResult(ReadinessState.NotReady, stopwatch.Elapsed, TimeoutDetail);
    }

    private static HttpClient CreateClient()
    {
        HttpClient client = new HttpClient();
        client.Timeout = TimeSpan.FromMilliseconds(700);
        return client;
    }
}
