using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using LabSupport.Shared.Protocol;
using LabSupport.Shared.Services;

namespace LabSupport.Agent.Services;

public class AgentDiscoveryClient : IDisposable
{
    private readonly string _expectedManagerId;
    private string _currentManagerIp;
    private int _currentHttpPort;
    private readonly int _beaconPort;
    private readonly string? _configFilePath;

    private UdpClient? _udpClient;
    private CancellationTokenSource? _cts;
    private Task? _listenTask;

    public bool IsRunning => _cts != null && !_cts.IsCancellationRequested;
    public string CurrentManagerIp => _currentManagerIp;
    public int CurrentHttpPort => _currentHttpPort;
    public string ExpectedManagerId => _expectedManagerId;

    public event Action<string, int>? OnManagerEndpointDiscovered;

    public AgentDiscoveryClient(
        string expectedManagerId,
        string currentManagerIp,
        int currentHttpPort = 8080,
        int beaconPort = 5903,
        string? configFilePath = null)
    {
        _expectedManagerId = expectedManagerId;
        _currentManagerIp = currentManagerIp;
        _currentHttpPort = currentHttpPort;
        _beaconPort = beaconPort;
        _configFilePath = configFilePath ?? Path.Combine(AppContext.BaseDirectory, "appsettings.json");
    }

    public void Start()
    {
        if (IsRunning) return;

        _cts = new CancellationTokenSource();

        try
        {
            _udpClient = new UdpClient();
            _udpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _udpClient.EnableBroadcast = true;
            _udpClient.Client.Bind(new IPEndPoint(IPAddress.Any, _beaconPort));
        }
        catch
        {
            try
            {
                _udpClient = new UdpClient();
                _udpClient.EnableBroadcast = true;
            }
            catch { }
        }

        _listenTask = Task.Run(() => ListenLoopAsync(_cts.Token));
    }

    public void Stop()
    {
        _cts?.Cancel();
        try { _udpClient?.Close(); } catch { }
        _udpClient?.Dispose();
        _udpClient = null;
        _cts?.Dispose();
        _cts = null;
    }

    public void Dispose()
    {
        Stop();
    }

    public async Task SendDiscoveryRequestAsync()
    {
        try
        {
            var req = new AgentDiscoveryRequest
            {
                ManagerId = _expectedManagerId,
                Hostname = Environment.MachineName,
                MacAddress = NetworkHelper.GetDefaultMacAddress(),
                Timestamp = DateTime.UtcNow
            };

            string json = MessageSerializer.Serialize(req);
            byte[] bytes = Encoding.UTF8.GetBytes(json);

            using var sender = new UdpClient();
            sender.EnableBroadcast = true;

            var targets = WolHelper.GetBroadcastAddresses();
            foreach (var bcast in targets)
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
        catch { }
    }

    private async Task ListenLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested && _udpClient != null)
        {
            try
            {
                var result = await _udpClient.ReceiveAsync(token);
                string json = Encoding.UTF8.GetString(result.Buffer);

                var beacon = MessageSerializer.Deserialize<ManagerBeaconMessage>(json);
                if (beacon != null && beacon.Type == "ManagerBeacon")
                {
                    ProcessBeacon(beacon, result.RemoteEndPoint);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch { }
        }
    }

    public bool ProcessBeacon(ManagerBeaconMessage beacon, IPEndPoint? sourceEndpoint = null)
    {
        if (beacon == null) return false;

        bool managerIdMatches;
        if (string.IsNullOrWhiteSpace(_expectedManagerId))
        {
            managerIdMatches = true;
        }
        else
        {
            managerIdMatches = !string.IsNullOrWhiteSpace(beacon.ManagerId) &&
                               string.Equals(_expectedManagerId.Trim(), beacon.ManagerId.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        if (!managerIdMatches) return false;

        string discoveredIp = beacon.ManagerIp;
        if (string.IsNullOrWhiteSpace(discoveredIp) || discoveredIp == "0.0.0.0" || discoveredIp == "127.0.0.1")
        {
            if (sourceEndpoint != null && !IPAddress.IsLoopback(sourceEndpoint.Address))
            {
                discoveredIp = sourceEndpoint.Address.ToString();
            }
        }

        if (string.IsNullOrWhiteSpace(discoveredIp)) return false;

        int discoveredPort = beacon.HttpPort > 0 ? beacon.HttpPort : 8080;

        bool changed = !string.Equals(_currentManagerIp, discoveredIp, StringComparison.OrdinalIgnoreCase) ||
                       _currentHttpPort != discoveredPort;

        if (changed)
        {
            _currentManagerIp = discoveredIp;
            _currentHttpPort = discoveredPort;

            SaveEndpointToAppSettings(discoveredIp, discoveredPort);
            OnManagerEndpointDiscovered?.Invoke(discoveredIp, discoveredPort);
            return true;
        }

        return false;
    }

    public bool SaveEndpointToAppSettings(string newIp, int newPort)
    {
        if (string.IsNullOrWhiteSpace(_configFilePath) || !File.Exists(_configFilePath))
        {
            return false;
        }

        try
        {
            string existingJson = File.ReadAllText(_configFilePath, Encoding.UTF8);
            var node = JsonNode.Parse(existingJson);
            if (node == null) return false;

            var managerNode = node["Manager"];
            if (managerNode == null)
            {
                managerNode = new JsonObject();
                node["Manager"] = managerNode;
            }

            managerNode["Ip"] = newIp;
            managerNode["Port"] = newPort;

            string updatedJson = node.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_configFilePath, updatedJson, Encoding.UTF8);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
