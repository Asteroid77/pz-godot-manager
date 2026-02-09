namespace PzManager.Transport.Protocol;

public sealed record ServerHelloV1(
    int Protocol,
    string Nonce,
    DateTimeOffset ServerTimeUtc);

