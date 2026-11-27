using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System.Runtime.InteropServices;

namespace Server.SoundPlayer;

public sealed class ChannelRouter :
    IWaveProvider,
    IDirectionalSoundSource
{
    public WaveFormat WaveFormat { get; }

    public ChannelRouter(
        ISampleProvider source,
        bool[] channelEnablingMap,
        WaveFormat? format = null)
    {
        if (source.WaveFormat.Channels != 1)
            throw new ArgumentException(
                "The source must be mono.",
                nameof(source));

        _source = source;
        _channelEnablingMap = (bool[])channelEnablingMap.Clone();
        _enabledChannelCount = _channelEnablingMap.Count(true);

        WaveFormat = format ?? WaveFormat.CreateIeeeFloatWaveFormat(
            _source.WaveFormat.SampleRate,
            _channelEnablingMap.Length);
    }

    // DSS
    public void SetChannelMap(float[] mapping)
    {
        lock (_mapping)
        {
            _mapping = (float[])mapping.Clone();
        }
    }

    public int Read(Span<byte> buffer)
    {
        int samplesRequested = buffer.Length / WaveFormat.Channels / sizeof(float);

        if (samplesRequested == 0)
            return 0;

        float[] monoBuffer = new float[samplesRequested];

        int samplesRead = _source.Read(monoBuffer);

        int outputIndex = 0;

        for (int frame = 0; frame < samplesRead; frame++)
        {
            float sample = monoBuffer[frame];

            for (int channel = 0, mapIndex = 0; 
                channel < _channelEnablingMap.Length;
                channel++)
            {
                if (!_channelEnablingMap[channel])
                {
                    buffer[outputIndex++] = ZERO.B1;
                    buffer[outputIndex++] = ZERO.B2;
                    buffer[outputIndex++] = ZERO.B3;
                    buffer[outputIndex++] = ZERO.B4;
                }
                else
                {
                    var channelSample = sample;
                    lock (_mapping)
                    {
                        if (_mapping.Length == _enabledChannelCount)
                        {
                            channelSample = sample * _mapping[mapIndex++];
                        }
                    }

                    var fb = new FloatBytes() { Float = channelSample };
                    buffer[outputIndex++] = fb.B1;
                    buffer[outputIndex++] = fb.B2;
                    buffer[outputIndex++] = fb.B3;
                    buffer[outputIndex++] = fb.B4;
                }
            }
        }

        return samplesRead * _channelEnablingMap.Length * sizeof(float);
    }

    public static async Task Test()
    {
        using var enumerator = new MMDeviceEnumerator();
        using var devices = enumerator.EnumerateAudioEndPoints(
            DataFlow.Render,
            DeviceState.Active);
        using var device = devices.FirstOrDefault(d => d.FriendlyName.Contains("MOTU pro audio", StringComparison.OrdinalIgnoreCase));

        WasapiPlayer soundPlayer = new WasapiPlayerBuilder()
            .WithDevice(device)
            .WithEventSync()
            .WithLatency(50)
            .WithCategory(AudioStreamCategory.Media)
            .Build();
        var sf = soundPlayer.DeviceMixFormat;
        Console.WriteLine($"Supported format : channels = {sf.Channels}, encoding = {sf.Encoding}, sample rate = {sf.SampleRate}, block align = {sf.BlockAlign}, bps = {sf.BitsPerSample}");

        var channelCount = soundPlayer.DeviceMixFormat.Channels;
        int sampleRate = 48000;

        for (int i = 0; i < channelCount; i++)
        {
            bool[] channelEnablingMap = new bool[channelCount];
            channelEnablingMap[i] = true;

            var signal = new SignalGenerator(sampleRate, 1)
            {
                Frequency = 1000,
                Gain = 0.2,
                Type = SignalGeneratorType.Sin
            }.Take(TimeSpan.FromSeconds(1));

            var router = new ChannelRouter(
                signal,
                channelEnablingMap,
                soundPlayer.DeviceMixFormat);

            if (!soundPlayer.IsFormatSupported(router.WaveFormat, out WaveFormat bmf))
            {
                var uf = router.WaveFormat;
                Console.WriteLine($"Unsupported format : channels = {uf.Channels}, encoding = {uf.Encoding}, sample rate = {uf.SampleRate}, block align = {uf.BlockAlign}, bps = {uf.BitsPerSample}");
                if (bmf != null)
                    Console.WriteLine($"Try this format : channels = {bmf.Channels}, encoding = {bmf.Encoding}, sample rate = {bmf.SampleRate}, block align = {bmf.BlockAlign}, bps = {bmf.BitsPerSample}");
                return;
            }

            Console.WriteLine($"Playing on channel {i+1}");

            try
            {
                soundPlayer.Init(router);
                soundPlayer.Play();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  failed: {ex.Message}");
                var uf = router.WaveFormat;
                Console.WriteLine($"Unsupported format : channels = {uf.Channels}, encoding = {uf.Encoding}, sample rate = {uf.SampleRate}, block align = {uf.BlockAlign}, bps = {uf.BitsPerSample}");
                return;
            }

            await Task.Delay(1000);
            soundPlayer.Dispose();

            if (i < channelCount - 1)
            {
                await Task.Delay(1000);
                soundPlayer = new WasapiPlayerBuilder()
                            .WithDevice(device)
                            .WithEventSync()
                            .WithLatency(50)
                            .WithCategory(AudioStreamCategory.Media)
                            .Build();
            }
        }
    }

    #region Internal

    [StructLayout(LayoutKind.Explicit)]
    struct FloatBytes
    {
        [FieldOffset(0)]
        public float Float;

        [FieldOffset(0)]
        public byte B1;

        [FieldOffset(1)]
        public byte B2;
        [FieldOffset(2)]
        public byte B3;
        [FieldOffset(3)]
        public byte B4;
    }

    readonly FloatBytes ZERO = new() { Float = 0f };

    readonly ISampleProvider _source;
    readonly bool[] _channelEnablingMap;
    readonly int _enabledChannelCount;

    float[] _mapping = [];

    /// <summary>
    /// Fills the map with gains
    /// </summary>
    /// <param name="direction">0..1</param>
    /// <param name="map">the buffer to fill</param>
    private static void DirectionToMap(float direction, float[] map)
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

    #endregion
}
