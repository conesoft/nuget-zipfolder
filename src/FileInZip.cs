using System.Text;

namespace Conesoft.ZipFolder;

record FileInZip(string Name, Stream Stream, long Size, DateTime LastModified)
{
    public ulong Offset { get; set; } = 0;
    public ushort TimeBits => (ushort)((LastModified.Second / 2) | LastModified.Minute << 5 | LastModified.Hour << 11);
    public ushort DateBits => (ushort)(LastModified.Day | LastModified.Month << 5 | (LastModified.Year - 1980) << 9);
    public uint CrcBits { get; set; } = 0;
    public byte[] NameAsBytes => Encoding.UTF8.GetBytes(Name);
}
