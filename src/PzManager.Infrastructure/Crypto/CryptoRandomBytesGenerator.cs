using System.Security.Cryptography;
using PzManager.Application.Abstractions;

namespace PzManager.Infrastructure.Crypto;

public sealed class CryptoRandomBytesGenerator : IRandomBytesGenerator
{
    public byte[] GetBytes(int length) => RandomNumberGenerator.GetBytes(length);
}

