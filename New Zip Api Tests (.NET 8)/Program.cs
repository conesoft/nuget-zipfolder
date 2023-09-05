using Humanizer;
using System.Diagnostics;

var source = @"C:\Users\davep\Downloads\TNG\Season 1";
var target = @"C:\Users\davep\Downloads\TNG\Season 1.zip";

var timer = new Stopwatch();
timer.Start();
{
    var c = 12595912021;
    Console.WriteLine($"{c} - {c.Bytes()}");
    var b = CountFast_(source);
    Console.WriteLine($"{b} - {b.Bytes()}");

    var x = Count_(source);
    Console.WriteLine($"{x} - {x.Bytes()}");
    //var y = WriteZip_(source, target);
    //Console.WriteLine($"{y} - {y.Bytes()}");
    var z = new FileInfo(target).Length;
    Console.WriteLine($"{z} - {z.Bytes()}");
}
timer.Stop();
Console.WriteLine(timer.Elapsed.Humanize());

static long CountFast_(string source)
{
    return 0;
}

static long Count_(string source)
{
    using var stream = new PositionWrapperStream();
    return WriteZipInternal_(source, stream);
}

static long WriteZip_(string source, string target)
{
    using var stream = new FileAppendOnlyWrapperStream(File.Open(target, FileMode.CreateNew));
    return WriteZipInternal_(source, stream);
}

static long WriteZipInternal_(string source, Stream stream)
{
    System.IO.Compression.ZipFile.CreateFromDirectory(source, stream, System.IO.Compression.CompressionLevel.NoCompression, false);
    return stream.Position;
}