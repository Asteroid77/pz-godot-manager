namespace PzManager.Application.Errors;

public static class AppErrorCodes
{
    public const string BadRequest = "bad_request";
    public const string Unauthorized = "unauthorized";
    public const string Forbidden = "forbidden";
    public const string InvalidSignature = "invalid_signature";
    public const string PairingInvalid = "pairing_invalid";
    public const string PairingExpired = "pairing_expired";
    public const string PairingConsumed = "pairing_consumed";
    public const string InternalError = "internal_error";
}

