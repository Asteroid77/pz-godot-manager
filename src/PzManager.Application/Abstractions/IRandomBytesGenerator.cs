namespace PzManager.Application.Abstractions;

public interface IRandomBytesGenerator
{
    byte[] GetBytes(int length);
}

