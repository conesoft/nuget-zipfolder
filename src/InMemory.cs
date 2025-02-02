namespace Conesoft.ZipFolder;

static class InMemoryStreamExtensions
{
    public static ValueTask WriteBytes(this Stream stream, Action<MemoryStream> action)
    {
        using var memory = new MemoryStream();
        action(memory);
        return stream.WriteAsync(memory.ToArray());
    }
}
