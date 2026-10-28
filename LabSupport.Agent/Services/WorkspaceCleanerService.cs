using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using LabSupport.Shared.Protocol;

namespace LabSupport.Agent.Services;

public class WorkspaceCleanerService
{
    private const uint SHERB_NOCONFIRMATION = 0x00000001;
    private const uint SHERB_NOPROGRESSUI = 0x00000002;
    private const uint SHERB_NOSOUND = 0x00000004;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHEmptyRecycleBin(IntPtr hwnd, string? pszRootPath, uint dwFlags);

    public Task<CleanWorkspaceResult> CleanWorkspaceAsync(CleanWorkspacePayload? payload = null, CancellationToken token = default)
    {
        return Task.Run(() => CleanWorkspace(payload), token);
    }

    public CleanWorkspaceResult CleanWorkspace(CleanWorkspacePayload? payload = null)
    {
        payload ??= new CleanWorkspacePayload();
        int deletedCount = 0;
        long bytesFreed = 0;

        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        // 1. Clean Downloads
        if (payload.CleanDownloads)
        {
            string downloads = Path.Combine(userProfile, "Downloads");
            CleanDirectory(downloads, preserveShortcuts: false, ref deletedCount, ref bytesFreed);
        }

        // 2. Clean .ssh folder (privacy & credential hygiene)
        if (payload.CleanSsh)
        {
            string sshDir = Path.Combine(userProfile, ".ssh");
            CleanDirectory(sshDir, preserveShortcuts: false, ref deletedCount, ref bytesFreed);
        }

        // 3. Clean VirtualBox VMs folder (massive disk hogs)
        if (payload.CleanVirtualBoxVms)
        {
            string vmsDir = Path.Combine(userProfile, "VirtualBox VMs");
            CleanDirectory(vmsDir, preserveShortcuts: false, ref deletedCount, ref bytesFreed);
        }

        // 4. Clean Desktop non-shortcut files (preserve *.lnk)
        if (payload.CleanDesktopDocs)
        {
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            CleanDirectory(desktop, preserveShortcuts: true, ref deletedCount, ref bytesFreed);
        }

        // 5. Clean Temp directories
        if (payload.CleanTemp)
        {
            try
            {
                string tempDir = Path.GetTempPath();
                CleanDirectory(tempDir, preserveShortcuts: false, ref deletedCount, ref bytesFreed);
            }
            catch { }

            try
            {
                if (OperatingSystem.IsWindows())
                {
                    string winTemp = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp");
                    CleanDirectory(winTemp, preserveShortcuts: false, ref deletedCount, ref bytesFreed);
                }
            }
            catch { }
        }

        // 6. Empty Recycle Bin
        if (payload.CleanRecycleBin && OperatingSystem.IsWindows())
        {
            try
            {
                SHEmptyRecycleBin(IntPtr.Zero, null, SHERB_NOCONFIRMATION | SHERB_NOPROGRESSUI | SHERB_NOSOUND);
            }
            catch { }
        }

        double mbFreed = bytesFreed / (1024.0 * 1024.0);
        string summary = mbFreed >= 1024
            ? $"{deletedCount} file dibersihkan, membebaskan {mbFreed / 1024.0:F2} GB ruang disk."
            : $"{deletedCount} file dibersihkan, membebaskan {mbFreed:F1} MB ruang disk.";

        return new CleanWorkspaceResult
        {
            FilesDeletedCount = deletedCount,
            BytesFreed = bytesFreed,
            SummaryText = summary
        };
    }

    public static void CleanDirectory(string dirPath, bool preserveShortcuts, ref int deletedCount, ref long bytesFreed)
    {
        if (string.IsNullOrWhiteSpace(dirPath) || !Directory.Exists(dirPath))
        {
            return;
        }

        try
        {
            var di = new DirectoryInfo(dirPath);

            // Delete files
            foreach (var file in di.GetFiles())
            {
                try
                {
                    // If preserving shortcuts, skip *.lnk files
                    if (preserveShortcuts && file.Extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    long len = file.Length;
                    file.Attributes = FileAttributes.Normal;
                    file.Delete();
                    deletedCount++;
                    bytesFreed += len;
                }
                catch
                {
                    // Ignore locked or permission-restricted files
                }
            }

            // Delete subdirectories
            foreach (var sub in di.GetDirectories())
            {
                try
                {
                    // Calculate size before deleting
                    CleanDirectory(sub.FullName, preserveShortcuts: false, ref deletedCount, ref bytesFreed);
                    sub.Delete(true);
                }
                catch
                {
                    // Ignore locked folders
                }
            }
        }
        catch
        {
            // Ignore access denied to parent directory
        }
    }
}
