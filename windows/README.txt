UxPlay for Windows - show your iPhone or iPad screen on this PC
================================================================

Start
-----
Double-click UxPlay.exe. A blue UxPlay icon appears near the clock
(click the ^ arrow there if you don't see it).

The first time, Windows may ask whether to allow UxPlay on your network.
Tick "Private networks" only, then click Allow. On public Wi-Fi (cafés,
hotels, airports) nobody can then reach UxPlay. If your home Wi-Fi is set
to Public, UxPlay tells you: set it to Private in Windows' network settings.

Connect your iPhone
-------------------
1. Put your iPhone and this PC on the same Wi-Fi network.
2. On the iPhone, swipe down from the top-right corner to open Control Center.
3. Tap Screen Mirroring and choose this PC's name.

Your iPhone screen opens in a window. To stop, tap Screen Mirroring
again and choose Stop Mirroring.

Options
-------
Right-click the UxPlay icon for:
  - Full screen on or off (borderless). You can also press Alt+Enter
    in the mirror window to switch in and out of full screen.
  - Settings: the name shown on the iPhone, quality, sound, a PIN code,
    and starting UxPlay when you sign in to Windows. "Ask for a PIN" is on
    from the start: a window shows the 4-digit code to type on the iPhone the first
    time it connects, so only someone who can see your screen can connect.
  - Troubleshooting: Windows Firewall fix, test sound and the log
  - Keep on top of other windows: the mirror window stays in front
  - Save a screenshot: saves what the mirror window shows as a PNG
    in Pictures\UxPlay
  - Settings > Save a video of each mirroring session: MP4 files in Videos\UxPlay
  - A notice when a new version is out (can be turned off in Settings)

Smoother mirroring
------------------
  - Settings > Smoothness is 60 fps by default. Choose 30 fps on an
    older PC or a weak Wi-Fi signal.
  - Use your router's 5 GHz Wi-Fi, keep the iPhone near the router, and
    connect the PC with an Ethernet cable if you can.
  - If the picture stutters, set Settings > Quality to 720p.
  - Playing games? Settings > tick "Lowest delay".

iPhone doesn't show this PC?
----------------------------
  - Check both are on the same Wi-Fi network (not a guest network).
  - On a campus, school or work network? Those often keep phones from
    finding PCs, even on the same Wi-Fi. Use this PC's own hotspot instead:
      1. Windows Settings > Network & internet > Mobile hotspot.
      2. Share my internet connection from: Ethernet (or Wi-Fi).
         Share over: Wi-Fi. Edit sets the name and password.
      3. Turn Power saving off, then turn Mobile hotspot on.
      4. On the iPhone: Settings > Wi-Fi > choose the hotspot's name.
    UxPlay finds the hotspot by itself within a few seconds; the log says
    "mDNS: also advertising on 192.168.137.1". Using the portable copy?
    Choose Troubleshooting > Allow through Windows Firewall once, so the
    iPhone can get through the firewall on the hotspot.
  - Right-click the UxPlay icon > Troubleshooting > Allow through Windows Firewall.
  - Using a VPN? Turn it off while mirroring, or allow local network access in it.
  - Right-click the UxPlay icon > Troubleshooting > Show log: the line
    "mDNS: advertising on ..." should show this PC's Wi-Fi or Ethernet address.
  - Right-click the UxPlay icon > Restart.
  - Still stuck: quit UxPlay, run uxplay\uxplay-console.bat and read
    the messages it prints.
  - Asking for help? Troubleshooting > Copy details for a bug report, and
    paste them into your message.

No sound from the PC?
---------------------
  - Right-click the UxPlay icon > Troubleshooting > Play a test sound. Heard
    the beep? The PC's sound works: check the iPhone's volume and the firewall.
    No beep? UxPlay tries compatibility mode and switches to it if it works.
  - Right-click the UxPlay icon > Settings: tick "Play iPhone sound on this PC".
  - While mirroring, the iPhone's volume buttons set the PC's volume: turn
    it up. Some apps and games stay quiet in Silent mode, so test with a video.
  - Open Windows' Volume mixer while sound plays: uxplay.exe should be listed
    and not muted, on the speakers you expect.
  - Using ESET, Norton or another firewall? Allow uxplay.exe there for both
    TCP and UDP. The picture uses TCP but the sound uses UDP.
  - Still nothing: Settings > Sound output, try "Windows audio (WASAPI)",
    then "Compatibility mode (DirectSound)".

UxPlay is open-source software (GPL v3); see uxplay\LICENSE.txt.
Source code: https://github.com/atrejo26/UxPlay
