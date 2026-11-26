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

    class EnabledCases
    {
        public bool BeltBrakes { get; set; }
        public bool SpeedWarning { get; set; }
        public bool CarHorn { get; set; }
        public bool Sirene { get; set; }

        public bool HasAnyCaseEnabled => BeltBrakes || SpeedWarning || CarHorn || Sirene;
    }

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

        if (!await CheckCoreVersion(host.Services, _logger))
        {
            await host.StopAsync();
            return;
        }

        Console.WriteLine("Belts & Brakes is running");
        Console.WriteLine("\nRun CARLA and start driving.");

        Console.WriteLine("Enabled effects (type Y to enable all):");

        bool acceptAll = false;
        var enabledCases = new EnabledCases
        {
            BeltBrakes = PromptEffect(
                "belt is tighten on sudden brakes",
                ref acceptAll),
            CarHorn = PromptEffect(
                "a sound is played upon pressing steering button [1] middle top-right: car horn",
                ref acceptAll),
            SpeedWarning = PromptEffect(
                "the following sign is shown in the left mirror when speed exceeds [1] 15m/s: warning, [2] 19m/s: critical",
                ref acceptAll),
            Sirene = PromptEffect(
                "a sound is played upon pressing a StreamDeck button [1] blue: around the room, [2] pink: around the seat",
                ref acceptAll)
        };

        if (!enabledCases.HasAnyCaseEnabled)
            return;

        Console.WriteLine();

        Communicator communicator;

        try
        {
            communicator = host.Services.GetRequiredService<Communicator>();

            Type[] requiredClients = [
                ..BeltController.RequiredGrpcClients,
                ..SpeedWarnSignController.RequiredGrpcClients,
                ..CarHornController.RequiredGrpcClients,
                ..SireneController.RequiredGrpcClients,
            ];
            requiredClients = requiredClients.Distinct().ToArray();
            if (!communicator.CheckClientAvailablility(requiredClients))
            {
                throw new InvalidOperationException("One or more required clients are not available.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to proceed:");
            return;
        }

        if (enabledCases.BeltBrakes)
        {
            var bc = host.Services.GetRequiredService<BeltController>();
            if (!bc.SelectComPort(_logger))
                return;

            _logger.LogInformation("Prepare to calibrate the belts, then press any key...");
            Console.ReadKey(true);

            if (!bc.PrepareBelt())
            {
                _logger.LogError("Failed to prepare the system");
                return;
            }
        }

        if (enabledCases.SpeedWarning)
            host.Services.GetRequiredService<SpeedWarnSignController>();
        if (enabledCases.CarHorn)
            host.Services.GetRequiredService<CarHornController>();
        if (enabledCases.Sirene)
            host.Services.GetRequiredService<SireneController>();

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

    private static async Task<bool> CheckCoreVersion(
        IServiceProvider services, ILogger<Program> logger)
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

    private static bool PromptEffect(string prompt, ref bool acceptAll)
    {
        var c = '\0';

        Console.Write($" - {prompt}" + (acceptAll ? "[Y] " : " [Y/n] "));
        if (!acceptAll)
        {
            c = Console.ReadKey(true).KeyChar;
            acceptAll = c == 'Y';
        }

        var isAccepted = c == 'y' || c == '\n' || c == '\r' || acceptAll;
        Console.WriteLine(isAccepted ? "+" : "-");

        return isAccepted;
    }
}
