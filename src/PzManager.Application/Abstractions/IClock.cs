namespace PzManager.Application.Abstractions;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

