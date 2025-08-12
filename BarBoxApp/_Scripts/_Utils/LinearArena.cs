using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Generic = System.Collections.Generic;

#nullable enable
public unsafe class LinearArena : IDisposable {
    public byte* _buffer;
    nint _offset;
    nint _capacity;
    nint _generation;

    public nint Offset => _offset;

    [StructLayout(LayoutKind.Sequential)]
    struct Handle {
        public int arenaID;
        public nint generation;
        public nint byteOffset;
    }

    static readonly LinearArena?[] _GlobalArenaTable = new LinearArena[1024];
    static readonly Generic.Queue<int> _IDQueue = new(1024);
    static int _NextArenaID = 0;
    static LinearArena? GetArena(int id) => _GlobalArenaTable[id];

    int _id;
    public int ID => _id;

    public static LinearArena Create(int initialCapacityBytes) {
        int id;
        if (_IDQueue.TryDequeue(out var x)) {
            id = x;
        } else {
            id = _NextArenaID;
            _NextArenaID += 1;
        }
        LinearArena arena = new() {
            _id = id,
            _buffer = (byte*)Marshal.AllocHGlobal(initialCapacityBytes),
            _offset = 0,
            _capacity = initialCapacityBytes,
            _generation = 0,
        };
        _GlobalArenaTable[id] = arena;
        return arena;
    }

    static Span<T> GetSpan<T>(Handle handle, int length) where T : unmanaged {
        if (GetArena(handle.arenaID) is {} arena) {
            if (handle.generation != arena._generation) {
                throw new ArgumentOutOfRangeException($"Attempted to use a handle with generation {handle.generation} to access arena with generation {arena._generation}");
            }
            var ptrBytes = arena._buffer + handle.byteOffset;
            return new((T*)ptrBytes, length);
        } else {
            throw new ArgumentException($"Arena ID {handle.arenaID} is not valid!");
        }
    }

    Handle Alloc<T>(int length = 1) where T: unmanaged {
        nint rawOffset = _offset;
        nint alignment = Alignment.Of<T>();
        int elemSize  = Unsafe.SizeOf<T>();
        int sizeBytes = elemSize * length;
        return AllocBytes(sizeBytes, alignment);
    }

    Handle AllocBytes(int sizeBytes, nint alignment = 1) {
        if (sizeBytes <= 0)  {
            throw new ArgumentOutOfRangeException(nameof(sizeBytes));
        }
        if ((alignment & (alignment - 1)) != 0) {
            throw new ArgumentException("Alignment must be power of two", nameof(alignment));
        }

        // how much slop do we need in the allocation to account for the provided alignment
        nint rem = _offset % alignment;
        nint aligned = (rem == 0) ? _offset : (_offset + (alignment - rem));
        if (aligned + sizeBytes > _capacity) {
            GrowBytes((int)(aligned + sizeBytes));
            rem = _offset % alignment;
            aligned = (rem == 0) ? _offset : (_offset + (alignment - rem));
        }

        byte* ptr = _buffer + aligned;
        _offset   = (int)(aligned + sizeBytes);

        var h = default(Handle);
        h.arenaID    = _id;
        h.generation = _generation;
        h.byteOffset = aligned;
        return h;
    }

    void GrowBytes(int minimum) {
        var newCap = _capacity;
        while (newCap < minimum) {
            newCap *= 2;
        }
        var prevBuffer = _buffer;
        _buffer = (byte*)Marshal.ReAllocHGlobal((IntPtr)_buffer, newCap);
        _capacity = newCap;
    }

    public void Dispose() {
        Reset();
        _capacity = _offset = 0;
        Marshal.FreeHGlobal((IntPtr)_buffer);
        _IDQueue.Enqueue(_id);
        _GlobalArenaTable[_id] = null!;
    }

    public void Reset() {
        _offset = 0;
        _generation += 1;
    }

    public Buffer<T> AllocBuffer<T>(int lengthElements) where T: unmanaged {
        var ptr = Alloc<T>(lengthElements);
        return Buffer<T>.Init(this, lengthElements);
    }

    public List<T> AllocList<T>(int initialCapacityElements) where T: unmanaged {
        if (initialCapacityElements < 1) initialCapacityElements = 4;
        return List<T>.Init(this, initialCapacityElements);
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct Buffer<T> where T: unmanaged {
        Handle _handle;
        int _length;
        public int Length => _length;
        public Span<T> Items() => GetSpan<T>(_handle, _length);
        public static Buffer<T> Init(LinearArena arena, int lengthElements) {
            return new() {
                _length = lengthElements,
                _handle = arena.Alloc<T>(lengthElements),
            };
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct List<T> where T : unmanaged {
        Handle _handle;
        int _length;
        int _capacity;
        bool _isCreated;

        public Span<T> Items() => GetSpan<T>(_handle, _length);
        public bool IsCreated() => _isCreated;
        public int Length => _length;
        public int Capacity => _capacity;

        public static List<T> Init(LinearArena arena, int initialCapacityElements) {
            List<T> list = new() {
                _length = 0,
                _capacity = initialCapacityElements,
                _handle = arena.Alloc<T>(initialCapacityElements),
                _isCreated = true,
            };
            return list;
        }

        void Grow(int minimum) {
            if (GetArena(_handle.arenaID) is {} arena) {
                int newCap = Math.Max(4, _capacity * 2);
                while (newCap < minimum) {
                    newCap *= 2;
                }
                Handle newHandle = arena.Alloc<T>(newCap);
                int elemSize = Unsafe.SizeOf<T>();

                var src = GetSpan<T>(_handle, _length);
                var dst = GetSpan<T>(newHandle, _length);
                src.CopyTo(dst);

                _handle = newHandle;
                _capacity = newCap;
            } else {
                // throw something
            }
        }

        public void Append(T item) {
            if (_length >= _capacity) {
                Grow(_capacity * 2);
            }
            var span = GetSpan<T>(_handle, _length + 1);
            span[_length++] = item;
        }

        public void AppendRange(ReadOnlySpan<T> items) {
            int newLen = _length + items.Length;
            if (newLen > _capacity) {
                Grow(newLen);
            }
            var dst = GetSpan<T>(_handle, newLen).Slice(_length, items.Length);
            items.CopyTo(dst);
            _length = newLen;
        }
    }
}

static class Alignment {
    [StructLayout(LayoutKind.Sequential)]
    private struct AlignOfHelper<T> where T : unmanaged {
        public byte  Dummy;
        public T     Value;
    }
    public static int Of<T>() where T : unmanaged {
        return (int)Marshal.OffsetOf<AlignOfHelper<T>>(nameof(AlignOfHelper<T>.Value));
    }
}
#nullable disable