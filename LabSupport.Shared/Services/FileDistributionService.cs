using System;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace LabSupport.Shared.Services;

public class FileTransferSession : IDisposable
{
    private long _bytesTransferred;

    public string Token { get; set; } = Guid.NewGuid().ToString("N");
    public string FilePath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public string Sha256Hash { get; set; } = string.Empty;
    public bool IsDirectory { get; set; }
    public bool IsTemporary { get; set; }
    public string ContentType { get; set; } = "application/octet-stream";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public long BytesTransferred
    {
        get => Interlocked.Read(ref _bytesTransferred);
        set => Interlocked.Exchange(ref _bytesTransferred, value);
    }

    public void AddBytesTransferred(long bytes)
    {
        Interlocked.Add(ref _bytesTransferred, bytes);
    }

    public void Dispose()
    {
        if (IsTemporary && !string.IsNullOrWhiteSpace(FilePath))
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    File.Delete(FilePath);
                }
            }
            catch { }
        }
    }
}

public class FileDistributionService
{
    private const int BufferSize = 64 * 1024; // 64 KB streaming buffer (Prevents Large Object Heap allocations)

    public static FileTransferSession CreateSession(string sourcePath, bool? isDirectory = null)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            throw new ArgumentException("Source path cannot be empty.", nameof(sourcePath));
        }

        string fullPath = Path.GetFullPath(sourcePath);
        bool isDir = isDirectory ?? Directory.Exists(fullPath);

        if (isDir)
        {
            if (!Directory.Exists(fullPath))
            {
                throw new DirectoryNotFoundException($"Directory not found: {fullPath}");
            }

            string dirName = Path.GetFileName(fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (string.IsNullOrWhiteSpace(dirName))
            {
                dirName = "DistributedFolder";
            }

            string tempZipPath = Path.Combine(Path.GetTempPath(), $"ls_dist_{Guid.NewGuid():N}.zip");
            PackageDirectoryToZip(fullPath, tempZipPath);

            var fileInfo = new FileInfo(tempZipPath);
            string sha256 = ComputeSha256(tempZipPath);

            return new FileTransferSession
            {
                FilePath = tempZipPath,
                FileName = dirName,
                FileSize = fileInfo.Length,
                Sha256Hash = sha256,
                IsDirectory = true,
                IsTemporary = true
            };
        }
        else
        {
            if (!File.Exists(fullPath))
            {
                throw new FileNotFoundException($"File not found: {fullPath}");
            }

            var fileInfo = new FileInfo(fullPath);
            string fileName = Path.GetFileName(fullPath);
            string sha256 = ComputeSha256(fullPath);

            return new FileTransferSession
            {
                FilePath = fullPath,
                FileName = fileName,
                FileSize = fileInfo.Length,
                Sha256Hash = sha256,
                IsDirectory = false,
                IsTemporary = false
            };
        }
    }

    public static void PackageDirectoryToZip(string sourceDir, string targetZipPath)
    {
        if (File.Exists(targetZipPath))
        {
            File.Delete(targetZipPath);
        }

        ZipFile.CreateFromDirectory(sourceDir, targetZipPath, CompressionLevel.Fastest, includeBaseDirectory: false);
    }

    public static void ExtractDirectoryArchive(string zipFilePath, string destinationDirectory, bool overwrite = true)
    {
        if (!File.Exists(zipFilePath))
        {
            throw new FileNotFoundException($"Archive not found: {zipFilePath}");
        }

        string targetDir = Path.GetFullPath(destinationDirectory);
        if (!Directory.Exists(targetDir))
        {
            Directory.CreateDirectory(targetDir);
        }

        string targetDirWithSeparator = targetDir.EndsWith(Path.DirectorySeparatorChar.ToString())
            ? targetDir
            : targetDir + Path.DirectorySeparatorChar;

        using var archive = ZipFile.OpenRead(zipFilePath);
        foreach (var entry in archive.Entries)
        {
            // Normalize slashes and trim leading slashes
            string cleanFullName = entry.FullName.TrimStart('/', '\\');
            string entryRelPath = cleanFullName.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
            string destPath = Path.GetFullPath(Path.Combine(targetDir, entryRelPath));

            // Defend against Zip Slip (Directory Traversal Attack)
            if (!destPath.StartsWith(targetDirWithSeparator, StringComparison.OrdinalIgnoreCase) &&
                !destPath.Equals(targetDir, StringComparison.OrdinalIgnoreCase))
            {
                throw new System.Security.SecurityException($"Zip slip directory traversal detected in entry: {entry.FullName}");
            }

            bool isDirectoryEntry = entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\') || string.IsNullOrEmpty(entry.Name);
            if (isDirectoryEntry)
            {
                // Directory entry
                if (!Directory.Exists(destPath))
                {
                    Directory.CreateDirectory(destPath);
                }
            }
            else
            {
                // Ensure parent directory exists
                string? parentDir = Path.GetDirectoryName(destPath);
                if (!string.IsNullOrEmpty(parentDir) && !Directory.Exists(parentDir))
                {
                    Directory.CreateDirectory(parentDir);
                }

                if (File.Exists(destPath))
                {
                    if (overwrite)
                    {
                        try { File.SetAttributes(destPath, FileAttributes.Normal); } catch { }
                        File.Delete(destPath);
                    }
                    else
                    {
                        throw new IOException($"Destination file already exists and overwrite is disabled: {destPath}");
                    }
                }

                entry.ExtractToFile(destPath, overwrite: overwrite);
            }
        }
    }

    public static string ComputeSha256(string filePath)
    {
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, useAsync: false);
        using var sha256 = SHA256.Create();
        byte[] hash = sha256.ComputeHash(stream);
        return Convert.ToHexString(hash);
    }

    public static async Task<string> ComputeSha256Async(string filePath, CancellationToken cancellationToken = default)
    {
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, useAsync: true);
        using var sha256 = SHA256.Create();
        byte[] hash = await sha256.ComputeHashAsync(stream, cancellationToken);
        return Convert.ToHexString(hash);
    }

    public static bool VerifySha256(string filePath, string expectedHash)
    {
        if (!File.Exists(filePath) || string.IsNullOrWhiteSpace(expectedHash))
        {
            return false;
        }

        string actual = ComputeSha256(filePath);
        return string.Equals(actual, expectedHash, StringComparison.OrdinalIgnoreCase);
    }

    public static string ResolveDestinationDirectory(string? targetDirOption, string? customSubfolder = null)
    {
        string desktopDir = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        if (string.IsNullOrWhiteSpace(desktopDir))
        {
            desktopDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Desktop");
        }

        string userProf = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string myDocs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (string.IsNullOrWhiteSpace(myDocs))
        {
            myDocs = !string.IsNullOrWhiteSpace(userProf)
                ? Path.Combine(userProf, "Documents")
                : Path.Combine(AppContext.BaseDirectory, "Documents");
        }

        string downloadsDir = !string.IsNullOrWhiteSpace(userProf)
            ? Path.Combine(userProf, "Downloads")
            : Path.Combine(AppContext.BaseDirectory, "Downloads");

        string option = targetDirOption?.Trim() ?? string.Empty;
        string baseDir;
        string? inlineSub = null;

        if (option.StartsWith("Documents/", StringComparison.OrdinalIgnoreCase) ||
            option.StartsWith("Documents\\", StringComparison.OrdinalIgnoreCase) ||
            option.Equals("Documents", StringComparison.OrdinalIgnoreCase))
        {
            baseDir = myDocs;
            if (option.Length > "Documents".Length)
            {
                inlineSub = option.Substring("Documents".Length + 1);
            }
        }
        else if (option.StartsWith("Downloads/", StringComparison.OrdinalIgnoreCase) ||
                 option.StartsWith("Downloads\\", StringComparison.OrdinalIgnoreCase) ||
                 option.Equals("Downloads", StringComparison.OrdinalIgnoreCase))
        {
            baseDir = downloadsDir;
            if (option.Length > "Downloads".Length)
            {
                inlineSub = option.Substring("Downloads".Length + 1);
            }
        }
        else if (option.StartsWith("Desktop/", StringComparison.OrdinalIgnoreCase) ||
                 option.StartsWith("Desktop\\", StringComparison.OrdinalIgnoreCase) ||
                 option.Equals("Desktop", StringComparison.OrdinalIgnoreCase) ||
                 string.IsNullOrWhiteSpace(option))
        {
            baseDir = desktopDir;
            if (option.Length > "Desktop".Length)
            {
                inlineSub = option.Substring("Desktop".Length + 1);
            }
        }
        else
        {
            if (option.Contains("..") || option.Contains('%'))
            {
                throw new ArgumentException("Path traversal characters not allowed in target directory.", nameof(targetDirOption));
            }

            if (Path.IsPathRooted(option))
            {
                string tempDir = Path.GetTempPath();
                string fullTarget = Path.GetFullPath(option);
                if (fullTarget.StartsWith(userProf, StringComparison.OrdinalIgnoreCase) ||
                    fullTarget.StartsWith(desktopDir, StringComparison.OrdinalIgnoreCase) ||
                    fullTarget.StartsWith(myDocs, StringComparison.OrdinalIgnoreCase) ||
                    fullTarget.StartsWith(downloadsDir, StringComparison.OrdinalIgnoreCase) ||
                    fullTarget.StartsWith(tempDir, StringComparison.OrdinalIgnoreCase))
                {
                    baseDir = fullTarget;
                }
                else
                {
                    throw new System.Security.SecurityException($"Rooted destination directory outside allowed user areas is prohibited: {option}");
                }
            }
            else
            {
                baseDir = Path.GetFullPath(Path.Combine(desktopDir, option));
            }
        }

        // Apply any inline subfolder from option and customSubfolder
        var subParts = new System.Collections.Generic.List<string>();
        if (!string.IsNullOrWhiteSpace(inlineSub)) subParts.Add(inlineSub);
        if (!string.IsNullOrWhiteSpace(customSubfolder)) subParts.Add(customSubfolder);

        if (subParts.Count > 0)
        {
            string combinedSub = string.Join(Path.DirectorySeparatorChar.ToString(), subParts);
            string trimmedSub = combinedSub.Trim();

            if (trimmedSub.Contains("..") || trimmedSub.Contains('%') || trimmedSub.IndexOfAny(Path.GetInvalidPathChars()) >= 0 ||
                Path.IsPathRooted(trimmedSub) || trimmedSub.Contains(':'))
            {
                throw new ArgumentException("Invalid custom subfolder name.", nameof(customSubfolder));
            }

            string baseDirWithSep = baseDir.EndsWith(Path.DirectorySeparatorChar.ToString())
                ? baseDir
                : baseDir + Path.DirectorySeparatorChar;

            string targetCombined = Path.GetFullPath(Path.Combine(baseDir, trimmedSub.TrimStart('/', '\\')));
            if (!targetCombined.StartsWith(baseDirWithSep, StringComparison.OrdinalIgnoreCase) &&
                !targetCombined.Equals(baseDir, StringComparison.OrdinalIgnoreCase))
            {
                throw new System.Security.SecurityException("Custom subfolder attempts to escape target base directory.");
            }

            baseDir = targetCombined;
        }

        return baseDir;
    }
}
