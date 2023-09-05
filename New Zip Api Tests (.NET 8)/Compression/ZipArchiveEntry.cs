// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text;

using static SystemIOCompression.ZipArchiveEntryConstants;

namespace SystemIOCompression
{
    // The disposable fields that this class owns get disposed when the ZipArchive it belongs to gets disposed
    public partial class ZipArchiveEntry
    {
        private ZipArchive _archive;
        private readonly bool _originallyInArchive;
        private readonly uint _diskNumberStart;
        private readonly ZipVersionMadeByPlatform _versionMadeByPlatform;
        private ZipVersionNeededValues _versionMadeBySpecification;
        internal ZipVersionNeededValues _versionToExtract;
        private BitFlagValues _generalPurposeBitFlag;
        private readonly bool _isEncrypted;
        private CompressionMethodValues _storedCompressionMethod;
        private DateTimeOffset _lastModified;
        private long _compressedSize;
        private long _uncompressedSize;
        private long _offsetOfLocalHeader;
        private long? _storedOffsetOfCompressedData;
        private uint _crc32;
        // An array of buffers, each a maximum of MaxSingleBufferSize in size
        private byte[][]? _compressedBytes;
        private MemoryStream? _storedUncompressedData;
        private bool _currentlyOpenForWrite;
        private bool _everOpenedForWrite;
        private DirectToArchiveWriterStream? _outstandingWriteStream;
        private uint _externalFileAttr;
        private string _storedEntryName;
        private byte[] _storedEntryNameBytes;
        // only apply to update mode
        private List<ZipGenericExtraField>? _cdUnknownExtraFields;
        private List<ZipGenericExtraField>? _lhUnknownExtraFields;
        private byte[] _fileComment;
        private readonly CompressionLevel? _compressionLevel;

        // Initializes a ZipArchiveEntry instance for an existing archive entry.
        internal ZipArchiveEntry(ZipArchive archive, ZipCentralDirectoryFileHeader cd)
        {
            _archive = archive;

            _originallyInArchive = true;

            _diskNumberStart = cd.DiskNumberStart;
            _versionMadeByPlatform = (ZipVersionMadeByPlatform)cd.VersionMadeByCompatibility;
            _versionMadeBySpecification = (ZipVersionNeededValues)cd.VersionMadeBySpecification;
            _versionToExtract = (ZipVersionNeededValues)cd.VersionNeededToExtract;
            _generalPurposeBitFlag = (BitFlagValues)cd.GeneralPurposeBitFlag;
            _isEncrypted = (_generalPurposeBitFlag & BitFlagValues.IsEncrypted) != 0;
            CompressionMethod = (CompressionMethodValues)cd.CompressionMethod;
            _lastModified = new DateTimeOffset(ZipHelper.DosTimeToDateTime(cd.LastModified));
            _compressedSize = cd.CompressedSize;
            _uncompressedSize = cd.UncompressedSize;
            _externalFileAttr = cd.ExternalFileAttributes;
            _offsetOfLocalHeader = cd.RelativeOffsetOfLocalHeader;
            // we don't know this yet: should be _offsetOfLocalHeader + 30 + _storedEntryNameBytes.Length + extrafieldlength
            // but entryname/extra length could be different in LH
            _storedOffsetOfCompressedData = null;
            _crc32 = cd.Crc32;

            _compressedBytes = null;
            _storedUncompressedData = null;
            _currentlyOpenForWrite = false;
            _everOpenedForWrite = false;
            _outstandingWriteStream = null;

            _storedEntryNameBytes = cd.Filename;
            _storedEntryName = (_archive.EntryNameAndCommentEncoding ?? Encoding.UTF8).GetString(_storedEntryNameBytes);
            DetectEntryNameVersion();

            _lhUnknownExtraFields = null;
            // the cd should have this as null if we aren't in Update mode
            _cdUnknownExtraFields = cd.ExtraFields;

            _fileComment = cd.FileComment;

            _compressionLevel = null;
        }

        // Initializes a ZipArchiveEntry instance for a new archive entry with a specified compression level.
        internal ZipArchiveEntry(ZipArchive archive, string entryName, CompressionLevel compressionLevel)
            : this(archive, entryName)
        {
            _compressionLevel = compressionLevel;
            if (_compressionLevel == CompressionLevel.NoCompression)
            {
                CompressionMethod = CompressionMethodValues.Stored;
            }
        }

