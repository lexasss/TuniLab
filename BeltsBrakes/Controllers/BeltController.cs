using GrpcClients;
using Microsoft.Extensions.Logging;
using TensionR;

namespace BeltsBrakes;

internal class BeltController : IController
{
    public float GForceThreshold { get; set; } = -0.2f;
    public float GForceAbsMaximum { get; set; } = 3f;
    public int BeltTensionMaximum { get; set; } = 255;

    public static Type[] RequiredGrpcClients => [
        typeof(CarlaClient),
        typeof(TensionRClient)
    ];

    public BeltController(
        CarlaClient carlaClient,
        TensionRClient tensionRClient)
    {
        _tensionR = tensionRClient;

        foreach (var port in _comUtils.Ports)
            _comPorts.Add(port);

        _beltComPort = _comPorts.FirstOrDefault(port =>
            port.Description?.Contains(TENSIONR_PORT_DESCRIPTION) ?? false);

        carlaClient.DataUpdated += (s, e) =>
        {
            if (e.Values.TryGetValue("gforce_longitudinal", out float gforce))
            {
                if (gforce < GForceThreshold)
                {
                    _beltTension = (int)Math.Clamp(
                        (-gforce + GForceThreshold) / GForceAbsMaximum * BeltTensionMaximum,
                        0, BeltTensionMaximum);
                    tensionRClient.SetTension(_beltTension, Side.Both);
                }
                else if (_beltTension > 0)
                {
                    _beltTension = 0;
                    tensionRClient.SetTension(_beltTension, Side.Both);
                }
            }
        };

        _tensionR.ConnectionChanged += (s, e) =>
        {
            _isBeltConnected = e;
        };
        _tensionR.EnabledChanged += (s, e) =>
        {
            _isBeltEnabled = e;
        };
        _tensionR.CalibrationChanged += (s, e) =>
        {
            _isBeltCalibrated = e;
        };
    }

    public bool SelectComPort(ILogger? logger)
    {
        Console.WriteLine("COM ports:");
        foreach (var port in _comPorts)
        {
            Console.WriteLine($"  - {port.Name}: {port.Description}");
        }
        Console.Write($"Belt COM port: {_beltComPort?.Name ?? ""}");
        var input = Console.ReadLine()?.Trim();

        if (_beltComPort != null && string.IsNullOrEmpty(input))
        {
            // OK, use default port
        }
        else if (!string.IsNullOrEmpty(input))
        {
            input = input.Trim().ToUpper();
            string comPortName = input.StartsWith("COM", StringComparison.CurrentCultureIgnoreCase) ? input : $"COM{input}";
            var port = _comPorts.FirstOrDefault(p => p.Name == comPortName);
            if (port != null)
            {
                _beltComPort = port;
            }
            else
            {
                logger?.LogError("Port {name} not found", comPortName);
                return false;
            }
        }
        else
        {
            logger?.LogWarning("No COM port selected");
            return false;
        }

        return true;
    }

    public bool PrepareBelt()
    {
        if (_beltComPort == null)
            return false;

        try
        {
            _tensionR.Connect(_beltComPort.Name);
            Wait(() => _isBeltConnected);
            Thread.Sleep(100);

            _tensionR.Start();
            Wait(() => _isBeltEnabled, 1000);
            Thread.Sleep(100);

            _tensionR.Calibrate();
            Wait(() => _isBeltCalibrated, 10000);
            Thread.Sleep(100);
        }
        catch
        {
            return false;
        }

        return _isBeltEnabled;
    }

    #region Internal

    const string TENSIONR_PORT_DESCRIPTION = "Silicon Labs CP210x USB to UART Bridge";

    readonly TensionRClient _tensionR;
    readonly COMUtils _comUtils = new();
    readonly List<COMUtils.Port> _comPorts = [];

    COMUtils.Port? _beltComPort;
    bool _isBeltConnected = false;
    bool _isBeltCalibrated = false;
    bool _isBeltEnabled = false;

    int _beltTension = 0;

    private static void Wait(Func<bool> condition, int timeoutMs = 5000)
    {
        var startTime = DateTime.Now;
        while (!condition())
        {
            if ((DateTime.Now - startTime).TotalMilliseconds > timeoutMs)
                throw new TimeoutException($"Condition not met within {(double)timeoutMs / 1000:F1} seconds.");
            Thread.Sleep(100);
        }
    }

    #endregion
}
