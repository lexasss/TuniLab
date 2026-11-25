using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Proto = global::GrpcServer;

namespace Server;

internal class ServerService :
    Proto.Dispatcher.DispatcherBase,
    IService
{
    public ServerService() : base() { }

    public void Dispose() { }

    public override Task<Proto.Version> GetCoreVersion(
        Empty request,
        ServerCallContext context) =>
        Task.FromResult(new Proto.Version
        {
            Value = Common.Constants.CORE_VERSION,
            BuildTime = Core.BuildInfo.BuildTime    // generated inside the Core/Core.csproj
        });

    public bool IsAvailable() => 
        true;

    public override Task<Common.Bool> IsAvailable(Empty request, ServerCallContext context) =>
        Common.Awaitable.True;
}
