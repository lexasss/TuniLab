using BeltsBrakes.Controllers;
using GrpcClients;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;

namespace BeltsBrakes;

internal class Program
{
    static ILogger<Program>? _logger;

    static async Task Main(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);

        builder.Logging.AddFilter("Microsoft.Hosting.Lifetime", LogLevel.None);
        builder.Logging.AddConsoleFormatter<PlainConsoleFormatter, ConsoleFormatterOptions>();
        builder.Logging.AddConsole(options =>
        {
            options.FormatterName = "plain";
        });

        ConfigureServices(builder.Services, builder.Configuration);

        var host = builder.Build();
        await host.StartAsync();

        _logger = host.Services.GetRequiredService<ILogger<Program>>();

        if (!await CheckServerVersion(host, _logger))
        {
            await host.StopAsync();
            return;
        }

        Console.WriteLine("Belts & Brakes is running");
        Console.WriteLine("\nRun CARLA and start driving.");
        Console.WriteLine("Effects:");
        Console.WriteLine(" - belt is tighten on sudden brakes");
        Console.WriteLine(" - a sound is played upon pressing steering button");
        Console.WriteLine("     middle top-right: car horn");
        Console.WriteLine(" - a sound is played upon pressing a StreamDeck button:");
        Console.WriteLine("     blue: around the room");
        Console.WriteLine("     pink: around the seat");
        Console.WriteLine(" - the following sign is shown in the left mirror when speed exceeds");
        Console.WriteLine("     15m/s: warning");
        Console.WriteLine("     19m/s: critical");
        Console.WriteLine();

        Communicator communicator;

        try
        {
            communicator = host.Services.GetRequiredService<Communicator>();
            /*
            if (!SetBeltComPort(communicator))
                return;

            _logger.LogInformation("Prepare to calibrate the belts, then press any key...");
            Console.ReadKey(true);

            if (!communicator.PrepareBelt())
            {
                _logger.LogError("Failed to prepare the system");
                return;
            }*/
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to proceed:");
            return;
        }

        //var beltController = host.Services.GetRequiredService<BeltController>();
        //var speedWarnSignController = host.Services.GetRequiredService<SpeedWarnSignController>();
        //var carHornController = host.Services.GetRequiredService<CarHornController>();
        var sireneController = host.Services.GetRequiredService<SireneController>();

        communicator.Start();

        Console.WriteLine("Press any key to stop...");
        Console.ReadKey(true);

        communicator.Stop();
    }

    private static void ConfigureServices(
        IServiceCollection services,
        ConfigurationManager config)
    {
        config.AddJsonFile(
            "settings.json",
            optional: false,
            reloadOnChange: false);

        services.Configure<AppSettings>(config);

        services.AddSingleton<ServerClient>();

        services.AddSingleton<TensionRClient>();
        services.AddSingleton<CarlaClient>();
        services.AddSingleton<ScreenClient>();
        services.AddSingleton<DrivingClient>();
        services.AddSingleton<SoundPlayerClient>();
        services.AddSingleton<StreamDeckClient>();

        services.AddSingleton<Communicator>();

        services.AddSingleton<BeltController>();
        services.AddSingleton<SpeedWarnSignController>();
        services.AddSingleton<CarHornController>();
        services.AddSingleton<SireneController>();
    }

    private static async Task<bool> CheckServerVersion(IHost host, ILogger<Program> logger)
    {
        try
        {
            using var server = host.Services.GetRequiredService<ServerClient>();
            if (server.CoreVersion != Common.Constants.CORE_VERSION)
            {
                throw new Exception(
                    "Cannot run the app, as it uses another core version than the server:\n" +
                    $"'{Common.Constants.CORE_VERSION}' and '{server.CoreVersion}', correspondingly");
            }
            else if (server.CoreBuildTime != Core.BuildInfo.BuildTime)
            {
                Console.Write(
                        "Core build time differs from the one used by the server,\n" +
                        "The functionality may not match, \n" +
                        "causing either the server or the app to crash\n\n" +
                        "Do you want to continue [y/N]? ");
                var input = Console.ReadLine();
                if (input?.Trim().ToLower() != "y")
                {
                    return false;
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex.Message);
            Console.WriteLine("Terminating the app...");

            return false;
        }

        return true;
    }

    private static bool SetBeltComPort(Communicator comm)
    {
        Console.WriteLine("COM ports:");
        foreach (var port in comm.ComPorts)
        {
            Console.WriteLine($"  - {port.Name}: {port.Description}");
        }
        Console.Write($"Belt COM port: {comm.BeltComPort?.Name ?? ""}");
        var input = Console.ReadLine()?.Trim();

        if (comm.BeltComPort != null && string.IsNullOrEmpty(input))
        {
            // OK, use default port
        }
        else if (!string.IsNullOrEmpty(input))
        {
            input = input.Trim().ToUpper();
            string comPortName = input.StartsWith("COM", StringComparison.CurrentCultureIgnoreCase) ? input : $"COM{input}";
            var port = comm.ComPorts.FirstOrDefault(p => p.Name == comPortName);
            if (port != null)
            {
                comm.BeltComPort = port;
            }
            else
            {
                _logger?.LogError("Port {name} not found", comPortName);
                return false;
            }
        }
        else
        {
            _logger?.LogWarning("No COM port selected");
            return false;
        }

        return true;
    }
}
