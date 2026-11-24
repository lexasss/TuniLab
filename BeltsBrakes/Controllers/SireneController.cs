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
            if (e.Id == 0 && e.IsPressed && !_isPlayingWallsSound)
                PlayWallsSound();
            else if (e.Id == 1 && e.IsPressed && !_isPlayingCabinSound)
                PlayCabinSound();
        };
    }

    readonly SoundPlayerClient _soundPlayerClient;
    readonly StreamDeckClient _streamDeckClient;
    
    string _motuWallsDeviceId = string.Empty;
    int _motuWallsChannelCount = 2;
    bool[] _motuWallsChannelEnablingMap = [];
    bool _isPlayingWallsSound = false;

    string _motuCabinDeviceId = string.Empty;
    int _motuCabinChannelCount = 2;
    bool[] _motuCabinChannelEnablingMap = [];
    bool _isPlayingCabinSound = false;


    private void InitSound()
    {
        var allDevices = _soundPlayerClient.GetDevices().Items;
        var multichannelDevices = allDevices
            .Where(d => d.ChannelCount > 2)
            .ToArray();

        SoundPlayer.Device? motuCabin = null;
        SoundPlayer.Device? motuWalls = null;

        if (multichannelDevices.Length > 0)
        {
            (motuCabin, motuWalls) = SelectSoundDevice(multichannelDevices);
            Console.WriteLine(motuCabin == null ? "No cabin speakers" : $"Cabin speakers on {motuCabin.Name}");
            Console.WriteLine(motuWalls == null ? "No walls speakers" : $"Walls speakers on {motuWalls.Name}");
        }
        else
        {
            Console.WriteLine("No multichannel sound devices found, using the default device, one of:");
            foreach (var device in allDevices)
                Console.WriteLine($" - {device.Name}");
        }

        if (motuCabin != null)
        {
            _motuCabinDeviceId = motuCabin.Id;
            _motuCabinChannelCount = motuCabin.ChannelCount;

            _motuCabinChannelEnablingMap = new bool[_motuCabinChannelCount];
            for (int i = 0; i < _motuCabinChannelCount; i++)
                _motuCabinChannelEnablingMap[i] = i < Math.Min(8, _motuCabinChannelCount); // only 8 wall speakers
        }


        if (motuWalls != null)
        {
            _motuWallsDeviceId = motuWalls.Id;
            _motuWallsChannelCount = motuWalls.ChannelCount;

            _motuWallsChannelEnablingMap = new bool[_motuWallsChannelCount];
            for (int i = 0; i < _motuWallsChannelCount; i++)
                _motuWallsChannelEnablingMap[i] = i < Math.Min(16, _motuWallsChannelCount); // only 16 wall speakers
        }
    }

    private static (SoundPlayer.Device?, SoundPlayer.Device?) SelectSoundDevice(
        SoundPlayer.Device[] multichannelDevices)
    {
        var motuCabin = multichannelDevices.FirstOrDefault(x => x.Name.Contains("(MOTU Pro Audio)"));
        var motuWalls = multichannelDevices.FirstOrDefault(x => x.Name.Contains("(2- MOTU Pro Audio)"));

        int Prompt(string target)
        {
            Console.WriteLine("Multichannel sound devices:");
            for (int i = 0; i < multichannelDevices.Length; i++)
            {
                var device = multichannelDevices[i];
                Console.WriteLine($"{i + 1}. {device.Name}: {device.ChannelCount} channels");
            }

            Console.Write($"Choose the device ID for {target}: ");

            var input = Console.ReadLine()?.Trim();
            if (int.TryParse(input, out int id) && id > 0 && id <= multichannelDevices.Length)
            {
                return id - 1;
            }

            return -1;
        }

        if (motuCabin == null)
        {
            var index = Prompt("cabin speakers");
            if (index >= 0)
                motuCabin = multichannelDevices[index];
        }

        if (motuWalls == null)
        {
            var index = Prompt("walls speakers");
            if (index >= 0)
                motuWalls = multichannelDevices[index];
        }

        return (motuCabin, motuWalls);
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

    private void PlayWallsSound()
    {
        int enabledChannelCount = _motuWallsChannelEnablingMap.Count(true);
        if (enabledChannelCount < 2)
        {
            Console.WriteLine("Not enough enabled sound channels to play the wall sound");
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
        tone.ChannelEnablingMap.AddRange(_motuWallsChannelEnablingMap);

        float direction = 0;
        float[] map = new float[enabledChannelCount];

        DirectionToMap(direction, map);
        _soundPlayerClient.SetChannelMap(map);

        _ = _soundPlayerClient.PlayTone(tone, _motuWallsDeviceId);

        _isPlayingWallsSound = true;

        Task.Run(async () =>
        {
            while (direction < 1f)
            {
                await Task.Delay(40);

                direction += 0.01f;

                DirectionToMap(direction, map);
                _soundPlayerClient.SetChannelMap(map);
            }

            _soundPlayerClient.Stop();
            _isPlayingWallsSound = false;
        });
    }


    private void PlayCabinSound()
    {
        int enabledChannelCount = _motuCabinChannelEnablingMap.Count(true);
        if (enabledChannelCount < 2)
        {
            Console.WriteLine("Not enough enabled sound channels to play the cabin sound");
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
        tone.ChannelEnablingMap.AddRange(_motuCabinChannelEnablingMap);

        float direction = 0;
        float[] map = new float[enabledChannelCount];

        DirectionToMap(direction, map);
        _soundPlayerClient.SetChannelMap(map);

        _ = _soundPlayerClient.PlayTone(tone, _motuCabinDeviceId);

        _isPlayingCabinSound = true;

        Task.Run(async () =>
        {
            while (direction < 1f)
            {
                await Task.Delay(40);

                direction += 0.01f;

                DirectionToMap(direction, map);
                _soundPlayerClient.SetChannelMap(map);
            }

            _soundPlayerClient.Stop();
            _isPlayingCabinSound = false;
        });
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
        double pow = 1.5;

        for (int i = 0; i < n; i++)
        {
            var a0 = Math.Exp(-Math.Pow(i - directionP0, 2) / doubleQ2);
            var a1 = Math.Exp(-Math.Pow(i - directionP1, 2) / doubleQ2);
            var a2 = Math.Exp(-Math.Pow(i - directionP2, 2) / doubleQ2);
            map[i] = (float)Math.Pow(Math.Clamp((a0 + a1 + a2) * scale - offset, 0, 1), pow);
        }
    }
}
