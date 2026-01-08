using NAudio.Wave;
using System;
using System.Diagnostics;

namespace AirPlay.Services
{
    public class WindowsAudioOutput : IDisposable
    {
        private WaveOutEvent _waveOut;
        private BufferedWaveProvider _waveProvider;
        private readonly object _lock = new object();
        private bool _disposed = false;
        private int _sampleCount = 0;
        private bool _isPlaying = false;
        private Stopwatch _prebufferTimer = new Stopwatch();
        private int _bytesPerSecond;
        private int _lastBufferedBytes = 0;
        private int _stuckBufferCount = 0;
        private int _restartCount = 0;
        private Stopwatch _restartTimer = new Stopwatch();

        public void Initialize()
        {
            lock (_lock)
            {
                if (_disposed) return;

                Console.WriteLine("Initializing WindowsAudioOutput...");

                InitializeAudioDevice();
                _prebufferTimer.Start();  // Start timer for first initialization

                Console.WriteLine($"✓ Audio initialized: 44100Hz, 16-bit, 2 channels");
            }
        }

        private void InitializeAudioDevice()
        {
            // PCM format: 16-bit, 44.1kHz, stereo (as decoded by AAC-ELD/AAC/ALAC)
            var waveFormat = new WaveFormat(44100, 16, 2);
            _bytesPerSecond = waveFormat.AverageBytesPerSecond; // 176,400 bytes/sec

            // Use buffer with automatic overflow handling
            _waveProvider = new BufferedWaveProvider(waveFormat)
            {
                BufferDuration = TimeSpan.FromSeconds(3),
                DiscardOnBufferOverflow = true,
                ReadFully = false
            };

            // Use WaveOutEvent with high latency
            _waveOut = new WaveOutEvent
            {
                DesiredLatency = 500,
                NumberOfBuffers = 3
            };

            _waveOut.PlaybackStopped += (sender, args) =>
            {
                lock (_lock)
                {
                    if (args.Exception != null)
                    {
                        Console.WriteLine($"⚠ Playback stopped with error: {args.Exception.Message}");
                    }
                    _isPlaying = false;
                }
            };

            _waveOut.Init(_waveProvider);
            // Note: _prebufferTimer is started/restarted by caller
        }

        public void AddSamples(byte[] buffer, int offset, int count)
        {
            lock (_lock)
            {
                if (_disposed || _waveProvider == null || _waveOut == null)
                    return;

                try
                {
                    _waveProvider.AddSamples(buffer, offset, count);
                    _sampleCount++;

                    // Start playback after prebuffering
                    if (!_isPlaying)
                    {
                        int prebufferBytes = _bytesPerSecond * 1; // 1 second prebuffer
                        if (_waveProvider.BufferedBytes >= prebufferBytes)
                        {
                            _waveOut.Play();
                            _isPlaying = true;
                            _lastBufferedBytes = _waveProvider.BufferedBytes;
                            _restartTimer.Restart();
                            Console.WriteLine($"✓ Audio playback started (prebuffered {_prebufferTimer.ElapsedMilliseconds}ms)");
                        }
                    }
                    else
                    {
                        // Check every 500 packets if buffer is stuck (not being consumed)
                        if (_sampleCount % 500 == 0)
                        {
                            int currentBuffered = _waveProvider.BufferedBytes;

                            // If buffer hasn't changed and is near full, NAudio has stopped consuming
                            if (currentBuffered == _lastBufferedBytes &&
                                currentBuffered > _bytesPerSecond * 2) // More than 2 seconds
                            {
                                _stuckBufferCount++;

                                if (_stuckBufferCount >= 3) // Stuck for 1500 packets
                                {
                                    // Check if we're in a restart loop (restarting too frequently)
                                    if (_restartTimer.IsRunning && _restartTimer.ElapsedMilliseconds < 10000)
                                    {
                                        _restartCount++;
                                        if (_restartCount >= 3)
                                        {
                                            Console.WriteLine($"⚠ Audio restart loop detected - fully reinitializing audio device...");
                                            RecreateAudioDevice();
                                            _restartCount = 0;
                                            _restartTimer.Restart();
                                            return;
                                        }
                                    }
                                    else
                                    {
                                        _restartCount = 0;
                                    }

                                    Console.WriteLine($"⚠ Buffer stuck at {currentBuffered} bytes - clearing and rebuffering...");

                                    // Clear buffer and let it rebuffer before playing
                                    _waveOut.Stop();
                                    _waveProvider.ClearBuffer();
                                    _isPlaying = false;  // Let prebuffering logic handle restart
                                    _stuckBufferCount = 0;
                                    _lastBufferedBytes = 0;
                                    _restartTimer.Restart();
                                    _prebufferTimer.Restart();

                                    Console.WriteLine("✓ Cleared buffer, waiting to rebuffer...");
                                }
                            }
                            else
                            {
                                _stuckBufferCount = 0;
                                _lastBufferedBytes = currentBuffered;
                            }

                            // Minimal logging - only every 5000 packets
                            if (_sampleCount % 5000 == 0)
                            {
                                Console.WriteLine($"Audio: {_sampleCount} packets | Buffer: {currentBuffered / 1024}KB");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Audio error: {ex.Message}");
                }
            }
        }

        private void RecreateAudioDevice()
        {
            // Fully dispose and recreate the audio device (for track changes)
            try
            {
                _waveOut?.Stop();
                _waveOut?.Dispose();
            }
            catch { }

            _waveOut = null;
            _waveProvider = null;
            _isPlaying = false;

            // Recreate from scratch
            InitializeAudioDevice();
            _prebufferTimer.Restart();  // Reset prebuffer timer for new device
            Console.WriteLine("✓ Audio device recreated, waiting to rebuffer...");
        }

        public void Dispose()
        {
            lock (_lock)
            {
                if (_disposed) return;
                _disposed = true;

                try
                {
                    _waveOut?.Stop();
                    _waveOut?.Dispose();
                }
                catch { }

                _waveOut = null;
                _waveProvider = null;

                Console.WriteLine($"Audio disposed (played {_sampleCount} packets total)");
            }
        }
    }
}
