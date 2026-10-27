namespace LabSupport.Shared.Models;

public class AppSettings
{
    public string Language { get; set; } = "id-ID";
    public int HttpPort { get; set; } = 8080;
    public int AgentPort { get; set; } = 5902;
    public int HeartbeatIntervalSeconds { get; set; } = 5;
    public string DatabasePath { get; set; } = "labsupport.db";
}
