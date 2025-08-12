using System;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;


public static class MemoryHelpers {
	public static int SizeOf<T>() {
		return Unsafe.SizeOf<T>();
	}

	public static unsafe ReadOnlySpan<T> AsReadOnlySpan<T>(this T[] array) where T: unmanaged {
		return new ReadOnlySpan<T>(array);
	}

	public static unsafe ReadOnlySpan<T> AsReadOnlySpan<T>(this T[] array, int start, int length) where T: unmanaged {
		return new ReadOnlySpan<T>(array, start, length);
	}

	public static unsafe ReadOnlySpan<byte> AsBytes<T>(this ReadOnlySpan<T> span) where T: unmanaged {
		return MemoryMarshal.Cast<T, byte>(span);
	}
	public static unsafe ReadOnlySpan<byte> AsBytes<T>(this Span<T> span) where T: unmanaged {
		return MemoryMarshal.Cast<T, byte>(span);
	}
}

