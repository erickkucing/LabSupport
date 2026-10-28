using System;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security;
using System.Text;
using System.Threading.Tasks;
using Xunit;
using LabSupport.Shared.Services;

namespace LabSupport.Tests;

public class FileDistributionServiceTests : IDisposable
{
    private readonly string _testTempDir;

    public FileDistributionServiceTests()
    {
        _testTempDir = Path.Combine(Path.GetTempPath(), $"ls_test_fds_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testTempDir);
    }

    [Fact]
    public void PrepareSession_SingleFile_SetsProperMetadataAndSha256()
    {
        string filePath = Path.Combine(_testTempDir, "document.pdf");
        File.WriteAllText(filePath, "PDF DUMMY CONTENT 123456789");

        using var session = FileDistributionService.CreateSession(filePath, false);

        Assert.False(session.IsDirectory);
        Assert.False(session.IsTemporary);
        Assert.Equal("document.pdf", session.FileName);
        Assert.True(session.FileSize > 0);
        Assert.False(string.IsNullOrWhiteSpace(session.Sha256Hash));
        Assert.True(FileDistributionService.VerifySha256(filePath, session.Sha256Hash));
    }

    [Fact]
    public void PrepareSession_Folder_PackagesToZipAndCleansUpOnDispose()
    {
        string folderPath = Path.Combine(_testTempDir, "SampleProject");
        Directory.CreateDirectory(folderPath);
        File.WriteAllText(Path.Combine(folderPath, "main.py"), "print('hello')");
        Directory.CreateDirectory(Path.Combine(folderPath, "sub"));
        File.WriteAllText(Path.Combine(folderPath, "sub", "config.json"), "{\"env\":\"test\"}");

        string tempZipPath;
        using (var session = FileDistributionService.CreateSession(folderPath, true))
        {
            Assert.True(session.IsDirectory);
            Assert.True(session.IsTemporary);
            Assert.Equal("SampleProject", session.FileName);
            Assert.True(File.Exists(session.FilePath));
            Assert.True(session.FileSize > 0);
            Assert.False(string.IsNullOrWhiteSpace(session.Sha256Hash));
            tempZipPath = session.FilePath;
        }

        // After Dispose, temporary zip must be cleanly deleted
        Assert.False(File.Exists(tempZipPath));
    }

    [Fact]
    public void PackageAndExtract_PreservesExactFolderHierarchyAndContent()
    {
        string srcDir = Path.Combine(_testTempDir, "SourceTree");
        Directory.CreateDirectory(srcDir);
        File.WriteAllText(Path.Combine(srcDir, "root.txt"), "Root content");
        string nestedDir = Path.Combine(srcDir, "level1", "level2");
        Directory.CreateDirectory(nestedDir);
        File.WriteAllText(Path.Combine(nestedDir, "deep.txt"), "Deep nested content");

        string zipPath = Path.Combine(_testTempDir, "tree.zip");
        FileDistributionService.PackageDirectoryToZip(srcDir, zipPath);

        string destDir = Path.Combine(_testTempDir, "ExtractedTree");
        FileDistributionService.ExtractDirectoryArchive(zipPath, destDir, overwrite: true);

        Assert.True(File.Exists(Path.Combine(destDir, "root.txt")));
        Assert.Equal("Root content", File.ReadAllText(Path.Combine(destDir, "root.txt")));
        Assert.True(File.Exists(Path.Combine(destDir, "level1", "level2", "deep.txt")));
        Assert.Equal("Deep nested content", File.ReadAllText(Path.Combine(destDir, "level1", "level2", "deep.txt")));
    }

    [Fact]
    public void ExtractDirectoryArchive_HandlesLeadingSlashAndOverwritesReadOnlyFile()
    {
        string zipPath = Path.Combine(_testTempDir, "leading_slash.zip");
        using (var zipStream = new FileStream(zipPath, FileMode.Create))
        using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("/nested/file.txt");
            using var writer = new StreamWriter(entry.Open());
            writer.Write("Leading slash content");
        }

        string destDir = Path.Combine(_testTempDir, "LeadingSlashExtracted");
        Directory.CreateDirectory(destDir);
        string existingFile = Path.Combine(destDir, "nested", "file.txt");
        Directory.CreateDirectory(Path.Combine(destDir, "nested"));
        File.WriteAllText(existingFile, "Old content");
        File.SetAttributes(existingFile, FileAttributes.ReadOnly);

        // Should cleanly overwrite even when destination file was read-only
        FileDistributionService.ExtractDirectoryArchive(zipPath, destDir, overwrite: true);

        Assert.True(File.Exists(existingFile));
        Assert.Equal("Leading slash content", File.ReadAllText(existingFile));
    }

    [Fact]
    public void ExtractDirectoryArchive_RejectsZipSlipPathTraversal()
    {
        string maliciousZipPath = Path.Combine(_testTempDir, "malicious.zip");

        // Manually create a zip with path traversal in entry name
        using (var zipStream = new FileStream(maliciousZipPath, FileMode.Create))
        using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("../../escaped.txt");
            using var writer = new StreamWriter(entry.Open());
            writer.Write("Hacked");
        }

