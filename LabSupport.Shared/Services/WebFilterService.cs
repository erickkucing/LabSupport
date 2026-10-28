using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using LabSupport.Shared.Protocol;

namespace LabSupport.Shared.Services;

public class WebFilterService
{
    public const string BeginMarker = "# BEGIN LABSUPPORT WEB FILTER";
    public const string EndMarker = "# END LABSUPPORT WEB FILTER";

    [DllImport("dnsapi.dll", EntryPoint = "DnsFlushResolverCache")]
    private static extern int DnsFlushResolverCache();

    private string _hostsFilePath;
    private WebFilterMode _currentMode = WebFilterMode.Disabled;
    private readonly List<string> _currentDomains = new();
    private readonly FirewallBlockerService _firewallBlocker;

    public string HostsFilePath
    {
        get => _hostsFilePath;
        set => _hostsFilePath = value;
    }

    public WebFilterMode CurrentMode => _currentMode;
    public IReadOnlyList<string> CurrentDomains => _currentDomains.AsReadOnly();
    public bool IsActive => _currentMode != WebFilterMode.Disabled;

    public static readonly string[] DefaultGamingDomains =
    {
        "roblox.com",
        "steamcommunity.com",
        "steampowered.com",
        "epicgames.com",
        "riotgames.com",
        "battle.net",
        "poki.com"
    };

    public static readonly string[] DefaultSocialDomains =
    {
        "tiktok.com",
        "youtube.com",
        "instagram.com",
        "facebook.com",
        "twitter.com",
        "x.com",
        "discord.com",
        "spotify.com",
        "netflix.com",
        "twitch.tv"
    };

    public WebFilterService(string? hostsFilePath = null, FirewallBlockerService? firewallBlocker = null)
    {
        if (!string.IsNullOrWhiteSpace(hostsFilePath))
        {
            _hostsFilePath = hostsFilePath;
        }
        else if (OperatingSystem.IsWindows())
        {
            string sysDir = Environment.GetFolderPath(Environment.SpecialFolder.System);
            _hostsFilePath = Path.Combine(sysDir, @"drivers\etc\hosts");
        }
        else
        {
            _hostsFilePath = "/etc/hosts";
        }

        _firewallBlocker = firewallBlocker ?? new FirewallBlockerService();
    }

    public bool ApplyFilter(WebFilterPayload payload)
    {
        if (payload == null) return ClearFilter();
        return ApplyFilter(payload.Mode, payload.Domains);
    }

    public bool ApplyFilter(WebFilterMode mode, IEnumerable<string> domains)
    {
        var domainList = domains
            .Select(d => d.Trim().ToLowerInvariant())
            .Where(d => !string.IsNullOrWhiteSpace(d))
            .Distinct()
            .ToList();

        if (mode == WebFilterMode.Disabled || domainList.Count == 0)
        {
            return ClearFilter();
        }

        _currentMode = mode;
        _currentDomains.Clear();
        _currentDomains.AddRange(domainList);

        try
        {
            string existingContent = File.Exists(_hostsFilePath) ? File.ReadAllText(_hostsFilePath, Encoding.UTF8) : string.Empty;
            string cleanContent = StripExistingFilterBlock(existingContent);

            var sb = new StringBuilder();
            sb.Append(cleanContent.TrimEnd());
            sb.AppendLine();
            sb.AppendLine();
            sb.AppendLine(BeginMarker);

            if (mode == WebFilterMode.Blacklist)
            {
                foreach (var domain in domainList)
                {
                    string clean = SanitizeDomain(domain);
                    if (string.IsNullOrWhiteSpace(clean)) continue;

                    sb.AppendLine($"0.0.0.0 {clean}");
                    if (!clean.StartsWith("www."))
                    {
                        sb.AppendLine($"0.0.0.0 www.{clean}");
                    }
                }
            }
            else if (mode == WebFilterMode.Whitelist)
            {
                // In whitelist mode, block common public categories while allowing specific school domains
                sb.AppendLine("# Whitelist Mode Active");
                foreach (var domain in domainList)
                {
                    string clean = SanitizeDomain(domain);
                    if (string.IsNullOrWhiteSpace(clean)) continue;
                    sb.AppendLine($"# ALLOW {clean}");
                }
            }

            sb.AppendLine(EndMarker);

            File.WriteAllText(_hostsFilePath, sb.ToString(), Encoding.UTF8);
            FlushDns();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public bool ClearFilter()
    {
        _currentMode = WebFilterMode.Disabled;
        _currentDomains.Clear();

        try
        {
            if (File.Exists(_hostsFilePath))
            {
                string existingContent = File.ReadAllText(_hostsFilePath, Encoding.UTF8);
                string cleanContent = StripExistingFilterBlock(existingContent);
                File.WriteAllText(_hostsFilePath, cleanContent, Encoding.UTF8);
            }

            FlushDns();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static string StripExistingFilterBlock(string content)
    {
        if (string.IsNullOrEmpty(content)) return string.Empty;

        int startIndex = content.IndexOf(BeginMarker, StringComparison.OrdinalIgnoreCase);
        while (startIndex >= 0)
        {
            int endIndex = content.IndexOf(EndMarker, startIndex, StringComparison.OrdinalIgnoreCase);
            if (endIndex >= 0)
            {
                endIndex += EndMarker.Length;
                // consume newline after end marker if present
                if (endIndex < content.Length && content[endIndex] == '\r') endIndex++;
                if (endIndex < content.Length && content[endIndex] == '\n') endIndex++;

                content = content.Remove(startIndex, endIndex - startIndex);
            }
            else
            {
                content = content.Remove(startIndex);
                break;
            }

            startIndex = content.IndexOf(BeginMarker, StringComparison.OrdinalIgnoreCase);
        }

        return content;
    }

    public static string SanitizeDomain(string domain)
    {
        if (string.IsNullOrWhiteSpace(domain)) return string.Empty;

        string clean = domain.Trim().ToLowerInvariant();
        if (clean.StartsWith("http://")) clean = clean[7..];
        if (clean.StartsWith("https://")) clean = clean[8..];
        int slashIdx = clean.IndexOf('/');
        if (slashIdx >= 0) clean = clean[..slashIdx];
        int colonIdx = clean.IndexOf(':');
        if (colonIdx >= 0) clean = clean[..colonIdx];
        if (clean.StartsWith("www.")) clean = clean[4..];

        return clean.Trim();
    }

    public void FlushDns()
    {
        if (OperatingSystem.IsWindows())
        {
            try
            {
                DnsFlushResolverCache();
            }
            catch { }

            try
            {
                var psi = new ProcessStartInfo("ipconfig", "/flushdns")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
                using var p = Process.Start(psi);
                p?.WaitForExit(2000);
            }
            catch { }
        }
    }
}