        // Initializes a ZipArchiveEntry instance for a new archive entry.
        internal ZipArchiveEntry(ZipArchive archive, string entryName)
        {
            _archive = archive;

            _originallyInArchive = false;

            _diskNumberStart = 0;
            _versionMadeByPlatform = CurrentZipPlatform;
            _versionMadeBySpecification = ZipVersionNeededValues.Default;
            _versionToExtract = ZipVersionNeededValues.Default; // this must happen before following two assignment
            _generalPurposeBitFlag = 0;
            CompressionMethod = CompressionMethodValues.Stored;
            _lastModified = DateTimeOffset.Now;

            _compressedSize = 0; // we don't know these yet
            _uncompressedSize = 0;
            _externalFileAttr = entryName.EndsWith(Path.DirectorySeparatorChar) || entryName.EndsWith(Path.AltDirectorySeparatorChar)
                                        ? DefaultDirectoryExternalAttributes
                                        : DefaultFileExternalAttributes;

            _offsetOfLocalHeader = 0;
            _storedOffsetOfCompressedData = null;
            _crc32 = 0;

            _compressedBytes = null;
            _storedUncompressedData = null;
            _currentlyOpenForWrite = false;
            _everOpenedForWrite = false;
            _outstandingWriteStream = null;

            FullName = entryName;

            _cdUnknownExtraFields = null;
            _lhUnknownExtraFields = null;

            _fileComment = Array.Empty<byte>();

            _compressionLevel = null;

            if (_storedEntryNameBytes.Length > ushort.MaxValue)
                throw new ArgumentException();

            // grab the stream if we're in create mode
            //if (_archive.Mode == ZipArchiveMode.Create)
            {
                _archive.AcquireArchiveStream(this);
            }
        }

        /// <summary>
        /// The relative path of the entry as stored in the Zip archive. Note that Zip archives allow any string to be the path of the entry, including invalid and absolute paths.
        /// </summary>
        public string FullName
        {
            get
            {
                return _storedEntryName;
            }

            [MemberNotNull(nameof(_storedEntryNameBytes))]
            [MemberNotNull(nameof(_storedEntryName))]
            private set
            {
                ArgumentNullException.ThrowIfNull(value, nameof(FullName));

                _storedEntryNameBytes = ZipHelper.GetEncodedTruncatedBytesFromString(
                    value, _archive.EntryNameAndCommentEncoding, 0 /* No truncation */, out bool isUTF8);

                _storedEntryName = value;

                if (isUTF8)
                {
                    _generalPurposeBitFlag |= BitFlagValues.UnicodeFileNameAndComment;
                }
                else
                {
                    _generalPurposeBitFlag &= ~BitFlagValues.UnicodeFileNameAndComment;
                }

                DetectEntryNameVersion();
            }
        }

        /// <summary>
        /// The last write time of the entry as stored in the Zip archive. When setting this property, the DateTime will be converted to the
        /// Zip timestamp format, which supports a resolution of two seconds. If the data in the last write time field is not a valid Zip timestamp,
        /// an indicator value of 1980 January 1 at midnight will be returned.
        /// </summary>
        /// <exception cref="NotSupportedException">An attempt to set this property was made, but the ZipArchive that this entry belongs to was
        /// opened in read-only mode.</exception>
        /// <exception cref="ArgumentOutOfRangeException">An attempt was made to set this property to a value that cannot be represented in the
        /// Zip timestamp format. The earliest date/time that can be represented is 1980 January 1 0:00:00 (midnight), and the last date/time
        /// that can be represented is 2107 December 31 23:59:58 (one second before midnight).</exception>
        public DateTimeOffset LastWriteTime
        {
            get
            {
                return _lastModified;
            }
        }

        /// <summary>
        /// Opens the entry. If the archive that the entry belongs to was opened in Read mode, the returned stream will be readable, and it may or may not be seekable. If Create mode, the returned stream will be writable and not seekable. If Update mode, the returned stream will be readable, writable, seekable, and support SetLength.
        /// </summary>
        /// <returns>A Stream that represents the contents of the entry.</returns>
        /// <exception cref="IOException">The entry is already currently open for writing. -or- The entry has been deleted from the archive. -or- The archive that this entry belongs to was opened in ZipArchiveMode.Create, and this entry has already been written to once.</exception>
        /// <exception cref="InvalidDataException">The entry is missing from the archive or is corrupt and cannot be read. -or- The entry has been compressed using a compression method that is not supported.</exception>
        /// <exception cref="ObjectDisposedException">The ZipArchive that this entry belongs to has been disposed.</exception>
        public WrappedStream Open()
        {
            ThrowIfInvalidArchive();
            return OpenInWriteMode();
        }

