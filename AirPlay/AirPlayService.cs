using AirPlay.Models.Configs;
using AirPlay.Services;
using AirPlay.Utils;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace AirPlay
{
    public class AirPlayService : IHostedService, IDisposable
    {
        private readonly IAirPlayReceiver _airPlayReceiver;
        private readonly DumpConfig _dConfig;

        private WindowsAudioOutput _audioOutput;
        private CrossPlatformVideoManager _videoManager;
        private List<byte> _audiobuf;
        private readonly object _audioOutputLock = new object();
        private volatile bool _isRecreatingAudio = false;

        public AirPlayService(IAirPlayReceiver airPlayReceiver, IOptions<DumpConfig> dConfig)
        {
            _airPlayReceiver = airPlayReceiver ?? throw new ArgumentNullException(nameof(airPlayReceiver));
            _dConfig = dConfig?.Value ?? throw new ArgumentNullException(nameof(dConfig));
        }

        private void RecreateAudioOutput()
        {
            lock (_audioOutputLock)
            {
                Console.WriteLine("Recreating audio output after unexpected stop...");
                _isRecreatingAudio = true; // Drop incoming packets during recreation

                try
                {
                    // Dispose old device
                    _audioOutput?.Dispose();

                    // CRITICAL: Wait for DirectSound to fully release COM resources
                    // Creating a new device immediately after disposal can reuse broken internal state
                    Console.WriteLine("Waiting 200ms for DirectSound to release resources...");
                    Thread.Sleep(200);

                    _audioOutput = new WindowsAudioOutput();
                    _audioOutput.Initialize();

                    // Subscribe to playback stopped event
                    _audioOutput.PlaybackStoppedUnexpectedly += (s, e) => RecreateAudioOutput();

                    Console.WriteLine("✓ Audio output recreated successfully");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"✗ Failed to recreate audio output: {ex.Message}");
                }
                finally
                {
                    _isRecreatingAudio = false; // Resume accepting packets
                }
            }
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
#if DUMP
            var bPath = _dConfig.Path;
            var fPath = Path.Combine(bPath, "frames/");
            var oPath = Path.Combine(bPath, "out/");
            var pPath = Path.Combine(bPath, "pcm/");

            if (!Directory.Exists(bPath))
            {
                Directory.CreateDirectory(bPath);
            }
            if (!Directory.Exists(fPath))
            {
                Directory.CreateDirectory(fPath);
            }
            if (!Directory.Exists(oPath))
            {
                Directory.CreateDirectory(oPath);
            }
            if (!Directory.Exists(pPath))
            {
                Directory.CreateDirectory(pPath);
            }
#endif

            // Initialize audio and video output (Windows/Linux)
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) || RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                try
                {
                    _audioOutput = new WindowsAudioOutput();
                    _audioOutput.Initialize();

                    // Subscribe to playback stopped event for automatic recreation
                    _audioOutput.PlaybackStoppedUnexpectedly += (s, e) => RecreateAudioOutput();

                    Console.WriteLine("Audio output initialized successfully");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to initialize audio output: {ex.Message}");
                }

                try
                {
                    _videoManager = new CrossPlatformVideoManager();
                    _videoManager.Initialize();
                    Console.WriteLine("✓ Video output initialized successfully");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"✗ Video output disabled: {ex.Message}");
                    _videoManager?.Dispose();
                    _videoManager = null;
                    Console.WriteLine("Continuing with audio-only mode...");
                }
            }

            await _airPlayReceiver.StartListeners(cancellationToken);
            await _airPlayReceiver.StartMdnsAsync().ConfigureAwait(false);

            _airPlayReceiver.OnSetVolumeReceived += (s, e) =>
            {
                // SET VOLUME
            };

            _airPlayReceiver.OnAudioFlushReceived += (s, e) =>
            {
                // Track change - proactively recreate DirectSound with deferred-init pattern
                // This treats each track change as a "mini-restart" with prebuffering
                Console.WriteLine("Audio flush received - proactively restarting DirectSound for new track");

                // Thread-safe access to audio output
                lock (_audioOutputLock)
                {
                    _audioOutput?.HandleFlush();
                }
            };

            // Process H264 video frames
            _airPlayReceiver.OnH264DataReceived += (s, e) =>
            {
                // Render video through Windows video manager
                _videoManager?.ProcessH264Frame(e);

#if DUMP
                using (FileStream writer = new FileStream($"{bPath}dump.h264", FileMode.Append))
                {
                    writer.Write(e.Data, 0, e.Length);
                }
#endif
            };

            _audiobuf = new List<byte>();
            var pcmReceived = false;
            _airPlayReceiver.OnPCMDataReceived += (s, e) =>
            {
                // Drop packets during recreation to prevent queue flooding
                if (_isRecreatingAudio)
                    return;

                // Play audio through Windows speakers
                if (!pcmReceived)
                {
                    Console.WriteLine($"PCM data stream started (receiving {e.Length} bytes per packet)");
                    pcmReceived = true;
                }

                // Thread-safe access to audio output
                lock (_audioOutputLock)
                {
                    _audioOutput?.AddSamples(e.Data, 0, e.Length);
                }

#if DUMP
                _audiobuf.AddRange(e.Data);
#endif
            };
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            // Cleanup audio and video output
            _audioOutput?.Dispose();
            _audioOutput = null;

            _videoManager?.Dispose();
            _videoManager = null;

#if DUMP
            // DUMP WAV AUDIO
            var bPath = _dConfig.Path;
            using (var wr = new FileStream($"{bPath}dequeued.wav", FileMode.Create))
            {
                var header = Utilities.WriteWavHeader(2, 44100, 16, (uint)_audiobuf.Count);
                wr.Write(header, 0, header.Length);
            }

            using (FileStream writer = new FileStream($"{bPath}dequeued.wav", FileMode.Append))
            {
                writer.Write(_audiobuf.ToArray(), 0, _audiobuf.Count);
            }
#endif
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            _audioOutput?.Dispose();
            _audioOutput = null;

            _videoManager?.Dispose();
            _videoManager = null;
        }
    }
}
