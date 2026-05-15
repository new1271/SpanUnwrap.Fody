using System;
using System.Runtime.CompilerServices;

using InlineIL;

namespace SpanUnwrap.Example;

public static class Stores
{
    public static void CopyData(ref byte destination)
    {
        const int Length = 8;
        CopyBlockUnaligned(
            ref destination,
            in SpanUnwrap.Unwrap(new byte[Length] { 0xFF, 0x5E, 0x30, 0x21, 0x44, 0x55, 0x55, 0x1A }),
            Length);
    }

    public static byte CopyDataAndReturn()
    {
        const int Length = 8;

        ref byte destination = ref SpanUnwrap.Unwrap(stackalloc byte[Length]);
        CopyData(ref destination);
        return destination;
    }

    public static byte CopyNativeDataAndReturn()
    {
        const int Length = 8;

        ref byte destination = ref SpanUnwrap.Unwrap(stackalloc byte[Length]);
        CopyBlockUnaligned(
            ref destination,
            in SpanUnwrap.Unwrap(new byte[Length] { 0xFE, 0x5C, 0x75, 0x22, 0x34, 0x15, 0x5C, 0x1A }),
            Length);
        return destination;
    }

    public static byte CopyNativeDataAndReturn2(Span<byte> dest)
    {
        const int Length = 8;

        ref byte destination = ref SpanUnwrap.Unwrap(dest);
        CopyBlockUnaligned(
            ref destination,
            in SpanUnwrap.Unwrap(new byte[Length] { 0xFE, 0x5C, 0x75, 0x22, 0x34, 0x15, 0x5C, 0x1A }),
            Length);
        return destination;
    }

    public static byte ShowData(ReadOnlySpan<byte> source) => SpanUnwrap.Unwrap(source);

    public static byte ShowData2(ReadOnlySpan<byte> source) => source.GetPinnableReference();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void CopyBlockUnaligned(ref byte destination, ref readonly byte source, nuint byteCount)
    {
        IL.Emit.Ldarg_0();
        IL.Emit.Ldarg_1();
        IL.Emit.Ldarg_2();
        IL.Emit.Unaligned(1);
        IL.Emit.Cpblk();
    }
}
