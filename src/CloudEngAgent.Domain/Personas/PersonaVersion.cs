namespace CloudEngAgent.Domain.Personas;

public sealed record PersonaVersion(string Value)
{
    public static PersonaVersion FromContentHash(ReadOnlySpan<byte> contentSha256)
    {
        if (contentSha256.Length == 0)
        {
            throw new ArgumentException("Hash cannot be empty.", nameof(contentSha256));
        }

        return new PersonaVersion(Convert.ToHexString(contentSha256).ToLowerInvariant());
    }

    public override string ToString() => Value;
}