        // Only allow opening ZipArchives with large ZipArchiveEntries in update mode when running in a 64-bit process.
        // This is for compatibility with old behavior that threw an exception for all process bitnesses, because this
        // will not work in a 32-bit process.
        private static readonly bool s_allowLargeZipArchiveEntriesInUpdateMode = IntPtr.Size > 4;

        internal bool EverOpenedForWrite => _everOpenedForWrite;

        private long OffsetOfCompressedData
        {
            get
            {
                if (_storedOffsetOfCompressedData == null)
                {
                    Debug.Assert(_archive.ArchiveReader != null);
                    _archive.ArchiveStream.Seek(_offsetOfLocalHeader, SeekOrigin.Begin);
                    // by calling this, we are using local header _storedEntryNameBytes.Length and extraFieldLength
                    // to find start of data, but still using central directory size information
                    if (!ZipLocalFileHeader.TrySkipBlock(_archive.ArchiveReader))
                        throw new InvalidDataException();
                    _storedOffsetOfCompressedData = _archive.ArchiveStream.Position;
                }
                return _storedOffsetOfCompressedData.Value;
            }
        }

        private CompressionMethodValues CompressionMethod
        {
            get { return _storedCompressionMethod; }
            set
            { _storedCompressionMethod = value; }
        }

        // does almost everything you need to do to forget about this entry
        // writes the local header/data, gets rid of all the data,
        // closes all of the streams except for the very outermost one that
        // the user holds on to and is responsible for closing
        //
        // after calling this, and only after calling this can we be guaranteed
        // that we are reading to write the central directory
        //
        // should only throw an exception in extremely exceptional cases because it is called from dispose
        internal void WriteAndFinishLocalEntry()
        {
            CloseStreams();
            WriteLocalFileHeaderAndDataIfNeeded();
            UnloadStreams();
        }

        // should only throw an exception in extremely exceptional cases because it is called from dispose
        internal void WriteCentralDirectoryFileHeader()
        {
            // This part is simple, because we should definitely know the sizes by this time
            BinaryWriter writer = new BinaryWriter(_archive.ArchiveStream);

            // _entryname only gets set when we read in or call moveTo. MoveTo does a check, and
            // reading in should not be able to produce an entryname longer than ushort.MaxValue
            Debug.Assert(_storedEntryNameBytes.Length <= ushort.MaxValue);

            // decide if we need the Zip64 extra field:
            Zip64ExtraField zip64ExtraField = default;
            uint compressedSizeTruncated, uncompressedSizeTruncated, offsetOfLocalHeaderTruncated;

            bool zip64Needed = false;

            if (SizesTooLarge()
#if DEBUG_FORCE_ZIP64
                || _archive._forceZip64
#endif
                )
            {
                zip64Needed = true;
                compressedSizeTruncated = ZipHelper.Mask32Bit;
                uncompressedSizeTruncated = ZipHelper.Mask32Bit;

                // If we have one of the sizes, the other must go in there as speced for LH, but not necessarily for CH, but we do it anyways
                zip64ExtraField.CompressedSize = _compressedSize;
                zip64ExtraField.UncompressedSize = _uncompressedSize;
            }
            else
            {
                compressedSizeTruncated = (uint)_compressedSize;
                uncompressedSizeTruncated = (uint)_uncompressedSize;
            }


            if (_offsetOfLocalHeader > uint.MaxValue
#if DEBUG_FORCE_ZIP64
                || _archive._forceZip64
#endif
                )
            {
                zip64Needed = true;
                offsetOfLocalHeaderTruncated = ZipHelper.Mask32Bit;

                // If we have one of the sizes, the other must go in there as speced for LH, but not necessarily for CH, but we do it anyways
                zip64ExtraField.LocalHeaderOffset = _offsetOfLocalHeader;
            }
            else
            {
                offsetOfLocalHeaderTruncated = (uint)_offsetOfLocalHeader;
            }

            if (zip64Needed)
                VersionToExtractAtLeast(ZipVersionNeededValues.Zip64);

            // determine if we can fit zip64 extra field and original extra fields all in
            int bigExtraFieldLength = (zip64Needed ? zip64ExtraField.TotalSize : 0)
                                      + (_cdUnknownExtraFields != null ? ZipGenericExtraField.TotalSize(_cdUnknownExtraFields) : 0);
            ushort extraFieldLength;
            if (bigExtraFieldLength > ushort.MaxValue)
            {
                extraFieldLength = (ushort)(zip64Needed ? zip64ExtraField.TotalSize : 0);
                _cdUnknownExtraFields = null;
            }
            else
            {
                extraFieldLength = (ushort)bigExtraFieldLength;
            }

            writer.Write(ZipCentralDirectoryFileHeader.SignatureConstant);      // Central directory file header signature  (4 bytes)
            writer.Write((byte)_versionMadeBySpecification);                    // Version made by Specification (version)  (1 byte)
            writer.Write((byte)CurrentZipPlatform);                             // Version made by Compatibility (type)     (1 byte)
            writer.Write((ushort)_versionToExtract);                            // Minimum version needed to extract        (2 bytes)
            writer.Write((ushort)_generalPurposeBitFlag);                       // General Purpose bit flag                 (2 bytes)
            writer.Write((ushort)CompressionMethod);                            // The Compression method                   (2 bytes)
            writer.Write(ZipHelper.DateTimeToDosTime(_lastModified.DateTime));  // File last modification time and date     (4 bytes)
            writer.Write(_crc32);                                               // CRC-32                                   (4 bytes)
            writer.Write(compressedSizeTruncated);                              // Compressed Size                          (4 bytes)
            writer.Write(uncompressedSizeTruncated);                            // Uncompressed Size                        (4 bytes)
            writer.Write((ushort)_storedEntryNameBytes.Length);                 // File Name Length                         (2 bytes)
            writer.Write(extraFieldLength);                                     // Extra Field Length                       (2 bytes)

            Debug.Assert(_fileComment.Length <= ushort.MaxValue);

            writer.Write((ushort)_fileComment.Length);
            writer.Write((ushort)0); // disk number start
            writer.Write((ushort)0); // internal file attributes
            writer.Write(_externalFileAttr); // external file attributes
            writer.Write(offsetOfLocalHeaderTruncated); // offset of local header

            writer.Write(_storedEntryNameBytes);

            // write extra fields
            if (zip64Needed)
                zip64ExtraField.WriteBlock(_archive.ArchiveStream);
            if (_cdUnknownExtraFields != null)
                ZipGenericExtraField.WriteAllBlocks(_cdUnknownExtraFields, _archive.ArchiveStream);

            if (_fileComment.Length > 0)
                writer.Write(_fileComment);
        }

