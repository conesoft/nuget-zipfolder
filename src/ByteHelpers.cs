using System.ComponentModel;

namespace Conesoft.ZipFolder;

[EditorBrowsable(EditorBrowsableState.Never)]
static public class ByteHelpers
{
    static public Stream WriteBytes(this Stream stream, params byte[] bytes)
    {
        stream.Write(bytes);
        return stream;
    }

    static public Stream Write_8(this Stream stream, params byte[] bytes)
    {
        stream.Write(bytes);
        return stream;
    }

    static public Stream Write16(this Stream stream, params UInt16[] values)
    {
        foreach (var value in values)
        {
            stream.WriteByte((byte)(value >> 0));
            stream.WriteByte((byte)(value >> 8));
        }
        return stream;
    }

    static public Stream Write32(this Stream stream, params UInt32[] values)
    {
        foreach (var value in values)
        {
            stream.WriteByte((byte)(value >>  0));
            stream.WriteByte((byte)(value >>  8));
            stream.WriteByte((byte)(value >> 16));
            stream.WriteByte((byte)(value >> 24));
        }
        return stream;
    }


    static public Stream Write64(this Stream stream, params UInt64[] values)
    {
        foreach (var value in values)
        {
            stream.WriteByte((byte)(value >>  0));
            stream.WriteByte((byte)(value >>  8));
            stream.WriteByte((byte)(value >> 16));
            stream.WriteByte((byte)(value >> 24));
            stream.WriteByte((byte)(value >> 32));
            stream.WriteByte((byte)(value >> 40));
            stream.WriteByte((byte)(value >> 48));
            stream.WriteByte((byte)(value >> 56));
        }
        return stream;
    }
}