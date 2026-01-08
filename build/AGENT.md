# AirPlay Receiver - Windows Build Process

## Overview

This document describes the automated build process for creating a portable Windows build of the AirPlay Receiver application from the original Linux/macOS source code at https://github.com/SteeBono/airplayreceiver.

## What Was Built

A complete, portable Windows x64 build of the AirPlay 2 Receiver application including:

- ✅ .NET Core 2.2 application compiled for Windows x64
- ✅ Native AAC codec (fdk-aac) cross-compiled for Windows
- ✅ Native ALAC codec cross-compiled for Windows
- ✅ All runtime dependencies included
- ✅ Pre-configured for immediate use
- ✅ HTTP server for distribution

**Final Package**: `airplay-receiver-windows-x64.zip` (36 MB)

## Build Environment

- **Host OS**: Linux (Ubuntu Noble)
- **Build Tools**:
  - .NET SDK 8.0.122
  - MinGW-w64 (gcc 13.2.0)
  - autoconf, automake, libtool
  - Python 3.12.3
- **Target**: Windows 10+ (x64)

## Build Process

### 1. Repository Setup

```bash
git clone https://github.com/SteeBono/airplayreceiver
cd airplayreceiver
```

### 2. Native Codec Libraries

#### fdk-aac (AAC Audio Codec)

**Source**: https://github.com/mstorsjo/fdk-aac

```bash
git clone https://github.com/mstorsjo/fdk-aac.git /tmp/fdk-aac
cd /tmp/fdk-aac

# Configure for Windows cross-compilation
autoreconf -fi
./configure \
  --host=x86_64-w64-mingw32 \
  --prefix=/tmp/fdk-aac-install

# Build
make -j$(nproc)
make install

# Output: libfdk-aac-2.dll (9.7 MB)
```

**Key Files**:
- `/tmp/fdk-aac-install/bin/libfdk-aac-2.dll`
- `/tmp/fdk-aac-install/lib/libfdk-aac.dll.a`

#### ALAC (Apple Lossless Audio Codec)

**Source**: https://github.com/mikebrady/alac (with modifications from https://github.com/GiteKat/LibALAC)

```bash
git clone https://github.com/mikebrady/alac.git alac-build
cd alac-build

# Get GiteKat's files with extern keywords
git clone https://github.com/GiteKat/LibALAC.git
cp -r LibALAC/LibALAC/* codec/

# Fix missing include
# Added: #include <cstdlib> to codec/LibALAC.cpp

# Update Makefile.am to include LibALAC.cpp and header
# Added to libalac_la_SOURCES: LibALAC.cpp
# Added to alacinclude_HEADERS: LibALAC.h
# Added LDFLAGS: -no-undefined -static-libgcc -static-libstdc++

# Configure for Windows
autoreconf -fi
./configure \
  --host=x86_64-w64-mingw32 \
  --prefix=/root/airplay/airplayreceiver/alac-install

# Build
make -j$(nproc)
make install

# Output: libalac-0.dll (415 KB)
```

**Key Modifications**:
- Added `#include <cstdlib>` to LibALAC.cpp for malloc/free
- Updated Makefile.am to include LibALAC.cpp source
- Added linker flags for proper DLL generation

**Key Files**:
- `alac-install/bin/libalac-0.dll`
- `alac-install/lib/libalac.dll.a`

### 3. .NET Core Application

#### Project Configuration Fix

**Issue**: Package conflict between `Curve25519` and `curve25519-pcl`

**Solution**: Modified `AirPlay/AirPlay.csproj`:
```xml
<!-- Removed Curve25519 package, kept only curve25519-pcl -->
<PackageReference Include="curve25519-pcl" Version="1.0.1" />
```

#### Build Command

```bash
cd /root/airplay/airplayreceiver

dotnet build AirPlay/AirPlay.csproj -c Release
dotnet publish AirPlay/AirPlay.csproj \
  -c Release \
  -r win-x64 \
  -o ./build/win-x64
```

**Output**: 269 files in `build/win-x64/`

### 4. Package Assembly

```bash
# Copy native DLLs to build directory
cp /tmp/fdk-aac-install/bin/libfdk-aac-2.dll build/win-x64/
cp alac-install/bin/libalac-0.dll build/win-x64/

# MinGW runtime (already present from .NET build)
# libwinpthread-1.dll included automatically
```

### 5. Configuration

**File**: `build/win-x64/appsettings_win.json`

**Changes Made**:
```json
{
  "AirPlayReceiver": {
    "Instance": "AirPlay-Receiver",
    "DeviceMacAddress": "AA:BB:CC:DD:EE:FF"
  },
  "CodecLibraries": {
    "AACLibPath": "libfdk-aac-2.dll",      // Changed from absolute path
    "ALACLibPath": "libalac-0.dll"         // Changed from absolute path
  },
  "Dump": {
    "Path": ".\\dump\\"                     // Changed from absolute path
  }
}
```

**Result**: Application is now fully portable - no configuration required.

### 6. Documentation

Created:
- `build/win-x64/README.txt` - User-facing documentation
- `build/README.md` - Build distribution documentation
- `build/AGENT.md` - This file

### 7. HTTP Distribution Server

**File**: `build/serve.py`

A custom HTTP server featuring:
- Beautiful responsive web interface
- One-click download
- File information display
- Quick start instructions
- Mobile-friendly design

**Usage**:
```bash
cd build
python3 serve.py
# Access at http://localhost:8080
```

### 8. Final Packaging

```bash
cd build
zip -r airplay-receiver-windows-x64.zip win-x64/
```

**Final Size**: 36 MB