        private void DetectEntryNameVersion()
        {
            if (ParseFileName(_storedEntryName, _versionMadeByPlatform) == "")
            {
                VersionToExtractAtLeast(ZipVersionNeededValues.ExplicitDirectory);
            }
        }

        private CheckSumAndSizeWriteStream GetDataCompressor(PositionWrapperStream backingStream, bool leaveBackingStreamOpen, EventHandler? onClose)
        {
            bool isIntermediateStream = false;
            PositionWrapperStream compressorStream = backingStream;

            bool leaveCompressorStreamOpenOnClose = leaveBackingStreamOpen && !isIntermediateStream;
            var checkSumStream = new CheckSumAndSizeWriteStream(
                compressorStream,
                backingStream,
                leaveCompressorStreamOpenOnClose,
                this,
                onClose,
                (long initialPosition, long currentPosition, uint checkSum, Stream backing, ZipArchiveEntry thisRef, EventHandler? closeHandler) =>
                {
                    thisRef._crc32 = checkSum;
                    thisRef._uncompressedSize = currentPosition;
                    thisRef._compressedSize = backing.Position - initialPosition;
                    closeHandler?.Invoke(thisRef, EventArgs.Empty);
                });

            return checkSumStream;
        }

        private WrappedStream OpenInWriteMode()
        {
            if (_everOpenedForWrite)
                throw new IOException();

            // we assume that if another entry grabbed the archive stream, that it set this entry's _everOpenedForWrite property to true by calling WriteLocalFileHeaderIfNeeed
            _archive.DebugAssertIsStillArchiveStreamOwner(this);

            _everOpenedForWrite = true;
            CheckSumAndSizeWriteStream crcSizeStream = GetDataCompressor(_archive.ArchiveStream, true, (object? o, EventArgs e) =>
            {
                // release the archive stream
                var entry = (ZipArchiveEntry)o!;
                entry._archive.ReleaseArchiveStream(entry);
                entry._outstandingWriteStream = null;
            });
            _outstandingWriteStream = new DirectToArchiveWriterStream(crcSizeStream, this);

            return new WrappedStream(baseStream: _outstandingWriteStream, closeBaseStream: true);
        }

