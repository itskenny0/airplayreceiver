using NAudio.Wave;
using System;
using System.Collections.Concurrent;
using System.Threading;

namespace AirPlay.Services
{
    /// <summary>
    /// A proper streaming wave provider for continuous audio streaming.
    /// NAudio pulls audio data on demand via Read() instead of us pushing via AddSamples().
    /// </summary>
    public class StreamingWaveProvider : IWaveProvider
    {
        private readonly WaveFormat _waveFormat;
        private readonly ConcurrentQueue<byte[]> _audioQueue;
        private readonly int _maxQueueSize;
        private byte[] _currentBuffer;
        private int _currentBufferOffset;
        private long _totalBytesRead;
        private readonly object _statsLock = new object();
        private DateTime _lastReadTime;

        public WaveFormat WaveFormat => _waveFormat;

        public int QueuedBuffers => _audioQueue.Count;

        public long TotalBytesRead
        {
            get { lock (_statsLock) return _totalBytesRead; }
        }

        public DateTime LastReadTime
        {
            get { lock (_statsLock) return _lastReadTime; }
        }

        public StreamingWaveProvider(WaveFormat waveFormat, int maxQueueSize = 100)
        {
            _waveFormat = waveFormat ?? throw new ArgumentNullException(nameof(waveFormat));
            _audioQueue = new ConcurrentQueue<byte[]>();
            _maxQueueSize = maxQueueSize;
            _currentBuffer = null;
            _currentBufferOffset = 0;
            _totalBytesRead = 0;
            _lastReadTime = DateTime.UtcNow;
        }

        /// <summary>
        /// Add audio data to the queue. Called from network thread.
        /// </summary>
        public bool AddSamples(byte[] buffer, int offset, int count)
        {
            if (buffer == null || count <= 0)
                return false;

            // Drop packets if queue is too full to prevent infinite buffering
            if (_audioQueue.Count >= _maxQueueSize)
            {
                return false; // Signal buffer full
            }

            // Copy data to new buffer (we need to own it)
            byte[] data = new byte[count];
            Array.Copy(buffer, offset, data, 0, count);

            _audioQueue.Enqueue(data);
            return true;
        }

        /// <summary>
        /// Called by NAudio to read audio data. This is the proper streaming pattern.
        /// </summary>
        public int Read(byte[] buffer, int offset, int count)
        {
            int bytesRead = 0;

            while (bytesRead < count)
            {
                // If we don't have a current buffer, try to get one from queue
                if (_currentBuffer == null || _currentBufferOffset >= _currentBuffer.Length)
                {
                    if (!_audioQueue.TryDequeue(out _currentBuffer))
                    {
                        // No data available - fill remaining buffer with silence to prevent DirectSound
                        // from treating this as end-of-stream and killing the render thread
                        int remainingBytes = count - bytesRead;
                        Array.Clear(buffer, offset + bytesRead, remainingBytes);
                        bytesRead = count; // Report that we filled the entire buffer with silence
                        break;
                    }
                    _currentBufferOffset = 0;
                }

                // Copy from current buffer to output
                int bytesAvailable = _currentBuffer.Length - _currentBufferOffset;
                int bytesToCopy = Math.Min(bytesAvailable, count - bytesRead);

                Array.Copy(_currentBuffer, _currentBufferOffset, buffer, offset + bytesRead, bytesToCopy);

                _currentBufferOffset += bytesToCopy;
                bytesRead += bytesToCopy;
            }

            lock (_statsLock)
            {
                _totalBytesRead += bytesRead;
                _lastReadTime = DateTime.UtcNow;
            }

            return bytesRead;
        }

        /// <summary>
        /// Clear all buffered audio data.
        /// </summary>
        public void Clear()
        {
            while (_audioQueue.TryDequeue(out _)) { }
            _currentBuffer = null;
            _currentBufferOffset = 0;
        }
    }
}