        string targetDir = Path.Combine(_testTempDir, "SafeDestination");
        Directory.CreateDirectory(targetDir);

        Assert.Throws<SecurityException>(() =>
        {
            FileDistributionService.ExtractDirectoryArchive(maliciousZipPath, targetDir);
        });
    }

    [Fact]
    public void ComputeSha256_ValidatesCorrectlyAndDetectsTampering()
    {
        string filePath = Path.Combine(_testTempDir, "integrity_test.bin");
        File.WriteAllBytes(filePath, new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 });

        string hash = FileDistributionService.ComputeSha256(filePath);
        Assert.True(FileDistributionService.VerifySha256(filePath, hash));

        // Corrupt 1 byte
        File.WriteAllBytes(filePath, new byte[] { 1, 2, 3, 4, 99, 6, 7, 8, 9 });
        Assert.False(FileDistributionService.VerifySha256(filePath, hash));
    }

    [Fact]
    public void ResolveDestinationDirectory_HandlesStandardLocationsAndTraversals()
    {
        string desktop = FileDistributionService.ResolveDestinationDirectory("Desktop");
        Assert.False(string.IsNullOrWhiteSpace(desktop));

        string docs = FileDistributionService.ResolveDestinationDirectory("Documents");
        Assert.False(string.IsNullOrWhiteSpace(docs));

        string downloads = FileDistributionService.ResolveDestinationDirectory("Downloads");
        Assert.False(string.IsNullOrWhiteSpace(downloads));

        string withSub = FileDistributionService.ResolveDestinationDirectory("Desktop", "Tugas1");
        Assert.EndsWith("Tugas1", withSub);

        // Subfolder under Documents
        string docsWithSub = FileDistributionService.ResolveDestinationDirectory("Documents", "Modul01");
        Assert.EndsWith("Modul01", docsWithSub);
        Assert.Contains("Documents", docsWithSub);

        // Inline subfolder notation: Documents/Modul01
        string docsInline = FileDistributionService.ResolveDestinationDirectory("Documents/Modul01");
        Assert.EndsWith("Modul01", docsInline);
        Assert.Contains("Documents", docsInline);

        // Path traversal rejection
        Assert.Throws<ArgumentException>(() => FileDistributionService.ResolveDestinationDirectory("../etc"));
        Assert.Throws<ArgumentException>(() => FileDistributionService.ResolveDestinationDirectory("Desktop", "../evil"));
        Assert.Throws<ArgumentException>(() => FileDistributionService.ResolveDestinationDirectory("Desktop", "/etc"));
        Assert.Throws<ArgumentException>(() => FileDistributionService.ResolveDestinationDirectory("Desktop", "C:\\Windows"));
        Assert.Throws<SecurityException>(() => FileDistributionService.ResolveDestinationDirectory("/etc"));
    }

    [Fact]
    public async Task HttpFileServer_StreamingEndpoint_DirectDiskToSocket()
    {
        int port = 28090;
        using var server = new HttpFileServer(port);
        server.Start();

        string sampleFile = Path.Combine(_testTempDir, "stream_test.dat");
        byte[] originalBytes = Encoding.UTF8.GetBytes("DISK_TO_SOCKET_HIGH_PERFORMANCE_STREAM_DATA");
        await File.WriteAllBytesAsync(sampleFile, originalBytes);

        using var session = FileDistributionService.CreateSession(sampleFile, false);
        server.RegisterFileSession(session);

        using var client = new HttpClient();
        string url = $"http://127.0.0.1:{port}/files/download/{session.Token}";

        using var response = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(session.FileSize, response.Content.Headers.ContentLength);
        Assert.Equal(session.Sha256Hash, response.Headers.GetValues("X-Checksum-SHA256").GetEnumerator().MoveNext() ? response.Headers.GetValues("X-Checksum-SHA256").AsList()[0] : null);

        byte[] downloadedBytes = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(originalBytes, downloadedBytes);
        Assert.True(session.BytesTransferred >= originalBytes.Length);
    }

    [Fact]
    public async Task HttpFileServer_InvalidOrTraversingToken_Returns404Or400()
    {
        int port = 28091;
        using var server = new HttpFileServer(port);
        server.Start();

        using var client = new HttpClient();

        // 1. Non-existent token -> 404
        var resp1 = await client.GetAsync($"http://127.0.0.1:{port}/files/download/nonexistenttoken123");
        Assert.Equal(HttpStatusCode.NotFound, resp1.StatusCode);

        // 2. Traversal in URL -> 400 Bad Request
        var resp2 = await client.GetAsync($"http://127.0.0.1:{port}/files/download/..%2f..%2fetc%2fpasswd");
        Assert.True(resp2.StatusCode == HttpStatusCode.BadRequest || resp2.StatusCode == HttpStatusCode.NotFound);
    }

    public void Dispose()
    {
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

file static class Extensions
{
    public static System.Collections.Generic.List<T> AsList<T>(this System.Collections.Generic.IEnumerable<T> source) =>
        new System.Collections.Generic.List<T>(source);
}
