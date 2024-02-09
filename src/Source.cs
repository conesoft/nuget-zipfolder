namespace Conesoft.ZipFolder;

public record Source(string From, string To, bool WithSubdirectories = true)
{
    public static implicit operator Source(string from) => new(from, string.Empty);
    public static implicit operator Source((string from, string to, bool withSubdirectories) source) => new(source.from, source.to, source.withSubdirectories);

    public static Source Directory(string From, string To, bool WithSubdirectories = true) => new(From, To, WithSubdirectories);
};