        private bool IsOpenable(bool needToUncompress, bool needToLoadIntoMemory, out string? message)
        {
            message = null;

            if (_originallyInArchive)
            {
                if (needToUncompress)
                {
                    if (CompressionMethod != CompressionMethodValues.Stored)
                    {

                                message = "UnsupportedCompression";
                        return false;
                    }
                }
                if (_diskNumberStart != _archive.NumberOfThisDisk)
                {
                    message = "SplitSpanned";
                    return false;
                }
                if (_offsetOfLocalHeader > _archive.ArchiveStream.Length)
                {
                    message = "SR.LocalFileHeaderCorrupt";
                    return false;
                }
                Debug.Assert(_archive.ArchiveReader != null);
                _archive.ArchiveStream.Seek(_offsetOfLocalHeader, SeekOrigin.Begin);
                if (!ZipLocalFileHeader.TrySkipBlock(_archive.ArchiveReader))
                {
                    message = "SR.LocalFileHeaderCorrupt";
                    return false;
                }
                // when this property gets called, some duplicated work
                if (OffsetOfCompressedData + _compressedSize > _archive.ArchiveStream.Length)
                {
                    message = "SR.LocalFileHeaderCorrupt";
                    return false;
                }
                // This limitation originally existed because a) it is unreasonable to load > 4GB into memory
                // but also because the stream reading functions make it hard.  This has been updated to handle
                // this scenario in a 64-bit process using multiple buffers, delivered first as an OOB for
                // compatibility.
                if (needToLoadIntoMemory)
                {
                    if (_compressedSize > int.MaxValue)
                    {
                        if (!s_allowLargeZipArchiveEntriesInUpdateMode)
                        {
                            message = "SR.EntryTooLarge";
                            return false;
                        }
                    }
                }
            }

            return true;
        }

        private bool SizesTooLarge() => _compressedSize > uint.MaxValue || _uncompressedSize > uint.MaxValue;

        // return value is true if we allocated an extra field for 64 bit headers, un/compressed size
        private bool WriteLocalFileHeader(bool isEmptyFile)
        {
            BinaryWriter writer = new BinaryWriter(_archive.ArchiveStream);

            // _entryname only gets set when we read in or call moveTo. MoveTo does a check, and
            // reading in should not be able to produce an entryname longer than ushort.MaxValue
            Debug.Assert(_storedEntryNameBytes.Length <= ushort.MaxValue);

            // decide if we need the Zip64 extra field:
            Zip64ExtraField zip64ExtraField = default;
            bool zip64Used = false;
            uint compressedSizeTruncated, uncompressedSizeTruncated;

            // if we already know that we have an empty file don't worry about anything, just do a straight shot of the header
            if (isEmptyFile)
            {
                CompressionMethod = CompressionMethodValues.Stored;
                compressedSizeTruncated = 0;
                uncompressedSizeTruncated = 0;
                Debug.Assert(_compressedSize == 0);
                //Debug.Assert(_uncompressedSize == 0);
                Debug.Assert(_crc32 == 0);
            }
            else
            {
                _generalPurposeBitFlag |= BitFlagValues.DataDescriptor;
                zip64Used = false;
                compressedSizeTruncated = 0;
                uncompressedSizeTruncated = 0;
                // the crc should not have been set if we are in create mode, but clear it just to be sure
                Debug.Assert(_crc32 == 0);
            }

            // save offset
            _offsetOfLocalHeader = writer.BaseStream.Position;

            // calculate extra field. if zip64 stuff + original extraField aren't going to fit, dump the original extraField, because this is more important
            int bigExtraFieldLength = (zip64Used ? zip64ExtraField.TotalSize : 0)
                                      + (_lhUnknownExtraFields != null ? ZipGenericExtraField.TotalSize(_lhUnknownExtraFields) : 0);
            ushort extraFieldLength;
            if (bigExtraFieldLength > ushort.MaxValue)
            {
                extraFieldLength = (ushort)(zip64Used ? zip64ExtraField.TotalSize : 0);
                _lhUnknownExtraFields = null;
            }
            else
            {
                extraFieldLength = (ushort)bigExtraFieldLength;
            }

            // write header
            writer.Write(ZipLocalFileHeader.SignatureConstant);
            writer.Write((ushort)_versionToExtract);
            writer.Write((ushort)_generalPurposeBitFlag);
            writer.Write((ushort)CompressionMethod);
            writer.Write(ZipHelper.DateTimeToDosTime(_lastModified.DateTime)); // uint
            writer.Write(_crc32); // uint
            writer.Write(compressedSizeTruncated); // uint
            writer.Write(uncompressedSizeTruncated); // uint
            writer.Write((ushort)_storedEntryNameBytes.Length);
            writer.Write(extraFieldLength); // ushort

            writer.Write(_storedEntryNameBytes);

            if (zip64Used)
                zip64ExtraField.WriteBlock(_archive.ArchiveStream);
            if (_lhUnknownExtraFields != null)
                ZipGenericExtraField.WriteAllBlocks(_lhUnknownExtraFields, _archive.ArchiveStream);

            return zip64Used;
        }

