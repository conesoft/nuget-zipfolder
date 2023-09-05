class PositionWrapperStream : Stream
{
    private long pos = 0;

    public override bool CanSeek { get { return false; } }
    public override bool CanWrite { get { return true; } }

    public override long Position
    {
        get { return pos; }
        set { throw new NotSupportedException(); }
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        pos += count;
    }

    public override void Flush()
    {
    }

    /* implementation garbage */
    public override long Seek(long offset, SeekOrigin origin) => throw new NotImplementedException();
    public override void SetLength(long value) => throw new NotImplementedException();
    public override bool CanRead => throw new NotImplementedException();
    public override int Read(byte[] buffer, int offset, int count) =>  throw new NotImplementedException();
    public override long Length => throw new NotImplementedException();
}
