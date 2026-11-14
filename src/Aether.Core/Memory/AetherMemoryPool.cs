using System;
using System.Buffers;
using System.Text;

namespace Aether.Core.Memory
{
    /// <summary>
    /// Atelier zero-allocation memory pool.
    /// Provides rented buffers and disposable scopes to prevent GC pressure in hot loops.
    /// </summary>
    public static class AetherMemoryPool
    {
        [ThreadStatic]
        private static StringBuilder? _threadLocalBuilder;

        public static StringBuilder RentStringBuilder()
        {
            var sb = _threadLocalBuilder ??= new StringBuilder(512);
            sb.Clear();
            return sb;
        }

        /// <summary>
        /// Rented buffer with a single-return guarantee. This used to be a copyable struct whose
        /// Dispose had no flag, so copying it (or disposing twice) returned the same array to the
        /// pool more than once and two callers could hold the same buffer (L4).
        /// </summary>
        public sealed class RentedArray<T> : IDisposable
        {
            private T[]? _array;

            public T[] Array => _array ?? throw new ObjectDisposedException(nameof(RentedArray<T>));
            public int Length { get; }

            public RentedArray(int minimumLength)
            {
                _array = ArrayPool<T>.Shared.Rent(minimumLength);
                Length = minimumLength;
            }

            public void Dispose()
            {
                T[]? array = _array;
                if (array == null) return;

                _array = null;
                ArrayPool<T>.Shared.Return(array, clearArray: false);
            }
        }

        public static RentedArray<T> Rent<T>(int minimumLength) => new(minimumLength);
    }
}
