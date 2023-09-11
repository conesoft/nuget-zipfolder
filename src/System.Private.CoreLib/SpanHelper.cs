// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

#pragma warning disable IDE0060 // https://github.com/dotnet/roslyn-analyzers/issues/6228

#pragma warning disable 8500 // sizeof of managed types

namespace System2
{
    internal static partial class SpanHelpers // .T
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe int ComputeFirstIndex<T>(ref T searchSpace, ref T current, Vector128<T> equals) where T : struct
        {
            uint notEqualsElements = equals.ExtractMostSignificantBits();
            int index = BitOperations.TrailingZeroCount(notEqualsElements);
            return index + (int)((nuint)Unsafe.ByteOffset(ref searchSpace, ref current) / (nuint)sizeof(T));
        }


        internal interface INegator<T> where T : struct
        {
            static abstract bool NegateIfNeeded(bool equals);
            static abstract Vector128<T> NegateIfNeeded(Vector128<T> equals);
            static abstract Vector256<T> NegateIfNeeded(Vector256<T> equals);
        }

        internal readonly struct DontNegate<T> : INegator<T> where T : struct
        {
            public static bool NegateIfNeeded(bool equals) => equals;
            public static Vector128<T> NegateIfNeeded(Vector128<T> equals) => equals;
            public static Vector256<T> NegateIfNeeded(Vector256<T> equals) => equals;
        }

        internal readonly struct Negate<T> : INegator<T> where T : struct
        {
            public static bool NegateIfNeeded(bool equals) => !equals;
            public static Vector128<T> NegateIfNeeded(Vector128<T> equals) => ~equals;
            public static Vector256<T> NegateIfNeeded(Vector256<T> equals) => ~equals;
        }
        internal static int IndexOfAnyExceptInRange<T>(ref T searchSpace, T lowInclusive, T highInclusive, int length)
            where T : IComparable<T>
        {
            for (int i = 0; i < length; i++)
            {
                ref T current = ref Unsafe.Add(ref searchSpace, i);
                if ((lowInclusive.CompareTo(current) > 0) || (highInclusive.CompareTo(current) < 0))
                {
                    return i;
                }
            }

            return -1;
        }


        internal static int IndexOfAnyExceptInRangeUnsignedNumber<T>(ref T searchSpace, T lowInclusive, T highInclusive, int length)
            where T : struct, IUnsignedNumber<T>, IComparisonOperators<T, T, bool> =>
            IndexOfAnyInRangeUnsignedNumber<T, Negate<T>>(ref searchSpace, lowInclusive, highInclusive, length);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe int IndexOfAnyInRangeUnsignedNumber<T, TNegator>(ref T searchSpace, T lowInclusive, T highInclusive, int length)
            where T : struct, IUnsignedNumber<T>, IComparisonOperators<T, T, bool>
            where TNegator : struct, INegator<T>
        {
            return NonPackedIndexOfAnyInRangeUnsignedNumber<T, TNegator>(ref searchSpace, lowInclusive, highInclusive, length);
        }

        internal static int NonPackedIndexOfAnyInRangeUnsignedNumber<T, TNegator>(ref T searchSpace, T lowInclusive, T highInclusive, int length)
            where T : struct, IUnsignedNumber<T>, IComparisonOperators<T, T, bool>
            where TNegator : struct, INegator<T>
        {
            // T must be a type whose comparison operator semantics match that of Vector128/256.

            if (!Vector128.IsHardwareAccelerated || length < Vector128<T>.Count)
            {
                T rangeInclusive = highInclusive - lowInclusive;
                for (int i = 0; i < length; i++)
                {
                    ref T current = ref Unsafe.Add(ref searchSpace, i);
                    if (TNegator.NegateIfNeeded((current - lowInclusive) <= rangeInclusive))
                    {
                        return i;
                    }
                }
            }
            else if (!Vector256.IsHardwareAccelerated || length < Vector256<T>.Count)
            {
                Vector128<T> lowVector = Vector128.Create(lowInclusive);
                Vector128<T> rangeVector = Vector128.Create(highInclusive - lowInclusive);
                Vector128<T> inRangeVector;

                ref T current = ref searchSpace;
                ref T oneVectorAwayFromEnd = ref Unsafe.Add(ref searchSpace, (uint)(length - Vector128<T>.Count));

                // Loop until either we've finished all elements or there's less than a vector's-worth remaining.
                do
                {
                    inRangeVector = TNegator.NegateIfNeeded(Vector128.LessThanOrEqual(Vector128.LoadUnsafe(ref current) - lowVector, rangeVector));
                    if (inRangeVector != Vector128<T>.Zero)
                    {
                        return ComputeFirstIndex(ref searchSpace, ref current, inRangeVector);
                    }

                    current = ref Unsafe.Add(ref current, Vector128<T>.Count);
                }
                while (Unsafe.IsAddressLessThan(ref current, ref oneVectorAwayFromEnd));

                // Process the last vector in the search space (which might overlap with already processed elements).
                inRangeVector = TNegator.NegateIfNeeded(Vector128.LessThanOrEqual(Vector128.LoadUnsafe(ref oneVectorAwayFromEnd) - lowVector, rangeVector));
                if (inRangeVector != Vector128<T>.Zero)
                {
                    return ComputeFirstIndex(ref searchSpace, ref oneVectorAwayFromEnd, inRangeVector);
                }
            }

            return -1;
        }

        public static void Replace<T>(ref T src, ref T dst, T oldValue, T newValue, nuint length) where T : IEquatable<T>?
        {
            if (default(T) is not null || oldValue is not null)
            {
                Debug.Assert(oldValue is not null);

                for (nuint idx = 0; idx < length; ++idx)
                {
                    T original = Unsafe.Add(ref src, idx);
                    Unsafe.Add(ref dst, idx) = oldValue.Equals(original) ? newValue : original;
                }
            }
            else
            {
                for (nuint idx = 0; idx < length; ++idx)
                {
                    T original = Unsafe.Add(ref src, idx);
                    Unsafe.Add(ref dst, idx) = original is null ? newValue : original;
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe void Replace<T>(this Span<T> span, T oldValue, T newValue) where T : IEquatable<T>?
        {
            nuint length = (uint)span.Length;

            ref T src2 = ref MemoryMarshal.GetReference(span);
            SpanHelpers.Replace(ref src2, ref src2, oldValue, newValue, length);
        }
    }
}