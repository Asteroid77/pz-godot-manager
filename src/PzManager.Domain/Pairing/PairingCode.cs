namespace PzManager.Domain.Pairing;

public readonly record struct PairingCode(string Value)
{
    public static bool TryParse(string? raw, out PairingCode code)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            code = default;
            return false;
        }

        var cleaned = new string(raw.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
        if (cleaned.Length < 8)
        {
            code = default;
            return false;
        }

        code = new PairingCode(cleaned);
        return true;
    }

    public string ToDisplayString()
    {
        var chunks = Value
            .Chunk(4)
            .Select(c => new string(c))
            .ToArray();

        return string.Join('-', chunks);
    }

    public override string ToString() => ToDisplayString();
}

