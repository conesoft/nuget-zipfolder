using System.ComponentModel;

namespace Conesoft.ZipFolder;

[EditorBrowsable(EditorBrowsableState.Never)]
static public class ByteHelpers
{
    static public T WriteBytes<T>(this T stream, params byte[] bytes) where T : Stream
    {
        stream.Write(bytes);
        return stream;
    }

    static public T Write_8<T>(this T stream, params byte[] bytes) where T : Stream
    {
        stream.Write(bytes);
        return stream;
    }

    static public T Write16<T>(this T stream, params UInt16[] values) where T : Stream
    {
        foreach (var value in values)
        {
            stream.WriteByte((byte)(value >> 0));
            stream.WriteByte((byte)(value >> 8));
        }
        return stream;
    }

    static public T Write32<T>(this T stream, params UInt32[] values) where T : Stream
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


    static public T Write64<T>(this T stream, params UInt64[] values) where T : Stream
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