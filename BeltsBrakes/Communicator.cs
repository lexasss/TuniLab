using GrpcClients;

namespace BeltsBrakes;

internal class Communicator
{
    public Communicator(
        CarlaClient carlaClient,
        TensionRClient tensionRClient,
        ScreenClient screenClient,
        DrivingClient drivingClient,
        SoundPlayerClient soundPlayerClient,
        StreamDeckClient streamDeckClient)
    {
        _carla = carlaClient;
        _tensionR = tensionRClient;
        _screen = screenClient;
        _driving = drivingClient;
        _soundPlayer = soundPlayerClient;
        _streamDeckClient = streamDeckClient;
    }

    public bool CheckClientAvailablility(Type[] requiredClientTypes)
    {
        try
        {
            IClient[] allClients = [
                _carla,
                _tensionR,
                _screen,
                _driving,
                _soundPlayer,
                _streamDeckClient
            ];

            var requiredClients = allClients.Where(c => 
                requiredClientTypes.Contains(c.GetType())
            );

            Wait(() => requiredClients.All(client => client.IsAvailable));
        }
        catch
        {
            return false;
        }

        return true;
    }

    public void Start()
    {
        _carla.SetStreamingFields(["speed", "gforce_longitudinal"]);
        _carla.Start();
        _driving.Start();
    }

    public void Stop()
    {
        _tensionR.Stop();
        _carla.Stop();
        _driving.Stop();
        _screen.Hide();     // hides all visible media
        _soundPlayer.Stop();
    }

    #region Internal

    readonly CarlaClient _carla;
    readonly TensionRClient _tensionR;
    readonly ScreenClient _screen;
    readonly DrivingClient _driving;
    readonly SoundPlayerClient _soundPlayer;
    readonly StreamDeckClient _streamDeckClient;

    private static void Wait(Func<bool> condition, int timeoutMs = 5000)
    {
        var startTime = DateTime.Now;
        while (!condition())
        {
            if ((DateTime.Now - startTime).TotalMilliseconds > timeoutMs)
                throw new TimeoutException($"Condition not met within {(double)timeoutMs/1000:F1} seconds.");
            Thread.Sleep(100);
        }
    }

    #endregion
}
