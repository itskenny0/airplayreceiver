using NAudio.Wave;
using System;
using System.Diagnostics;

namespace AirPlay.Services
{
    public class WindowsAudioOutput : IDisposable
    {
        private IWavePlayer _waveOut;
        private BufferedWaveProvider _waveProvider;
        private readonly object _lock = new object();
        private bool _disposed = false;
        private int _sampleCount = 0;
        private bool _isPlaying = false;
        private Stopwatch _prebufferTimer = new Stopwatch();
        private int _bytesPerSecond;
        private int _lastBufferedBytes = 0;
        private int _stuckBufferCount = 0;

        public void Initialize()
        {
            lock (_lock)
            {
                if (_disposed) return;

                Console.WriteLine("Initializing WindowsAudioOutput...");

                // PCM format: 16-bit, 44.1kHz, stereo (as decoded by AAC-ELD/AAC/ALAC)
                var waveFormat = new WaveFormat(44100, 16, 2);
                _bytesPerSecond = waveFormat.AverageBytesPerSecond; // 176,400 bytes/sec

                CreateAudioDevice(waveFormat);
                _prebufferTimer.Start();

                Console.WriteLine($"✓ Audio initialized: 44100Hz, 16-bit, 2 channels");
            }
        }

        private void CreateAudioDevice(WaveFormat waveFormat)
        {
            // Use buffer with automatic overflow handling
            _waveProvider = new BufferedWaveProvider(waveFormat)
            {
                BufferDuration = TimeSpan.FromSeconds(3),
                DiscardOnBufferOverflow = true,
                ReadFully = false
            };

            // Use WASAPI for modern Windows audio support
            _waveOut = new WasapiOut(NAudio.CoreAudioApi.AudioClientShareMode.Shared, 200); // 200ms latency

            _waveOut.PlaybackStopped += (sender, args) =>
            {
                Console.WriteLine($"⚠ Playback stopped event fired. Exception: {args.Exception?.Message ?? "none"}");
                if (args.Exception != null)
                {
                    Console.WriteLine($"   Stack trace: {args.Exception.StackTrace}");
                }
                lock (_lock)
                {
                    _isPlaying = false;
                }
            };

            _waveOut.Init(_waveProvider);

            // Give DirectSoundOut time to initialize with Windows audio subsystem
            System.Threading.Thread.Sleep(50);
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
                        int prebufferBytes = _bytesPerSecond * 2; // 2 second prebuffer for stability
                        if (_waveProvider.BufferedBytes >= prebufferBytes)
                        {
                            _waveOut.Play();
                            _isPlaying = true;
                            _lastBufferedBytes = _waveProvider.BufferedBytes;
                            var state = _waveOut.PlaybackState;
                            Console.WriteLine($"✓ Audio playback started (prebuffered {_prebufferTimer.ElapsedMilliseconds}ms, state: {state})");
                        }
                    }
                    else
                    {
                        // Check every 1000 packets if buffer is stuck (not being consumed)
                        if (_sampleCount % 1000 == 0)
                        {
                            int currentBuffered = _waveProvider.BufferedBytes;

                            // If buffer hasn't changed significantly and is near full, NAudio has stopped consuming
                            if (Math.Abs(currentBuffered - _lastBufferedBytes) < _bytesPerSecond / 10 && // Less than 100ms change
                                currentBuffered > _bytesPerSecond * 2) // More than 2 seconds buffered
                            {
                                _stuckBufferCount++;

                                if (_stuckBufferCount >= 2) // Stuck for 2000 packets
                                {
                                    Console.WriteLine($"⚠ Buffer stuck at {currentBuffered} bytes - fully recreating audio device...");

                                    // Fully dispose and recreate audio device
                                    var waveFormat = _waveProvider.WaveFormat;

                                    try
                                    {
                                        _waveOut.Stop();
                                        _waveOut.Dispose();
                                    }
                                    catch { }

                                    _waveOut = null;
                                    _waveProvider = null;
                                    _isPlaying = false;
                                    _stuckBufferCount = 0;
                                    _lastBufferedBytes = 0;

                                    // Give Windows time to release audio device resources
                                    System.Threading.Thread.Sleep(100);

                                    // Create fresh audio device
                                    CreateAudioDevice(waveFormat);
                                    _prebufferTimer.Restart();

                                    Console.WriteLine("✓ Audio device recreated, waiting to rebuffer...");
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
                                Console.WriteLine($"Audio: {_sampleCount} packets | Buffer: {currentBuffered / 1024}KB | State: {_waveOut.PlaybackState}");
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
