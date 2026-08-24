using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NetworkAdminTool.Forms;
using NetworkAdminTool.Interfaces;
using NetworkAdminTool.Services;

namespace NetworkAdminTool
{
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            ApplicationConfiguration.Initialize();

            var host = CreateHostBuilder().Build();
            Application.Run(host.Services.GetRequiredService<MainForm>());
        }

        private static IHostBuilder CreateHostBuilder()
        {
            return Host.CreateDefaultBuilder()
                .ConfigureAppConfiguration((context, config) =>
                {
                    config.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);
                    config.AddEnvironmentVariables();
                })
                .ConfigureServices((context, services) =>
                {
                    services.AddSingleton<ILoggerService, LoggerService>();
                    services.AddSingleton<INetworkInfoService, NetworkInfoService>();
                    services.AddSingleton<INetworkScannerService, NetworkScannerService>();
                    services.AddSingleton<IPingService, PingService>();
                    services.AddSingleton<ISystemMonitorService, SystemMonitorService>();

                    services.AddTransient<MainForm>();
                    services.AddTransient<ScannerForm>();
                    services.AddTransient<PingForm>();
                    services.AddTransient<MonitorForm>();
                });
        }
    }
}