        private void WriteLocalFileHeaderAndDataIfNeeded()
        {
            // _storedUncompressedData gets frozen here, and is what gets written to the file
            if (_storedUncompressedData != null || _compressedBytes != null)
            {
                if (_storedUncompressedData != null)
                {
                    _uncompressedSize = _storedUncompressedData.Length;

                    //The compressor fills in CRC and sizes
                    //The DirectToArchiveWriterStream writes headers and such
                    using (Stream entryWriter = new DirectToArchiveWriterStream(
                                                    GetDataCompressor(_archive.ArchiveStream, true, null),
                                                    this))
                    {
                        _storedUncompressedData.Seek(0, SeekOrigin.Begin);
                        _storedUncompressedData.CopyTo(entryWriter);
                        _storedUncompressedData.Dispose();
                        _storedUncompressedData = null;
                    }
                }
                else
                {
                    if (_uncompressedSize == 0)
                    {
                        // reset size to ensure proper central directory size header
                        _compressedSize = 0;
                    }

                    WriteLocalFileHeader(isEmptyFile: _uncompressedSize == 0);

                    // according to ZIP specs, zero-byte files MUST NOT include file data
                    if (_uncompressedSize != 0)
                    {
                        Debug.Assert(_compressedBytes != null);
                        foreach (byte[] compressedBytes in _compressedBytes)
                        {
                            _archive.ArchiveStream.Write(compressedBytes, 0, compressedBytes.Length);
                        }
                    }
                }
            }
        }

        // Using _offsetOfLocalHeader, seeks back to where CRC and sizes should be in the header,
        // writes them, then seeks back to where you started
        // Assumes that the stream is currently at the end of the data
        private void WriteCrcAndSizesInLocalHeader(bool zip64HeaderUsed)
        {
            long finalPosition = _archive.ArchiveStream.Position;
            BinaryWriter writer = new BinaryWriter(_archive.ArchiveStream);

            bool zip64Needed = SizesTooLarge()
#if DEBUG_FORCE_ZIP64
                || _archive._forceZip64
#endif
            ;

            bool pretendStreaming = zip64Needed && !zip64HeaderUsed;

            uint compressedSizeTruncated = zip64Needed ? ZipHelper.Mask32Bit : (uint)_compressedSize;
            uint uncompressedSizeTruncated = zip64Needed ? ZipHelper.Mask32Bit : (uint)_uncompressedSize;

            // first step is, if we need zip64, but didn't allocate it, pretend we did a stream write, because
            // we can't go back and give ourselves the space that the extra field needs.
            // we do this by setting the correct property in the bit flag to indicate we have a data descriptor
            // and setting the version to Zip64 to indicate that descriptor contains 64-bit values
            if (pretendStreaming)
            {
                VersionToExtractAtLeast(ZipVersionNeededValues.Zip64);
                _generalPurposeBitFlag |= BitFlagValues.DataDescriptor;

                _archive.ArchiveStream.Seek(_offsetOfLocalHeader + ZipLocalFileHeader.OffsetToVersionFromHeaderStart,
                                            SeekOrigin.Begin);
                writer.Write((ushort)_versionToExtract);
                writer.Write((ushort)_generalPurposeBitFlag);
            }

            // next step is fill out the 32-bit size values in the normal header. we can't assume that
            // they are correct. we also write the CRC
            _archive.ArchiveStream.Seek(_offsetOfLocalHeader + ZipLocalFileHeader.OffsetToCrcFromHeaderStart,
                                            SeekOrigin.Begin);
            if (!pretendStreaming)
            {
                writer.Write(_crc32);
                writer.Write(compressedSizeTruncated);
                writer.Write(uncompressedSizeTruncated);
            }
            else // but if we are pretending to stream, we want to fill in with zeroes
            {
                writer.Write((uint)0);
                writer.Write((uint)0);
                writer.Write((uint)0);
            }

            // next step: if we wrote the 64 bit header initially, a different implementation might
            // try to read it, even if the 32-bit size values aren't masked. thus, we should always put the
            // correct size information in there. note that order of uncomp/comp is switched, and these are
            // 64-bit values
            // also, note that in order for this to be correct, we have to insure that the zip64 extra field
            // is always the first extra field that is written
            if (zip64HeaderUsed)
            {
                _archive.ArchiveStream.Seek(_offsetOfLocalHeader + ZipLocalFileHeader.SizeOfLocalHeader
                                            + _storedEntryNameBytes.Length + Zip64ExtraField.OffsetToFirstField,
                                            SeekOrigin.Begin);
                writer.Write(_uncompressedSize);
                writer.Write(_compressedSize);
            }

            // now go to the where we were. assume that this is the end of the data
            _archive.ArchiveStream.Seek(finalPosition, SeekOrigin.Begin);

            // if we are pretending we did a stream write, we want to write the data descriptor out
            // the data descriptor can have 32-bit sizes or 64-bit sizes. In this case, we always use
            // 64-bit sizes
            if (pretendStreaming)
            {
                writer.Write(_crc32);
                writer.Write(_compressedSize);
                writer.Write(_uncompressedSize);
            }
        }

