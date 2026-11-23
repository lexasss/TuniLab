using GrpcClients;

namespace BeltsBrakes;

internal class SireneController
{
    public SireneController(
        SoundPlayerClient soundPlayerClient,
        StreamDeckClient streamDeckClient)
    {
        _soundPlayerClient = soundPlayerClient;
        if (_soundPlayerClient.IsAvailable)
            InitSound();
        else
            _soundPlayerClient.AvailabilityChanged += (s, e) => InitSound();

        _streamDeckClient = streamDeckClient;
        if (_streamDeckClient.IsAvailable)
            InitStreamDeck();
        else
            _streamDeckClient.AvailabilityChanged += (s, e) => InitStreamDeck();

        _streamDeckClient.KeyStateChanged += (s, e) =>
        {
            if (e.Id == 0 && e.IsPressed && !_isPlayingSireneSound)
            {
                _soundPlayerClient.DeviceId = _motuDeviceId;
                var tone = new SoundPlayer.ToneDescription()
                {
                    ToneType = SoundPlayer.ToneType.Triangle,
                    Frequency = 440,
                    Gain = 1,
                    PulseDuration = 0,
                    TotalDuration = 0
                };
                tone.EnabledChannels.Clear();
                for (int i = 0; i < _channelCount; i++)
                    tone.EnabledChannels.Add(i < Math.Min(16, _channelCount)); // wall-mounted MOTU has only 16 speakers connected

                float direction = 0;
                float[] map = new float[_channelCount];
                DirectionToMap(direction, map);
                _soundPlayerClient.SetChannelMap(map);

                _ = _soundPlayerClient.PlayTone(tone);

                _isPlayingSireneSound = true;

                Task.Run(async () =>
                {
                    while (direction < 1f)
                    {
                        await Task.Delay(40);
                        direction += 0.01f;
                        DirectionToMap(direction, map);
                        /*Console.Write($"D = {direction:F2}");
                        foreach (var v in map.Take(16))
                            Console.Write($" {v:F3}");
                        Console.WriteLine();*/
                        _soundPlayerClient.SetChannelMap(map);
                    }

                    _soundPlayerClient.Stop();
                    _isPlayingSireneSound = false;
                });
            }
        };
    }

    readonly SoundPlayerClient _soundPlayerClient;
    readonly StreamDeckClient _streamDeckClient;

    string _motuDeviceId = string.Empty;
    int _channelCount = 2;

    bool _isPlayingSireneSound = false;

    private void InitSound()
    {
        var allDevices = _soundPlayerClient.GetDevices().Items;
        var multichannelDevices = allDevices
            .Where(d => d.ChannelCount > 2)
            .ToArray();

        SoundPlayer.Device? motu = null;
        if (multichannelDevices.Length > 1)
        {
            motu = SelectDevice(multichannelDevices);
        }
        else if (multichannelDevices.Length == 1)
        {
            motu = multichannelDevices.First();
        }
        else
        {
            Console.WriteLine("No multichannel sound devices found, using the default device, one of:");
            foreach (var device in allDevices)
                Console.WriteLine($" - {device.Name}");
        }

        if (motu != null)
        {
            _motuDeviceId = motu.Id;
            _channelCount = motu.ChannelCount;
            Console.WriteLine($"Playing on {motu.Name}");
        }
    }

    private void InitStreamDeck()
    {
        _streamDeckClient.SetKey(new StreamDeck.Key()
        {
            Id = 0,
            FileNameOrColor = "128,196,255"
        });
    }

    private static SoundPlayer.Device? SelectDevice(SoundPlayer.Device[] devices)
    {
        Console.WriteLine("Multichannel sound ports:");
        for (int i = 0; i < devices.Length; i++)
        {
            var device = devices[i];
            Console.WriteLine($"{i + 1}. {device.Name}: {device.ChannelCount} channels");
        }
        Console.Write("Type the device ID: ");
        var input = Console.ReadLine()?.Trim();
        if (int.TryParse(input, out int id) && id > 0 && id <= devices.Length)
        {
            return devices[id - 1];
        }
        else
        {
            Console.WriteLine("Unknown ID, continue with the default device.");
            return null;
        }
    }

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
            map[i] = (float)Math.Pow(Math.Clamp((a0 + a1 + a2) * scale - offset, 0, 1), 2);
        }
    }
}
