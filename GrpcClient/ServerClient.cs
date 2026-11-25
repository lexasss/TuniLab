using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.Extensions.Options;

namespace GrpcClients;

public sealed class ServerClient : IDisposable
{
    public string CoreVersion { get; }
    public string CoreBuildTime { get; }

    public ServerClient(IOptions<AppSettings> appSettings)
    {
        try
        {
            _channel = new Channel(
                appSettings.Value.ServerIp,
                (int)Common.Ports.Server,
                ChannelCredentials.Insecure);

            _client = new GrpcServer.Dispatcher.DispatcherClient(_channel);

            if (!_client.IsAvailable(new Empty()).Value)
                throw new Exception();

            var version = _client.GetCoreVersion(new Empty());
            CoreVersion = version.Value;
            CoreBuildTime = version.BuildTime;
        }
        catch
        {
            throw new Exception($"Cannot connect to the server on {appSettings.Value.ServerIp}:{(int)Common.Ports.Server}");
        }
    }

    public void Dispose()
    {
        try
        {
            if (_channel != null)
                _channel.ShutdownAsync().Wait();
        }
        finally
        {
            _channel = null;
            GC.SuppressFinalize(this);
        }
    }


    #region Internal

    Channel? _channel;
    readonly GrpcServer.Dispatcher.DispatcherClient _client;

    #endregion
}