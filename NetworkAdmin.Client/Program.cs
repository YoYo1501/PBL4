using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NetworkAdmin.Client.Services;
using NetworkAdminTool.Forms;
using NetworkAdminTool.Interfaces;
using NetworkAdminTool.Services;

namespace NetworkAdmin.Client;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        var standalone = args.Contains("--standalone", StringComparer.OrdinalIgnoreCase);
        var options = ClientOptions.Load(args);
        using var host = Host.CreateDefaultBuilder(args)
            .ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json"), optional: true, reloadOnChange: true);
                configuration.AddEnvironmentVariables();
            })
            .ConfigureServices((context, services) =>
            {
                services.AddSingleton<IConfiguration>(context.Configuration);
                services.AddSingleton<ILoggerService>(_ => new LoggerService(Path.Combine(AppContext.BaseDirectory, "Logs")));
                services.AddSingleton<INetworkInfoService, NetworkInfoService>();
                services.AddSingleton<IPingService, PingService>();
                services.AddSingleton<INetworkScannerService>(provider => new NetworkScannerService(
                    provider.GetRequiredService<IPingService>(),
                    provider.GetRequiredService<INetworkInfoService>()));
                services.AddSingleton<ISystemMonitorService, SystemMonitorService>();
                services.AddSingleton<NetworkDashboardState>();
                services.AddSingleton<MainForm>();
                services.AddTransient<ScannerForm>();
                services.AddTransient<PingForm>();
                services.AddTransient<MonitorForm>();
                services.AddTransient<NetworkInfoForm>();
                services.AddTransient<AlertsForm>();
                services.AddTransient<LogsForm>();
                services.AddTransient<SettingsForm>();
                services.AddTransient<AboutForm>();
                services.AddSingleton<ClientRuntime>(provider => new ClientRuntime(
                    options,
                    provider.GetRequiredService<INetworkInfoService>(),
                    provider.GetRequiredService<IPingService>(),
                    provider.GetRequiredService<INetworkScannerService>(),
                    provider.GetRequiredService<ISystemMonitorService>()));
            })
            .Build();

        // Keep Application.Run on the STA entry thread even if host startup is asynchronous.
        host.StartAsync().GetAwaiter().GetResult();
        var runtime = host.Services.GetRequiredService<ClientRuntime>();
        if (!standalone) runtime.Start();
        Application.Run(host.Services.GetRequiredService<MainForm>());
        runtime.StopAsync().GetAwaiter().GetResult();
        host.StopAsync().GetAwaiter().GetResult();
    }
}
