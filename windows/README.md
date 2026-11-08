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
  tag such as `v1.74-win2` and click **Run workflow**. A tag containing `beta`
  (for example `v1.74-win3-beta`) is published as a pre-release. The release notes come
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
PIN, recording, start with Windows, new-version notice), keep the mirror window on top,
restart, the log, a test sound and a Windows Firewall fix.

With **Ask for a PIN** on, a window pops up with the 4-digit code to type on
the iPhone the first time it connects; UxPlay remembers the iPhone after that.
While an iPhone is connected the PC's screen stays on; when nothing is
connected UxPlay sits idle and the PC sleeps as usual.

### Smoother mirroring

- **Settings → Smoothness** is 60 fps by default (UxPlay on its own asks the
  iPhone for only 30). Pick 30 fps on an older PC or a weak Wi-Fi signal.
- Wi-Fi matters most. Use your router's **5 GHz** network, keep the iPhone
  near the router, and connect the PC with an **Ethernet cable** if you can.
- If the picture stutters or freezes, set **Quality** to 720p.
- Playing games? Tick **Lowest delay** in Settings. Each frame is shown as
  soon as it arrives, at the cost of sound that can lag the picture slightly.
- Close heavy programs (games, video calls, big downloads) while mirroring.

The installer adds a Windows Firewall rule for UxPlay on all network types,
so the iPhone can find the PC even when Windows calls your home Wi-Fi
"Public". On networks you don't trust, such as café Wi-Fi, quit UxPlay or
turn on **Ask for a PIN** in Settings.

### iPhone doesn't list the PC?

- Both must be on the same Wi-Fi network, not a guest network.
- **Troubleshooting → Allow through Windows Firewall**.
- **Troubleshooting → Show log**: the line `mDNS: advertising on …` gives the
  address the iPhone is told to use. It should be this PC's Wi-Fi or Ethernet
  address. UxPlay skips Hyper-V/WSL, virtual-machine and VPN adapters and picks
  the one Windows itself prefers.
- A VPN can block the local network: turn it off while mirroring, or allow
  local network access in its settings.

### No sound?

- Right-click the tray icon → **Troubleshooting → Play a test sound**. If you
  hear the beep, the PC's sound works and the problem is the network or the
  iPhone's volume. If you don't, UxPlay tries compatibility mode and switches
  to it if that one is heard.
- In **Settings**, tick **Play iPhone sound on this PC**.
- While mirroring, the iPhone's volume buttons set the PC's volume, so turn it
  up. Some apps and games stay quiet in Silent mode, so test with a video.
- Open Windows' **Volume mixer** while sound plays. `uxplay.exe` should be
  listed, not muted, and on the speakers you expect.
- With a third-party firewall (ESET, Norton and so on), allow `uxplay.exe` for
  both **TCP and UDP**. The picture arrives over TCP but the sound over UDP,
  so a TCP-only rule gives you video with no sound.
- Still nothing? In **Settings → Sound output**, try *Windows audio (WASAPI)*,
  then *Compatibility mode (DirectSound)*.

## What's in this folder

