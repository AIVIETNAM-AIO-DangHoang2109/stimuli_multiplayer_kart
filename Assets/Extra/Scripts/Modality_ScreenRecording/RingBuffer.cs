using System;
using UnityEngine;

namespace Extra.ScreenRecording
{
    public class RingBuffer<T>
    {
        private readonly T[] _buffer;
        private readonly int _capacity;
        
        // Cursors are monotonic and allowed to overflow.
        // Using volatile to ensure thread safety without heavy locking.
        private volatile int _writePos;
        private volatile int _readPos;

        public int Capacity => _capacity;

        public int AvailableRead
        {
            get
            {
                int write = _writePos;
                int read = _readPos;
                int diff = write - read;
                // If write has overflowed relative to read or vice versa, handle it
                return diff >= 0 ? diff : diff + int.MaxValue + 1; // Handled monotonic difference
            }
        }

        public int AvailableWrite => _capacity - AvailableRead;

        public RingBuffer(int capacity)
        {
            if (capacity <= 0)
                throw new ArgumentException("Capacity must be greater than 0", nameof(capacity));
            _capacity = capacity;
            _buffer = new T[capacity];
            _writePos = 0;
            _readPos = 0;
        }

        public void Clear()
        {
            _writePos = 0;
            _readPos = 0;
            Array.Clear(_buffer, 0, _buffer.Length);
        }

        private int GetBufferIndex(int monotonicPosition)
        {
            int index = monotonicPosition % _capacity;
            if (index < 0) index += _capacity;
            return index;
        }

        public void Write(T[] data, int offset, int count)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (offset < 0 || count < 0 || offset + count > data.Length)
                throw new ArgumentOutOfRangeException();

            if (count > _capacity)
            {
                // Can't write more than capacity, adjust start to only write the last capacity elements
                offset += (count - _capacity);
                count = _capacity;
            }

            int availableWrite = AvailableWrite;
            if (count > availableWrite)
            {
                // Buffer overflow: advance read cursor to overwrite oldest data
                int overflow = count - availableWrite;
                _readPos += overflow;
            }

            for (int i = 0; i < count; i++)
            {
                int index = GetBufferIndex(_writePos + i);
                _buffer[index] = data[offset + i];
            }

            _writePos += count;
        }

        public int Read(T[] destination, int offset, int maxCount)
        {
            if (destination == null) throw new ArgumentNullException(nameof(destination));
            if (offset < 0 || maxCount < 0 || offset + maxCount > destination.Length)
                throw new ArgumentOutOfRangeException();

            int available = AvailableRead;
            int countToRead = Math.Min(available, maxCount);

            if (countToRead <= 0) return 0;

            for (int i = 0; i < countToRead; i++)
            {
                int index = GetBufferIndex(_readPos + i);
                destination[offset + i] = _buffer[index];
                _buffer[index] = default; // clear reference to allow GC
            }

            _readPos += countToRead;
            return countToRead;
        }
    }
}
