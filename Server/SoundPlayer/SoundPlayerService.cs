using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using Proto = global::SoundPlayer;

namespace Server.SoundPlayer;

public class SoundPlayerService :
    Proto.Dispatcher.DispatcherBase,
    IFileService
{
    public string StorageFolder { get; } = "sounds";
    public bool IsAvailable() => true;

    public SoundPlayerService(ILoggerFactory loggerFactory) : base()
    {
        _logger = loggerFactory.CreateLogger("SNDP");

        foreach (var device in GetSoundDevices().Result)
        {
            _logger.LogInformation("Found sound device {name}", device.Name);
        }

        Tools.FileHelper.ListFiles(
            StorageFolder,
            _supportedAudioFormats,
            _logger
        );

        _baseService = new(_logger);
    }

    public void Dispose()
    {
        _tonePlayer?.Dispose();
        _filePlayer?.Dispose();
        _soundPlayer?.Dispose();
        _baseService?.Dispose();

        GC.SuppressFinalize(this);
    }

    public override Task<Common.Bool> IsAvailable(
        Empty request,
        ServerCallContext context)
    {
        return Common.Awaitable.True;
    }

    public override async Task<Proto.Devices> GetDevices(
        Empty request,
        ServerCallContext context)
    {
        var result = new Proto.Devices();
        var devices = await GetSoundDevices();
        foreach (var device in devices)
        {
            result.Items.Add(device);
        }
        return result;
    }

    public override async Task ReadEvents(
        Empty request,
        IServerStreamWriter<Proto.Event> responseStream,
        ServerCallContext context)
    {
        if (_baseService == null)
            return;

        await _baseService.ReadEvents(request, responseStream, context);
    }

    public override Task<Empty> SetChannelMap(
        Proto.ChannelMap request,
        ServerCallContext context)
    {
        _channelMap = request.Items.ToArray();
        _tonePlayer?.Directional?.SetChannelMap(_channelMap);
        _filePlayer?.Directional?.SetChannelMap(_channelMap);

        return Common.Awaitable.Empty;
    }

    public override Task<Common.Int> Play(
        Proto.SoundDescription request,
        ServerCallContext context)
    {
        int result = 0;

        _soundPlayer?.Dispose();
        _soundPlayer = CreatePlayer(request.DeviceId);

        if (request.SoundCase == Proto.SoundDescription.SoundOneofCase.Tone)
        {
            _tonePlayer?.Dispose();
            _tonePlayer = PlayTone(_soundPlayer, request.Tone);
            result = request.Tone.TotalDuration > 0 ? request.Tone.TotalDuration : int.MaxValue;
        }
        else if (request.SoundCase == Proto.SoundDescription.SoundOneofCase.File)
        {
            _filePlayer?.Dispose();
            _filePlayer = PlayFile(_soundPlayer, request.File);
            result = (int?)_filePlayer?.Duration.TotalMilliseconds ?? 0;
        }
        else
        {
            _logger.LogWarning("Unsupported sound type");
        }

        return Common.Awaitable.From(result);
    }

    public override Task<Empty> Stop(
        Empty request,
        ServerCallContext context)
    {
        _logger.LogInformation("Stopping playback");

        try
        {
            _toneCancellationSource?.Cancel();
        }
        finally
        {
            _toneCancellationSource?.Dispose();
            _toneCancellationSource = null;
        }

        _tonePlayer?.Stop();
        _tonePlayer?.Dispose();
        _tonePlayer = null;

        _filePlayer?.Dispose();
        _filePlayer = null;

        _soundPlayer?.Stop();
        _soundPlayer?.Dispose();
        _soundPlayer = null;

        return Common.Awaitable.Empty;
    }

    public override async Task<Common.UploadResult> UploadFile(
        IAsyncStreamReader<Common.UploadRequest> requestStream,
        ServerCallContext context)
    {
        return await Tools.FileHelper.UploadFile(
            requestStream,
            context,
            StorageFolder,
            _logger);
    }

    public override async Task<Common.Strings> GetFileList(
        Empty request,
        ServerCallContext context)
    {
        return await Tools.FileHelper.GetFileList(
            StorageFolder,
            _supportedAudioFormats);
    }

    #region Internal

    static readonly string[] _supportedAudioFormats = [".wav", ".mp3", ".aiff", ".aif"];

    readonly ILogger _logger;
    readonly Tools.Service<Proto.Event> _baseService;

    WasapiPlayer? _soundPlayer;
    TonePlayer? _tonePlayer;
    FilePlayer? _filePlayer;
    CancellationTokenSource? _toneCancellationSource;

    float[] _channelMap = [];

    private static async Task<Proto.Device[]> GetSoundDevices()
    {
        var devices = await Task.Run(() => {
            var enumerator = new MMDeviceEnumerator();
            return enumerator.EnumerateAudioEndPoints(
                DataFlow.Render,
                DeviceState.Active);
        });

        return devices
            .Select(device => {
                using var client = device.CreateAudioClient();
                return new Proto.Device()
                {
                    Id = device.ID,
                    Name = device.FriendlyName,
                    ChannelCount = client.MixFormat.Channels
                };
            })
            .ToArray();
    }

    private static MMDevice? GetDevice(string id)
    {
        var enumerator = new MMDeviceEnumerator();
        var devices = enumerator.EnumerateAudioEndPoints(
            DataFlow.Render,
            DeviceState.Active);
        return devices.FirstOrDefault(d => d.ID == id);
    }

    private WasapiPlayer CreatePlayer(string deviceId)
    {
        var device = GetDevice(deviceId);
        var soundPlayer = new WasapiPlayerBuilder()
            .WithDevice(device)
            .WithEventSync()
            .WithLatency(50)
            .WithCategory(AudioStreamCategory.Media)
            .Build();

        soundPlayer.PlaybackStopped += (sender, e) =>
        {
            _logger.LogInformation("Playback finished");
            _baseService.Publish(new Proto.Event {
                PlaybackFinished = System.IO.Path.GetFileName(_filePlayer?.FilePath) ?? string.Empty
            });
        };

        return soundPlayer;
    }

    private TonePlayer PlayTone(
        WasapiPlayer soundPlayer,
        Proto.ToneDescription tone)
    {
        var tonePlayer = new TonePlayer(
            soundPlayer,
            tone.ToneType,
            tone.Frequency,
            tone.Gain,
            tone.PulseDuration,
            tone.ChannelEnablingMap.ToArray()
        );
        
        _toneCancellationSource = new CancellationTokenSource();

        tonePlayer.Directional?.SetChannelMap(_channelMap);
        tonePlayer.Start();
        
        if (tone.TotalDuration > 0)
        {
            Task.Run(async () =>
            {
                await Task.Delay(tone.TotalDuration, _toneCancellationSource.Token);

                tonePlayer.Stop();

                _logger.LogInformation("Tone finished");
                _baseService.Publish(new Proto.Event {
                    PlaybackFinished = tone.ToneType.ToString()
                });

                _toneCancellationSource?.Dispose();
                _toneCancellationSource = null;

            }, _toneCancellationSource.Token);
        }

        _logger.LogInformation("Playing tone {tone}", tone.ToneType);
        return tonePlayer;
    }

    private FilePlayer? PlayFile(
        WasapiPlayer soundPlayer,
        Proto.FileDescription file)
    {
        string? filePath = Tools.FileHelper.FileNameToPath(
            file.Filename,
            StorageFolder,
            _logger);

        if (string.IsNullOrEmpty(filePath))
            return null;

        try
        {
            var filePlayer = new FilePlayer(soundPlayer, filePath, file.ChannelEnablingMap.ToArray());
            filePlayer.Directional?.SetChannelMap(_channelMap);
            filePlayer.Start();

            _logger.LogInformation("Playing sound from {filename}", file.Filename);
            return filePlayer;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error playing {filename}: {reason}", file.Filename, ex.Message);
        }

        return null;
    }

    #endregion
}
