using System.ComponentModel;

namespace Conesoft.ZipFolder;

[EditorBrowsable(EditorBrowsableState.Never)]
public static class ExtensionsForSource
{
    public static IEnumerable<Source> UnpackDirectories(this IEnumerable<Source> files)
    {
        foreach (var source in files)
        {
            if (Directory.Exists(source.From))
            {
                foreach (string file in Directory.GetFiles(source.From, "*", source.WithSubdirectories ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly))
                {
                    var filepath = (Path.GetDirectoryName(file) ?? "").TrimEnd('\\', '/');
                    var rootpath = Path.GetFullPath(source.From).TrimEnd('\\', '/');
                    var relative = filepath.Replace(rootpath, "").TrimStart('\\', '/');
                    var inziprelative = Path.Combine(source.To, relative).TrimStart('\\', '/');
                    yield return new Source(file, inziprelative);
                }
            }
            if (File.Exists(source.From))
            {
                yield return source;
            }
        }
    }
}
