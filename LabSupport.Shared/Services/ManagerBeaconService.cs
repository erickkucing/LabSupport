using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LabSupport.Shared.Protocol;

namespace LabSupport.Shared.Services;

public class ManagerBeaconService : IDisposable
{
    private readonly int _beaconPort;
    private string _managerId;
    private string _managerIp;
    private int _httpPort;
    private int _commandPort;
    private readonly int _intervalSeconds;

    private UdpClient? _udpListener;
    private CancellationTokenSource? _cts;
    private Task? _broadcastTask;
    private Task? _listenTask;

    public bool IsRunning => _cts != null && !_cts.IsCancellationRequested;
    public string ManagerId => _managerId;
    public string ManagerIp => _managerIp;
    public int HttpPort => _httpPort;
    public int BeaconPort => _beaconPort;

    public event Action<AgentDiscoveryRequest, IPEndPoint>? OnAgentDiscovered;

    public ManagerBeaconService(
        string managerId,
        string managerIp,
        int httpPort = 8080,
        int commandPort = 5902,
        int beaconPort = 5903,
        int intervalSeconds = 5)
    {
        _managerId = managerId;
        _managerIp = managerIp;
        _httpPort = httpPort;
        _commandPort = commandPort;
        _beaconPort = beaconPort;
        _intervalSeconds = Math.Max(1, intervalSeconds);
    }

    public void UpdateEndpoint(string newIp, int newHttpPort, string? newManagerId = null)
    {
        _managerIp = newIp;
        _httpPort = newHttpPort;
        if (!string.IsNullOrWhiteSpace(newManagerId))
        {
            _managerId = newManagerId;
        }
    }

    public void Start()
    {
        if (IsRunning) return;

        _cts = new CancellationTokenSource();

        try
        {
            _udpListener = new UdpClient();
            _udpListener.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _udpListener.EnableBroadcast = true;
            _udpListener.Client.Bind(new IPEndPoint(IPAddress.Any, _beaconPort));
        }
        catch
        {
            // If binding to beacon port fails (e.g. port already held), create client without bind for broadcasting only
            try
            {
                _udpListener = new UdpClient();
                _udpListener.EnableBroadcast = true;
            }
            catch { }
        }

        _broadcastTask = Task.Run(() => RunBroadcastLoopAsync(_cts.Token));
        if (_udpListener?.Client.IsBound == true)
        {
            _listenTask = Task.Run(() => RunListenLoopAsync(_cts.Token));
        }
    }

    public void Stop()
    {
        _cts?.Cancel();
        try { _udpListener?.Close(); } catch { }
        _udpListener?.Dispose();
        _udpListener = null;
        _cts?.Dispose();
        _cts = null;
    }

    public void Dispose()
    {
        Stop();
    }

    private async Task RunBroadcastLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                await BroadcastBeaconOnceAsync();
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch { }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(_intervalSeconds), token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    public async Task BroadcastBeaconOnceAsync()
    {
        if (string.IsNullOrWhiteSpace(_managerIp))
        {
            _managerIp = NetworkHelper.GetPrimaryLanIPv4Address();
        }

        var beacon = new ManagerBeaconMessage
        {
            ManagerId = _managerId,
            ManagerIp = _managerIp,
            HttpPort = _httpPort,
            CommandPort = _commandPort,
            Timestamp = DateTime.UtcNow
        };

        string json = MessageSerializer.Serialize(beacon);
        byte[] bytes = Encoding.UTF8.GetBytes(json);

        var broadcastEndpoints = WolHelper.GetBroadcastAddresses();
        using var sender = new UdpClient();
        sender.EnableBroadcast = true;

        foreach (var bcast in broadcastEndpoints)
        {
            try
            {
                await sender.SendAsync(bytes, bytes.Length, new IPEndPoint(bcast, _beaconPort));
            }
            catch { }
        }

        try
        {
            await sender.SendAsync(bytes, bytes.Length, new IPEndPoint(IPAddress.Broadcast, _beaconPort));
        }
        catch { }
    }

    private async Task RunListenLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested && _udpListener != null)
        {
            try
            {
                var result = await _udpListener.ReceiveAsync(token);
                string json = Encoding.UTF8.GetString(result.Buffer);

                var req = MessageSerializer.Deserialize<AgentDiscoveryRequest>(json);
                if (req != null && req.Type == "FindManager")
                {
                    bool match;
                    if (string.IsNullOrWhiteSpace(_managerId))
                    {
                        match = true;
                    }
                    else
                    {
                        match = !string.IsNullOrWhiteSpace(req.ManagerId) &&
                                string.Equals(_managerId.Trim(), req.ManagerId.Trim(), StringComparison.OrdinalIgnoreCase);
                    }

                    if (match)
                    {
                        OnAgentDiscovered?.Invoke(req, result.RemoteEndPoint);

                        // Direct reply to the agent
                        var reply = new ManagerBeaconMessage
                        {
                            ManagerId = _managerId,
                            ManagerIp = _managerIp,
                            HttpPort = _httpPort,
                            CommandPort = _commandPort,
                            Timestamp = DateTime.UtcNow
                        };

                        byte[] replyBytes = Encoding.UTF8.GetBytes(MessageSerializer.Serialize(reply));
                        await _udpListener.SendAsync(replyBytes, replyBytes.Length, result.RemoteEndPoint);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch { }
        }
    }
}