        private void WriteDataDescriptor()
        {
            // We enter here because we cannot seek, so the data descriptor bit should be on
            Debug.Assert((_generalPurposeBitFlag & BitFlagValues.DataDescriptor) != 0);

            // data descriptor can be 32-bit or 64-bit sizes. 32-bit is more compatible, so use that if possible
            // signature is optional but recommended by the spec

            BinaryWriter writer = new BinaryWriter(_archive.ArchiveStream);

            writer.Write(ZipLocalFileHeader.DataDescriptorSignature);
            writer.Write(_crc32);
            if (SizesTooLarge())
            {
                writer.Write(_compressedSize);
                writer.Write(_uncompressedSize);
            }
            else
            {
                writer.Write((uint)_compressedSize);
                writer.Write((uint)_uncompressedSize);
            }
        }

        private void UnloadStreams()
        {
            _storedUncompressedData?.Dispose();
            _compressedBytes = null;
            _outstandingWriteStream = null;
        }

        private void CloseStreams()
        {
            // if the user left the stream open, close the underlying stream for them
            _outstandingWriteStream?.Dispose();
        }

        private void VersionToExtractAtLeast(ZipVersionNeededValues value)
        {
            if (_versionToExtract < value)
            {
                _versionToExtract = value;
            }
            if (_versionMadeBySpecification < value)
            {
                _versionMadeBySpecification = value;
            }
        }

        private void ThrowIfInvalidArchive()
        {
            if (_archive == null)
                throw new InvalidOperationException();
            _archive.ThrowIfDisposed();
        }

        /// <summary>
        /// Gets the file name of the path based on Windows path separator characters
        /// </summary>
        private static string GetFileName_Windows(string path)
        {
            int i = path.AsSpan().LastIndexOfAny('\\', '/', ':');
            return i >= 0 ?
                path.Substring(i + 1) :
                path;
        }

        /// <summary>
        /// Gets the file name of the path based on Unix path separator characters
        /// </summary>
        private static string GetFileName_Unix(string path)
        {
            int i = path.LastIndexOf('/');
            return i >= 0 ?
                path.Substring(i + 1) :
                path;
        }

        public sealed class DirectToArchiveWriterStream : Stream
        {
            private long _position;
            private readonly CheckSumAndSizeWriteStream _crcSizeStream;
            private bool _everWritten;
            private bool _isDisposed;
            private readonly ZipArchiveEntry _entry;
            private bool _usedZip64inLH;
            private bool _canWrite;

            // makes the assumption that somewhere down the line, crcSizeStream is eventually writing directly to the archive
            // this class calls other functions on ZipArchiveEntry that write directly to the archive
            public DirectToArchiveWriterStream(CheckSumAndSizeWriteStream crcSizeStream, ZipArchiveEntry entry)
            {
                _position = 0;
                _crcSizeStream = crcSizeStream;
                _everWritten = false;
                _isDisposed = false;
                _entry = entry;
                _usedZip64inLH = false;
                _canWrite = true;
            }
            public void AdvancePosition(long amount)
            {
                _position += amount;
                _crcSizeStream.AdvancePosition(amount);
            }

