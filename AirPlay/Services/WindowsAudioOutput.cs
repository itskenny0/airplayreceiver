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
        private volatile bool _needsRecreation = false;

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
            // Use 3-second buffer for stability
            _waveProvider = new BufferedWaveProvider(waveFormat)
            {
                BufferDuration = TimeSpan.FromSeconds(3),
                DiscardOnBufferOverflow = true,
                ReadFully = false
            };

            // Use WaveOutEvent - most reliable for continuous streaming
            _waveOut = new WaveOutEvent
            {
                DesiredLatency = 300,
                NumberOfBuffers = 3
            };

            _waveOut.PlaybackStopped += (sender, args) =>
            {
                if (_disposed || _needsRecreation) return; // Ignore if already flagged for recreation

                Console.WriteLine($"⚠ Playback stopped event fired. Exception: {args.Exception?.Message ?? "none"}");
                if (args.Exception != null)
                {
                    Console.WriteLine($"   Stack trace: {args.Exception.StackTrace}");
                }

                // Set flag to recreate device on next AddSamples call
                _isPlaying = false;
                _needsRecreation = true;

                var buffered = _waveProvider?.BufferedBytes ?? 0;
                Console.WriteLine($"   Buffer at stop: {buffered} bytes ({buffered / 1024}KB)");
                Console.WriteLine($"   Will recreate device on next audio packet");
            };

            _waveOut.Init(_waveProvider);

            // Give DirectSoundOut time to initialize with Windows audio subsystem
            System.Threading.Thread.Sleep(50);
        }

        public void AddSamples(byte[] buffer, int offset, int count)
        {
            lock (_lock)
            {
                if (_disposed)
                    return;

                // Handle device recreation if PlaybackStopped was fired
                if (_needsRecreation)
                {
                    Console.WriteLine("   Recreating audio device after playback stopped...");

                    var oldFormat = _waveProvider?.WaveFormat ?? new WaveFormat(44100, 16, 2);

                    try
                    {
                        _waveOut?.Dispose();
                    }
                    catch { }

                    _waveOut = null;
                    _waveProvider = null;
                    _isPlaying = false;
                    _lastBufferedBytes = 0;
                    _sampleCount = 0; // Reset sample count for fresh start
                    _needsRecreation = false;

                    // Wait for Windows to release resources
                    System.Threading.Thread.Sleep(150);

                    // Create fresh device
                    CreateAudioDevice(oldFormat);
                    _prebufferTimer.Restart();

                    Console.WriteLine("✓ Audio device recreated, ready for new stream");
                }

                if (_waveProvider == null || _waveOut == null)
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
                            var state = _waveOut.PlaybackState;
                            Console.WriteLine($"✓ Audio playback started (prebuffered {_prebufferTimer.ElapsedMilliseconds}ms, state: {state})");
                        }
                    }
                    else
                    {
                        // Check if buffer is stuck (NAudio claims Playing but isn't consuming)
                        if (_sampleCount % 500 == 0)
                        {
                            int currentBuffered = _waveProvider.BufferedBytes;

                            // If buffer is maxed out and NAudio claims it's playing, something is wrong
                            if (currentBuffered >= _bytesPerSecond * 2.9 && // Near max (3s buffer)
                                _waveOut.PlaybackState == PlaybackState.Playing &&
                                currentBuffered == _lastBufferedBytes) // Buffer hasn't decreased
                            {
                                Console.WriteLine($"⚠ NAudio stuck! Buffer maxed at {currentBuffered} bytes but not consuming.");
                                Console.WriteLine($"   Force-stopping playback to trigger recovery...");

                                // Force NAudio to realize there's a problem
                                try
                                {
                                    _waveOut.Stop(); // This will trigger PlaybackStopped event
                                    _isPlaying = false;
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine($"   Error stopping playback: {ex.Message}");
                                    // If stop fails, set recreation flag manually
                                    _needsRecreation = true;
                                }
                            }

                            _lastBufferedBytes = currentBuffered;

                            // Log status every 5000 packets
                            if (_sampleCount % 5000 == 0)
                            {
                                var state = _waveOut.PlaybackState;
                                Console.WriteLine($"Audio: {_sampleCount} packets | Buffer: {currentBuffered / 1024}KB | State: {state}");
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
