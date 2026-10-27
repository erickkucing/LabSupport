using System;
using System.Threading.Tasks;
using LabSupport.Shared.Protocol;

namespace LabSupport.Shared.Services;

public class WolService
{
    private readonly DatabaseService? _databaseService;

    public WolService(DatabaseService? databaseService = null)
    {
        _databaseService = databaseService;
    }

    public async Task<bool> WakeComputerAsync(string hostname, string macAddress)
    {
        try
        {
            await WolHelper.SendMagicPacketAsync(macAddress);
            if (_databaseService != null)
            {
                await _databaseService.LogActivityAsync(hostname, "Wake-on-LAN", $"Sent magic packet to MAC {macAddress}", true);
            }
            return true;
        }
        catch (Exception ex)
        {
            if (_databaseService != null)
            {
                await _databaseService.LogActivityAsync(hostname, "Wake-on-LAN", $"Error: {ex.Message}", false);
            }
            return false;
        }
    }
}
