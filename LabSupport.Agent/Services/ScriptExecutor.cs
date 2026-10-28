using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LabSupport.Shared.Protocol;

namespace LabSupport.Agent.Services;

public class ScriptExecutor
{
    public virtual async Task<(bool Success, string Output, string Error, long ExecutionTimeMs)> ExecuteAsync(
        ScriptPayload payload,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        string tempFile = string.Empty;

        try
        {
            bool isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
            var utf8NoBom = new UTF8Encoding(false);
            string scriptContent = payload.ScriptContent ?? string.Empty;

            var psi = new ProcessStartInfo
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            if (payload.Type == ScriptType.PowerShell)
            {
                tempFile = Path.Combine(Path.GetTempPath(), $"lab_script_{Guid.NewGuid():N}.ps1");
                await File.WriteAllTextAsync(tempFile, scriptContent, utf8NoBom, cancellationToken);

                if (isWindows)
                {
                    psi.FileName = "powershell.exe";
                    psi.ArgumentList.Add("-NoProfile");
                    psi.ArgumentList.Add("-NonInteractive");
                    psi.ArgumentList.Add("-ExecutionPolicy");
                    psi.ArgumentList.Add("Bypass");
                    psi.ArgumentList.Add("-File");
                    psi.ArgumentList.Add(tempFile);
                }
                else
                {
                    psi.FileName = "pwsh";
                    psi.ArgumentList.Add("-NoProfile");
                    psi.ArgumentList.Add("-NonInteractive");
                    psi.ArgumentList.Add("-File");
                    psi.ArgumentList.Add(tempFile);
                }
            }
            else // Batch
            {
                if (isWindows)
                {
                    tempFile = Path.Combine(Path.GetTempPath(), $"lab_script_{Guid.NewGuid():N}.bat");
                    await File.WriteAllTextAsync(tempFile, scriptContent, Encoding.Default, cancellationToken);
                    psi.FileName = "cmd.exe";
                    // Explicit double quoting required by cmd.exe /c for paths with spaces
                    psi.Arguments = $"/c \"\"{tempFile}\"\"";
                }
                else
                {
                    tempFile = Path.Combine(Path.GetTempPath(), $"lab_script_{Guid.NewGuid():N}.sh");
                    await File.WriteAllTextAsync(tempFile, scriptContent, utf8NoBom, cancellationToken);
                    psi.FileName = "sh";
                    psi.ArgumentList.Add(tempFile);
                }
            }

            using var process = new Process { StartInfo = psi };
            process.Start();

            int timeoutSeconds = Math.Clamp(payload.TimeoutSeconds <= 0 ? 30 : payload.TimeoutSeconds, 1, 3600);
            int timeoutMs = timeoutSeconds * 1000;
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(timeoutMs);

            try
            {
                var stdoutTask = process.StandardOutput.ReadToEndAsync(cts.Token);
                var stderrTask = process.StandardError.ReadToEndAsync(cts.Token);

                await process.WaitForExitAsync(cts.Token);

                string output = await stdoutTask;
                string error = await stderrTask;

                sw.Stop();
                bool success = process.ExitCode == 0;
                return (success, output.TrimEnd(), error.TrimEnd(), sw.ElapsedMilliseconds);
            }
            catch (OperationCanceledException)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch { }

                sw.Stop();
                return (false, string.Empty, $"Script execution timed out after {payload.TimeoutSeconds}s.", sw.ElapsedMilliseconds);
            }
        }
        catch (Exception ex)
        {
            sw.Stop();
            return (false, string.Empty, ex.Message, sw.ElapsedMilliseconds);
        }
        finally
        {
            if (!string.IsNullOrEmpty(tempFile) && File.Exists(tempFile))
            {
                try { File.Delete(tempFile); } catch { }
            }
        }
    }
}
