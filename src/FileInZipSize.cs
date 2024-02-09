using System.Text;

namespace Conesoft.ZipFolder;

record FileInZipSize(string Name, long Size)
{
    public byte[] NameAsBytes => Encoding.UTF8.GetBytes(Name);
}