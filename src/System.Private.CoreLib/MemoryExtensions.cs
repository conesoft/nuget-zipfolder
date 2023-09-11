// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

#pragma warning disable 8500 // sizeof of managed types

namespace System2
{
    public static partial class MemoryExtensions
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool ContainsAnyExceptInRange(this ReadOnlySpan<char> span, char lowInclusive, char highInclusive)
        {
            if (Vector128.IsHardwareAccelerated)
            {
                return SpanHelpers.IndexOfAnyExceptInRangeUnsignedNumber(
                    ref Unsafe.As<char, ushort>(ref MemoryMarshal.GetReference(span)),
                    Unsafe.As<char, ushort>(ref lowInclusive),
                    Unsafe.As<char, ushort>(ref highInclusive),
                    span.Length) >= 0;
            }

            return SpanHelpers.IndexOfAnyExceptInRange(ref MemoryMarshal.GetReference(span), lowInclusive, highInclusive, span.Length) >= 0;
        }
    }
}