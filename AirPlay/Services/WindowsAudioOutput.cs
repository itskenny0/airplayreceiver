using NAudio.Wave;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace AirPlay.Services
{
    public class WindowsAudioOutput : IDisposable
    {
        private static string Timestamp() => DateTime.Now.ToString("HH:mm:ss.fff");
        private IWavePlayer _waveOut;
        private StreamingWaveProvider _streamProvider;
        private readonly object _lock = new object();
        private bool _disposed = false;
        private int _sampleCount = 0;
        private bool _isPlaying = false;
        private bool _initialized = false;  // Track if Init() has been called
        private bool _awaitingFlush = false;  // Track if we're waiting to restart after flush
        private Stopwatch _prebufferTimer = new Stopwatch();
        private DateTime _playbackStartTime;
        private int _bytesPerSecond;
        private int _prebufferPackets;
        private bool _needsReinit = false;
        private WaveFormat _waveFormat;
        private CancellationTokenSource _watchdogCts;
        private int _queueFullCounter = 0;
        private int _consecutiveSkips = 0;  // v47: Track consecutive bad packets
        private const int MAX_CONSECUTIVE_SKIPS = 100;  // Reset decoder after 100 bad packets

        public event EventHandler PlaybackStoppedUnexpectedly;
        public event EventHandler DecoderNeedsReset;  // v47: Signal decoder is broken

        public bool NeedsRecreation
        {
            get
            {
                lock (_lock)
                {
                    return _needsReinit;
                }
            }
        }

        public void Initialize()
        {
            lock (_lock)
            {
                if (_disposed) return;

                Console.WriteLine($"[{Timestamp()}] " + "=== WindowsAudioOutput v48 - SKIP-FIX ===");
                Console.WriteLine($"[{Timestamp()}] " + "Initializing audio output (skip-frame fix for throughput)...");

                // PCM format: 16-bit, 44.1kHz, stereo (as decoded by AAC-ELD/AAC/ALAC)
                _waveFormat = new WaveFormat(44100, 16, 2);
                _bytesPerSecond = _waveFormat.AverageBytesPerSecond; // 176,400 bytes/sec

                // Create streaming provider that NAudio pulls from
                // v44: Increased from 100 to 500 to handle iOS burst sending (280+ packets/sec)
                _streamProvider = new StreamingWaveProvider(_waveFormat, maxQueueSize: 500);

                // Create DirectSound device (but DON'T call Init() yet)
                _waveOut = new DirectSoundOut(100); // 100ms latency

                _waveOut.PlaybackStopped += (sender, args) =>
                {
                    if (_disposed) return;

                    Console.WriteLine($"[{Timestamp()}] " + $"⚠ Playback stopped event fired. Exception: {args.Exception?.Message ?? "none"}");

                    if (args.Exception != null)
                    {
                        // Real error - need full recreation
                        Console.WriteLine($"[{Timestamp()}] " + $"   Stack trace: {args.Exception.StackTrace}");

                        lock (_lock)
                        {
                            _isPlaying = false;
                            _needsReinit = true;
                        }

                        Console.WriteLine($"[{Timestamp()}] " + $"  Audio device needs full recreation - notifying service");
                        PlaybackStoppedUnexpectedly?.Invoke(this, EventArgs.Empty);
                    }
                };

                // CRITICAL FIX: Don't call Init() until we have audio data in the queue
                // If we call Init() on an empty queue, DirectSound's render thread starts immediately,
                // gets silence, and terminates. Even calling Play() later won't resurrect it.
                _initialized = false;
                _prebufferTimer.Start();

                // Prebuffer 50 packets (~1 second of audio) before calling Init()
                _prebufferPackets = 50;

                Console.WriteLine($"[{Timestamp()}] " + $"✓ Audio device created: {_waveFormat.SampleRate}Hz, {_waveFormat.BitsPerSample}-bit, {_waveFormat.Channels} channels");
                Console.WriteLine($"[{Timestamp()}] " + $"  Will call Init() after prebuffering {_prebufferPackets} packets");
            }
        }

        public void HandleFlush()
        {
            lock (_lock)
            {
                if (_disposed)
                    return;

                Console.WriteLine($"[{Timestamp()}] " + "Track change detected - proactively restarting DirectSound...");

                // Stop watchdog during restart
                _watchdogCts?.Cancel();

                try
                {
                    // Stop and dispose current DirectSound device
                    if (_waveOut != null)
                    {
                        try
                        {
                            _waveOut.Stop();
                            _waveOut.Dispose();
                        }
                        catch { }
                        _waveOut = null;
                    }

                    // Clear queue of old track data
                    _streamProvider?.Clear();

                    // Reset state for "mini-restart"
                    _initialized = false;
                    _isPlaying = false;
                    _awaitingFlush = true;
                    _sampleCount = 0;
                    _queueFullCounter = 0;
                    _prebufferTimer.Restart();

                    Console.WriteLine($"[{Timestamp()}] " + "Waiting 200ms for DirectSound to release COM resources...");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[{Timestamp()}] " + $"Error during flush cleanup: {ex.Message}");
                }
            }

            // Wait OUTSIDE the lock to prevent blocking incoming audio packets
            Thread.Sleep(200);

            lock (_lock)
            {
                try
                {
                    // Create new DirectSound device (but don't Init() yet - wait for prebuffering)
                    _waveOut = new DirectSoundOut(100);

                    _waveOut.PlaybackStopped += (sender, args) =>
                    {
                        if (_disposed) return;

                        Console.WriteLine($"[{Timestamp()}] " + $"⚠ Playback stopped event fired. Exception: {args.Exception?.Message ?? "none"}");

                        if (args.Exception != null)
                        {
                            Console.WriteLine($"[{Timestamp()}] " + $"   Stack trace: {args.Exception.StackTrace}");

                            lock (_lock)
                            {
                                _isPlaying = false;
                                _needsReinit = true;
                            }

                            Console.WriteLine($"[{Timestamp()}] " + $"  Audio device needs full recreation - notifying service");
                            PlaybackStoppedUnexpectedly?.Invoke(this, EventArgs.Empty);
                        }
                    };

                    Console.WriteLine($"[{Timestamp()}] " + $"✓ DirectSound recreated for new track, waiting for {_prebufferPackets} packets before Init()...");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[{Timestamp()}] " + $"✗ Failed to recreate DirectSound: {ex.Message}");
                    _needsReinit = true;
                }
            }
        }

        public void AddSamples(byte[] buffer, int offset, int count)
        {
            lock (_lock)
            {
                if (_disposed)
                    return;

                // If device needs full recreation, don't accept new samples
                if (_needsReinit)
                {
                    // Drop packets until external code recreates us
                    return;
                }

                if (_waveOut == null || _streamProvider == null)
                    return;

                try
                {
                    // CRITICAL: Detect if DirectSound died after pause/gap in audio
                    // If queue was empty and DirectSound hasn't called Read() recently, it's dead
                    bool queueWasEmpty = _streamProvider.QueuedBuffers == 0;
                    var lastReadAge = DateTime.UtcNow - _streamProvider.LastReadTime;
                    bool directSoundDead = _initialized && queueWasEmpty && lastReadAge.TotalSeconds > 3;

                    if (directSoundDead)
                    {
                        Console.WriteLine($"[{Timestamp()}] " + $"Detected DirectSound death after pause/gap (queue empty for 3s)");
                        Console.WriteLine($"[{Timestamp()}] " + $"Recreating DirectSound with deferred init...");

                        // Stop watchdog during restart
                        _watchdogCts?.Cancel();

                        // Dispose dead DirectSound
                        try
                        {
                            _waveOut.Stop();
                            _waveOut.Dispose();
                        }
                        catch { }

                        // Wait for COM cleanup
                        Thread.Sleep(200);

                        // Create new DirectSound device
                        _waveOut = new DirectSoundOut(100);
                        _waveOut.PlaybackStopped += (sender, args) =>
                        {
                            if (_disposed) return;
                            if (args.Exception != null)
                            {
                                Console.WriteLine($"[{Timestamp()}] " + $"⚠ Playback stopped with error: {args.Exception.Message}");
                                lock (_lock)
                                {
                                    _isPlaying = false;
                                    _needsReinit = true;
                                }
                                PlaybackStoppedUnexpectedly?.Invoke(this, EventArgs.Empty);
                            }
                        };

                        // Reset state for restart
                        _initialized = false;
                        _isPlaying = false;
                        _sampleCount = 0;
                        _queueFullCounter = 0;
                        _prebufferTimer.Restart();

                        Console.WriteLine($"[{Timestamp()}] " + $"✓ DirectSound recreated, waiting for {_prebufferPackets} packets before Init()");
                    }

                    // Validate PCM data before adding to queue (v47: detect decoder corruption)
                    bool isCorrupted = false;

                    if (buffer == null || count <= 0)
                    {
                        _consecutiveSkips++;
                        if (_consecutiveSkips % 50 == 1)
                        {
                            Console.WriteLine($"[{Timestamp()}] " + $"Skipping invalid PCM data: buffer={buffer != null}, count={count} (skip #{_consecutiveSkips})");
                        }
                        isCorrupted = true;
                    }
                    else if (offset < 0 || offset + count > buffer.Length)
                    {
                        _consecutiveSkips++;
                        if (_consecutiveSkips % 50 == 1)
                        {
                            Console.WriteLine($"[{Timestamp()}] " + $"PCM data bounds error: buffer.Length={buffer.Length}, offset={offset}, count={count} (skip #{_consecutiveSkips})");
                        }
                        isCorrupted = true;
                    }

                    // v47: Detect decoder corruption - if too many consecutive bad packets, reset decoder
                    if (isCorrupted)
                    {
                        if (_consecutiveSkips >= MAX_CONSECUTIVE_SKIPS)
                        {
                            Console.WriteLine($"[{Timestamp()}] " + $"⚠ DECODER CORRUPTION DETECTED: {_consecutiveSkips} consecutive bad packets!");
                            Console.WriteLine($"[{Timestamp()}] " + $"  Decoder is returning empty/corrupted buffers - needs reset");
                            _consecutiveSkips = 0;

                            // Signal that decoder needs to be reset
                            DecoderNeedsReset?.Invoke(this, EventArgs.Empty);
                        }
                        return;
                    }

                    // Reset consecutive skip counter on good packet
                    if (_consecutiveSkips > 0)
                    {
                        Console.WriteLine($"[{Timestamp()}] " + $"✓ Decoder recovered (good packet after {_consecutiveSkips} skips)");
                        _consecutiveSkips = 0;
                    }

                    // Add samples to streaming queue
                    bool added = _streamProvider.AddSamples(buffer, offset, count);

                    _sampleCount++;

                    // CRITICAL FIX: Initialize DirectSound AFTER we have prebuffered data
                    // This ensures the queue has audio when DirectSound's render thread starts
                    // This works for both initial startup AND track changes
                    if (!_initialized && _sampleCount >= _prebufferPackets)
                    {
                        var queuedPackets = _streamProvider.QueuedBuffers;

                        if (_awaitingFlush)
                        {
                            Console.WriteLine($"[{Timestamp()}] " + $"New track prebuffered {queuedPackets} packets, now calling Init()...");
                            _awaitingFlush = false;
                        }
                        else
                        {
                            Console.WriteLine($"[{Timestamp()}] " + $"Prebuffered {queuedPackets} packets, now calling Init()...");
                        }

                        _waveOut.Init(_streamProvider);
                        _initialized = true;
                        Console.WriteLine($"[{Timestamp()}] " + $"✓ DirectSound Init() completed with {queuedPackets} packets ready");
                    }

                    // Start playback after Init (separate check to ensure Init completed first)
                    if (_initialized && !_isPlaying)
                    {
                        _waveOut.Play();
                        _isPlaying = true;
                        _playbackStartTime = DateTime.UtcNow;
                        var state = _waveOut.PlaybackState;
                        Console.WriteLine($"[{Timestamp()}] " + $"✓ Audio playback started (prebuffered {_prebufferPackets} packets / {_prebufferTimer.ElapsedMilliseconds}ms)");
                        Console.WriteLine($"[{Timestamp()}] " + $"  PlaybackState: {state}, Queue: {_streamProvider.QueuedBuffers} packets");

                        // Start watchdog to detect if NAudio stops calling Read()
                        StartWatchdog();
                    }

                    // Log status every 50000 packets (~1 minute of audio)
                    if (_sampleCount % 50000 == 0)
                    {
                        var state = _waveOut.PlaybackState;
                        var queued = _streamProvider.QueuedBuffers;
                        var totalRead = _streamProvider.TotalBytesRead;
                        Console.WriteLine($"[{Timestamp()}] " + $"Audio: {_sampleCount} packets | Queue: {queued} packets | Read: {totalRead / 1024}KB | State: {state}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[{Timestamp()}] " + $"Audio error: {ex.Message}");
                }
            }
        }

        private void StartWatchdog()
        {
            // Cancel any existing watchdog
            _watchdogCts?.Cancel();

            _watchdogCts = new CancellationTokenSource();
            var token = _watchdogCts.Token;

            Task.Run(async () =>
            {
                try
                {
                    Console.WriteLine($"[{Timestamp()}] " + "Watchdog started");
                    int checkCount = 0;

                    while (!token.IsCancellationRequested && !_disposed)
                    {
                        await Task.Delay(2000, token);
                        checkCount++;

                        bool shouldRecreate = false;

                        lock (_lock)
                        {
                            if (_disposed || !_isPlaying || _needsReinit)
                            {
                                Console.WriteLine($"[{Timestamp()}] " + $"Watchdog check #{checkCount}: Skipped (disposed={_disposed}, playing={_isPlaying}, needsReinit={_needsReinit})");
                                _queueFullCounter = 0;
                                continue;
                            }

                            var timeSinceLastRead = DateTime.UtcNow - _streamProvider.LastReadTime;
                            var queuedPackets = _streamProvider.QueuedBuffers;

                            // Log every 5th check for debugging
                            if (checkCount % 5 == 0)
                            {
                                Console.WriteLine($"[{Timestamp()}] " + $"Watchdog check #{checkCount}: Queue={queuedPackets}, LastRead={timeSinceLastRead.TotalSeconds:F1}s ago, Counter={_queueFullCounter}");
                            }

                            // Check 1: NAudio has stopped calling Read() entirely
                            if (timeSinceLastRead.TotalSeconds > 3 && queuedPackets > 10)
                            {
                                Console.WriteLine($"[{Timestamp()}] " + $"⚠ NAudio watchdog: No Read() calls for {timeSinceLastRead.TotalSeconds:F1}s with {queuedPackets} packets queued");
                                Console.WriteLine($"[{Timestamp()}] " + $"  NAudio has stopped consuming audio - forcing recreation");

                                _isPlaying = false;
                                _needsReinit = true;
                                shouldRecreate = true;
                            }
                            // Check 2: Queue stuck at max capacity (NAudio calling Read() but too slowly)
                            else if (queuedPackets >= 480) // Near max (v44: 500)
                            {
                                _queueFullCounter++;
                                Console.WriteLine($"[{Timestamp()}] " + $"Watchdog: Queue stuck at {queuedPackets}, counter now {_queueFullCounter}/5");

                                // If queue stuck near-max for 10+ seconds (5 checks), NAudio is too slow
                                // v44: More lenient threshold since buffer is 5x larger
                                if (_queueFullCounter >= 5)
                                {
                                    Console.WriteLine($"[{Timestamp()}] " + $"⚠ NAudio watchdog: Queue stuck at {queuedPackets} packets for {_queueFullCounter * 2}+ seconds");
                                    Console.WriteLine($"[{Timestamp()}] " + $"  NAudio consuming too slowly - forcing recreation");

                                    _isPlaying = false;
                                    _needsReinit = true;
                                    shouldRecreate = true;
                                }
                            }
                            else
                            {
                                // Queue healthy, reset counter
                                if (_queueFullCounter > 0)
                                {
                                    Console.WriteLine($"[{Timestamp()}] " + $"Watchdog: Queue recovered to {queuedPackets}, resetting counter");
                                }
                                _queueFullCounter = 0;
                            }
                        }

                        // Fire event OUTSIDE the lock to avoid deadlock
                        if (shouldRecreate)
                        {
                            Console.WriteLine($"[{Timestamp()}] " + "Watchdog: Firing PlaybackStoppedUnexpectedly event");
                            PlaybackStoppedUnexpectedly?.Invoke(this, EventArgs.Empty);
                            break;
                        }
                    }

                    Console.WriteLine($"[{Timestamp()}] " + $"Watchdog stopped (cancelled={token.IsCancellationRequested}, disposed={_disposed})");
                }
                catch (TaskCanceledException)
                {
                    Console.WriteLine($"[{Timestamp()}] " + "Watchdog cancelled normally");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[{Timestamp()}] " + $"Watchdog error: {ex.Message}");
                    Console.WriteLine($"[{Timestamp()}] " + $"Stack trace: {ex.StackTrace}");
                }
            }, token);
        }

        public void Dispose()
        {
            lock (_lock)
            {
                if (_disposed) return;
                _disposed = true;

                _watchdogCts?.Cancel();
                _watchdogCts?.Dispose();

                try
                {
                    _waveOut?.Stop();
                    _waveOut?.Dispose();
                }
                catch { }

                _streamProvider?.Clear();
                _waveOut = null;
                _streamProvider = null;

                Console.WriteLine($"[{Timestamp()}] " + $"Audio disposed (played {_sampleCount} packets total)");
            }
        }
    }
}