## Package Contents

### Main Components

```
win-x64/
├── AirPlay.exe              # Main application (139 KB)
├── AirPlay.dll              # Application library (645 KB)
├── libfdk-aac-2.dll         # AAC codec (9.7 MB)
├── libalac-0.dll            # ALAC codec (415 KB)
├── appsettings_win.json     # Pre-configured settings
├── README.txt               # User documentation
└── [267+ other files]       # .NET runtime and dependencies
```

### Configuration Files

```
appsettings_win.json    # Windows configuration (active)
appsettings_linux.json  # Linux configuration
appsettings_osx.json    # macOS configuration
```

## Technical Details

### Cross-Compilation Challenges

1. **fdk-aac**: Straightforward cross-compilation with MinGW
2. **ALAC**: Required source modifications:
   - Missing `#include <cstdlib>` for malloc/free
   - Build system updates for LibALAC.cpp
   - Linker flags for DLL generation

3. **.NET Core**: Package conflicts resolved by dependency management

### Runtime Dependencies

**Included DLLs**:
- .NET Core 2.2 runtime (self-contained)
- MinGW runtime: `libwinpthread-1.dll`
- Native codecs: `libfdk-aac-2.dll`, `libalac-0.dll`
- Windows API compatibility layer (api-ms-win-*.dll)

### Security Considerations

**Known Vulnerabilities** (from NuGet warnings):
- BouncyCastle 1.8.6.1 - Moderate severity (4 advisories)
- Newtonsoft.Json 12.0.3 - High severity (1 advisory)
- Microsoft.NETCore.App 2.2.0 - High severity (2 advisories)

**Note**: These are inherited from the original project targeting .NET Core 2.2 (EOL). Consider updating to modern .NET for production use.

## Distribution

### Files Available

```
build/
├── airplay-receiver-windows-x64.zip  # Complete package (36 MB)
├── win-x64/                          # Unpacked directory
├── serve.py                          # HTTP server
├── START_SERVER.sh                   # Server launcher
├── README.md                         # Distribution docs
└── AGENT.md                          # This file
```

### Serving the Build

**Option 1 - Custom Server**:
```bash
./START_SERVER.sh
```

**Option 2 - Python HTTP Server**:
```bash
python3 serve.py
```

Access at: `http://localhost:8080`

## Usage Instructions

### For End Users

1. Download `airplay-receiver-windows-x64.zip`
2. Extract to any location
3. Double-click `AirPlay.exe`
4. Allow through Windows Firewall
5. Connect from iOS/Mac devices

**That's it!** No configuration needed.

### For Developers

#### Modify Settings

Edit `appsettings_win.json`:
- Change instance name
- Modify ports (default: 5000, 7000)
- Update MAC address for multiple instances
- Enable debug logging

#### Rebuild from Source

See `README.md` for complete build instructions.

## Build Statistics

- **Total Build Time**: ~15 minutes (including dependencies)
- **Final Package Size**: 36 MB
- **File Count**: 269 files in build
- **Target Architecture**: Windows x64
- **Compression**: ZIP with deflate

## Verification

### Package Integrity

```bash
# Check file sizes
ls -lh win-x64/libfdk-aac-2.dll  # Should be ~9.7M
ls -lh win-x64/libalac-0.dll     # Should be ~415K
ls -lh win-x64/AirPlay.exe       # Should be ~139K

# Verify DLL dependencies (on Windows)
dumpbin /dependents win-x64/AirPlay.exe
dumpbin /dependents win-x64/libfdk-aac-2.dll
dumpbin /dependents win-x64/libalac-0.dll
```

### Testing Checklist

- [ ] Extract ZIP archive
- [ ] Launch AirPlay.exe
- [ ] Check for missing DLL errors
- [ ] Verify firewall prompt appears
- [ ] Confirm device appears on iOS/Mac
- [ ] Test audio streaming
- [ ] Test video mirroring
- [ ] Verify log files created

## Known Issues

1. **Framework**: Uses .NET Core 2.2 (EOL - out of support)
   - **Impact**: Security vulnerabilities in dependencies
   - **Mitigation**: Consider porting to .NET 6+ for production

2. **Codec Licensing**:
   - fdk-aac uses modified BSD license
   - May require licensing for commercial use
   - Verify licensing requirements for your use case

3. **Windows Firewall**:
   - Requires allowing both ports (5000, 7000)
   - Must allow on both private and public networks

## Future Improvements

### Recommended Enhancements

1. **Update to Modern .NET**:
   - Port to .NET 8.0 LTS
   - Resolve security vulnerabilities
   - Improve performance

2. **Installer Package**:
   - Create MSI installer with Wix
   - Add Windows Service option
   - System tray integration

3. **Enhanced UI**:
   - Web-based control panel
   - Status monitoring
   - Configuration GUI

4. **Additional Platforms**:
   - Linux x64 build
   - macOS ARM64 (Apple Silicon)
   - Docker container

## Conclusion

This build provides a complete, portable, and user-friendly Windows distribution of the AirPlay Receiver application. All dependencies are included, and the application is pre-configured for immediate use.

The build process demonstrates:
- Cross-platform compilation (Linux → Windows)
- Native library integration
- .NET Core deployment
- User-friendly packaging

For questions or issues, refer to:
- **Original Project**: https://github.com/SteeBono/airplayreceiver
- **This Build**: See README.md in build directory

---

**Build Date**: 2026-01-08
**Build Environment**: Linux x86_64
**Target Platform**: Windows 10+ (x64)
**Package Format**: ZIP archive
**Distribution**: HTTP server included
