using GrpcClients;

namespace BeltsBrakes;

internal class SireneController
{
    public SireneController(
        SoundPlayerClient soundPlayerClient,
        StreamDeckClient streamDeckClient)
    {
        _soundPlayerClient = soundPlayerClient;
        _soundPlayerClient.AvailabilityChanged += (s, e) =>
        {
            var devices = _soundPlayerClient.GetDevices();
            var motu = devices.Items.FirstOrDefault(d => d.ChannelCount > 2);
            if (motu != null)
            {
                _motuDeviceId = motu.Id;
                _channelCount = motu.ChannelCount;
                Console.WriteLine($"Playing on {motu.Name}");
            }
            else
            {
                Console.WriteLine("No multichannel sound devices found:");
                foreach (var device in devices.Items)
                    Console.WriteLine($" - {device.Name}");
            }
        };
        _soundPlayerClient.PlaybackFinished += (s, e) =>
        {
            if (e.StartsWith("Triangle"))
            {
                _isPlayingSireneSound = false;
            }
        };

        _streamDeckClient = streamDeckClient;
        _streamDeckClient.KeyStateChanged += (s, e) =>
        {
            if (e.Id == 0 && e.IsPressed && !_isPlayingSireneSound)
            {
                _soundPlayerClient.DeviceId = _motuDeviceId;
                _ = _soundPlayerClient.PlayTone(new SoundPlayer.ToneDescription()
                {
                    ToneType = SoundPlayer.ToneType.Triangle,
                    Frequency = 440,
                    Gain = 1,
                    PulseDuration = 0,
                    TotalDuration = 0
                });

                _isPlayingSireneSound = true;

                Task.Run(async () =>
                {
                    float direction = 0;
                    float[] map = new float[_channelCount];

                    while (direction < 1f)
                    {
                        DirectionToMap(direction, map);
                        _soundPlayerClient.SetChannelMap(map);
                        direction += 0.02f;
                        await Task.Delay(80);
                    }

                    _soundPlayerClient.Stop();
                });
            }
        };
    }

    readonly SoundPlayerClient _soundPlayerClient;
    readonly StreamDeckClient _streamDeckClient;

    string _motuDeviceId = string.Empty;
    int _channelCount = 2;

    bool _isPlayingSireneSound = false;
    /// <summary>
    /// Fills the map with gains
    /// </summary>
    /// <param name="direction">0..1</param>
    /// <param name="map">the buffer to fill</param>
    static void DirectionToMap(float direction, float[] map)
    {
        if (map.Length < 2)
            return;

        if (map.Length == 2)
        {
            if (direction > 0.5f)
                direction = 1f - direction;

            map[0] = direction <= 0.25f ? 1 : (1f - 2f * direction) * 2;
            map[1] = direction >= 0.25f ? 1 : direction * 4;
            return;
        }

        int n = map.Length;

        // to emulate cyclicity, we need two extra components, with ±n to the direction
        var directionP0 = direction * n;
        var directionP1 = (direction + 1f) * n;
        var directionP2 = (direction - 1f) * n;

        // The map is a normal distribution scaled, shifted down, and cut from top and bottom:
        //      ____
        //     /    \
        // ¯¯¯¯      ¯¯¯¯
        // Desmos:
        // https://www.desmos.com/calculator/s4dj0lchie
        //
        // With 2s^2 = 0.685, scale = 2.162 and offset = 0.5,
        // the top flat area ranges from -0.5 to 0.5, the slop length is 0.5

        double doubleQ2 = 0.684;
        double scale = 2.162;
        double offset = 0.5;

        for (int i = 0; i < n; i++)
        {
            var a0 = Math.Exp(-Math.Pow(i - directionP0, 2) / doubleQ2);
            var a1 = Math.Exp(-Math.Pow(i - directionP1, 2) / doubleQ2);
            var a2 = Math.Exp(-Math.Pow(i - directionP2, 2) / doubleQ2);
            map[i] = (float)Math.Clamp((a0 + a1 + a2) * scale - offset, 0, 1);
        }
    }
}
