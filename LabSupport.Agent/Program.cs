using System;
using System.IO;
using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace LabSupport.Agent;

public class Program
{
    public static void Main(string[] args)
    {
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
