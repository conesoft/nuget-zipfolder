using System.IO.Hashing;

/// https://pkware.cachefly.net/webdocs/casestudies/APPNOTE.TXT
/// https://rzymek.github.io/post/excel-zip64/
namespace Conesoft.ZipFolder;

static public class Zip
{
    const ushort bitflags = 0b0000_1000_0000_1000; // (bit 3 for Data Descriptor at End, bit 11 for UTF-8)

    static public long CalculateSize(params Source[] sources) => CalculateSize((IEnumerable<Source>)sources);
    static public long CalculateSize(IEnumerable<string> sources) => CalculateSize(sources.Select(s => new Source(s)));
    static public long CalculateSize(IEnumerable<Source> sources)
    {
        var files = sources.UnpackDirectories().Select(f => new FileInZipSize(
            Name: Path.Combine(f.To, Path.GetFileName(f.From)),
            Size: new FileInfo(f.From).Length
        ));

        int zip64offsetReached = 0;
        ulong offset = 0;
        foreach (var file in files)
        {
            if (offset >= 0xFFFFFFFF)
            {
                zip64offsetReached++;
            }
            offset += (ulong)(50 + file.NameAsBytes.Length + file.Size);
        }

        return files.Sum(f => 50 + f.NameAsBytes.Length + f.Size + 66 + f.NameAsBytes.Length)
            + zip64offsetReached * 8
            + 98;
    }


    static public void ZipSources(this Stream zip, params Source[] sources) => zip.ZipSources((IEnumerable<Source>)sources);
    static public void ZipSources(this Stream zip, IEnumerable<string> sources) => zip.ZipSources(sources.Select(s => new Source(s)));
    static public void ZipSources(this Stream zip, IEnumerable<Source> sources)
    {
        var files = sources.UnpackDirectories().Select(f => new FileInZip(
            Name: Path.Combine(f.To, Path.GetFileName(f.From)),
            Stream: File.OpenRead(f.From),
            Size: new FileInfo(f.From).Length,
            LastModified: new FileInfo(f.From).LastWriteTime
        )).ToArray();

        ulong position = 0;

        /// [local file entries]
        foreach (var file in files)
        {
            position += zip.WriteFileEntry(file, position);
            file.Stream.Close();
        }

        /// [central directory]
        var start = position;

        ulong length = 0;
        foreach (var file in files)
        {
            length += zip.WriteCentralDirectoryEntry(file);
        }

        zip.WriteEndOfCentralDirectory(count: (ulong)files.Length, offset: start, length: length);
    }

    static ulong WriteFileEntry(this Stream zip, FileInZip file, ulong offset)
    {
        file.Offset = offset;

        // 4 + 2*5 + 4*3 + 2*2 + ... + ... + 4 + 8*2 = 50 + filename.Length + file.Length

        zip
            .Write_8(0x50, 0x4b, 0x03, 0x04)                                  /// header [local file header]
            .Write16(45, bitflags, 0, file.TimeBits, file.DateBits)           // version (45 = ZIP64) | general purpose bitflag | compression method (0 = store) | time | date
            .Write32(0, 0, 0)                                                 // CRC bits | compressed size | uncompressed size => 0 each for data descriptor
            .Write16((ushort)file.NameAsBytes.Length, 0)                      // filename length | extrafield size
            .Write_8(file.NameAsBytes)                                        // filename

            .WriteStreamAndComputeCrc(file.Stream, crc => file.CrcBits = crc) /// write the actual data and calculate crc

            .Write32(file.CrcBits)                                            // CRC bits
            .Write64((ulong)file.Size, (ulong)file.Size)                      // compressed size: ZIP64 extra | uncompressed size: ZIP64 extra
        ;

        return (ulong)(50 + file.NameAsBytes.Length + file.Size);
    }

    static ulong WriteCentralDirectoryEntry(this Stream zip, FileInZip file)
    {
        // 4 + 2*6 + 4*3 + 2*5 + 4*2 + ... + 2 + 2 + 8*2 = 74 + filename.Length

        var zip64offset = file.Offset >= 0xFFFFFFFF;

        zip
            .Write_8(0x50, 0x4b, 0x01, 0x02)                              /// header [central directory header]
            .Write16(45, 45, bitflags, 0, file.TimeBits, file.DateBits)   // version (ZIP64) | min version to extract (ZIP64) | general purpose bitflag (bit 3 for Data Descriptor at End, bit 11 for UTF-8) | compression method (0 = store) | time | date
            .Write32(file.CrcBits, 0xFFFFFFFF, 0xFFFFFFFF)                // CRC bits | compressed size | uncompressed size => FFFFFFFF for ZIP64
            .Write16((ushort)file.NameAsBytes.Length)                     // filename length
            .Write16(zip64offset ? (ushort)28 : (ushort)20, 0, 0, 0)      // extrafield length | file comment length | disk number | internal file attributes
            .Write32(0, zip64offset ? 0xFFFFFFFF : (uint)file.Offset)     // external file attributes, offset of file
            .Write_8(file.NameAsBytes)                                    // filename

            .Write_8(0x01, 0x00)                                          /// extrafield header
            .Write16(zip64offset ? (ushort)24 : (ushort)16)               // size of extrafield (below)
            .Write64((ulong)file.Size, (ulong)file.Size)                  // compressed size: ZIP64 extra | uncompressed size: ZIP64 extra
        ;

        if (zip64offset)
        {
            zip.Write64(file.Offset);
        }

        return (ulong)(66 + file.NameAsBytes.Length + (file.Offset >= 0xFFFFFFFF ? 8 : 0));
    }

    static Stream WriteEndOfCentralDirectory(this Stream zip, ulong count, ulong offset, ulong length)
    {
        //   4 + 8 + 2*2 + 4*2 + 8*4
        // + 4 + 4 + 8 + 4
        // + 4 + 2*4 + 4 + 4 + 2
        // = 98

        return zip
            .Write_8(0x50, 0x4b, 0x06, 0x06)       /// header [zip64 end of central directory record]
            .Write64(44)                           // size of remaining record is 56 bytes
            .Write16(45, 45)                       // version (ZIP64) | min version to extract (ZIP64)
            .Write32(0, 0)                         // number of this disk | number of the disk with the start of the central directory
            .Write64(count, count, length, offset) // total number of entries in the central directory on this disk | total number of entries in the central directory | size of central directory | offset of start of central directory with respect to the starting disk number

            .Write_8(0x50, 0x4b, 0x06, 0x07)       /// header [zip64 end of central directory locator]
            .Write32(0)                            // number of the disk with the start of the zip64 end of central directory
            .Write64(offset + length)              // relative offset of the zip64 end of central directory record
            .Write32(1)                            // total number of disks

            .Write_8(0x50, 0x4b, 0x05, 0x06)       /// header [end of central directory record]
            .Write16(0, 0, 0xFFFF, 0xFFFF)         // disk number | starting disk | central directory number | central directory amount
            .Write32(0xFFFFFFFF)                   // central directory sizes
            .Write32(0xFFFFFFFF)                   // central directory offset
            .Write16(0)
        ;
    }

    static readonly byte[] buff = new byte[1024 * 1024 * 4];

    static Stream WriteStreamAndComputeCrc(this Stream output, Stream input, Action<uint> calculatedCrc)
    {
        var crc = new Crc32();
        int length;
        while ((length = input.Read(buff)) > 0)
        {
            var bytes = new ReadOnlySpan<byte>(buff, 0, length);
            crc.Append(bytes);
            output.Write(bytes);
        }
        calculatedCrc(crc.GetCurrentHashAsUInt32());
        return output;
    }
}
