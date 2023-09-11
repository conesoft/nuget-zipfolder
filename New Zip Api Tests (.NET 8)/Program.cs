using Conesoft.StreamResults;

var source = @"C:\Users\davep\Downloads\TNG\Season 1";
source = @"C:\Temp\Zip\Data";
var target = $@"{source}.zip";

var zr = new ZippedResult(source);

Console.WriteLine("Content-Length: " + zr.ContentLength);

using (var file = File.Open(target, FileMode.CreateNew))
{
    zr.StreamTo(file);
}

Console.WriteLine("File Length:    " + new FileInfo(target).Length);