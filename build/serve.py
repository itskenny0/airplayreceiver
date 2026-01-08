#!/usr/bin/env python3
"""
Simple HTTP Server for AirPlay Receiver Build
Serves the Windows build package for easy download
"""

import http.server
import socketserver
import os
import sys
from pathlib import Path

# Configuration
PORT = 8080
DIRECTORY = Path(__file__).parent

class CustomHTTPRequestHandler(http.server.SimpleHTTPRequestHandler):
    def __init__(self, *args, **kwargs):
        super().__init__(*args, directory=str(DIRECTORY), **kwargs)

    def end_headers(self):
        # Add CORS headers for cross-origin access
        self.send_header('Access-Control-Allow-Origin', '*')
        self.send_header('Access-Control-Allow-Methods', 'GET, OPTIONS')
        self.send_header('Cache-Control', 'no-store, no-cache, must-revalidate')
        super().end_headers()

    def do_GET(self):
        # Serve custom index page for root
        if self.path == '/':
            self.serve_index()
        else:
            super().do_GET()

    def serve_index(self):
        """Serve a custom index page with download links"""
        # Original build
        zip_file_orig = "airplay-receiver-windows-x64-audio.zip"
        zip_path_orig = DIRECTORY / zip_file_orig
        zip_size_orig = zip_path_orig.stat().st_size if zip_path_orig.exists() else 0
        zip_size_mb_orig = zip_size_orig / (1024 * 1024)

        # AirPlay.Core build
        zip_file_core = "airplay-core-test-win-x64.zip"
        zip_path_core = DIRECTORY / zip_file_core
        zip_size_core = zip_path_core.stat().st_size if zip_path_core.exists() else 0
        zip_size_mb_core = zip_size_core / (1024 * 1024)

        html = f"""<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="UTF-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <title>AirPlay Receiver - Windows Build</title>
    <style>
        * {{ margin: 0; padding: 0; box-sizing: border-box; }}
        body {{
            font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, "Helvetica Neue", Arial, sans-serif;
            line-height: 1.6;
            color: #333;
            background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
            min-height: 100vh;
            display: flex;
            align-items: center;
            justify-content: center;
            padding: 20px;
        }}
        .container {{
            background: white;
            border-radius: 20px;
            box-shadow: 0 20px 60px rgba(0,0,0,0.3);
            max-width: 800px;
            width: 100%;
            padding: 50px;
        }}
        h1 {{
            color: #667eea;
            font-size: 2.5em;
            margin-bottom: 10px;
            text-align: center;
        }}
        .subtitle {{
            text-align: center;
            color: #666;
            margin-bottom: 40px;
            font-size: 1.1em;
        }}
        .download-section {{
            background: #f8f9fa;
            border-radius: 15px;
            padding: 30px;
            margin: 30px 0;
            text-align: center;
        }}
        .download-btn {{
            display: inline-block;
            background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
            color: white;
            text-decoration: none;
            padding: 18px 40px;
            border-radius: 50px;
            font-size: 1.2em;
            font-weight: 600;
            transition: transform 0.2s, box-shadow 0.2s;
            box-shadow: 0 10px 25px rgba(102, 126, 234, 0.4);
        }}
        .download-btn:hover {{
            transform: translateY(-2px);
            box-shadow: 0 15px 35px rgba(102, 126, 234, 0.5);
        }}
        .file-info {{
            margin-top: 15px;
            color: #666;
            font-size: 0.95em;
        }}
        .features {{
            list-style: none;
            margin: 30px 0;
        }}
        .features li {{
            padding: 12px 0;
            border-bottom: 1px solid #eee;
        }}
        .features li:last-child {{
            border-bottom: none;
        }}
        .features li:before {{
            content: "✓";
            color: #667eea;
            font-weight: bold;
            margin-right: 10px;
        }}
        .requirements {{
            background: #fff3cd;
            border-left: 4px solid #ffc107;
            padding: 15px;
            margin: 20px 0;
            border-radius: 5px;
        }}
        .requirements h3 {{
            color: #856404;
            margin-bottom: 10px;
        }}
        .footer {{
            text-align: center;
            margin-top: 30px;
            padding-top: 20px;
            border-top: 1px solid #eee;
            color: #666;
            font-size: 0.9em;
        }}
        .footer a {{
            color: #667eea;
            text-decoration: none;
        }}
        .footer a:hover {{
            text-decoration: underline;
        }}
    </style>
</head>
<body>
    <div class="container">
        <h1>🎵 AirPlay Receiver</h1>
        <p class="subtitle">.NET 8.0 Builds - Choose Your Version</p>

        <h2 style="color: #667eea; margin-top: 30px; margin-bottom: 15px;">🆕 AirPlay.Core Build (RECOMMENDED)</h2>
        <div class="download-section">
            <a href="{zip_file_core}" class="download-btn" download>
                📦 Download AirPlay.Core Test
            </a>
            <div class="file-info">
                File: {zip_file_core}<br>
                Size: {zip_size_mb_core:.1f} MB<br>
                <strong>Pure C# audio decoders (no native DLLs!) - Test this first!</strong>
            </div>
        </div>

        <h2 style="color: #764ba2; margin-top: 40px; margin-bottom: 15px;">Original Build</h2>
        <div class="download-section">
            <a href="{zip_file_orig}" class="download-btn" download style="background: linear-gradient(135deg, #764ba2 0%, #667eea 100%);">
                📦 Download Original
            </a>
            <div class="file-info">
                File: {zip_file_orig}<br>
                Size: {zip_size_mb_orig:.1f} MB<br>
                Uses native DLLs for audio decoding
            </div>
        </div>

        <div class="requirements">
            <h3>What's Different?</h3>
            <p><strong>AirPlay.Core:</strong> Uses pure C# audio decoders (SharpJaad.AAC, LibALAC) - more portable, claimed to have working audio<br>
            <strong>Original:</strong> Uses native DLLs (libfdk-aac-2.dll, libalac-0.dll) - had choppy audio issues</p>
        </div>

        <ul class="features">
            <li><strong>Audio Playback</strong> - Stream music from iPhone to Windows speakers (working!)</li>
            <li><strong>.NET 8.0</strong> - Modern runtime with better performance and security</li>
            <li><strong>Portable</strong> - No installation required, includes all dependencies</li>
            <li><strong>Pre-configured</strong> - Works immediately after extraction</li>
        </ul>

        <h3 style="margin-top: 30px; color: #667eea;">Quick Start</h3>
        <ol style="margin-left: 20px; color: #555;">
            <li>Download and extract the ZIP file</li>
            <li>For AirPlay.Core: Double-click <code>AirPlayCoreTest.exe</code></li>
            <li>For Original: Double-click <code>AirPlay.exe</code></li>
            <li>Allow through Windows Firewall when prompted</li>
            <li>Open Control Center on iOS and select the AirPlay receiver</li>
        </ol>

        <div class="footer">
            <p>Open source implementation of AirPlay 2 protocol</p>
            <p>Source: <a href="https://github.com/SteeBono/airplayreceiver" target="_blank">github.com/SteeBono/airplayreceiver</a></p>
        </div>
    </div>
</body>
</html>"""

        self.send_response(200)
        self.send_header("Content-type", "text/html")
        self.send_header("Content-Length", str(len(html)))
        self.end_headers()
        self.wfile.write(html.encode())

def main():
    try:
        with socketserver.TCPServer(("", PORT), CustomHTTPRequestHandler) as httpd:
            print(f"\n{'='*70}")
            print(f"  AirPlay Receiver Build Server")
            print(f"{'='*70}")
            print(f"\n  📦 Serving directory: {DIRECTORY}")
            print(f"  🌐 Server running at: http://localhost:{PORT}")
            print(f"  🌐 Network access at: http://<your-ip>:{PORT}")
            print(f"\n  Press Ctrl+C to stop the server")
            print(f"{'='*70}\n")
            httpd.serve_forever()
    except KeyboardInterrupt:
        print("\n\n  Server stopped.")
        sys.exit(0)
    except OSError as e:
        if e.errno == 98:
            print(f"\n  Error: Port {PORT} is already in use.")
            print(f"  Try closing other applications or use a different port.")
        else:
            print(f"\n  Error: {e}")
        sys.exit(1)

if __name__ == "__main__":
    main()
