using NAudio.Wave;

namespace Server.SoundPlayer;

public class FilePlayer : IDisposable
{
    public string FilePath { get; }
    public IDirectionalSoundSource? Directional => _channelRouter;

    public FilePlayer(
        WasapiPlayer player,
        string filePath,
        bool[] channelEnablingMap)
    {
        FilePath = filePath;

        _player = player;

        if (channelEnablingMap.Length == _player.OutputWaveFormat.Channels)
        {
            _audioFile = new AudioFileReader(filePath);

            _channelRouter = new ChannelRouter(
                _audioFile.ToMono(),
                channelEnablingMap);
        }
        else
        {
            _audioFile = new AudioFileReader(filePath);
        }
    }

    public void Start()
    {
        if (_channelRouter != null)
        {
            _player.Init(_channelRouter);
        }
        else
        {
            _player.Init(_audioFile);
        }

        _player.Play();
    }

    public void Stop()
    {
        if (_player.PlaybackState == PlaybackState.Playing)
        {
            _player.Stop();
        }
    }

    public void Dispose()
    {
        _player.Dispose();
        GC.SuppressFinalize(this);
    }

    #region Internal

    readonly WasapiPlayer _player;
    readonly ChannelRouter? _channelRouter;
    readonly AudioFileReader? _audioFile = null;

    #endregion
}
