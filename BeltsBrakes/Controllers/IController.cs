namespace BeltsBrakes;

internal interface IController
{
    static GrpcClients.IClient[] RequiredGrpcClients { get; } = [];
}
