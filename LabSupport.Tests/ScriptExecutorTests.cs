using System;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;
using LabSupport.Agent.Services;
using LabSupport.Shared.Protocol;

namespace LabSupport.Tests;

public class ScriptExecutorTests
{
    private readonly ScriptExecutor _executor = new();
    private readonly ITestOutputHelper _output;

    public ScriptExecutorTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task ExecuteAsync_ShouldCaptureOutputSuccessfully()
    {
        var payload = new ScriptPayload
        {
            Type = ScriptType.Batch,
            ScriptContent = "echo Hello LabSupport Tests",
            TimeoutSeconds = 10
        };

        var result = await _executor.ExecuteAsync(payload);
        _output.WriteLine($"SUCCESS: {result.Success}");
        _output.WriteLine($"OUTPUT: '{result.Output}'");
        _output.WriteLine($"ERROR: '{result.Error}'");
        _output.WriteLine($"TIME: {result.ExecutionTimeMs}ms");

        Assert.True(result.Success, $"Failed with error: {result.Error}, output: {result.Output}");
        Assert.Contains("Hello LabSupport Tests", result.Output);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldHandleTimeoutGracefully()
    {
        var payload = new ScriptPayload
        {
            Type = ScriptType.Batch,
            ScriptContent = "sleep 5",
            TimeoutSeconds = 1
        };

        var result = await _executor.ExecuteAsync(payload);
        _output.WriteLine($"SUCCESS: {result.Success}");
        _output.WriteLine($"OUTPUT: '{result.Output}'");
        _output.WriteLine($"ERROR: '{result.Error}'");

        Assert.False(result.Success);
        Assert.Contains("timed out", result.Error, StringComparison.OrdinalIgnoreCase);
    }
}
