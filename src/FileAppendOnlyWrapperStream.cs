﻿namespace Conesoft.ZipFolder;

internal class FileAppendOnlyWrapperStream : Stream
{
    private readonly Stream stream;
    private long position;

    public FileAppendOnlyWrapperStream(Stream stream)
    {
        this.stream = stream;
        this.position = 0;
    }

    public override bool CanSeek { get { return false; } }
    public override bool CanWrite { get { return true; } }

    public override long Position
    {
        get { return position; }
        set { throw new NotSupportedException(); }
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        position += count;
        stream.Write(buffer, offset, count);
    }

    public override void Flush()
    {
        stream.Flush();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            stream.Dispose();
        }
        base.Dispose(disposing);
    }

    /* implementation garbage */
    public override long Seek(long offset, SeekOrigin origin) => throw new NotImplementedException();
    public override void SetLength(long value) => throw new NotImplementedException();
    public override bool CanRead => throw new NotImplementedException();
    public override int Read(byte[] buffer, int offset, int count) => throw new NotImplementedException();
    public override long Length => throw new NotImplementedException();
}