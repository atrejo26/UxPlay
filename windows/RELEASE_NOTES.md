## UxPlay 1.74 for Windows

Mirror your iPhone or iPad screen to your Windows PC with AirPlay. No MSYS2, Bonjour or compiling needed.

### What's new
- **Sound output choice (beta).** Settings → **Sound output**: if you see the picture but hear nothing, try *Windows audio (WASAPI)* or *Compatibility mode (DirectSound)*.
- **Play a test sound** (Troubleshooting menu): a beep through the same sound output as mirroring, so you can tell a PC sound problem from a network one. If it can't be heard, UxPlay tries compatibility mode and switches to it when that works.
- **Finds the right network on PCs with a VPN or virtual machines.** UxPlay now skips VPN (WireGuard, Tailscale, TAP and so on), Hyper-V/WSL and virtual-machine adapters and self-assigned 169.254 addresses, and picks the network Windows itself prefers. The log shows which address it advertises.
- **Record your mirroring (beta).** Settings → *Save a video of each mirroring session* saves every session as an MP4 in **Videos\UxPlay**, named with the date and time. The tray menu gets **Open my recordings**. The video is saved as the iPhone sends it, with no re-encoding, so recording adds almost no load. Tip: VLC plays the iPhone's sound in these files; some built-in Windows players show the picture only.
- **Keep on top of other windows** (tray menu): the mirror window stays above your other windows, handy for following along on the iPhone while you work.
- **New-version notice.** About a minute after UxPlay starts, it checks once whether a newer release is out and shows a notice you can click to download it. Turn it off in Settings.
- **About / check for updates** in the tray menu, and **Troubleshooting → Open UxPlay's data folder**.
- **Borderless full screen that you can switch at any time.** Press **Alt+Enter** in the mirror window, or use **Full screen** in the tray menu, without disconnecting your iPhone. Settings has an option to start in full screen.
- **Smoother video:** mirroring now runs at 60 fps by default (UxPlay on its own uses 30). Settings → **Smoothness** has a 30 fps option for older PCs or weak Wi-Fi.
- **PIN codes now appear on screen.** With **Ask for a PIN** turned on, a window shows the 4-digit code to type on your iPhone. Before, the code was hidden and couldn't be read.
- **The screen stays on while you mirror**, and the PC can sleep normally when nothing is connected.
- **Lighter on your PC:** changing a setting that doesn't affect mirroring no longer disconnects your iPhone, nothing runs in the background while idle, and the log can't grow without limit.
- Fixes: the tray shows when your iPhone disconnects, the Full screen tick matches the window after Alt+Enter, the tray icon no longer lingers after Quit, and the log of a crash is kept.

### Download
- **UxPlay-Setup.exe**: installer (recommended). Adds Start menu and desktop shortcuts, sets up Windows Firewall, and can start UxPlay when you sign in.
- **UxPlay-Windows-portable.zip**: no install needed. Unzip it anywhere and double-click `UxPlay.exe`.

### How to use
1. Start UxPlay. A blue icon appears near the clock.
2. Connect your iPhone and PC to the same Wi-Fi network.
3. On your iPhone, open Control Center, tap **Screen Mirroring**, and choose your PC.

Right-click the tray icon for full screen, settings (name, quality, smoothness, sound, PIN, start with Windows), restart, and troubleshooting.

### Notes
- Windows 10 or 11, 64-bit (Intel/AMD).
- The installer isn't code-signed, so Windows SmartScreen may warn you. Click **More info → Run anyway**.
- The firewall rule is for Windows Firewall. If you use another firewall or security suite (ESET, Norton and so on), allow `uxplay.exe` there for both TCP and UDP. Sound uses UDP.
- On public Wi-Fi, quit UxPlay or turn on **Ask for a PIN** in Settings.

Based on [UxPlay](https://github.com/FDH2/UxPlay) 1.74 (GPL v3).
