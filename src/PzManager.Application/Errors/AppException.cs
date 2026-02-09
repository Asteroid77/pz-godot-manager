namespace PzManager.Application.Errors;

public sealed class AppException : Exception
{
    public AppException(string code, string message, string? details = null) : base(message)
    {
        Code = code;
        Details = details;
    }

    public string Code { get; }

    public string? Details { get; }
}

