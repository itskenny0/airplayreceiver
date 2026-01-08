using System;
using System.Threading;
using AirPlay.Models;
using Veldrid.Sdl2;

namespace AirPlay.Services
{
    public class CrossPlatformVideoManager : IDisposable
    {
        private VideoDecoder _decoder;
        private bool _initialized = false;
        private int _frameCount = 0;
        private bool _disposed = false;
        private int _width = 0;
        private int _height = 0;

        // SDL2 objects
        private Sdl2Window _window;
        private IntPtr _renderer;
        private IntPtr _texture;
        private Thread _renderThread;
        private readonly object _frameLock = new object();
        private byte[] _currentFrame;
        private bool _hasNewFrame = false;

        public void Initialize()
        {
            if (_initialized) return;

            Console.WriteLine("Initializing cross-platform video manager with SDL2...");

            // Initialize video decoder
            _decoder = new VideoDecoder();
            _decoder.Initialize();

            _initialized = true;
            Console.WriteLine("Video manager initialized successfully");
        }

        private unsafe void InitializeSDL(int width, int height)
        {
            try
            {
                // Create SDL2 window
                _window = new Sdl2Window(
                    "AirPlay Screen Mirroring",
                    50, 50,
                    width, height,
                    SDL_WindowFlags.Resizable | SDL_WindowFlags.Shown,
                    false);

                // Create SDL2 renderer
                _renderer = SDL_CreateRenderer(
                    _window.SdlWindowHandle,
                    -1,
                    SDL_RendererFlags.Accelerated | SDL_RendererFlags.PresentVsync);

                if (_renderer == IntPtr.Zero)
                {
                    var error = System.Runtime.InteropServices.Marshal.PtrToStringAnsi((IntPtr)Sdl2Native.SDL_GetError());
                    Console.WriteLine($"Failed to create SDL renderer: {error}");
                    return;
                }

                // Create SDL2 texture for video frames (BGR24 format)
                _texture = SDL_CreateTexture(
                    _renderer,
                    SDL_PIXELFORMAT_RGB24,
                    (int)SDL_TextureAccess.Streaming,
                    width,
                    height);

                if (_texture == IntPtr.Zero)
                {
                    var error = System.Runtime.InteropServices.Marshal.PtrToStringAnsi((IntPtr)Sdl2Native.SDL_GetError());
                    Console.WriteLine($"Failed to create SDL texture: {error}");
                    return;
                }

                // Start render loop in separate thread
                _renderThread = new Thread(RenderLoop);
                _renderThread.IsBackground = true;
                _renderThread.Start();

                Console.WriteLine($"SDL2 video window created ({width}x{height})");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to initialize SDL2: {ex.Message}");
            }
        }

        private unsafe void RenderLoop()
        {
            try
            {
                while (!_disposed && _window != null)
                {
                    // Process SDL events
                    SDL_Event evt;
                    while (Sdl2Native.SDL_PollEvent(&evt) != 0)
                    {
                        if (evt.type == SDL_EventType.Quit)
                        {
                            Console.WriteLine("User closed video window");
                            Dispose();
                            return;
                        }
                    }

                    // Update texture if we have a new frame
                    lock (_frameLock)
                    {
                        if (_hasNewFrame && _currentFrame != null && _texture != IntPtr.Zero)
                        {
                            unsafe
                            {
                                fixed (byte* pFrame = _currentFrame)
                                {
                                    SDL_UpdateTexture(_texture, IntPtr.Zero, (IntPtr)pFrame, _width * 3);
                                }
                            }
                            _hasNewFrame = false;
                        }
                    }

                    // Clear renderer
                    SDL_RenderClear(_renderer);

                    // Render texture
                    if (_texture != IntPtr.Zero)
                    {
                        SDL_RenderCopy(_renderer, _texture, IntPtr.Zero, IntPtr.Zero);
                    }

                    // Present
                    SDL_RenderPresent(_renderer);

                    // Small delay to avoid maxing CPU
                    Thread.Sleep(16); // ~60 FPS
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in render loop: {ex.Message}");
            }
        }

        public void ProcessH264Frame(H264Data h264Data)
        {
            if (!_initialized || _disposed)
            {
                Initialize();
            }

            if (_decoder == null)
                return;

            try
            {
                // Decode H.264 to RGB
                byte[] rgbFrame = _decoder.DecodeFrame(
                    h264Data.Data, h264Data.Length,
                    out int width, out int height, out int stride);

                if (rgbFrame == null || width == 0 || height == 0)
                    return;

                _frameCount++;

                // Initialize SDL on first successful frame
                if (_frameCount == 1)
                {
                    _width = width;
                    _height = height;
                    InitializeSDL(width, height);
                    Console.WriteLine($"✓ Video rendering started ({width}x{height})");
                }

                // Update current frame for rendering
                lock (_frameLock)
                {
                    _currentFrame = rgbFrame;
                    _hasNewFrame = true;
                }

                // Log periodically
                if (_frameCount % 300 == 0)
                {
                    Console.WriteLine($"Video: {_frameCount} frames rendered ({_width}x{_height})");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error processing video frame: {ex.Message}");
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            Console.WriteLine($"Disposing video manager ({_frameCount} frames processed)...");

            // Cleanup SDL resources
            if (_texture != IntPtr.Zero)
            {
                SDL_DestroyTexture(_texture);
                _texture = IntPtr.Zero;
            }

            if (_renderer != IntPtr.Zero)
            {
                SDL_DestroyRenderer(_renderer);
                _renderer = IntPtr.Zero;
            }

            _window?.Close();
            _window = null;

            _decoder?.Dispose();
            _decoder = null;

            _initialized = false;
        }

        // SDL2 P/Invoke declarations
        [System.Runtime.InteropServices.DllImport("SDL2", CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
        private static extern IntPtr SDL_CreateRenderer(IntPtr window, int index, SDL_RendererFlags flags);

        [System.Runtime.InteropServices.DllImport("SDL2", CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
        private static extern void SDL_DestroyRenderer(IntPtr renderer);

        [System.Runtime.InteropServices.DllImport("SDL2", CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
        private static extern IntPtr SDL_CreateTexture(IntPtr renderer, uint format, int access, int w, int h);

        [System.Runtime.InteropServices.DllImport("SDL2", CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
        private static extern void SDL_DestroyTexture(IntPtr texture);

        [System.Runtime.InteropServices.DllImport("SDL2", CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
        private static extern int SDL_UpdateTexture(IntPtr texture, IntPtr rect, IntPtr pixels, int pitch);

        [System.Runtime.InteropServices.DllImport("SDL2", CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
        private static extern int SDL_RenderClear(IntPtr renderer);

        [System.Runtime.InteropServices.DllImport("SDL2", CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
        private static extern int SDL_RenderCopy(IntPtr renderer, IntPtr texture, IntPtr srcrect, IntPtr dstrect);

        [System.Runtime.InteropServices.DllImport("SDL2", CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
        private static extern void SDL_RenderPresent(IntPtr renderer);

        private const uint SDL_PIXELFORMAT_RGB24 = 0x17101803;

        [System.Flags]
        private enum SDL_RendererFlags : uint
        {
            Software = 0x01,
            Accelerated = 0x02,
            PresentVsync = 0x04,
            TargetTexture = 0x08
        }

        private enum SDL_TextureAccess
        {
            Static = 0,
            Streaming = 1,
            Target = 2
        }
    }
}
