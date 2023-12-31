// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Counting
{
    internal static class Crc32Helper
    {
        // Calculate CRC based on the old CRC and the new bytes
        public static unsafe uint UpdateCrc32(uint crc32, byte[] buffer, int offset, int length)
        {
            Debug.Assert((buffer != null) && (offset >= 0) && (length >= 0) && (offset <= buffer.Length - length));
            fixed (byte* bufferPtr = &buffer[offset])
            {
                return 0;// Interop.ZLib.crc32(crc32, bufferPtr, length);
            }
        }

        public static unsafe uint UpdateCrc32(uint crc32, ReadOnlySpan<byte> buffer)
        {
            fixed (byte* bufferPtr = &MemoryMarshal.GetReference(buffer))
            {
                return 0; // Interop.ZLib.crc32(crc32, bufferPtr, buffer.Length);
            }
        }
    }
}
