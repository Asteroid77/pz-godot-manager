using PzManager.Transport.Protocol;

namespace PzManager.Client;

public sealed class ManagerWsException : Exception
{
    public ManagerWsException(string code, string message, string? details = null) : base(message)
    {
        Code = code;
        Details = details;
    }

    public string Code { get; }

    public string? Details { get; }

    public static ManagerWsException FromError(ErrorV1 error) => new(error.Code, error.Message, error.Details);
}

