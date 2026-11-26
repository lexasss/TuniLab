using GrpcClients;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Windows;

namespace Dashboard;

public partial class App : Application
{
    public static string Name { get; } = "Dashboard";

    IHost? _host;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var builder = Host.CreateApplicationBuilder();

        ConfigureServices(builder.Services, builder.Configuration);

        _host = builder.Build();
        await _host.StartAsync();

        if (!await CheckServerVersion(_host.Services))
            return;

        var mainWindow = _host.Services.GetRequiredService<MainWindow>();
        MainWindow = mainWindow;
        mainWindow.Show();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        await CleanUp();
        base.OnExit(e);
    }

    private static void ConfigureServices(
        IServiceCollection services,
        ConfigurationManager config)
    {
        config.AddJsonFile(
            "appSettings.json",
            optional: false,
            reloadOnChange: false);

        services.Configure<AppSettings>(config);

        services.AddSingleton<ServerClient>();

        services.AddSingleton<LeapMotionClient>();
        services.AddSingleton<SmartEyeClient>();
        services.AddSingleton<SoundPlayerClient>();
        services.AddSingleton<ScreenClient>();
        services.AddSingleton<PointingClient>();
        services.AddSingleton<TensionRClient>();
        services.AddSingleton<StreamDeckClient>();
        services.AddSingleton<DrivingClient>();
        services.AddSingleton<TobiiEyeXClient>();
        services.AddSingleton<CameraClient>();
        services.AddSingleton<CarlaClient>();

        services.AddTransient<LeapMotionViewModel>();
        services.AddTransient<SmartEyeViewModel>();
        services.AddTransient<SoundPlayerViewModel>();
        services.AddTransient<ScreenViewModel>();
        services.AddTransient<PointingViewModel>();
        services.AddTransient<TensionRViewModel>();
        services.AddTransient<StreamDeckViewModel>();
        services.AddTransient<DrivingViewModel>();
        services.AddTransient<TobiiEyeXViewModel>();
        services.AddTransient<CameraViewModel>();
        services.AddTransient<CarlaViewModel>();

        services.AddTransient<MainViewModel>();
        services.AddTransient<MainWindow>();
    }

    private async Task<bool> CheckServerVersion(IServiceProvider services)
    {
        try
        {
            using var server = services.GetRequiredService<ServerClient>();
            if (server.CoreVersion != Common.Constants.CORE_VERSION)
            {
                throw new Exception(
                    "Cannot run the app, as it uses another core version than the server:\n" +
                    $"'{Common.Constants.CORE_VERSION}' and '{server.CoreVersion}', correspondingly");
            }
            else if (server.CoreBuildTime != Core.BuildInfo.BuildTime)
            {
                if (MessageBox.Show(
                        "Core build time differs from the one used by the server,\n" +
                        "The functionality may not match, \n" +
                        "causing either the server or the app to crash\n\n" +
                        $"Do you want to continue?",
                        Name,
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Exclamation)
                    == MessageBoxResult.No)
                {
                    await CleanUp();
                    Shutdown();
                    return false;
                }
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, Name, MessageBoxButton.OK, MessageBoxImage.Error);

            await CleanUp();
            Shutdown();

            return false;
        }

        return true;
    }

    private async Task CleanUp()
    {
        if (_host is not null)
        {
            await _host.StopAsync();
            _host.Dispose();
            _host = null;
        }
    }
}
