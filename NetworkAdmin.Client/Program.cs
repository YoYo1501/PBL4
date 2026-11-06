using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NetworkAdmin.Client.Application.State;
using NetworkAdmin.Client.Infrastructure.Configuration;
using NetworkAdmin.Client.Infrastructure.Networking;
using NetworkAdmin.Client.Application.Interfaces;
using NetworkAdmin.Client.Infrastructure.Logging;
using NetworkAdmin.Client.Infrastructure.Monitoring;
using NetworkAdmin.Client.Infrastructure.Runtime;
using NetworkAdmin.Client.Infrastructure.Scanning;
using NetworkAdmin.Client.UI.Services;
using NetworkAdmin.Client.Application.Presentation;
using NetworkAdmin.Client.UI.Forms;

namespace NetworkAdmin.Client;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        var standalone = args.Contains("--standalone", StringComparer.OrdinalIgnoreCase);
        ClientOptions options;
        try
        {
            options = standalone ? new ClientOptions() : ClientOptions.Load(args);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Client configuration failed: {ex.Message}", "NetworkAdmin Client",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
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
                services.AddSingleton<ScreenShareUi>();
                services.AddSingleton<ClientChatUi>();
                services.AddSingleton<DashboardPresenter>();
                services.AddTransient<ScannerPresenter>();
                services.AddSingleton<MainForm>(provider => new MainForm(provider,
                    provider.GetRequiredService<DashboardPresenter>(), provider.GetRequiredService<ScreenShareUi>(),
                    provider.GetRequiredService<ClientChatUi>()));
                services.AddTransient<ScannerForm>(provider => new ScannerForm(provider.GetRequiredService<ScannerPresenter>()));
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
                    provider.GetRequiredService<ISystemMonitorService>(),
                    provider.GetRequiredService<ScreenShareUi>(),
                    chatUi: provider.GetRequiredService<ClientChatUi>()));
            })
            .Build();

        // Keep Application.Run on the STA entry thread even if host startup is asynchronous.
        host.StartAsync().GetAwaiter().GetResult();
        var mainForm = host.Services.GetRequiredService<MainForm>();
        var runtime = host.Services.GetRequiredService<ClientRuntime>();
        if (!standalone)
        {
            // Start the network reader only after WinForms has created the main
            // window handle. ScreenShareUi posts consent dialogs through this
            // handle; starting earlier can lose the first cross-machine request.
            mainForm.Shown += (_, _) => runtime.Start();
        }
        System.Windows.Forms.Application.Run(mainForm);
        runtime.StopAsync().GetAwaiter().GetResult();
        host.StopAsync().GetAwaiter().GetResult();
    }
}
