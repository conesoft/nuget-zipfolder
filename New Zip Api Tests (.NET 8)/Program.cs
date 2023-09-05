using Humanizer;
using System.Diagnostics;

var source = @"C:\Users\davep\Downloads\TNG\Season 1";
var target = $@"{source}.zip";

var timer = new Stopwatch();
timer.Start();
{
    var c = 16106127864;
    c = 12595912021;
    Console.WriteLine($"{c} - {c.Bytes()}");
    var b = CountFast_(source);
    Console.WriteLine($"{b.length} - {b.length.Bytes()} in {b.elapsed.Humanize()}");
    var x = Count_(source);
    Console.WriteLine($"{x.length} - {x.length.Bytes()} in {x.elapsed.Humanize()}");
    //var y = WriteZip_(source, target);
    //Console.WriteLine($"{y.length} - {y.length.Bytes()} in {y.elapsed.Humanize()}");
    var z = new FileInfo(target).Length;
    Console.WriteLine($"{z} - {z.Bytes()}");

    if (b.length != x.length)
    {
        Console.WriteLine();
        Console.WriteLine($"{x.length - b.length} - {(x.length - b.length).Bytes()}");
    }
    else
    {
        Console.WriteLine();
        Console.WriteLine("success !!");
    }
    Console.WriteLine();
}
timer.Stop();
Console.WriteLine(timer.Elapsed.Humanize(precision: 2));

static (long length, TimeSpan elapsed) CountFast_(string source)
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
    var results = WriteZipInternal_(source, stream, defaultNamespace: false);

    Console.WriteLine(filelengths.Sum() + " <-> " + results.length);

    return (results.length + missedbits, results.elapsed);
}

static (long length, TimeSpan elapsed) Count_(string source)
{
    using var stream = new PositionWrapperStream();
    return WriteZipInternal_(source, stream, defaultNamespace: true);
}

static (long length, TimeSpan elapsed) WriteZip_(string source, string target)
{
    using var stream = new FileAppendOnlyWrapperStream(File.Open(target, FileMode.CreateNew));
    return WriteZipInternal_(source, stream, defaultNamespace: true);
}

static (long length, TimeSpan elapsed) WriteZipInternal_(string source, Stream stream, bool defaultNamespace)
{
    var timer = new Stopwatch();
    timer.Start();

    if (defaultNamespace == true)
    {
        System.IO.Compression.ZipFile.CreateFromDirectory(source, stream, System.IO.Compression.CompressionLevel.NoCompression, false);
    }
    else
    {
        SystemIOCompression.ZipFile.CreateFromDirectory(source, (PositionWrapperStream)stream);
    }

    timer.Stop();
    return (stream.Position, timer.Elapsed);
}