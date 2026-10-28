using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Xunit;
using LabSupport.Agent.Services;
using LabSupport.Shared.Models;
using LabSupport.Shared.Protocol;
using LabSupport.Shared.Services;

namespace LabSupport.Tests;

public class SecurityAuditAndEdgeCaseTests : IDisposable
{
    private readonly int _httpPort = 18090;
    private readonly string _dummyAgentPath;
    private readonly HttpFileServer _httpServer;
    private readonly HttpClient _httpClient;
    private readonly int _commandPort = 15915;
    private readonly CommandListener _commandListener;
    private readonly string _tempTestDir;

    public SecurityAuditAndEdgeCaseTests()
    {
        _tempTestDir = Path.Combine(Path.GetTempPath(), $"sec_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempTestDir);

        _dummyAgentPath = Path.Combine(_tempTestDir, "LabSupport.Agent.exe");
        File.WriteAllText(_dummyAgentPath, "MOCK_EXE_BINARY_DATA");

        _httpServer = new HttpFileServer(_httpPort, _dummyAgentPath);
        _httpServer.Start();

        _httpClient = new HttpClient();

        _commandListener = new CommandListener(_commandPort, expectedManagerId: "LAB-SECURE-99");
        _commandListener.Start();
    }

    // ---------------------------------------------------------
    // 1. HttpFileServer Security & Path Traversal Tests
    // ---------------------------------------------------------

    [Fact]
    public async Task HttpFileServer_RawPathTraversal_ReturnsBadRequest()
    {
        string url = $"http://127.0.0.1:{_httpPort}/agent/download/../../secret.txt";
        var response = await _httpClient.GetAsync(url);

        Assert.True(response.StatusCode == HttpStatusCode.BadRequest || response.StatusCode == HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task HttpFileServer_UrlEncodedPathTraversal_ReturnsBadRequest()
    {
        using var tcp = new System.Net.Sockets.TcpClient();
        await tcp.ConnectAsync("127.0.0.1", _httpPort);
        using var stream = tcp.GetStream();
        byte[] req = Encoding.ASCII.GetBytes("GET /agent/download/%2e%2e/test HTTP/1.1\r\nHost: 127.0.0.1\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(req);
        using var reader = new StreamReader(stream, Encoding.ASCII);
        string? statusLine = await reader.ReadLineAsync();

        Assert.NotNull(statusLine);
        Assert.Contains("400", statusLine);
    }

    [Fact]
    public async Task HttpFileServer_QueryParamFileOverride_ReturnsBadRequest()
    {
        string url = $"http://127.0.0.1:{_httpPort}/agent/download?file=../../secret.txt";
        var response = await _httpClient.GetAsync(url);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public void HttpFileServer_ResolveAgentBinaryPath_RejectsNonExecutableCandidates()
    {
        string invalidPath = Path.Combine(_tempTestDir, "secret.conf");
        File.WriteAllText(invalidPath, "sensitive=123");

        var serverWithBadPath = new HttpFileServer(_httpPort + 1, invalidPath);
        string? resolved = serverWithBadPath.ResolveAgentBinaryPath();

        // Must NOT resolve the non-.exe candidate
        Assert.NotEqual(invalidPath, resolved);
    }

    // ---------------------------------------------------------
    // 2. CommandListener Security & Parameter Validation Tests
    // ---------------------------------------------------------

    [Fact]
    public async Task CommandListener_UploadFile_PathTraversalInFileName_IsRejected()
    {
        string targetDir = Path.Combine(_tempTestDir, "uploads");
        var client = new AgentCommandService(_commandPort, managerId: "LAB-SECURE-99");

        var payload = new FileTransferPayload
        {
            FileName = "../../escaped_file.txt",
            TargetDirectory = targetDir,
            ContentBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes("evil payload"))
        };

        var cmd = new CommandMessage
        {
            Type = CommandType.UploadFile,
            Payload = MessageSerializer.Serialize(payload)
        };

        var response = await client.SendCommandAsync("127.0.0.1", cmd, _commandPort, timeoutMs: 3000);

        Assert.False(response.Success);
        Assert.Contains("insecure", response.Error, StringComparison.OrdinalIgnoreCase);

        // Verify escaped file was NOT created
        string escapedPath = Path.Combine(_tempTestDir, "escaped_file.txt");
        Assert.False(File.Exists(escapedPath));
    }

    [Fact]
    public async Task CommandListener_KillProcess_CurrentAgentProcess_IsProtected()
    {
        var client = new AgentCommandService(_commandPort, managerId: "LAB-SECURE-99");

        var cmdByPid = new CommandMessage
        {
            Type = CommandType.KillProcess,
            Payload = Environment.ProcessId.ToString()
        };

        var response = await client.SendCommandAsync("127.0.0.1", cmdByPid, _commandPort, timeoutMs: 3000);

        Assert.False(response.Success);
        Assert.Contains("Cannot terminate the LabSupport Agent host process", response.Error);

        var cmdByName = new CommandMessage
        {
            Type = CommandType.KillProcess,
            Payload = "LabSupport.Agent"
        };

        var responseByName = await client.SendCommandAsync("127.0.0.1", cmdByName, _commandPort, timeoutMs: 3000);
        Assert.False(responseByName.Success);
    }

    [Fact]
    public async Task CommandListener_StartBroadcast_InvalidOrMaliciousUrl_IsRejected()
    {
        var client = new AgentCommandService(_commandPort, managerId: "LAB-SECURE-99");

        // Shell injection attempt in stream URL
        var cmd = new CommandMessage
        {
            Type = CommandType.StartBroadcast,
            Payload = "http://localhost:8080/stream & calc.exe"
        };

        var response = await client.SendCommandAsync("127.0.0.1", cmd, _commandPort, timeoutMs: 3000);

        Assert.False(response.Success);
        Assert.Contains("Invalid or insecure broadcast URL", response.Error);
    }

    [Fact]
    public async Task CommandListener_DownloadFile_EmptyOrInvalidPath_ReturnsError()
    {
        var client = new AgentCommandService(_commandPort, managerId: "LAB-SECURE-99");

        var cmd = new CommandMessage
        {
            Type = CommandType.DownloadFile,
            Payload = ""
        };

        var response = await client.SendCommandAsync("127.0.0.1", cmd, _commandPort, timeoutMs: 3000);
        Assert.False(response.Success);
    }

    // ---------------------------------------------------------
    // 3. Pairing Code Bypass Defense Tests
    // ---------------------------------------------------------

    [Fact]
    public void AgentDiscoveryClient_EmptyBeaconManagerId_DoesNotBypassExpectedPairingCode()
    {
        var discoveryClient = new AgentDiscoveryClient("LAB-SECURE-ONLY", "192.168.1.1", 8080);

        // Attacker sends beacon with empty ManagerId
        var rogueBeacon = new ManagerBeaconMessage
        {
            ManagerId = "",
            ManagerIp = "10.10.10.10",
            HttpPort = 8080
        };

        bool matched = discoveryClient.ProcessBeacon(rogueBeacon);

        Assert.False(matched, "Empty beacon ManagerId must NOT bypass an agent configured with an expected pairing code!");
        Assert.Equal("192.168.1.1", discoveryClient.CurrentManagerIp);
    }

    [Fact]
    public void AgentDiscoveryClient_MatchingBeaconManagerId_IsAccepted()
    {
        var discoveryClient = new AgentDiscoveryClient("LAB-SECURE-ONLY", "192.168.1.1", 8080);

        var validBeacon = new ManagerBeaconMessage
        {
            ManagerId = "LAB-SECURE-ONLY",
            ManagerIp = "192.168.1.200",
            HttpPort = 8080
        };

        bool matched = discoveryClient.ProcessBeacon(validBeacon);

        Assert.True(matched);
        Assert.Equal("192.168.1.200", discoveryClient.CurrentManagerIp);
    }

    // ---------------------------------------------------------
    // 4. Script Generator & Executor Security Tests
    // ---------------------------------------------------------

    [Fact]
    public void ScriptGenerator_SanitizesCRLF_ToPreventCommandInjection()
    {
        string maliciousManagerId = "LAB-TEST\r\ncalc.exe\r\n#";
        string maliciousRoom = "Lab A\r\nInvoke-Expression 'evil'\r\n";

        string script = ScriptGenerator.GeneratePowerShellScript(
            "192.168.1.10",
            8080,
            maliciousManagerId,
            "LabSupportAgent",
            @"C:\ProgramData\LabSupport",
            maliciousRoom);

        Assert.NotNull(script);
        // Ensure CRLF was stripped so no newline command execution occurs
        Assert.DoesNotContain("LAB-TEST\r\ncalc.exe", script);
        Assert.Contains("calc.exe", script); // Escaped within the single quote parameter value
        Assert.Contains("$ManagerId      = 'LAB-TESTcalc.exe#'", script);
    }

    [Fact]
    public void ScriptGenerator_NewService_QuotesBinaryPathForCWE428()
    {
        string script = ScriptGenerator.GeneratePowerShellScript("192.168.1.10", 8080, "LAB-DEFAULT");

        // CWE-428 fix: Must contain escaped double quotes around $agentExe in New-Service
        Assert.Contains(@"-BinaryPathName ""`""$agentExe`""""", script);
    }

    [Fact]
    public async Task ScriptExecutor_NullScriptContent_DoesNotThrowException()
    {
        var executor = new ScriptExecutor();
        var payload = new ScriptPayload
        {
            Type = ScriptType.Batch,
            ScriptContent = null!,
            TimeoutSeconds = 5
        };

        var result = await executor.ExecuteAsync(payload);

        // Must execute cleanly without unhandled ArgumentNullException
        Assert.True(result.Success || !string.IsNullOrEmpty(result.Error) || !string.IsNullOrEmpty(result.Output));
    }

    [Fact]
    public async Task ScriptExecutor_LargeTimeout_DoesNotCauseIntegerOverflow()
    {
        var executor = new ScriptExecutor();
        var payload = new ScriptPayload
        {
            Type = ScriptType.Batch,
            ScriptContent = "echo SafeTimeout",
            TimeoutSeconds = int.MaxValue // Could cause overflow if multiplied by 1000
        };

        var result = await executor.ExecuteAsync(payload);

        Assert.True(result.Success);
        Assert.Contains("SafeTimeout", result.Output);
    }

    // ---------------------------------------------------------
    // 5. Multi-Lab Batch Generation & Input Simulator Edge Cases
    // ---------------------------------------------------------

    [Fact]
    public async Task ScriptGenerator_MultiLabPackage_HandlesInvalidFileNameCharacters()
    {
        string multiDir = Path.Combine(_tempTestDir, "multi_labs");
        var rooms = new[]
        {
            new LabRoom { Name = "Lab:101/Special*Room?\"Test|End", Capacity = 25 },
            new LabRoom { Name = "   ", Capacity = 10 } // Whitespace room should be skipped
        };

        await ScriptGenerator.GenerateMultiLabPackageAsync(
            multiDir,
            "192.168.1.10",
            8080,
            "LAB-MGR-TEST",
            rooms);

        Assert.True(Directory.Exists(multiDir));
        var files = Directory.GetFiles(multiDir, "*.bat");
        Assert.Single(files); // Only the valid (sanitized) room was created
        Assert.DoesNotContain(":", Path.GetFileName(files[0]));
        Assert.DoesNotContain("*", Path.GetFileName(files[0]));
        Assert.DoesNotContain("?", Path.GetFileName(files[0]));
    }

    [Fact]
    public async Task CommandListener_UnknownCommandType_ReturnsUnsupportedError()
    {
        var client = new AgentCommandService(_commandPort, managerId: "LAB-SECURE-99");
        var cmd = new CommandMessage
        {
            Type = (CommandType)99999
        };

        var response = await client.SendCommandAsync("127.0.0.1", cmd, _commandPort, timeoutMs: 3000);

        Assert.False(response.Success);
        Assert.Contains("Unsupported command type", response.Error);
    }

    [Fact]
    public async Task CommandListener_ExecuteScript_EmptyPayload_ReturnsError()
    {
        var client = new AgentCommandService(_commandPort, managerId: "LAB-SECURE-99");
        var cmd = new CommandMessage
        {
            Type = CommandType.ExecuteScript,
            Payload = ""
        };

        var response = await client.SendCommandAsync("127.0.0.1", cmd, _commandPort, timeoutMs: 3000);

        Assert.False(response.Success);
        Assert.Contains("Empty script payload", response.Error);
    }

    [Fact]
    public void InputSimulator_OutOfBoundCoordinates_ClampedSafely()
    {
        var simulator = new InputSimulator();

        var payloadNegative = new MouseInputPayload
        {
            Action = MouseAction.Move,
            NormalizedX = -100.5,
            NormalizedY = -50.0
        };

        var payloadOverflow = new MouseInputPayload
        {
            Action = MouseAction.Move,
            NormalizedX = 9999.0,
            NormalizedY = 5555.0
        };

        bool ok1 = simulator.SimulateMouse(payloadNegative);
        bool ok2 = simulator.SimulateMouse(payloadOverflow);

        Assert.True(ok1);
        Assert.True(ok2);
    }

    [Fact]
    public void InputSimulator_UnknownKeyboardShortcut_DoesNotThrow()
    {
        var simulator = new InputSimulator();

        var payload = new KeyboardInputPayload
        {
            Action = KeyboardAction.Shortcut,
            Key = "SOME_UNKNOWN_KEY_COMBO"
        };

        bool result = simulator.SimulateKeyboard(payload);
        Assert.True(result);
    }

    // ---------------------------------------------------------
    // 6. Hardening Regression Tests (Pairing Leak, Batch Extraction, Double URL Decode, CSV Injection)
    // ---------------------------------------------------------

    [Fact]
    public async Task CommandListener_PairingCodeMismatch_DoesNotLeakExpectedManagerId()
    {
        var client = new AgentCommandService(_commandPort, managerId: "WRONG-ATTACKER-ID");
        var pingCmd = new CommandMessage
        {
            Type = CommandType.Ping,
            ManagerId = "WRONG-ATTACKER-ID"
        };

        var response = await client.SendCommandAsync("127.0.0.1", pingCmd, _commandPort, timeoutMs: 3000);

        Assert.False(response.Success);
        Assert.Contains("Unauthorized", response.Error, StringComparison.OrdinalIgnoreCase);
        // CRITICAL: Must NEVER leak the expected pairing code in the error response!
        Assert.DoesNotContain("LAB-SECURE-99", response.Error);
    }

    [Fact]
    public async Task CommandListener_UploadFile_TargetDirectoryTraversal_IsRejected()
    {
        var client = new AgentCommandService(_commandPort, managerId: "LAB-SECURE-99");
        var payload = new FileTransferPayload
        {
            FileName = "safe_name.txt",
            TargetDirectory = "../../Windows/System32",
            ContentBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes("test"))
        };

        var cmd = new CommandMessage
        {
            Type = CommandType.UploadFile,
            Payload = MessageSerializer.Serialize(payload)
        };

        var response = await client.SendCommandAsync("127.0.0.1", cmd, _commandPort, timeoutMs: 3000);

        Assert.False(response.Success);
        Assert.Contains("traversal", response.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CommandListener_LaunchProcess_ShellMetacharacters_IsRejected()
    {
        var client = new AgentCommandService(_commandPort, managerId: "LAB-SECURE-99");
        var cmd = new CommandMessage
        {
            Type = CommandType.LaunchProcess,
            Payload = MessageSerializer.Serialize(new LaunchPayload
            {
                Target = "notepad.exe",
                Arguments = "& calc.exe"
            })
        };

        var response = await client.SendCommandAsync("127.0.0.1", cmd, _commandPort, timeoutMs: 3000);

        Assert.False(response.Success);
        Assert.Contains("Prohibited characters", response.Error);
    }

    [Fact]
    public async Task HttpFileServer_DoubleUrlEncodedTraversal_ReturnsBadRequest()
    {
        using var tcp = new System.Net.Sockets.TcpClient();
        await tcp.ConnectAsync("127.0.0.1", _httpPort);
        using var stream = tcp.GetStream();
        // Double-encoded: %252e%252e -> %2e%2e -> ..
        byte[] req = Encoding.ASCII.GetBytes("GET /agent/download/%252e%252e/test HTTP/1.1\r\nHost: 127.0.0.1\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(req);
        using var reader = new StreamReader(stream, Encoding.ASCII);
        string? statusLine = await reader.ReadLineAsync();

        Assert.NotNull(statusLine);
        Assert.Contains("400", statusLine);
    }

    [Fact]
    public async Task HttpFileServer_EncodedBackslashTraversal_ReturnsBadRequest()
    {
        using var tcp = new System.Net.Sockets.TcpClient();
        await tcp.ConnectAsync("127.0.0.1", _httpPort);
        using var stream = tcp.GetStream();
        byte[] req = Encoding.ASCII.GetBytes("GET /agent/download/%5c..%5ctest HTTP/1.1\r\nHost: 127.0.0.1\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(req);
        using var reader = new StreamReader(stream, Encoding.ASCII);
        string? statusLine = await reader.ReadLineAsync();

        Assert.NotNull(statusLine);
        Assert.Contains("400", statusLine);
    }

    [Fact]
    public void ScriptGenerator_BatchInstaller_PayloadDelimiter_ExtractsCleanPowerShellCode()
    {
        string bat = ScriptGenerator.GenerateSingleBatchInstaller("192.168.1.100", 8080, "LAB-DELIMITER-TEST");

        // Simulate the extraction logic that powershell executes:
        string marker = ":::" + "___LABSUPPORT_POWERSHELL_PAYLOAD___";
        int idx = bat.LastIndexOf(marker);

        Assert.True(idx > 0, "Marker must exist in the batch file");

        string extractedPs = bat.Substring(idx + marker.Length).TrimStart('\r', '\n');

        // Verify the extracted code starts with the PowerShell header, NOT batch commands
        Assert.StartsWith("#", extractedPs);
        Assert.DoesNotContain("set EXIT_CODE", extractedPs);
        Assert.DoesNotContain("powershell.exe -NoProfile", extractedPs);
        Assert.Contains("$ManagerId      = 'LAB-DELIMITER-TEST'", extractedPs);
    }

    [Fact]
    public void ScriptGenerator_GeneratePowerShellScript_SanitizesQuotesAndQuotesServiceInScDelete()
    {
        string script = ScriptGenerator.GeneratePowerShellScript("192.168.1.100", 8080, "LAB\"-SEC-TEST", "Lab Support Service", roomName: "Room\"1");

        // Double quotes stripped to prevent JSON injection in appsettings.json
        Assert.DoesNotContain("LAB\"-SEC-TEST", script);
        Assert.Contains("LAB-SEC-TEST", script);
        Assert.DoesNotContain("Room\"1", script);
        Assert.Contains("Room1", script);

        // sc.exe delete must quote the service name
        Assert.Contains(@"sc.exe delete ""`""$ServiceName`""""", script);
    }

    [Fact]
    public void DatabaseService_EscapeCsv_NeutralizesFormulaInjection()
    {
        string malicious1 = "=cmd|' /C calc'!A0";
        string malicious2 = "@SUM(1+1)";
        string malicious3 = "+12345";
        string malicious4 = "-54321";

        string escaped1 = DatabaseService.EscapeCsv(malicious1);
        string escaped2 = DatabaseService.EscapeCsv(malicious2);
        string escaped3 = DatabaseService.EscapeCsv(malicious3);
        string escaped4 = DatabaseService.EscapeCsv(malicious4);

        Assert.StartsWith("\"'=", escaped1);
        Assert.StartsWith("\"'@", escaped2);
        Assert.StartsWith("\"'+", escaped3);
        Assert.StartsWith("\"'-", escaped4);
    }

    public void Dispose()
    {
        _httpServer.Dispose();
        _httpClient.Dispose();
        _commandListener.Dispose();

        if (Directory.Exists(_tempTestDir))
        {
            try { Directory.Delete(_tempTestDir, recursive: true); } catch { }
        }
    }
}
