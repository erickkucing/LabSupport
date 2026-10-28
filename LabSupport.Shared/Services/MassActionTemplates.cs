using System.Collections.Generic;
using LabSupport.Shared.Protocol;

namespace LabSupport.Shared.Services;

public enum MassScriptTemplateKey
{
    FlushDns,
    CheckDisk,
    KillFrozenBrowsers,
    SyncTime
}

public class MassScriptTemplateItem
{
    public MassScriptTemplateKey Key { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public ScriptType Type { get; set; } = ScriptType.PowerShell;
    public string ScriptContent { get; set; } = string.Empty;
}

public static class MassActionTemplates
{
    private static readonly Dictionary<MassScriptTemplateKey, MassScriptTemplateItem> _templates = new()
    {
        [MassScriptTemplateKey.FlushDns] = new MassScriptTemplateItem
        {
            Key = MassScriptTemplateKey.FlushDns,
            Title = "Flush DNS & Reset IP",
            Description = "Bersihkan DNS cache lokal dan minta alamat IP baru dari DHCP server.",
            Type = ScriptType.Batch,
            ScriptContent = "ipconfig /flushdns\r\nipconfig /renew"
        },
        [MassScriptTemplateKey.CheckDisk] = new MassScriptTemplateItem
        {
            Key = MassScriptTemplateKey.CheckDisk,
            Title = "Cek Kapasitas Disk (C:)",
            Description = "Cek ruang terpakai dan ruang bebas pada partisi sistem C:.",
            Type = ScriptType.PowerShell,
            ScriptContent = "Get-PSDrive C | Select-Object Name, @{Name='Used(GB)';Expression={[math]::Round($_.Used/1GB,2)}}, @{Name='Free(GB)';Expression={[math]::Round($_.Free/1GB,2)}} | Format-Table -AutoSize"
        },
        [MassScriptTemplateKey.KillFrozenBrowsers] = new MassScriptTemplateItem
        {
            Key = MassScriptTemplateKey.KillFrozenBrowsers,
            Title = "Tutup Browser Macet",
            Description = "Hentikan paksa proses browser Google Chrome dan Microsoft Edge yang hang.",
            Type = ScriptType.Batch,
            ScriptContent = "taskkill /F /IM chrome.exe /IM msedge.exe /T\r\necho Browser berhasil ditutup."
        },
        [MassScriptTemplateKey.SyncTime] = new MassScriptTemplateItem
        {
            Key = MassScriptTemplateKey.SyncTime,
            Title = "Sinkronisasi Waktu Windows",
            Description = "Sinkronisasi jam sistem ke Windows Time Service (NTP).",
            Type = ScriptType.Batch,
            ScriptContent = "w32tm /resync /nowait\r\necho Sinkronisasi waktu diminta."
        }
    };

    public static MassScriptTemplateItem GetTemplate(MassScriptTemplateKey key)
    {
        return _templates.TryGetValue(key, out var template)
            ? template
            : _templates[MassScriptTemplateKey.FlushDns];
    }

    public static IReadOnlyList<MassScriptTemplateItem> GetAllTemplates()
    {
        return new List<MassScriptTemplateItem>(_templates.Values);
    }
}
