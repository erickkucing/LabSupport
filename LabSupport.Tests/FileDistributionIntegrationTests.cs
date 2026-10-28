using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Xunit;
using LabSupport.Agent.Services;
using LabSupport.Shared.Models;
using LabSupport.Shared.Protocol;
using LabSupport.Shared.Services;

namespace LabSupport.Tests;

public class FileDistributionIntegrationTests : IDisposable
{
    private static int _portCounter = 27000;
    private readonly int _httpPort;
    private readonly int _commandPort;
    private readonly string _testTempDir;
    private readonly HttpFileServer _httpServer;
    private readonly CommandListener _agentListener;
    private readonly AgentCommandService _commandClient;

    public FileDistributionIntegrationTests()
    {
        _commandPort = Interlocked.Add(ref _portCounter, 2);
        _httpPort = _commandPort + 1;

        _testTempDir = Path.Combine(Path.GetTempPath(), $"ls_fdist_it_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testTempDir);

        _httpServer = new HttpFileServer(_httpPort);
        _httpServer.Start();

        _agentListener = new CommandListener(_commandPort);
        _agentListener.Start();

        _commandClient = new AgentCommandService(_commandPort);
    }

    [Fact]
    public async Task DistributeSingleFile_ValidChecksum_DeploysSuccessfully()
    {
        string srcFile = Path.Combine(_testTempDir, "lab_notes.txt");
        string expectedContent = "Praktikum Jaringan Komputer - Modul 01";
        await File.WriteAllTextAsync(srcFile, expectedContent);

        using var session = FileDistributionService.CreateSession(srcFile, false);
        _httpServer.RegisterFileSession(session);

        string targetDir = Path.Combine(_testTempDir, "StudentDesktop");
        Directory.CreateDirectory(targetDir);

        var payload = new FileDistributionPayload
        {
            TransferToken = session.Token,
            DownloadUrl = $"http://127.0.0.1:{_httpPort}/files/download/{session.Token}",
            ItemName = "lab_notes.txt",
            IsDirectory = false,
            TargetDirectory = targetDir,
            ChecksumSha256 = session.Sha256Hash,
            FileSizeBytes = session.FileSize,
            Overwrite = true
        };

        var cmd = new CommandMessage
        {
            Type = CommandType.DownloadAndDeployFile,
            Payload = MessageSerializer.Serialize(payload)
        };

        var response = await _commandClient.SendCommandAsync("127.0.0.1", cmd, _commandPort, timeoutMs: 5000);

        Assert.True(response.Success);
        string deployedPath = Path.Combine(targetDir, "lab_notes.txt");
        Assert.True(File.Exists(deployedPath));
        Assert.Equal(expectedContent, await File.ReadAllTextAsync(deployedPath));
    }

    [Fact]
    public async Task DistributeSingleFile_InvalidChecksum_RejectsAndCleansUp()
    {
        string srcFile = Path.Combine(_testTempDir, "cheat_sheet.txt");
        await File.WriteAllTextAsync(srcFile, "Original Content");

        using var session = FileDistributionService.CreateSession(srcFile, false);
        _httpServer.RegisterFileSession(session);

        string targetDir = Path.Combine(_testTempDir, "StudentDesktopFail");
        Directory.CreateDirectory(targetDir);

        var payload = new FileDistributionPayload
        {
            TransferToken = session.Token,
            DownloadUrl = $"http://127.0.0.1:{_httpPort}/files/download/{session.Token}",
            ItemName = "cheat_sheet.txt",
            IsDirectory = false,
            TargetDirectory = targetDir,
            ChecksumSha256 = "INVALID_CORRUPTED_HASH_000000000000000000000000000000000000000000",
            FileSizeBytes = session.FileSize,
            Overwrite = true
        };

        var cmd = new CommandMessage
        {
            Type = CommandType.DownloadAndDeployFile,
            Payload = MessageSerializer.Serialize(payload)
        };

        var response = await _commandClient.SendCommandAsync("127.0.0.1", cmd, _commandPort, timeoutMs: 5000);

        Assert.False(response.Success);
        Assert.Contains("SHA256 hash mismatch", response.Error);
        string deployedPath = Path.Combine(targetDir, "cheat_sheet.txt");
        Assert.False(File.Exists(deployedPath));
    }

    [Fact]
    public async Task DistributeFolder_TransparentlyReconstructsFolderHierarchy()
    {
        string srcFolder = Path.Combine(_testTempDir, "TugasProject");
        Directory.CreateDirectory(srcFolder);
        await File.WriteAllTextAsync(Path.Combine(srcFolder, "index.html"), "<h1>Project Siswa</h1>");
        string assetsDir = Path.Combine(srcFolder, "css");
        Directory.CreateDirectory(assetsDir);
        await File.WriteAllTextAsync(Path.Combine(assetsDir, "style.css"), "body { color: blue; }");

        using var session = FileDistributionService.CreateSession(srcFolder, true);
        _httpServer.RegisterFileSession(session);

        string targetParentDir = Path.Combine(_testTempDir, "StudentDocuments");
        Directory.CreateDirectory(targetParentDir);

        var payload = new FileDistributionPayload
        {
            TransferToken = session.Token,
            DownloadUrl = $"http://127.0.0.1:{_httpPort}/files/download/{session.Token}",
            ItemName = "TugasProject",
            IsDirectory = true,
            TargetDirectory = targetParentDir,
            ChecksumSha256 = session.Sha256Hash,
            FileSizeBytes = session.FileSize,
            Overwrite = true
        };

        var cmd = new CommandMessage
        {
            Type = CommandType.DownloadAndDeployFile,
            Payload = MessageSerializer.Serialize(payload)
        };

        var response = await _commandClient.SendCommandAsync("127.0.0.1", cmd, _commandPort, timeoutMs: 5000);

        Assert.True(response.Success);

        string expectedFolderPath = Path.Combine(targetParentDir, "TugasProject");
        Assert.True(Directory.Exists(expectedFolderPath));
        Assert.True(File.Exists(Path.Combine(expectedFolderPath, "index.html")));
        Assert.Equal("<h1>Project Siswa</h1>", await File.ReadAllTextAsync(Path.Combine(expectedFolderPath, "index.html")));
        Assert.True(File.Exists(Path.Combine(expectedFolderPath, "css", "style.css")));
        Assert.Equal("body { color: blue; }", await File.ReadAllTextAsync(Path.Combine(expectedFolderPath, "css", "style.css")));
    }

    [Fact]
    public async Task OverwriteBehavior_ExistingFileIsCleanlyReplaced()
    {
        string targetDir = Path.Combine(_testTempDir, "OverwriteTestDir");
        Directory.CreateDirectory(targetDir);
        string existingFilePath = Path.Combine(targetDir, "config.ini");
        await File.WriteAllTextAsync(existingFilePath, "VERSION=1.0");

        string newFile = Path.Combine(_testTempDir, "config.ini");
        await File.WriteAllTextAsync(newFile, "VERSION=2.0_UPDATED");

        using var session = FileDistributionService.CreateSession(newFile, false);
        _httpServer.RegisterFileSession(session);

        var payload = new FileDistributionPayload
        {
            TransferToken = session.Token,
            DownloadUrl = $"http://127.0.0.1:{_httpPort}/files/download/{session.Token}",
            ItemName = "config.ini",
            IsDirectory = false,
            TargetDirectory = targetDir,
            ChecksumSha256 = session.Sha256Hash,
            FileSizeBytes = session.FileSize,
            Overwrite = true
        };

        var cmd = new CommandMessage
        {
            Type = CommandType.DownloadAndDeployFile,
            Payload = MessageSerializer.Serialize(payload)
        };

        var response = await _commandClient.SendCommandAsync("127.0.0.1", cmd, _commandPort, timeoutMs: 5000);

        Assert.True(response.Success);
        Assert.Equal("VERSION=2.0_UPDATED", await File.ReadAllTextAsync(existingFilePath));
    }

    [Fact]
    public async Task OverwriteBehavior_OverwriteDisabled_RejectsExistingFileWithoutModifying()
    {
        string targetDir = Path.Combine(_testTempDir, "NoOverwriteDir");
        Directory.CreateDirectory(targetDir);
        string existingFilePath = Path.Combine(targetDir, "original.txt");
        await File.WriteAllTextAsync(existingFilePath, "PRESERVED_ORIGINAL_CONTENT");

        string newFile = Path.Combine(_testTempDir, "incoming.txt");
        await File.WriteAllTextAsync(newFile, "NEW_CONTENT_SHOULD_BE_REJECTED");

        using var session = FileDistributionService.CreateSession(newFile, false);
        _httpServer.RegisterFileSession(session);

        var payload = new FileDistributionPayload
        {
            TransferToken = session.Token,
            DownloadUrl = $"http://127.0.0.1:{_httpPort}/files/download/{session.Token}",
            ItemName = "original.txt",
            IsDirectory = false,
            TargetDirectory = targetDir,
            ChecksumSha256 = session.Sha256Hash,
            FileSizeBytes = session.FileSize,
            Overwrite = false
        };

        var cmd = new CommandMessage
        {
            Type = CommandType.DownloadAndDeployFile,
            Payload = MessageSerializer.Serialize(payload)
        };

        var response = await _commandClient.SendCommandAsync("127.0.0.1", cmd, _commandPort, timeoutMs: 5000);

        Assert.False(response.Success);
        Assert.Contains("overwrite is disabled", response.Error);
        Assert.Equal("PRESERVED_ORIGINAL_CONTENT", await File.ReadAllTextAsync(existingFilePath));
    }

    [Fact]
    public async Task OverwriteBehavior_DirectoryReplacesExistingFileOfSameName_WhenOverwriteEnabled()
    {
        string targetDir = Path.Combine(_testTempDir, "DirReplaceFileDir");
        Directory.CreateDirectory(targetDir);
        string conflictPath = Path.Combine(targetDir, "CollisionItem");
        // Create as file first
        await File.WriteAllTextAsync(conflictPath, "OLD_FILE_CONTENT");

        // Now send folder with the same name
        string srcFolder = Path.Combine(_testTempDir, "CollisionItem");
        Directory.CreateDirectory(srcFolder);
        await File.WriteAllTextAsync(Path.Combine(srcFolder, "inside.txt"), "NEW_FOLDER_INSIDE");

        using var session = FileDistributionService.CreateSession(srcFolder, true);
        _httpServer.RegisterFileSession(session);

        var payload = new FileDistributionPayload
        {
            TransferToken = session.Token,
            DownloadUrl = $"http://127.0.0.1:{_httpPort}/files/download/{session.Token}",
            ItemName = "CollisionItem",
            IsDirectory = true,
            TargetDirectory = targetDir,
            ChecksumSha256 = session.Sha256Hash,
            FileSizeBytes = session.FileSize,
            Overwrite = true
        };

        var cmd = new CommandMessage
        {
            Type = CommandType.DownloadAndDeployFile,
            Payload = MessageSerializer.Serialize(payload)
        };

        var response = await _commandClient.SendCommandAsync("127.0.0.1", cmd, _commandPort, timeoutMs: 5000);

        Assert.True(response.Success);
        Assert.True(Directory.Exists(conflictPath));
        Assert.True(File.Exists(Path.Combine(conflictPath, "inside.txt")));
        Assert.Equal("NEW_FOLDER_INSIDE", await File.ReadAllTextAsync(Path.Combine(conflictPath, "inside.txt")));
    }

    [Fact]
    public async Task DistributeFolder_WithCustomSubfolder_DeploysToSubfolderStructure()
    {
        string srcFolder = Path.Combine(_testTempDir, "StudentSource");
        Directory.CreateDirectory(srcFolder);
        await File.WriteAllTextAsync(Path.Combine(srcFolder, "assignment.cs"), "// Student code");

        using var session = FileDistributionService.CreateSession(srcFolder, true);
        _httpServer.RegisterFileSession(session);

        string targetParentDir = Path.Combine(_testTempDir, "WorkspaceBase");
        Directory.CreateDirectory(targetParentDir);

        var payload = new FileDistributionPayload
        {
            TransferToken = session.Token,
            DownloadUrl = $"http://127.0.0.1:{_httpPort}/files/download/{session.Token}",
            ItemName = "StudentSource",
            IsDirectory = true,
            TargetDirectory = targetParentDir,
            CustomSubfolder = "ClassA",
            ChecksumSha256 = session.Sha256Hash,
            FileSizeBytes = session.FileSize,
            Overwrite = true
        };

        var cmd = new CommandMessage
        {
            Type = CommandType.DownloadAndDeployFile,
            Payload = MessageSerializer.Serialize(payload)
        };

        var response = await _commandClient.SendCommandAsync("127.0.0.1", cmd, _commandPort, timeoutMs: 5000);

        Assert.True(response.Success);
        string expectedPath = Path.Combine(targetParentDir, "ClassA", "StudentSource", "assignment.cs");
        Assert.True(File.Exists(expectedPath));
        Assert.Equal("// Student code", await File.ReadAllTextAsync(expectedPath));
    }

    [Fact]
    public async Task MonitoringModal_TracksProgressAndRetryFailedFunctionality()
    {
        using var progressVM = new FileTransferProgressTracker
        {
            ItemSummary = "Test Distribution to 2 PCs",
            IsInProgress = true,
            CanCancel = true
        };

        var target1 = new FileTransferTargetItem { Hostname = "PC-01", IpAddress = "192.168.1.101" };
        var target2 = new FileTransferTargetItem { Hostname = "PC-02", IpAddress = "192.168.1.102" };
        progressVM.Targets.Add(target1);
        progressVM.Targets.Add(target2);

        // Simulate PC-01 completes, PC-02 fails
        progressVM.MarkTargetCompleted("192.168.1.101", "Selesai (100%)");
        progressVM.MarkTargetFailed("192.168.1.102", "Connection timeout");

        Assert.Equal(TargetTransferState.Completed, target1.State);
        Assert.Equal(TargetTransferState.Failed, target2.State);
        Assert.False(progressVM.IsInProgress);
        Assert.True(progressVM.CanRetryFailed);

        // Test Retry Failed
        bool retriedFailedTargetOnly = false;
        progressVM.RetryFailedHandler = async failedItems =>
        {
            Assert.Single(failedItems);
            Assert.Equal("192.168.1.102", failedItems[0].IpAddress);
            retriedFailedTargetOnly = true;

            // Simulate second attempt succeeds
            progressVM.MarkTargetCompleted("192.168.1.102", "Selesai (100%)");
            await Task.CompletedTask;
        };

        await progressVM.ExecuteRetryFailedAsync();

        Assert.True(retriedFailedTargetOnly);
        Assert.Equal(TargetTransferState.Completed, target2.State);
        Assert.False(progressVM.CanRetryFailed);
        Assert.False(progressVM.IsInProgress);
    }

    [Fact]
    public void MonitoringModal_CancelTransfer_CancelsPendingTargets()
    {
        using var progressVM = new FileTransferProgressTracker
        {
            IsInProgress = true,
            CanCancel = true
        };

        var target1 = new FileTransferTargetItem { Hostname = "PC-01", IpAddress = "192.168.1.101", State = TargetTransferState.Transferring };
        var target2 = new FileTransferTargetItem { Hostname = "PC-02", IpAddress = "192.168.1.102", State = TargetTransferState.Waiting };
        progressVM.Targets.Add(target1);
        progressVM.Targets.Add(target2);

        progressVM.CancelTransfer();

        Assert.True(progressVM.CancellationToken.IsCancellationRequested);
        Assert.Equal(TargetTransferState.Cancelled, target1.State);
        Assert.Equal(TargetTransferState.Cancelled, target2.State);
        Assert.False(progressVM.CanCancel);
    }

    [Fact]
    public async Task DirectClient_SendFileAsync_UsesHttpStreaming()
    {
        string testFile = Path.Combine(_testTempDir, "remote_send_test.txt");
        await File.WriteAllTextAsync(testFile, "Direct transfer to remote student PC");

        string targetDir = Path.Combine(_testTempDir, "RemoteStudentDest");
        Directory.CreateDirectory(targetDir);

        using var session = FileDistributionService.CreateSession(testFile, false);
        _httpServer.RegisterFileSession(session);

        var payload = new FileDistributionPayload
        {
            TransferToken = session.Token,
            DownloadUrl = $"http://127.0.0.1:{_httpPort}/files/download/{session.Token}",
            ItemName = session.FileName,
            IsDirectory = session.IsDirectory,
            TargetDirectory = targetDir,
            ChecksumSha256 = session.Sha256Hash,
            FileSizeBytes = session.FileSize,
            Overwrite = true
        };

        var cmd = new CommandMessage
        {
            Type = CommandType.DownloadAndDeployFile,
            Payload = MessageSerializer.Serialize(payload)
        };

        var resp = await _commandClient.SendCommandAsync("127.0.0.1", cmd, _commandPort, timeoutMs: 5000);

        Assert.True(resp.Success);
        Assert.True(File.Exists(Path.Combine(targetDir, "remote_send_test.txt")));
        Assert.Equal("Direct transfer to remote student PC", await File.ReadAllTextAsync(Path.Combine(targetDir, "remote_send_test.txt")));
    }

    [Fact]
    public void HttpFileServer_UnregisterSession_DeletesTemporaryFileCleanly()
    {
        string folderToDistribute = Path.Combine(_testTempDir, "TempFolderForUnregister");
        Directory.CreateDirectory(folderToDistribute);
        File.WriteAllText(Path.Combine(folderToDistribute, "dummy.txt"), "DUMMY");

        var session = FileDistributionService.CreateSession(folderToDistribute, true);
        string tempZip = session.FilePath;
        Assert.True(File.Exists(tempZip));

        _httpServer.RegisterFileSession(session);
        Assert.NotNull(_httpServer.GetFileSession(session.Token));

        _httpServer.UnregisterFileSession(session.Token);

        Assert.Null(_httpServer.GetFileSession(session.Token));
        Assert.False(File.Exists(tempZip), "Temporary zip file must be deleted upon unregistering the session.");
    }

    [Fact]
    public async Task DistributeFolder_EmptyFolder_ReconstructsEmptyFolderStructure()
    {
        string emptySrcDir = Path.Combine(_testTempDir, "EmptySourceProject");
        Directory.CreateDirectory(emptySrcDir);
        Directory.CreateDirectory(Path.Combine(emptySrcDir, "Sub1", "Sub2"));

        using var session = FileDistributionService.CreateSession(emptySrcDir, true);
        _httpServer.RegisterFileSession(session);

        string targetParentDir = Path.Combine(_testTempDir, "EmptyDestParent");
        Directory.CreateDirectory(targetParentDir);

        var payload = new FileDistributionPayload
        {
            TransferToken = session.Token,
            DownloadUrl = $"http://127.0.0.1:{_httpPort}/files/download/{session.Token}",
            ItemName = "EmptySourceProject",
            IsDirectory = true,
            TargetDirectory = targetParentDir,
            ChecksumSha256 = session.Sha256Hash,
            FileSizeBytes = session.FileSize,
            Overwrite = true
        };

        var cmd = new CommandMessage
        {
            Type = CommandType.DownloadAndDeployFile,
            Payload = MessageSerializer.Serialize(payload)
        };

        var response = await _commandClient.SendCommandAsync("127.0.0.1", cmd, _commandPort, timeoutMs: 5000);

        Assert.True(response.Success);
        string expectedDeepPath = Path.Combine(targetParentDir, "EmptySourceProject", "Sub1", "Sub2");
        Assert.True(Directory.Exists(expectedDeepPath));
    }

    public void Dispose()
    {
        _agentListener.Dispose();
        _httpServer.Dispose();

        try
        {
            if (Directory.Exists(_testTempDir))
            {
                Directory.Delete(_testTempDir, true);
            }
        }
        catch { }
    }
}
