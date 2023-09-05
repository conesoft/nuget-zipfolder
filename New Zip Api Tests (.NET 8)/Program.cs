using Humanizer;
using System.Diagnostics;

var source = @"C:\Temp\Zip\Data";
var target = @"C:\Temp\Zip\Data.zip";

var timer = new Stopwatch();
timer.Start();
{
    var c = 16106127864;
    Console.WriteLine($"{c} - {c.Bytes()}");
    var b = CountFast_(source);
    Console.WriteLine($"{b.length} - {b.length.Bytes()} in {b.elapsed.Humanize()}");
    var x = Count_(source);
    Console.WriteLine($"{x.length} - {x.length.Bytes()} in {x.elapsed.Humanize()}");
    //var y = WriteZip_(source, target);
    //Console.WriteLine($"{y.length} - {y.length.Bytes()} in {y.elapsed.Humanize()}");
    var z = new FileInfo(target).Length;
    Console.WriteLine($"{z} - {z.Bytes()}");
}
timer.Stop();
Console.WriteLine(timer.Elapsed.Humanize(precision: 2));

static (long length, TimeSpan elapsed) CountFast_(string source)
{
    return (0, TimeSpan.Zero);
}

static (long length, TimeSpan elapsed) Count_(string source)
{
    using var stream = new PositionWrapperStream();
    return WriteZipInternal_(source, stream, defaultNamespace: false);
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
        SystemIOCompression.ZipFile.CreateFromDirectory(source, stream);
    }

    timer.Stop();
    return (stream.Position, timer.Elapsed);
}