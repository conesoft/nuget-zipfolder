// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.


// Zip Spec here: http://www.pkware.com/documents/casestudies/APPNOTE.TXT

using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text;

namespace SystemIOCompression
{
    public class ZipArchive : IDisposable
    {
        private readonly PositionWrapperStream _archiveStream;
        private ZipArchiveEntry? _archiveStreamOwner;
        private readonly BinaryReader? _archiveReader;
        private readonly List<ZipArchiveEntry> _entries;
        private readonly ReadOnlyCollection<ZipArchiveEntry> _entriesCollection;
        private readonly Dictionary<string, ZipArchiveEntry> _entriesDictionary;
        private bool _readEntries;
        private readonly bool _leaveOpen;
        //private long _centralDirectoryStart; //only valid after ReadCentralDirectory
        private bool _isDisposed;
        private uint _numberOfThisDisk; //only valid after ReadCentralDirectory
        //private long _expectedNumberOfEntries;
        private readonly Stream? _backingStream;
        private byte[] _archiveComment;
        private Encoding? _entryNameAndCommentEncoding;

        public ZipArchive(PositionWrapperStream stream, bool leaveOpen, Encoding? entryNameEncoding)
        {
            ArgumentNullException.ThrowIfNull(stream);

            EntryNameAndCommentEncoding = entryNameEncoding;
            Stream? extraTempStream = null;

            try
            {
                _backingStream = null;

                if (!stream.CanWrite)
                    throw new ArgumentException();

                _archiveStream = stream;
                _archiveStreamOwner = null;
                _archiveReader = null;
                _entries = new List<ZipArchiveEntry>();
                _entriesCollection = new ReadOnlyCollection<ZipArchiveEntry>(_entries);
                _entriesDictionary = new Dictionary<string, ZipArchiveEntry>();
                _readEntries = false;
                _leaveOpen = leaveOpen;
                //_centralDirectoryStart = 0; // invalid until ReadCentralDirectory
                _isDisposed = false;
                //_numberOfThisDisk = 0; // invalid until ReadCentralDirectory
                _archiveComment = Array.Empty<byte>();

                _readEntries = true;
            }
            catch
            {
                extraTempStream?.Dispose();

                throw;
            }
        }

        public ZipArchiveEntry CreateEntry(string entryName)
        {
            return DoCreateEntry(entryName);
        }

        /// <summary>
        /// Releases the unmanaged resources used by ZipArchive and optionally finishes writing the archive and releases the managed resources.
        /// </summary>
        /// <param name="disposing">true to finish writing the archive and release unmanaged and managed resources, false to release only unmanaged resources.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (disposing && !_isDisposed)
            {
                try
                {
                    WriteFile();
                }
                finally
                {
                    CloseStreams();
                    _isDisposed = true;
                }
            }
        }

        /// <summary>
        /// Finishes writing the archive and releases all resources used by the ZipArchive object, unless the object was constructed with leaveOpen as true. Any streams from opened entries in the ZipArchive still open will throw exceptions on subsequent writes, as the underlying streams will have been closed.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }


        internal BinaryReader? ArchiveReader => _archiveReader;

        internal PositionWrapperStream ArchiveStream => _archiveStream;

        internal uint NumberOfThisDisk => _numberOfThisDisk;

        internal Encoding? EntryNameAndCommentEncoding
        {
            get => _entryNameAndCommentEncoding;

            private set
            {
                // value == null is fine. This means the user does not want to overwrite default encoding picking logic.

                // The Zip file spec [http://www.pkware.com/documents/casestudies/APPNOTE.TXT] specifies a bit in the entry header
                // (specifically: the language encoding flag (EFS) in the general purpose bit flag of the local file header) that
                // basically says: UTF8 (1) or CP437 (0). But in reality, tools replace CP437 with "something else that is not UTF8".
                // For instance, the Windows Shell Zip tool takes "something else" to mean "the local system codepage".
                // We default to the same behaviour, but we let the user explicitly specify the encoding to use for cases where they
                // understand their use case well enough.
                // Since the definition of acceptable encodings for the "something else" case is in reality by convention, it is not
                // immediately clear, whether non-UTF8 Unicode encodings are acceptable. To determine that we would need to survey
                // what is currently being done in the field, but we do not have the time for it right now.
                // So, we artificially disallow non-UTF8 Unicode encodings for now to make sure we are not creating a compat burden
                // for something other tools do not support. If we realise in future that "something else" should include non-UTF8
                // Unicode encodings, we can remove this restriction.

                if (value != null &&
                        (value.Equals(Encoding.BigEndianUnicode)
                        || value.Equals(Encoding.Unicode)))
                {
                    throw new ArgumentException(nameof(EntryNameAndCommentEncoding));
                }

                _entryNameAndCommentEncoding = value;
            }
        }

