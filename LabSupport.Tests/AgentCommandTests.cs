using System;
using System.Threading.Tasks;
using Xunit;
using LabSupport.Agent.Services;
using LabSupport.Shared.Services;
using LabSupport.Shared.Protocol;

namespace LabSupport.Tests;

public class AgentCommandTests : IDisposable
{
    private readonly int _testPort = 15902;
    private readonly CommandListener _listener;
    private readonly AgentCommandService _client;

    public AgentCommandTests()
    {
        _listener = new CommandListener(_testPort);
        _listener.Start();

        _client = new AgentCommandService(_testPort);
    }

    [Fact]
    public async Task PingCommand_ShouldReturnPong()
    {
        var cmd = new CommandMessage
        {
            Type = CommandType.Ping
        };

        var response = await _client.SendCommandAsync("127.0.0.1", cmd, _testPort, timeoutMs: 3000);

        Assert.True(response.Success);
        Assert.Equal("Pong", response.Output);
    }

    [Fact]
    public async Task GetProcessListCommand_ShouldReturnProcessItems()
    {
        var cmd = new CommandMessage
        {
            Type = CommandType.GetProcessList
        };

        var response = await _client.SendCommandAsync("127.0.0.1", cmd, _testPort, timeoutMs: 3000);

        Assert.True(response.Success);
        Assert.NotNull(response.Output);

        var list = MessageSerializer.Deserialize<System.Collections.Generic.List<ProcessInfoItem>>(response.Output);
        Assert.NotNull(list);
        Assert.NotEmpty(list);
    }

    [Fact]
    public async Task UnknownCommandOrInvalidTarget_ShouldHandleGracefully()
    {
        var cmd = new CommandMessage
        {
            Type = CommandType.Ping
        };

        // Non-existent port
        var response = await _client.SendCommandAsync("127.0.0.1", cmd, 19999, timeoutMs: 1000);

        Assert.False(response.Success);
        Assert.NotNull(response.Error);
    }

    public void Dispose()
    {
        _listener.Dispose();
    }
}
