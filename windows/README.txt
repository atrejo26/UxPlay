UxPlay for Windows - show your iPhone or iPad screen on this PC
================================================================

Start
-----
Double-click UxPlay.exe. A blue UxPlay icon appears near the clock
(click the ^ arrow there if you don't see it).

The first time, Windows may ask whether to allow UxPlay on your network.
Tick "Private networks" (and "Public networks" if your home Wi-Fi is set
to Public), then click Allow.

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
    and starting UxPlay when you sign in to Windows. With "Ask for a PIN"
    on, a window shows the 4-digit code to type on the iPhone the first
    time it connects.
  - Troubleshooting: Windows Firewall fix and the log

Smoother mirroring
------------------
  - Settings > Smoothness is 60 fps by default. Choose 30 fps on an
    older PC or a weak Wi-Fi signal.
  - Use your router's 5 GHz Wi-Fi, keep the iPhone near the router, and
    connect the PC with an Ethernet cable if you can.
  - If the picture stutters, set Settings > Quality to 720p.

iPhone doesn't show this PC?
----------------------------
  - Check both are on the same Wi-Fi network (not a guest network).
  - Right-click the UxPlay icon > Troubleshooting > Allow through Windows Firewall.
  - Right-click the UxPlay icon > Restart.
  - Still stuck: quit UxPlay, run uxplay\uxplay-console.bat and read
    the messages it prints.

No sound from the PC?
---------------------
  - Right-click the UxPlay icon > Settings: tick "Play iPhone sound on this PC".
  - While mirroring, the iPhone's volume buttons set the PC's volume: turn
    it up. Some apps and games stay quiet in Silent mode, so test with a video.
  - Open Windows' Volume mixer while sound plays: uxplay.exe should be listed
    and not muted, on the speakers you expect.
  - Using ESET, Norton or another firewall? Allow uxplay.exe there for both
    TCP and UDP. The picture uses TCP but the sound uses UDP.

UxPlay is open-source software (GPL v3); see uxplay\LICENSE.txt.
Source code: https://github.com/atrejo26/UxPlay
