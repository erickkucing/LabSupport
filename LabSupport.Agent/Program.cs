using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using LabSupport.Agent.Services;

namespace LabSupport.Agent;

public class Program
{
    public static void Main(string[] args)
    {
        if (args.Any(a => a.Equals("--uninstall", StringComparison.OrdinalIgnoreCase) || a.Equals("-uninstall", StringComparison.OrdinalIgnoreCase)))
        {
            Console.WriteLine("Executing LabSupport Agent self-uninstall...");
            AgentUninstallerService.ExecuteDirectUninstallAsync().GetAwaiter().GetResult();
            return;
        }

        var builder = Host.CreateDefaultBuilder(args);

        // Allow running as a Windows Service if registered in SCM
        if (OperatingSystem.IsWindows())
        {
            builder.UseWindowsService(options =>
            {
                options.ServiceName = "LabSupportAgent";
            });
        }

        string basePath = AppContext.BaseDirectory;
        Directory.SetCurrentDirectory(basePath);

        builder.ConfigureAppConfiguration((hostingContext, config) =>
        {
            config.SetBasePath(basePath);
            config.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);
            config.AddCommandLine(args);
            config.AddEnvironmentVariables(prefix: "LABSUPPORT_");
        });

        builder.ConfigureServices((hostContext, services) =>
        {
            services.AddHostedService<Worker>();
        });

        var host = builder.Build();
        host.Run();
    }
}
