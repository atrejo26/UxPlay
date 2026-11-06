@echo off
rem Runs uxplay.exe in a console window using the bundled GStreamer, so you can
rem read its messages. Extra uxplay options can be added after the file name,
rem e.g.  uxplay-console.bat -d
rem Quit the UxPlay tray app first, or the two copies will clash.
setlocal
set "UX=%~dp0"
set "PATH=%UX%bin;%PATH%"
set "GST_PLUGIN_SYSTEM_PATH_1_0=%UX%lib\gstreamer-1.0"
set "GST_PLUGIN_SCANNER_1_0=%UX%libexec\gstreamer-1.0\gst-plugin-scanner.exe"
set "GST_REGISTRY_1_0=%LOCALAPPDATA%\UxPlay\gst-registry.bin"
set "GST_PLUGIN_PATH="
set "GST_PLUGIN_PATH_1_0="
set "GST_PLUGIN_SYSTEM_PATH="
set "FONTCONFIG_PATH=%UX%etc\fonts"
set "HOME=%LOCALAPPDATA%\UxPlay"
if not exist "%LOCALAPPDATA%\UxPlay" mkdir "%LOCALAPPDATA%\UxPlay"
"%UX%bin\uxplay.exe" %*
pause
