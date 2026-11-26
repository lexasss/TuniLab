using GrpcClients;

namespace BeltsBrakes.Controllers;

internal class CarHornController
{
    public static Type[] RequiredGrpcClients => [
        typeof(SoundPlayerClient),
        typeof(DrivingClient)
    ];

    public CarHornController(
        SoundPlayerClient soundPlayerClient,
        DrivingClient drivingClient)
    {
        _soundPlayerClient = soundPlayerClient;
        _soundPlayerClient.PlaybackFinished += (s, e) =>
        {
            if (e.StartsWith("car-horn"))
            {
                _isPlayingHornSound = false;
            }
        };

        _drivingClient = drivingClient;
        _drivingClient.DataUpdated += (s, e) =>
        {
            if (_drivingClient.IsStreamingData && _soundPlayerClient.IsAvailable && !_isPlayingHornSound)
            {
                if (e.TopRightButton2.IsPressed && !_isHornButtonPressed)
                {
                    _soundPlayerClient.DeviceId = string.Empty; // default device
                    _ = _soundPlayerClient.PlayFile("car-horn.mp3");
                    _isPlayingHornSound = true;
                }

                _isHornButtonPressed = e.TopRightButton2.IsPressed;
            }
        };
    }

    #region Internal

    readonly SoundPlayerClient _soundPlayerClient;
    readonly DrivingClient _drivingClient;

    bool _isHornButtonPressed = false;
    bool _isPlayingHornSound = false;

    #endregion
}