| Path | What it is |
| --- | --- |
| `tray/` | `UxPlay.exe`, the system-tray app (C#, .NET Framework 4.8, built into Windows 10/11). It runs `uxplay.exe` hidden, with the options from its Settings window. |
| `bundle.sh` | Run in MSYS2 after building: copies `uxplay.exe`, its DLLs and the GStreamer plugins into one self-contained folder. |
| `uxplay-console.bat` | Runs the bundled `uxplay.exe` in a console window for troubleshooting. |
| `RELEASE_NOTES.md` | Notes used for releases published from the Actions tab. |
| `installer.iss` | Inno Setup script for `UxPlay-Setup.exe`: shortcuts, firewall rule, optional start at sign-in, uninstaller. |
| `README.txt` | Quick-start guide shipped inside the app folder. |
| `tests/` | Unit tests for the tray app's logic, with a coverage minimum: `dotnet test windows/tests`. |
| `tools/make_icon.py` | Draws `tray/uxplay.ico` (needs Pillow). |

### How the tray app is organised

The tray app follows the SOLID principles. Each class has one job, and
`TrayApp` works only through small interfaces. `Program.Main` is the one place
that picks the concrete classes.

| Class | Its one job |
| --- | --- |
| `TrayApp` | Tray icon and menu; coordinates the parts below |
| `IReceiver` / `ReceiverProcess` | Starts and stops `uxplay.exe` with the bundled GStreamer; its log; job object |
| `GStreamerEnvironment` | Points the bundled GStreamer programs at the bundled plugins |
| `ISoundTest` / `GStreamerSoundTest` | Plays the test beep through the chosen sound output |
| `SoundTroubleshooter`, `IPrompt` / `MessageBoxPrompt` | The test-sound questions and advice; message boxes |
| `ReleaseTag`, `IUpdateCheck` / `GitHubUpdateCheck` | Compares release tags; asks GitHub for the newest release once per start |
| `ConnectionMonitor` | Reads uxplay's messages: PIN asked, iPhone connected, iPhone gone |
| `OutputPatterns` | The uxplay messages it recognises |
| `Settings` | What the user chose (plain data) |
| `ISettingsStore` / `IniSettingsStore` | Loads and saves `settings.ini` |
| `UxPlayCommandLine` | Turns `Settings` into uxplay options |
| `IStartupRegistration` / `RunKeyStartup` | "Start UxPlay when I sign in" |
| `IMirrorWindow` / `MirrorWindow` | Finds the video window and switches full screen |
| `RestartPolicy` | Restarts a crashed `uxplay.exe`, at most 3 times in 10 minutes, then tells the user |
| `OnTopWatcher` | "Keep on top": retries until the video window has opened, for up to 30 s |
| `BugReport` | "Copy details for a bug report": versions, options and the log's end, without the account name |
| `ErrorLog` | tray-error.log: unexpected errors, kept below 1 MB (the older part moves to tray-error.old.log) |
| `ScreenshotName` | File names for "Save a screenshot": date and time, never overwriting an earlier one |
| `IKeepAwake` / `KeepAwake` | Keeps the screen on while mirroring |
| `IFirewall` / `WindowsFirewall` | The Windows Firewall rule |
| `SettingsForm`, `PinForm` | The Settings and PIN windows |
| `LogFile` | `uxplay.log`, written from two threads and kept under 5 MB |
| `JobObject`, `AppPaths`, `AppIcon` | Small helpers |

New features go in new classes, or new entries in the option tables in
`Settings`, without changing the others. The Windows-free parts are covered by
`windows/tests`.

### Tests and coverage

A release must be rock solid, so the tests come in two layers:

- **Everyday cases** (`tests/TrayTests.cs`): what normal use does. Each setting
  and the uxplay options it gives, saving and loading settings, a PIN, a
  connect and a disconnect, the update check, the sound test.
- **Hard cases** (`tests/EdgeCaseTests.cs`): what can go wrong. A broken or
  hand-edited `settings.ini`, one from an older version, odd device names
  ("Sam's (Work) iPhone", other alphabets), huge or repeated socket numbers,
  two devices taking turns, uxplay restarting mid-mirror, names that try to
  break out of their quotes, unusual release tags and GitHub replies. Each
  hard-case group also has a seeded random test: thousands of random settings,
  settings files and uxplay messages that must never crash and never lose a
  setting. The seed is fixed, so a failure always comes back the same way.

`dotnet test windows/tests` runs both and measures code coverage (coverlet).
**It fails below 95% of lines or 90% of branches**, so new logic needs tests
with it. The report is written to `windows/tests/TestResults/coverage.cobertura.xml`.

**Nothing is released untested.** The Windows App workflow runs these tests on
every push and pull request. Its release job only runs after the whole build
job has passed: the tests and coverage minimum, the check that the bundle
works on its own, and a smoke start of the app with recording, low delay and
YouTube video on.

What is measured is the logic that doesn't need Windows: settings, the uxplay
command line, reading uxplay's messages, release tags, the GStreamer
environment and the sound-test steps. Code that only starts a real program or
talks to the network is marked `[ExcludeFromCodeCoverage]`; the Windows build
runs it for real (bundle check, test sound, smoke test). The windows and the
tray menu (`TrayApp`, `SettingsForm`, `PinForm`, `MirrorWindow` and the other
Win32 helpers) are thin layers over Windows and are checked by building them
and by the smoke test, not by unit tests.

When you fix a bug, add the test that would have caught it to the hard cases.

Files the app keeps on the PC are in `%LOCALAPPDATA%\UxPlay`: `settings.ini`,
`uxplay.log`, the receiver's key `uxplay.pem` (so the iPhone recognises the PC
after a restart) and GStreamer's plugin cache.

The only connection the app makes on its own is one request per start to
`api.github.com` for the newest release's name, when the release build has
**Tell me when a new version of UxPlay is out** ticked in Settings. Nothing
about you or your PC is sent.

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
