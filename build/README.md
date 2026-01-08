# AirPlay Receiver - Windows Build Distribution

This directory contains the Windows build of the AirPlay Receiver and an HTTP server to distribute it.

## Contents

- `airplay-receiver-windows-x64.zip` - The portable Windows build (36 MB)
- `win-x64/` - Unpacked build directory
- `serve.py` - HTTP server to serve the package
- `START_SERVER.sh` - Quick start script for the server

## Quick Start - Serving the Build

### Option 1: Using the Start Script (Linux/macOS)
```bash
./START_SERVER.sh
```

### Option 2: Using Python Directly
```bash
python3 serve.py
```

### Option 3: Using Python's Built-in Server (Simple Alternative)
```bash
python3 -m http.server 8080
```

## Accessing the Build

Once the server is running:

1. **Locally**: Open http://localhost:8080 in your browser
2. **On Network**: Open http://<your-server-ip>:8080 from any device on the same network

The server provides a nice download page with information about the build.

## What's in the Windows Build

The `airplay-receiver-windows-x64.zip` contains:

- ✅ **Pre-configured** - Works out of the box, no manual configuration needed
- ✅ **Portable** - No installation required
- ✅ **Complete** - All codecs and runtime libraries included
- ✅ **Ready to use** - Just extract and run AirPlay.exe

### Included Components
- AirPlay.exe - Main application
- libfdk-aac-2.dll - AAC audio codec (9.7 MB)
- libalac-0.dll - ALAC (Apple Lossless) codec (415 KB)
- .NET Core 2.2 runtime libraries
- Pre-configured appsettings_win.json

### Default Configuration
- **Instance Name**: "AirPlay-Receiver"
- **Audio Port**: 5000
- **Video Port**: 7000
- **MAC Address**: AA:BB:CC:DD:EE:FF
- **Codec Paths**: Relative paths (portable)
- **Dump Folder**: .\dump\

## Server Features

The custom HTTP server (`serve.py`) provides:

- 🎨 Beautiful download page with instructions
- 📊 File size information
- 🔗 Direct download link
- 📱 Mobile-friendly responsive design
- 🌐 CORS headers for cross-origin access
- 🚀 Fast and lightweight

## Building from Source

If you want to rebuild the Windows package:

1. Install dependencies:
   - .NET SDK 8.0+
   - MinGW-w64 cross-compiler
   - autoconf, automake, libtool

2. Build native libraries:
   ```bash
   # Build fdk-aac
   git clone https://github.com/mstorsjo/fdk-aac.git
   cd fdk-aac
   autoreconf -fi
   ./configure --host=x86_64-w64-mingw32 --prefix=/tmp/fdk-aac-install
   make && make install

   # Build ALAC
   git clone https://github.com/mikebrady/alac.git
   # (Additional steps for ALAC - see original README)
   ```

3. Build .NET application:
   ```bash
   dotnet publish AirPlay/AirPlay.csproj -c Release -r win-x64 -o ./build/win-x64
   ```

4. Copy native DLLs to build directory

5. Update configuration to use relative paths

6. Create ZIP archive

## Notes

- The build is targeting Windows 10+ (64-bit)
- .NET Core 2.2 runtime is included (self-contained)
- All codec libraries are cross-compiled using MinGW-w64
- The application uses standard AirPlay 2 protocol

## Source

Original project: https://github.com/SteeBono/airplayreceiver
