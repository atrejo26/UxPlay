## UxPlay 1.74 for Windows

Mirror your iPhone or iPad screen to your Windows PC with AirPlay. No MSYS2, Bonjour or compiling needed.

### What's new
- **Works on campus, school and work networks.** Those networks often keep phones from finding PCs (your iPhone's Wi-Fi and the PC's Ethernet can even be separate networks). Turn on Windows' **Mobile hotspot** and join it from your iPhone: UxPlay now finds the hotspot by itself, also when you turn it on after UxPlay started, and tells iPhones on it the hotspot's own address. The firewall lets in only devices on your own hotspot, and still nothing on other Public networks. See **How to connect my iPhone** in the tray menu.
- **Safe on any network.** UxPlay can only be reached on networks Windows calls Private (or a work domain), never on public Wi-Fi in cafés, hotels or airports. If your home Wi-Fi is set to Public, UxPlay tells you and opens the setting to change it. **Ask for a PIN** is now on for new installs: each iPhone types the code shown on your PC the first time it connects, so nobody else on your Wi-Fi can take over your screen. uxplay.exe is now built with stack protection, checked memory functions, ASLR and DEP, and for any 64-bit PC, not only the CPU of the build machine.
- **Tested before every release.** 219 unit tests cover everyday use and the hard cases: broken settings files, odd device names, devices taking turns, and thousands of random inputs. Tests of how iPhones find the PC, including a real query and answer over the network, run on every build too. Every build measures their coverage (99% of lines, 97% of branches) and fails below 95% / 90%. A release can only be published from a build that passed them.
- **No endless restart loops.** If uxplay.exe crashes, UxPlay still restarts it by itself, but after 3 crashes in 10 minutes it stops and tells you, with the log one click away.
- **Fixes found by the new tests:** a device name with brackets, such as "Sam's (Work) iPhone", is shown in full; a malformed message from uxplay can no longer crash the tray app; a hand-edited quality value in settings.ini can't add uxplay options; Advanced options separated by tabs are recognised; and a crash or power cut while saving settings can no longer leave a broken settings file.
- **Sound output choice (beta).** Settings → **Sound output**: if you see the picture but hear nothing, try *Windows audio (WASAPI)* or *Compatibility mode (DirectSound)*.
- **Play a test sound** (Troubleshooting menu): a beep through the same sound output as mirroring, so you can tell a PC sound problem from a network one. If it can't be heard, UxPlay tries compatibility mode and switches to it when that works.
- **Finds the right network on PCs with a VPN or virtual machines.** UxPlay now skips VPN (WireGuard, Tailscale, TAP and so on), Hyper-V/WSL and virtual-machine adapters and self-assigned 169.254 addresses, and picks the network Windows itself prefers. The log shows which address it advertises.
- **YouTube videos in full quality (beta).** Settings → *Play YouTube videos sent with AirPlay in full quality*: a video you AirPlay from the YouTube app is streamed and played by the PC itself, instead of mirroring the iPhone's screen.
- **Settings are grouped** into Picture, Sound, Privacy and recording, and This PC.
- **Lowest delay (for games).** Settings → *Lowest delay* shows each frame the moment it arrives. The sound may then lag the picture slightly.
- **Record your mirroring (beta).** Settings → *Save a video of each mirroring session* saves every session as an MP4 in **Videos\UxPlay**, named with the date and time. The tray menu gets **Open my recordings**. The video is saved as the iPhone sends it, with no re-encoding, so recording adds almost no load. Tip: VLC plays the iPhone's sound in these files; some built-in Windows players show the picture only.
- **Status at a glance:** the tray icon gets a green dot while an iPhone is mirroring and a grey one when UxPlay is stopped.
- **Save a screenshot** (tray menu, while an iPhone is mirroring): saves the mirror window as a PNG in **Pictures\UxPlay**, named with the date and time. Click the notice to open the folder.
- **Keep on top of other windows** (tray menu): the mirror window stays above your other windows, handy for following along on the iPhone while you work.
- **New-version notice.** About a minute after UxPlay starts, it checks once whether a newer release is out and shows a notice you can click to download it. Turn it off in Settings.
- **About / check for updates** in the tray menu, and **Troubleshooting → Open UxPlay's data folder**.
- **Troubleshooting → Copy details for a bug report:** copies the app and Windows versions, UxPlay's options and the end of the log, ready to paste into an issue. Your Windows account name is left out.
- **Borderless full screen that you can switch at any time.** Press **Alt+Enter** in the mirror window, or use **Full screen** in the tray menu, without disconnecting your iPhone. Settings has an option to start in full screen.
- **Smoother video:** mirroring now runs at 60 fps by default (UxPlay on its own uses 30). Settings → **Smoothness** has a 30 fps option for older PCs or weak Wi-Fi.
- **PIN codes now appear on screen.** With **Ask for a PIN** turned on, a window shows the 4-digit code to type on your iPhone. Before, the code was hidden and couldn't be read.
- **The screen stays on while you mirror**, and the PC can sleep normally when nothing is connected.
- **Lighter on your PC:** changing a setting that doesn't affect mirroring no longer disconnects your iPhone, nothing runs in the background while idle, and neither the log nor the error log can grow without limit.
- Fixes: the tray shows when your iPhone disconnects, the Full screen tick matches the window after Alt+Enter, the tray icon no longer lingers after Quit, and the log of a crash is kept.

### Download
- **UxPlay-Setup.exe**: installer (recommended). Adds Start menu and desktop shortcuts, sets up Windows Firewall, and can start UxPlay when you sign in.
- **UxPlay-Windows-portable.zip**: no install needed. Unzip it anywhere and double-click `UxPlay.exe`.
- **SHA256SUMS.txt**: checksums to make sure a download wasn't changed. In PowerShell: `Get-FileHash UxPlay-Setup.exe` and compare.

### How to use
1. Start UxPlay. A blue icon appears near the clock.
2. Connect your iPhone and PC to the same Wi-Fi network.
3. On your iPhone, open Control Center, tap **Screen Mirroring**, and choose your PC.

Right-click the tray icon for full screen, settings (name, quality, smoothness, sound, PIN, start with Windows), restart, and troubleshooting.

### Notes
- Windows 10 or 11, 64-bit (Intel/AMD).
- The installer isn't code-signed, so Windows SmartScreen may warn you. Click **More info → Run anyway**.
- The firewall rule is for Windows Firewall. If you use another firewall or security suite (ESET, Norton and so on), allow `uxplay.exe` there for both TCP and UDP. Sound uses UDP.
- Using the portable copy on a Mobile hotspot? Choose **Troubleshooting → Allow through Windows Firewall** once; the installer does this for you.
- Upgrading? The firewall rule now covers Private networks only. If your iPhone no longer finds the PC, set your home Wi-Fi to Private in Windows' network settings (UxPlay's notice opens them).

Based on [UxPlay](https://github.com/FDH2/UxPlay) 1.74 (GPL v3).
