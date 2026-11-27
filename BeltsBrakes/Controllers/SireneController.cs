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
            if (e.Id == 0 && e.IsPressed && !(_wallsSoundDevice?.IsPlaying ?? true))
                _ = PlayCircularSound(_wallsSoundDevice, async (sd) => await StartTonePlayback(sd, _tone440));
            else if (e.Id == 1 && e.IsPressed && !(_cabinSoundDevice?.IsPlaying ?? true))
                _ = PlayCircularSound(_cabinSoundDevice, async (sd) => await StartFilePlayback(sd, _fileCounting));
            else if (e.Id == 2 && e.IsPressed && !(_cabinSoundDevice?.IsPlaying ?? true))
                _ = PlayCabinTraversingSound(_cabinSoundDevice, async (sd) => await StartTonePlayback(sd, _tone440));
        };
    }

    /*public void Test()
    {
        if (_cabinSoundDevice == null)
        {
            var devices = _soundPlayerClient.GetDevices().Items.ToArray();
            _cabinSoundDevice = InitSoundDeviceDevice(devices, "test", 2, "Logitech");
        }
        if (!(_cabinSoundDevice?.IsPlaying ?? true))
            _ = PlayCircularSound(_cabinSoundDevice, async (sd) => await StartFilePlayback(sd, _fileCounting));
    }*/

    #region Internal

    const string WALL_SPEAKERS_DEVICE = "(2- MOTU Pro Audio)";
    const int WALL_SPEAKERS_COUNT = 16;
    const string CABIN_SPEAKERS_DEVICE = "(MOTU Pro Audio)";
    const int CABIN_SPEAKERS_COUNT = 8;
    const int CHANNEL_MAP_UPDATE_INTERVAL = 40; // ms

    class SoundDevice(
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

    readonly SoundPlayer.ToneDescription _tone440 = new()
    {
        ToneType = SoundPlayer.ToneType.Triangle,
        Frequency = 440,
        Gain = 1,
        PulseDuration = 0,
        TotalDuration = 0
    };

    readonly SoundPlayer.FileDescription _fileCounting = new()
    {
        Filename = "counting.mp3"
    };

    SoundDevice? _wallsSoundDevice;
    SoundDevice? _cabinSoundDevice;

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

        _cabinSoundDevice = InitSoundDeviceDevice(multichannelDevices, "cabin", CABIN_SPEAKERS_COUNT, CABIN_SPEAKERS_DEVICE);
        _wallsSoundDevice = InitSoundDeviceDevice(multichannelDevices, "wall", WALL_SPEAKERS_COUNT, WALL_SPEAKERS_DEVICE);
    }


    private static SoundDevice? InitSoundDeviceDevice(
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

            return new SoundDevice(speakersLocation, soundDevice.Id, soundDevice.ChannelCount, channelEnablingMap);
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

    private async Task<int?> StartTonePlayback(SoundDevice sd, SoundPlayer.ToneDescription tone)
    {
        tone.ChannelEnablingMap.Clear();
        tone.ChannelEnablingMap.AddRange(sd.ChannelEnablingMap);
        await _soundPlayerClient.PlayTone(tone, sd.DeviceId);
        return 0;
    }

    private async Task<int?> StartFilePlayback(SoundDevice sd, SoundPlayer.FileDescription file)
    {
        file.ChannelEnablingMap.Clear();
        file.ChannelEnablingMap.AddRange(sd.ChannelEnablingMap);
        return await _soundPlayerClient.PlayFile(file, sd.DeviceId);
    }

    private void InitStreamDeck()
    {
        // blue button for wall circle sound
        _streamDeckClient.SetKey(new StreamDeck.Key()
        {
            Id = 0,
            FileNameOrColor = "64,160,255"
        });

        // red button for cabin double-Z sound from a file
        _streamDeckClient.SetKey(new StreamDeck.Key()
        {
            Id = 1,
            FileNameOrColor = "255,128,128"
        });

        // green button for cabin circle forward and return sound
        _streamDeckClient.SetKey(new StreamDeck.Key()
        {
            Id = 2,
            FileNameOrColor = "0,255,0"
        });
    }

    private async Task PlayCircularSound(SoundDevice sd, Func<SoundDevice, Task<int?>> startPlayback)
    {
        int enabledChannelCount = sd.ChannelEnablingMap.Count(true);

        float direction = 0;
        float[] map = new float[enabledChannelCount];

        DirectionToCircularMap(direction, map);
        _soundPlayerClient.SetChannelMap(map);

        int? soundDuration = await startPlayback(sd);
        if (soundDuration != null)
            Console.Write("Playing circular sound... ");
        else
            return;

        sd.IsPlaying = true;

        _ = Task.Run(async () =>
        {
            float step = 0.01f;
            if (soundDuration != null && soundDuration > 0 && soundDuration < int.MaxValue)
                step = (float)CHANNEL_MAP_UPDATE_INTERVAL / (int)soundDuration;

            while (direction < 1f)
            {
                await Task.Delay(CHANNEL_MAP_UPDATE_INTERVAL);

                direction += step;

                DirectionToCircularMap(direction, map);
                _soundPlayerClient.SetChannelMap(map);
            }

            _soundPlayerClient.Stop();
            sd.IsPlaying = false;
            Console.WriteLine(" stopped");
        });
    }

    private float[] Reroute(int[] channels, float[] gains)
    {
        if (channels.Length != gains.Length ||              // must be of the same length
            channels.Any(i => i < 0 || i >= gains.Length))  // channel indexes do not exceed the number of channels/gains
            throw new Exception("Invalid channel routing parameters");

        float[] result = new float[gains.Length];
        for (int i = 0; i < gains.Length; i++)
            result[i] = gains[channels[i]];

        return result;
    }

    /// <summary>
    /// The sound circles the driver clockwise on the top speakers, then move down, 
    /// and circles the driver counter-clockwise on the bottom speakers
    /// </summary>
    private async Task PlayCabinTraversingSound(SoundDevice sd, Func<SoundDevice, Task<int?>> startPlayback)
    {
        int enabledChannelCount = sd.ChannelEnablingMap.Count(true);
        if (enabledChannelCount != CABIN_SPEAKERS_COUNT)
        {
            Console.WriteLine($"There must be exactly {CABIN_SPEAKERS_COUNT} enabled channels to play the traversing sound");
            return;
        }

        float direction = 0;
        float[] map = new float[enabledChannelCount];
        int[] channelRouter = [0, 1, 3, 2, 7, 6, 4, 5]; // these indexes reflect the cabin speakers connection order

        DirectionToMap(direction, map);
        map = Reroute(channelRouter, map);
        _soundPlayerClient.SetChannelMap(map);

        int? soundDuration = await startPlayback(sd);
        if (soundDuration != null)
            Console.Write("Playing cabin travsering sound... ");
        else
            return;

        sd.IsPlaying = true;

        _ = Task.Run(async () =>
        {
            float step = 0.01f;
            if (soundDuration != null && soundDuration > 0 && soundDuration < int.MaxValue)
                step = (float)CHANNEL_MAP_UPDATE_INTERVAL / (int)soundDuration;

            while (direction < 1f)
            {
                await Task.Delay(CHANNEL_MAP_UPDATE_INTERVAL);

                direction += step;

                DirectionToMap(direction, map);
                map = Reroute(channelRouter, map);
                _soundPlayerClient.SetChannelMap(map);
            }

            _soundPlayerClient.Stop();
            sd.IsPlaying = false;
            Console.WriteLine(" stopped");
        });
    }

    /// <summary>
    /// Fills the map with gains. 
    /// Requires at least two channels (i.e. the map stores at least 2 elements).
    /// </summary>
    /// <param name="direction">0..1</param>
    /// <param name="map">the buffer to fill, initialized with at least two elements</param>
    private static void DirectionToMap(float direction, float[] map)
    {
        if (map.Length < 2)
            return;

        if (map.Length == 2)
        {
            map[0] = (float)(Math.Cos(direction * Math.PI) + 1.0) / 2;
            map[1] = (float)(Math.Sin(direction * Math.PI - Math.PI / 2) + 1.0) / 2;
            return;
        }
        
        int n = map.Length;

        direction *= n - 1;

        // The map is a cos function at directions of ±π, otherwise 0:
        //         ⎽⎼⎻⎺⎺‾⎺⎺⎻⎼⎽ 
        //        ╱      ╲
        // ____⎽⎽⎼⎼⎻⎺         ⎺⎻⎼⎼⎽⎽____
        // https://www.desmos.com/calculator/cvxvjwuvrk

        for (int i = 0; i < n; i++)
        {
            var a = (Math.Cos(Math.PI * (i - direction)) + 1) / 2;
            map[i] = (i < direction - 1) || (i > direction + 1) ? 0f : (float)a;
        }
    }

    /// <summary>
    /// Fills the map with gains. 
    /// The algorithm assumes that the first channel is following the last channel, 
    /// i.e. the speakers are arranged in a cirlce.
    /// Requires at least two channels (i.e. the map stores at least 2 elements).
    /// </summary>
    /// <param name="direction">0..1</param>
    /// <param name="map">the buffer to fill, initialized with at least two elements</param>
    private static void DirectionToCircularMap(float direction, float[] map)
    {
        if (map.Length < 2)
            return;

        if (map.Length == 2)
        {
            map[0] = (float)(Math.Cos(direction * 2 * Math.PI) + 1.0) / 2;
            map[1] = (float)(Math.Sin(direction * 2 * Math.PI - Math.PI / 2) + 1.0) / 2;
            return;
        }

        int n = map.Length;

        // to emulate cyclicity, we need an extra component, with -n to the direction
        var directionP0 = direction * n;
        var directionP1 = (direction - 1f) * n;

        // The map is a normal distribution scaled, shifted down, cut from the bottom, and distored by raising to a power:
        //         ⟋¯⟍
        //        /    \
        // ____⎽⎽⎼⎼⎻⎺      ⎺⎻⎼⎼⎽⎽____
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

    #endregion
}
