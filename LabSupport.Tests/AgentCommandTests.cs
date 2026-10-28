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

    [Fact]
    public async Task MouseEventCommand_ShouldReturnSuccess()
    {
        var mousePayload = new MouseInputPayload
        {
            Action = MouseAction.LeftClick,
            NormalizedX = 0.5,
            NormalizedY = 0.5
        };

        var cmd = new CommandMessage
        {
            Type = CommandType.MouseEvent,
            Payload = MessageSerializer.Serialize(mousePayload)
        };

        var response = await _client.SendCommandAsync("127.0.0.1", cmd, _testPort, timeoutMs: 3000);
        Assert.True(response.Success);
    }

    [Fact]
    public async Task KeyboardEventCommand_ShouldReturnSuccess()
    {
        var keyPayload = new KeyboardInputPayload
        {
            Action = KeyboardAction.Shortcut,
            Key = "WIN+D"
        };

        var cmd = new CommandMessage
        {
            Type = CommandType.KeyboardEvent,
            Payload = MessageSerializer.Serialize(keyPayload)
        };

        var response = await _client.SendCommandAsync("127.0.0.1", cmd, _testPort, timeoutMs: 3000);
        Assert.True(response.Success);
    }

    [Fact]
    public async Task FileUploadAndDownload_ShouldPersistAndRetrieveFile()
    {
        string testFileName = $"test_transfer_{Guid.NewGuid():N}.txt";
        string testContent = "Hello from LabSupport File Transfer integration test!";
        string testBase64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(testContent));
        string targetDir = Path.Combine(Path.GetTempPath(), $"lab_transfer_test_{Guid.NewGuid():N}");

        try
        {
            // 1. Upload File
            var uploadPayload = new FileTransferPayload
            {
                FileName = testFileName,
                TargetDirectory = targetDir,
                ContentBase64 = testBase64
            };

            var uploadCmd = new CommandMessage
            {
                Type = CommandType.UploadFile,
                Payload = MessageSerializer.Serialize(uploadPayload)
            };

            var uploadResp = await _client.SendCommandAsync("127.0.0.1", uploadCmd, _testPort, timeoutMs: 5000);
            Assert.True(uploadResp.Success);

            string uploadedFilePath = Path.Combine(targetDir, testFileName);
            Assert.True(File.Exists(uploadedFilePath));

            // 2. Download File back
            var downloadCmd = new CommandMessage
            {
                Type = CommandType.DownloadFile,
                Payload = uploadedFilePath
            };

            var downloadResp = await _client.SendCommandAsync("127.0.0.1", downloadCmd, _testPort, timeoutMs: 5000);
            Assert.True(downloadResp.Success);
            Assert.NotNull(downloadResp.Output);

            string retrievedText = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(downloadResp.Output));
            Assert.Equal(testContent, retrievedText);
        }
        finally
        {
            if (Directory.Exists(targetDir))
            {
                try { Directory.Delete(targetDir, true); } catch { }
            }
        }
    }

    [Fact]
    public async Task BroadcastCommands_ShouldStartAndStopSuccessfully()
    {
        var startCmd = new CommandMessage
        {
            Type = CommandType.StartBroadcast,
            Payload = "http://127.0.0.1:8080/presentation/view"
        };

        var startResp = await _client.SendCommandAsync("127.0.0.1", startCmd, _testPort, timeoutMs: 3000);
        Assert.True(startResp.Success);

        var stopCmd = new CommandMessage
        {
            Type = CommandType.StopBroadcast
        };

        var stopResp = await _client.SendCommandAsync("127.0.0.1", stopCmd, _testPort, timeoutMs: 3000);
        Assert.True(stopResp.Success);
    }

    [Fact]
    public async Task ManagerIdValidation_ShouldAcceptMatchingAndRejectMismatch()
    {
        int authPort = 15903;
        using var secureListener = new CommandListener(authPort, expectedManagerId: "LAB-ROOM-101");
        secureListener.Start();

        var authClient = new AgentCommandService(authPort, managerId: "LAB-ROOM-101");

        // 1. Matching ManagerId -> Success
        var matchingCmd = new CommandMessage { Type = CommandType.Ping };
        var okResp = await authClient.SendCommandAsync("127.0.0.1", matchingCmd, authPort, timeoutMs: 3000);
        Assert.True(okResp.Success);
        Assert.Equal("Pong", okResp.Output);

        // 2. Mismatched ManagerId -> Unauthorized Failure
        var mismatchCmd = new CommandMessage { Type = CommandType.Ping, ManagerId = "LAB-OTHER-999" };
        var failResp = await authClient.SendCommandAsync("127.0.0.1", mismatchCmd, authPort, timeoutMs: 3000);
        Assert.False(failResp.Success);
        Assert.Contains("Unauthorized", failResp.Error);

        // 3. Empty ManagerId -> Unauthorized Failure
        var emptyCmd = new CommandMessage { Type = CommandType.Ping, ManagerId = "" };
        var unauthClient = new AgentCommandService(authPort, managerId: "");
        var unauthResp = await unauthClient.SendCommandAsync("127.0.0.1", emptyCmd, authPort, timeoutMs: 3000);
        Assert.False(unauthResp.Success);
        Assert.Contains("Unauthorized", unauthResp.Error);
    }

    public void Dispose()
    {
        _listener.Dispose();
    }
}
