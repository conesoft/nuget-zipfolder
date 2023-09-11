using Humanizer;
using Conesoft.StreamResults;
using System.Diagnostics;

var source = @"C:\Users\davep\Downloads\TNG\Season 1";
//source = @"C:\Temp\Zip\Data";
var target = $@"{source}.zip";

var zr = new ZippedResult(source);

MeasureFunction("Content-Length calculation", () => zr.ContentLength, cl =>
{
    Console.WriteLine($"Content-Length: {cl} or {cl.Bytes()}");
});

MeasureAction("Generating Zip on the fly", () =>
{
    using (var file = File.Open(target, FileMode.CreateNew))
    {
        zr.StreamTo(file);
    }
}, () => Console.WriteLine($"File Length:    {new FileInfo(target).Length} or {new FileInfo(target).Length.Bytes()}"));




void MeasureAction(string description, Action action, Action display)
{
    var stopwatch = new Stopwatch();
    stopwatch.Start();
    action();
    stopwatch.Stop();
    display();
    Console.WriteLine($"{description} took {stopwatch.Elapsed.Humanize()}");
}

void MeasureFunction<T>(string description, Func<T> function, Action<T> display)
{
    var stopwatch = new Stopwatch();
    stopwatch.Start();
    var result = function();
    stopwatch.Stop();
    display(result);
    Console.WriteLine($"{description} took {stopwatch.Elapsed.Humanize()}");
}