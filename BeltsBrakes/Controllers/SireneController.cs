using GrpcClients;

namespace BeltsBrakes;

internal class SireneController
{
    public static Type[] RequiredGrpcClients => [
        typeof(SoundPlayerClient),
        typeof(StreamDeckClient)
    ];

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
            if (e.Id == 0 && e.IsPressed && !(_motuWalls?.IsPlaying ?? true))
                PlayCircularSound(_motuWalls);
            else if (e.Id == 1 && e.IsPressed && !(_motuCabin?.IsPlaying ?? true))
                PlayCircularSound(_motuCabin);
        };
    }

    const string WALL_SPEAKERS_DEVICE = "(2- MOTU Pro Audio)";
    const int WALL_SPEAKERS_COUNT = 16;
    const string CABIN_SPEAKERS_DEVICE = "(MOTU Pro Audio)";
    const int CABIN_SPEAKERS_COUNT = 8;

    class Motu(
        string location,
        string deviceId,
        int channelCount,
        bool[] channelEnablingMap)
    {
        public string Location => location;
        public string DeviceId => deviceId;
        public int ChannelCount => channelCount;
        public bool[] ChannelEnablingMap => channelEnablingMap;
        public bool IsPlaying { get; set; } = false;
    }

    readonly SoundPlayerClient _soundPlayerClient;
    readonly StreamDeckClient _streamDeckClient;

    Motu? _motuWalls;
    Motu? _motuCabin;

    private void InitSound()
    {
        var allDevices = _soundPlayerClient.GetDevices().Items;
        var multichannelDevices = allDevices
            .Where(d => d.ChannelCount > 2)
            .ToArray();
        
        if (multichannelDevices.Length == 0)
        {
            Console.WriteLine("No multichannel sound devices found.");
            return;
        }

        _motuCabin = InitMotuDevice(multichannelDevices, "cabin", CABIN_SPEAKERS_COUNT, CABIN_SPEAKERS_DEVICE);
        _motuWalls = InitMotuDevice(multichannelDevices, "wall", WALL_SPEAKERS_COUNT, WALL_SPEAKERS_DEVICE);
    }


    private static Motu? InitMotuDevice(
        SoundPlayer.Device[] multichannelDevices, 
        string speakersLocation,
        int speakersCount,
        string defaultDeviceName)
    {
        SoundPlayer.Device? soundDevice =
            multichannelDevices.FirstOrDefault(x => x.Name.Contains(defaultDeviceName)) ??
            SelectSoundDevice(multichannelDevices, speakersLocation);

        Console.WriteLine(soundDevice == null ? $"No {speakersLocation} speakers" : $"{speakersLocation} speakers on {soundDevice.Name}");

        if (soundDevice != null)
        {
            var channelEnablingMap = new bool[soundDevice.ChannelCount];
            for (int i = 0; i < soundDevice.ChannelCount; i++)
                channelEnablingMap[i] = i < Math.Min(speakersCount, soundDevice.ChannelCount);

            return new Motu(speakersLocation, soundDevice.Id, soundDevice.ChannelCount, channelEnablingMap);
        }

        return null;
    }

    private static SoundPlayer.Device? SelectSoundDevice(
        SoundPlayer.Device[] multichannelDevices,
        string speakersLocation)
    {
        Console.WriteLine("Multichannel sound devices:");
        for (int i = 0; i < multichannelDevices.Length; i++)
        {
            var device = multichannelDevices[i];
            Console.WriteLine($"{i + 1}. {device.Name}: {device.ChannelCount} channels");
        }

        Console.Write($"Choose the device ID for {speakersLocation} speakers: ");

        int index = -1;
        var input = Console.ReadLine()?.Trim();
        if (int.TryParse(input, out int id) && id > 0 && id <= multichannelDevices.Length)
        {
            index = id - 1;
        }

        return index < 0 ? null : multichannelDevices[index];
    }

    private void InitStreamDeck()
    {
        _streamDeckClient.SetKey(new StreamDeck.Key()
        {
            Id = 0,
            FileNameOrColor = "64,160,255"
        });
        _streamDeckClient.SetKey(new StreamDeck.Key()
        {
            Id = 1,
            FileNameOrColor = "255,128,128"
        });
    }

    private void PlayCircularSound(Motu motu)
    {
        int enabledChannelCount = motu.ChannelEnablingMap.Count(true);
        if (enabledChannelCount < 2)
        {
            Console.WriteLine($"Not enough enabled channels to play the curlcular sound on {motu.Location} speakers");
            return;
        }

        var tone = new SoundPlayer.ToneDescription()
        {
            ToneType = SoundPlayer.ToneType.Triangle,
            Frequency = 440,
            Gain = 1,
            PulseDuration = 0,
            TotalDuration = 0
        };
        tone.ChannelEnablingMap.AddRange(motu.ChannelEnablingMap);

        float direction = 0;
        float[] map = new float[enabledChannelCount];

        DirectionToCircularMap(direction, map);
        _soundPlayerClient.SetChannelMap(map);

        _ = _soundPlayerClient.PlayTone(tone, motu.DeviceId);

        motu.IsPlaying = true;

        Task.Run(async () =>
        {
            while (direction < 1f)
            {
                await Task.Delay(40);

                direction += 0.01f;

                DirectionToCircularMap(direction, map);
                _soundPlayerClient.SetChannelMap(map);
            }

            _soundPlayerClient.Stop();
            motu.IsPlaying = false;
        });
    }

    /// <summary>
    /// Fills the map with gains
    /// </summary>
    /// <param name="direction">0..1</param>
    /// <param name="map">the buffer to fill</param>
    static void DirectionToCircularMap(float direction, float[] map)
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

        // to emulate cyclicity, we need an extra component, with -n to the direction
        var directionP0 = direction * n;
        var directionP1 = (direction - 1f) * n;

        // The map is a normal distribution scaled, shifted down, and cut from the bottom:
        //      ⟋¯⟍
        //     /    \
        // ¯¯¯¯      ¯¯¯¯
        // https://www.desmos.com/calculator/xh2bw7m6qt

        double scale = 2;
        double offset = 1;
        double doubleQ2 = -1.0 / Math.Log(offset / scale); // to be positive within ±1
        double pow = 1.8;       // makes the sum of map equal to 1; with p = 1 the sum gets >1 when direction is not an integer

        for (int i = 0; i < n; i++)
        {
            var a0 = Math.Exp(-Math.Pow(i - directionP0, 2) / doubleQ2);
            var a1 = Math.Exp(-Math.Pow(i - directionP1, 2) / doubleQ2);
            map[i] = (float)Math.Pow(Math.Clamp((a0 + a1) * scale - offset, 0, 1), pow);
        }
    }
}
