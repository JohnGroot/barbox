using System;
using System.Runtime.InteropServices;

public static class MemoryHelpers {
	public static unsafe int SizeOf<T>() where T: unmanaged {
		return sizeof(T);
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
}

