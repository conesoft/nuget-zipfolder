namespace Conesoft.ZipFolder;

internal class ZippedResult
{
    readonly string source;

    public ZippedResult(string source)
    {
        this.source = source;
    }

    public long ContentLength
    {
        get
        {
            const long _4gb = (long)4 * 1024 * 1024 * 1024;

            var files = Directory.GetFiles(source);
            var filelengths = files.Select(f => new FileInfo(f).Length).ToArray();
            var missedbits = (long)0;

            missedbits += files.Length * 16;
            missedbits += filelengths.Sum() >= _4gb ? -12 : 0;
            missedbits += filelengths.Any(l => l >= _4gb) ? 4 : 0;
            missedbits += filelengths.Count(l => l >= _4gb) * 8;

            using var stream = new PositionWrapperStream();

            Counting.ZipFile.CreateFromDirectory(source, stream);

            return stream.Position + missedbits;
        }
    }


    public void StreamTo(Stream stream)
    {
        SystemIOCompression.ZipFile.CreateFromDirectory(source, new FileAppendOnlyWrapperStream(stream));
    }
}