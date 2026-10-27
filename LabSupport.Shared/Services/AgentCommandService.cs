using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LabSupport.Shared.Protocol;

namespace LabSupport.Shared.Services;

public class AgentCommandService
{
    private readonly int _defaultPort;

    public AgentCommandService(int defaultPort = 5902)
    {
        _defaultPort = defaultPort;
    }

    public async Task<CommandResponse> SendCommandAsync(
        string agentIp,
        CommandMessage command,
        int port = 0,
        int timeoutMs = 15000,
        CancellationToken cancellationToken = default)
    {
        int targetPort = port > 0 ? port : _defaultPort;
        using var client = new TcpClient();

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeoutMs);

        try
        {
            await client.ConnectAsync(agentIp, targetPort, cts.Token);
            using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.UTF8);
            using var writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };

            string json = MessageSerializer.Serialize(command);
            await writer.WriteLineAsync(json);

            string? responseLine = await reader.ReadLineAsync(cts.Token);
            if (string.IsNullOrWhiteSpace(responseLine))
            {
                return new CommandResponse
                {
                    CommandId = command.Id,
                    Success = false,
                    Error = "Empty response received from agent."
                };
            }

            var response = MessageSerializer.Deserialize<CommandResponse>(responseLine);
            return response ?? new CommandResponse
            {
                CommandId = command.Id,
                Success = false,
                Error = "Failed to deserialize agent response."
            };
        }
        catch (OperationCanceledException)
        {
            return new CommandResponse
            {
                CommandId = command.Id,
                Success = false,
                Error = $"Command timed out after {timeoutMs}ms."
            };
        }
        catch (Exception ex)
        {
            return new CommandResponse
            {
                CommandId = command.Id,
                Success = false,
                Error = $"Connection error: {ex.Message}"
            };
        }
    }

    public async Task<Dictionary<string, CommandResponse>> SendBroadcastCommandAsync(
        IEnumerable<string> agentIps,
        CommandMessage command,
        int port = 0,
        int timeoutMs = 15000,
        CancellationToken cancellationToken = default)
    {
        var results = new ConcurrentDictionary<string, CommandResponse>();
        var tasks = agentIps.Select(async ip =>
        {
            var res = await SendCommandAsync(ip, command, port, timeoutMs, cancellationToken);
            results[ip] = res;
        });

        await Task.WhenAll(tasks);
        return results.ToDictionary(k => k.Key, v => v.Value);
    }
}
