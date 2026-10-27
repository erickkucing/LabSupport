using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Xunit;
using LabSupport.Shared.Services;
using LabSupport.Shared.Protocol;

namespace LabSupport.Tests;

public class HttpServerTests : IDisposable
{
    private readonly int _testPort = 18080;
    private readonly string _dummyAgentPath;
    private readonly HttpFileServer _server;
    private readonly HttpClient _client;

    public HttpServerTests()
    {
        _dummyAgentPath = Path.Combine(Path.GetTempPath(), $"dummy_agent_{Guid.NewGuid():N}.exe");
        File.WriteAllText(_dummyAgentPath, "MOCK_EXE_BINARY_CONTENT");

        _server = new HttpFileServer(_testPort, _dummyAgentPath);
        _server.Start();

        _client = new HttpClient();
    }

    [Fact]
    public async Task HealthEndpoint_ShouldReturn200OK()
    {
        string url = $"http://127.0.0.1:{_testPort}/health";
        var response = await _client.GetAsync(url);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        string text = await response.Content.ReadAsStringAsync();
        Assert.Equal("OK", text);
    }

    [Fact]
    public async Task AgentDownloadEndpoint_ShouldServeFile()
    {
        string url = $"http://127.0.0.1:{_testPort}/agent/download";
        var response = await _client.GetAsync(url);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/octet-stream", response.Content.Headers.ContentType?.MediaType);
        string content = await response.Content.ReadAsStringAsync();
        Assert.Equal("MOCK_EXE_BINARY_CONTENT", content);
    }

    [Fact]
    public async Task HeartbeatEndpoint_ShouldTriggerEventWithPayload()
    {
        HeartbeatMessage? received = null;
        _server.OnHeartbeatReceived += hb => received = hb;

        var sample = new HeartbeatMessage
        {
            Hostname = "PC-TEST-99",
            IpAddress = "192.168.1.99",
            MacAddress = "AA:BB:CC:DD:EE:99",
            CpuUsage = 15.0,
            RamUsageMb = 2048,
            RamTotalMb = 8192,
            DiskFreeGb = 50.5
        };

        string json = MessageSerializer.Serialize(sample);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        string url = $"http://127.0.0.1:{_testPort}/agent/heartbeat";
        var response = await _client.PostAsync(url, content);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(received);
        Assert.Equal("PC-TEST-99", received.Hostname);
        Assert.Equal("192.168.1.99", received.IpAddress);
        Assert.Equal(15.0, received.CpuUsage);
    }

    [Fact]
    public async Task NonExistentRoute_ShouldReturn404()
    {
        string url = $"http://127.0.0.1:{_testPort}/unknown_route";
        var response = await _client.GetAsync(url);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    public void Dispose()
    {
        _server.Dispose();
        _client.Dispose();
        if (File.Exists(_dummyAgentPath))
        {
            try { File.Delete(_dummyAgentPath); } catch { }
        }
    }
}
