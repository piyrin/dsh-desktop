using System;
using System.Net.Http;

internal static class DshReadinessTests
{
    internal static void Register(TestRunner runner)
    {
        runner.Add("DshReadiness connection refusal is not ready", delegate {
            AssertEx.Equal(ReadinessState.NotReady,
                DshReadiness.Classify(null, null, new HttpRequestException("refused")));
        });
        runner.Add("DshReadiness shipped title marker is ready", delegate {
            AssertEx.Equal(ReadinessState.Ready,
                DshReadiness.Classify(200, "<title>DeepSeek Harness</title>", null));
        });
        runner.Add("DshReadiness unrelated html is a port conflict", delegate {
            AssertEx.Equal(ReadinessState.PortConflict,
                DshReadiness.Classify(200, "<title>Other App</title>", null));
        });
        runner.Add("DshReadiness non-success http response is a port conflict", delegate {
            AssertEx.Equal(ReadinessState.PortConflict,
                DshReadiness.Classify(404, "missing", null));
        });
    }
}
