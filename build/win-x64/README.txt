AirPlay Receiver for Windows
=============================

This application allows you to receive audio and video from your iPhone/iPad via AirPlay.

QUICK START
-----------
1. Run AirPlay.exe
2. On your iOS device: Control Center -> AirPlay/Screen Mirroring
3. Select "AirPlay-Receiver"
4. Audio will play through your speakers

AUDIO-ONLY MODE
----------------
Audio works out of the box - no additional software needed!

ENABLE VIDEO (SCREEN MIRRORING)
--------------------------------
To enable screen mirroring, you need FFmpeg with libavcodec 60 or 61:

IMPORTANT: Check your FFmpeg version with "ffmpeg -version".
You need libavcodec 60.x or 61.x (avcodec-60.dll or avcodec-61.dll).
FFmpeg 7.0+ with libavcodec 62.x is NOT compatible.

Option 1 - Quick Install:
  1. Download FFmpeg 6.1 from: https://github.com/BtbN/FFmpeg-Builds/releases
     Look for: "ffmpeg-n6.1-latest-win64-gpl-shared-6.1.zip"
     Direct link: https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-n6.1-latest-win64-gpl-shared-6.1.zip
  2. Extract the ZIP file
  3. Copy the "bin" folder contents to C:\ffmpeg\bin
  4. Restart AirPlay.exe

Option 2 - Add to PATH:
  1. Download and extract FFmpeg 6.1 as above
  2. Add the FFmpeg bin folder to your Windows PATH
  3. Restart AirPlay.exe

Option 3 - Environment Variable:
  1. Download and extract FFmpeg 6.1 as above
  2. Set environment variable: FFMPEG_PATH=C:\path\to\ffmpeg\bin
  3. Restart AirPlay.exe

REQUIREMENTS
------------
- Windows 10/11
- .NET 8.0 Runtime (included)
- FFmpeg 6.0 or 6.1 ONLY (for video - NOT 7.0)

TROUBLESHOOTING
---------------
Audio stops after 10 seconds:
  - This should be fixed - app auto-restarts playback
  - Check Windows audio settings if issues persist

Video not working:
  - Make sure FFmpeg is installed (see above)
  - Check that avcodec-60.dll exists in C:\ffmpeg\bin
  - Look at console output for error messages

Can't see receiver on iPhone:
  - Make sure Windows Firewall allows the application
  - Check that ports 5000 and 7000 are not in use

PORTS USED
----------
- UDP 5000: AirTunes audio streaming
- TCP 7000: AirPlay video/control

FFmpeg Download: https://github.com/BtbN/FFmpeg-Builds/releases
