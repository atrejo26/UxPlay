# UxPlay Windows app

A ready-to-run Windows version of UxPlay: no MSYS2, Bonjour or compiling.
You start it from a desktop shortcut, it sits in the system tray, and your
iPhone or iPad mirrors its screen to the PC through **Control Center → Screen
Mirroring**.

## Get it

GitHub builds it automatically with the **Windows App** workflow
(`.github/workflows/windows-app.yml`):

- **Installer (easiest):** open the repository's **Actions** tab, pick the
  latest successful *Windows App* run, and download **UxPlay-Windows-installer**
  under *Artifacts*. Unzip it and run `UxPlay-Setup.exe`.
- **Portable:** download **UxPlay-Windows-portable** from the same run, unzip
  it anywhere and double-click `UxPlay.exe`.
- **Releases:** to publish both files on the repository's **Releases** page,
  open **Actions → Windows App → Run workflow**, choose `master`, enter a new
  tag such as `v1.74-win2` and click **Run workflow**. The release notes come
  from `windows/RELEASE_NOTES.md`, so update its "What's new" section first.
  Pushing a tag that starts with `v`, or creating a release on GitHub, also
  works; a release created by hand keeps the notes typed there.

The installer is not code-signed, so Windows SmartScreen may say *"Windows
protected your PC"*. Click **More info → Run anyway**.

## Use it

1. Start UxPlay. A blue icon appears near the clock.
2. Put the iPhone on the same Wi-Fi network as the PC.
3. On the iPhone, open Control Center, tap **Screen Mirroring** and choose the PC.

Press **Alt+Enter** in the mirror window, or use **Full screen** in the tray
menu, to switch between a window and borderless full screen without
disconnecting. Right-click the tray icon for settings (name, quality, sound,
PIN, start with Windows), restart, the log and a Windows Firewall fix.

### Smoother mirroring

- **Settings → Smoothness** is 60 fps by default (UxPlay on its own asks the
  iPhone for only 30). Pick 30 fps on an older PC or a weak Wi-Fi signal.
- Wi-Fi matters most. Use your router's **5 GHz** network, keep the iPhone
  near the router, and connect the PC with an **Ethernet cable** if you can.
- If the picture stutters or freezes, set **Quality** to 720p.
- Close heavy programs (games, video calls, big downloads) while mirroring.

The installer adds a Windows Firewall rule for UxPlay on all network types,
so the iPhone can find the PC even when Windows calls your home Wi-Fi
"Public". On networks you don't trust, such as café Wi-Fi, quit UxPlay or
turn on **Ask for a PIN** in Settings.

### No sound?

- In **Settings**, tick **Play iPhone sound on this PC**.
- While mirroring, the iPhone's volume buttons set the PC's volume, so turn it
  up. Some apps and games stay quiet in Silent mode, so test with a video.
- Open Windows' **Volume mixer** while sound plays. `uxplay.exe` should be
  listed, not muted, and on the speakers you expect.
- With a third-party firewall (ESET, Norton and so on), allow `uxplay.exe` for
  both **TCP and UDP**. The picture arrives over TCP but the sound over UDP,
  so a TCP-only rule gives you video with no sound.

## What's in this folder

| Path | What it is |
| --- | --- |
| `tray/` | `UxPlay.exe`, the system-tray app (C#, .NET Framework 4.8, built into Windows 10/11). It runs `uxplay.exe` hidden, with the options from its Settings window. |
| `bundle.sh` | Run in MSYS2 after building: copies `uxplay.exe`, its DLLs and the GStreamer plugins into one self-contained folder. |
| `uxplay-console.bat` | Runs the bundled `uxplay.exe` in a console window for troubleshooting. |
| `RELEASE_NOTES.md` | Notes used for releases published from the Actions tab. |
| `installer.iss` | Inno Setup script for `UxPlay-Setup.exe`: shortcuts, firewall rule, optional start at sign-in, uninstaller. |
| `README.txt` | Quick-start guide shipped inside the app folder. |
| `tools/make_icon.py` | Draws `tray/uxplay.ico` (needs Pillow). |

Files the app keeps on the PC are in `%LOCALAPPDATA%\UxPlay`: `settings.ini`,
`uxplay.log`, the receiver's key `uxplay.pem` (so the iPhone recognises the PC
after a restart) and GStreamer's plugin cache.

## Building it yourself

In an MSYS2 **UCRT64** shell, from the repository root:

```sh
pacman -S mingw-w64-ucrt-x86_64-{cmake,ninja,gcc,pkgconf,openssl,libplist,gstreamer,gst-plugins-base,gst-plugins-good,gst-plugins-bad,gst-libav,ntldd}
cmake -B build -G Ninja -DCMAKE_BUILD_TYPE=Release -DNO_X11_DEPS=ON
cmake --build build
windows/bundle.sh build windows/out/UxPlay
```

Then, with the .NET SDK in PowerShell:

```powershell
dotnet build windows\tray\UxPlayTray.csproj -c Release -o windows\out\tray
Copy-Item windows\out\tray\UxPlay.exe, windows\out\tray\UxPlay.exe.config, windows\README.txt windows\out\UxPlay\
& "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe" /DAppVersion=1.74 windows\installer.iss   # optional installer
```
