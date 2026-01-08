using System;
using System.Runtime.InteropServices;
using FFmpeg.AutoGen;

namespace AirPlay.Services
{
    public unsafe class VideoDecoder : IDisposable
    {
        private AVCodecContext* _codecContext;
        private AVFrame* _frame;
        private AVFrame* _frameRGB;
        private SwsContext* _swsContext;
        private AVPacket* _packet;
        private byte[] _rgbBuffer;
        private bool _initialized = false;
        private int _currentWidth = 0;
        private int _currentHeight = 0;

        public VideoDecoder()
        {
            // Try to locate FFmpeg from system installation
            try
            {
                string ffmpegPath = FindFFmpegPath();
                if (!string.IsNullOrEmpty(ffmpegPath))
                {
                    FFmpeg.AutoGen.ffmpeg.RootPath = ffmpegPath;
                    Console.WriteLine($"Found FFmpeg at: {ffmpegPath}");
                }
                else
                {
                    // Try current directory as fallback
                    FFmpeg.AutoGen.ffmpeg.RootPath = AppDomain.CurrentDomain.BaseDirectory;
                    Console.WriteLine($"Using bundled FFmpeg from: {FFmpeg.AutoGen.ffmpeg.RootPath}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: Could not set FFmpeg path: {ex.Message}");
            }
        }

        private string FindFFmpegPath()
        {
            Console.WriteLine("Searching for FFmpeg installation...");

            // Common FFmpeg installation locations on Windows
            var possiblePaths = new[]
            {
                @"C:\ffmpeg\bin",
                @"C:\Program Files\ffmpeg\bin",
                @"C:\Program Files (x86)\ffmpeg\bin",
                Environment.GetEnvironmentVariable("FFMPEG_PATH"),
                AppDomain.CurrentDomain.BaseDirectory // Bundled with app
            };

            // Check for FFmpeg versions 6.0, 6.1, and 7.0
            var versionDlls = new[] { "avcodec-60.dll", "avcodec-61.dll", "avcodec-62.dll" };

            foreach (var path in possiblePaths)
            {
                if (string.IsNullOrEmpty(path)) continue;
                Console.WriteLine($"  Checking: {path}");

                foreach (var dll in versionDlls)
                {
                    var avcodecPath = System.IO.Path.Combine(path, dll);
                    if (System.IO.File.Exists(avcodecPath))
                    {
                        Console.WriteLine($"  ✓ Found FFmpeg: {avcodecPath}");
                        return path;
                    }
                }
            }

            // Check PATH environment variable
            var pathVar = Environment.GetEnvironmentVariable("PATH");
            if (!string.IsNullOrEmpty(pathVar))
            {
                Console.WriteLine("  Checking system PATH...");
                foreach (var pathDir in pathVar.Split(';'))
                {
                    if (string.IsNullOrEmpty(pathDir)) continue;

                    foreach (var dll in versionDlls)
                    {
                        var avcodecPath = System.IO.Path.Combine(pathDir.Trim(), dll);
                        if (System.IO.File.Exists(avcodecPath))
                        {
                            Console.WriteLine($"  ✓ Found FFmpeg in PATH: {avcodecPath}");
                            return pathDir.Trim();
                        }
                    }
                }
            }

            Console.WriteLine("  ✗ FFmpeg not found in any standard location");
            return null;
        }

        public void Initialize()
        {
            if (_initialized) return;

            try
            {
                Console.WriteLine("Initializing H.264 video decoder...");

                // Find H.264 decoder
                AVCodec* codec = ffmpeg.avcodec_find_decoder(AVCodecID.AV_CODEC_ID_H264);
                if (codec == null)
                    throw new Exception("H.264 codec not found");

                _codecContext = ffmpeg.avcodec_alloc_context3(codec);
                if (_codecContext == null)
                    throw new Exception("Could not allocate codec context");

                // Open codec
                int result = ffmpeg.avcodec_open2(_codecContext, codec, null);
                if (result < 0)
                    throw new Exception($"Could not open codec: {GetErrorMessage(result)}");

                // Allocate frames and packet
                _frame = ffmpeg.av_frame_alloc();
                _frameRGB = ffmpeg.av_frame_alloc();
                _packet = ffmpeg.av_packet_alloc();

                _initialized = true;
                Console.WriteLine("✓ Video decoder initialized successfully");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to initialize video decoder: {ex.Message}");
                Console.WriteLine("");
                Console.WriteLine("Video support requires FFmpeg 6.0 or 6.1 to be installed.");
                Console.WriteLine("Installation options:");
                Console.WriteLine("  1. Download from: https://github.com/BtbN/FFmpeg-Builds/releases");
                Console.WriteLine("     (Look for 'ffmpeg-n6.0-*-win64-gpl-shared'");
                Console.WriteLine("  2. Extract to C:\\ffmpeg\\bin");
                Console.WriteLine("  3. Or set FFMPEG_PATH environment variable");
                Console.WriteLine("");
                Console.WriteLine("Audio will continue to work without FFmpeg.");
                throw;
            }
        }

        public byte[] DecodeFrame(byte[] h264Data, int length, out int width, out int height, out int stride)
        {
            width = 0;
            height = 0;
            stride = 0;

            if (!_initialized)
            {
                Initialize();
            }

            try
            {
                fixed (byte* pData = h264Data)
                {
                    _packet->data = pData;
                    _packet->size = length;

                    // Send packet to decoder
                    int ret = ffmpeg.avcodec_send_packet(_codecContext, _packet);
                    if (ret < 0)
                    {
                        // Not necessarily an error - could be waiting for more data
                        return null;
                    }

                    // Receive decoded frame
                    ret = ffmpeg.avcodec_receive_frame(_codecContext, _frame);
                    if (ret == ffmpeg.AVERROR(ffmpeg.EAGAIN) || ret == ffmpeg.AVERROR_EOF)
                    {
                        // Need more data or end of stream
                        return null;
                    }
                    if (ret < 0)
                    {
                        Console.WriteLine($"Error receiving frame: {GetErrorMessage(ret)}");
                        return null;
                    }

                    width = _frame->width;
                    height = _frame->height;
                    stride = width * 3; // BGR24 = 3 bytes per pixel

                    // Initialize SwsContext for color conversion if needed
                    if (_swsContext == null || width != _currentWidth || height != _currentHeight)
                    {
                        if (_swsContext != null)
                            ffmpeg.sws_freeContext(_swsContext);

                        _swsContext = ffmpeg.sws_getContext(
                            width, height, (AVPixelFormat)_frame->format,
                            width, height, AVPixelFormat.AV_PIX_FMT_BGR24,
                            ffmpeg.SWS_BILINEAR, null, null, null);

                        if (_swsContext == null)
                            throw new Exception("Could not initialize SwsContext");

                        _currentWidth = width;
                        _currentHeight = height;
                        _rgbBuffer = new byte[stride * height];

                        Console.WriteLine($"Video frame dimensions: {width}x{height}");
                    }

                    // Convert YUV to BGR24
                    fixed (byte* pBuffer = _rgbBuffer)
                    {
                        byte*[] dstData = new byte*[] { pBuffer, null, null, null };
                        int[] dstLinesize = new int[] { stride, 0, 0, 0 };

                        ffmpeg.sws_scale(_swsContext, _frame->data, _frame->linesize, 0,
                            height, dstData, dstLinesize);
                    }

                    return _rgbBuffer;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error decoding frame: {ex.Message}");
                return null;
            }
        }

        private string GetErrorMessage(int error)
        {
            byte[] buffer = new byte[1024];
            fixed (byte* pBuffer = buffer)
            {
                ffmpeg.av_strerror(error, pBuffer, (ulong)buffer.Length);
                return Marshal.PtrToStringAnsi((IntPtr)pBuffer);
            }
        }

        public void Dispose()
        {
            if (_swsContext != null)
            {
                ffmpeg.sws_freeContext(_swsContext);
                _swsContext = null;
            }

            if (_frame != null)
            {
                AVFrame* frame = _frame;
                ffmpeg.av_frame_free(&frame);
                _frame = null;
            }

            if (_frameRGB != null)
            {
                AVFrame* frameRGB = _frameRGB;
                ffmpeg.av_frame_free(&frameRGB);
                _frameRGB = null;
            }

            if (_packet != null)
            {
                AVPacket* packet = _packet;
                ffmpeg.av_packet_free(&packet);
                _packet = null;
            }

            if (_codecContext != null)
            {
                AVCodecContext* ctx = _codecContext;
                ffmpeg.avcodec_free_context(&ctx);
                _codecContext = null;
            }

            _initialized = false;
        }
    }
}