        private ZipArchiveEntry DoCreateEntry(string entryName)
        {
            ArgumentException.ThrowIfNullOrEmpty(entryName);

            ThrowIfDisposed();


            ZipArchiveEntry entry = new(this, entryName);

            AddEntry(entry);

            return entry;
        }

        internal void AcquireArchiveStream(ZipArchiveEntry entry)
        {
            // if a previous entry had held the stream but never wrote anything, we write their local header for them
            if (_archiveStreamOwner != null)
            {
                if (!_archiveStreamOwner.EverOpenedForWrite)
                {
                    _archiveStreamOwner.WriteAndFinishLocalEntry();
                }
                else
                {
                    throw new IOException();
                }
            }

            _archiveStreamOwner = entry;
        }

        private void AddEntry(ZipArchiveEntry entry)
        {
            _entries.Add(entry);
            _entriesDictionary.TryAdd(entry.FullName, entry);
        }

        [Conditional("DEBUG")]
        internal void DebugAssertIsStillArchiveStreamOwner(ZipArchiveEntry entry) => Debug.Assert(_archiveStreamOwner == entry);

        internal void ReleaseArchiveStream(ZipArchiveEntry entry)
        {
            Debug.Assert(_archiveStreamOwner == entry);

            _archiveStreamOwner = null;
        }


        internal void ThrowIfDisposed()
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);
        }

        private void CloseStreams()
        {
            if (!_leaveOpen)
            {
                _archiveStream.Dispose();
                _backingStream?.Dispose();
                _archiveReader?.Dispose();
            }
            else
            {
                // if _backingStream isn't null, that means we assigned the original stream they passed
                // us to _backingStream (which they requested we leave open), and _archiveStream was
                // the temporary copy that we needed
                if (_backingStream != null)
                    _archiveStream.Dispose();
            }
        }

        private void WriteFile()
        {
            // if we are in create mode, we always set readEntries to true in Init
            // if we are in update mode, we call EnsureCentralDirectoryRead, which sets readEntries to true
            Debug.Assert(_readEntries);

            foreach (ZipArchiveEntry entry in _entries)
            {
                entry.WriteAndFinishLocalEntry();
            }

            long startOfCentralDirectory = _archiveStream.Position;

            foreach (ZipArchiveEntry entry in _entries)
            {
                entry.WriteCentralDirectoryFileHeader();
            }

            long sizeOfCentralDirectory = _archiveStream.Position - startOfCentralDirectory;

            WriteArchiveEpilogue(startOfCentralDirectory, sizeOfCentralDirectory);
        }

        // writes eocd, and if needed, zip 64 eocd, zip64 eocd locator
        // should only throw an exception in extremely exceptional cases because it is called from dispose
        private void WriteArchiveEpilogue(long startOfCentralDirectory, long sizeOfCentralDirectory)
        {
            // determine if we need Zip 64
            if (startOfCentralDirectory >= uint.MaxValue
                || sizeOfCentralDirectory >= uint.MaxValue
                || _entries.Count >= ZipHelper.Mask16Bit
#if DEBUG_FORCE_ZIP64
                || _forceZip64
#endif
                )
            {
                // if we need zip 64, write zip 64 eocd and locator
                long zip64EOCDRecordStart = _archiveStream.Position;
                Zip64EndOfCentralDirectoryRecord.WriteBlock(_archiveStream, _entries.Count, startOfCentralDirectory, sizeOfCentralDirectory);
                Zip64EndOfCentralDirectoryLocator.WriteBlock(_archiveStream, zip64EOCDRecordStart);
            }

            // write normal eocd
            ZipEndOfCentralDirectoryBlock.WriteBlock(_archiveStream, _entries.Count, startOfCentralDirectory, sizeOfCentralDirectory, _archiveComment);
        }
    }
}