            public override long Length
            {
                get
                {
                    ThrowIfDisposed();
                    throw new NotSupportedException();
                }
            }
            public override long Position
            {
                get
                {
                    ThrowIfDisposed();
                    return _position;
                }
                set
                {
                    ThrowIfDisposed();
                    throw new NotSupportedException();
                }
            }

            public override bool CanRead => false;
            public override bool CanSeek => false;
            public override bool CanWrite => _canWrite;

            private void ThrowIfDisposed()
            {
                if (_isDisposed)
                    throw new ObjectDisposedException(GetType().ToString());
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                ThrowIfDisposed();
                throw new NotSupportedException();
            }

            public override long Seek(long offset, SeekOrigin origin)
            {
                ThrowIfDisposed();
                throw new NotSupportedException();
            }

            public override void SetLength(long value)
            {
                ThrowIfDisposed();
                throw new NotSupportedException();
            }

            // careful: assumes that write is the only way to write to the stream, if writebyte/beginwrite are implemented
            // they must set _everWritten, etc.
            public override void Write(byte[] buffer, int offset, int count)
            {
                ValidateBufferArguments(buffer, offset, count);

                ThrowIfDisposed();
                Debug.Assert(CanWrite);

                // if we're not actually writing anything, we don't want to trigger the header
                if (count == 0)
                    return;

                if (!_everWritten)
                {
                    _everWritten = true;
                    // write local header, we are good to go
                    _usedZip64inLH = _entry.WriteLocalFileHeader(isEmptyFile: false);
                }

                _crcSizeStream.Write(buffer, offset, count);
                _position += count;
            }

            public override void Write(ReadOnlySpan<byte> source)
            {
                ThrowIfDisposed();
                Debug.Assert(CanWrite);

                // if we're not actually writing anything, we don't want to trigger the header
                if (source.Length == 0)
                    return;

                if (!_everWritten)
                {
                    _everWritten = true;
                    // write local header, we are good to go
                    _usedZip64inLH = _entry.WriteLocalFileHeader(isEmptyFile: false);
                }

                _crcSizeStream.Write(source);
                _position += source.Length;
            }

            public override void WriteByte(byte value) =>
                Write(new ReadOnlySpan<byte>(in value));

            public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            {
                ValidateBufferArguments(buffer, offset, count);
                return WriteAsync(new ReadOnlyMemory<byte>(buffer, offset, count), cancellationToken).AsTask();
            }

            public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
            {
                ThrowIfDisposed();
                Debug.Assert(CanWrite);

                return !buffer.IsEmpty ?
                    Core(buffer, cancellationToken) :
                    default;

                async ValueTask Core(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
                {
                    if (!_everWritten)
                    {
                        _everWritten = true;
                        // write local header, we are good to go
                        _usedZip64inLH = _entry.WriteLocalFileHeader(isEmptyFile: false);
                    }

                    await _crcSizeStream.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
                    _position += buffer.Length;
                }
            }

            public override void Flush()
            {
                ThrowIfDisposed();
                Debug.Assert(CanWrite);

                _crcSizeStream.Flush();
            }

            public override Task FlushAsync(CancellationToken cancellationToken)
            {
                ThrowIfDisposed();
                Debug.Assert(CanWrite);

                return _crcSizeStream.FlushAsync(cancellationToken);
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing && !_isDisposed)
                {
                    _crcSizeStream.Dispose(); // now we have size/crc info

                    if (!_everWritten)
                    {
                        // write local header, no data, so we use stored
                        _entry.WriteLocalFileHeader(isEmptyFile: true);
                    }
                    else
                    {
                        // go back and finish writing
                        if (_entry._archive.ArchiveStream.CanSeek)
                            // finish writing local header if we have seek capabilities
                            _entry.WriteCrcAndSizesInLocalHeader(_usedZip64inLH);
                        else
                            // write out data descriptor if we don't have seek capabilities
                            _entry.WriteDataDescriptor();
                    }
                    _canWrite = false;
                    _isDisposed = true;
                }

                base.Dispose(disposing);
            }
        }

        [Flags]
        internal enum BitFlagValues : ushort { IsEncrypted = 0x1, DataDescriptor = 0x8, UnicodeFileNameAndComment = 0x800 }

        internal enum CompressionMethodValues : ushort { Stored = 0x0 }
    }
}
