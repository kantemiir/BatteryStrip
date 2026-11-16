# Changelog

[Русская версия](CHANGELOG.ru.md)

## 0.3.0-beta.6 — 2026-11-17

- **Many more devices**, from the research collected by [Wireless Battery](https://github.com/krisgfx9/Wireless-Device-Battery-Monitor):
  Razer, SteelSeries, Corsair, HyperX, Glorious, Hitscan, Kysona, Pulsar and ATK mice and headsets, and any HID device
  that reports its charge the standard way. Each is asked with read requests only, every 5 minutes and on *Refresh now*,
  never during full-screen games. A mouse seen over both its cable and its dongle shows once, the cable's value first.
  None of them has been tested on real hardware yet: testers welcome.
- **Older Logitech devices** on Unifying receivers (HID++ 1.0) show their charge, and Logitech G533, G535, G633–G935 and
  G PRO headsets use their own voltage curves, so their estimate is closer to what G HUB shows.
- **HeadsetControl**: if you installed it yourself, the app runs it every 5 minutes and shows the headsets it knows.
- **Credits** for Wireless Battery in the README and THIRD-PARTY-NOTICES.
- **Easier-to-read numbers.** The number in the battery is larger and bold (14 px) and stays one colour all the way
  across. The fill is now a lighter tone of the text colour instead of solid white with the number cut out of it, so
  a digit no longer splits in two where the fill ends ("45" had turned into a dark 4 and a white 5). The battery is a
  little bigger (34 × 18 px instead of 30 × 15). Warning colours, the bolt and the sleeping look are as before.

## 0.3.0-beta.5 — 2026-11-17

- **JBL speakers.** A JBL speaker connected over Bluetooth now shows its charge and charging state. Windows does not
  know it (the speaker connects for music only), so the app asks the speaker itself over Bluetooth Low Energy, with the
  read request the JBL Portable app uses, every 10 minutes and on *Refresh now*. Tested on a Charge 6, including during
  music. Only speakers paired with this PC are asked; nothing is sent during full-screen games.
- **A battery in the Windows style** instead of the iPhone one: a thin outline with the number inside, the fill in
  white (black on a light taskbar), and Windows' own yellow and red when the charge is low. Charging is shown by the
  bolt alone, the fill no longer turns green.

## 0.3.0-beta.4 — 2026-11-13

Fixes from a full quality audit.

- **The G435 is never read blind.** If the app cannot find the headset's audio device, it no longer assumes silence
  and reads during music (on other PCs that would crackle). If the receiver is already in service mode (a firmware
  update), the app leaves it alone and does not take it out of that mode.
- **Tooltip and hover plate** on the strip: the device's name, charge and state, like a tray icon.
- **Device list**: status lines no longer get cut off ("Estimated charge, 12 min ago", "Asleep · 3 h ago"), batteries
  stay in one column with a charging bolt to their left, a hairline edge, no empty first frame, the gear menu no
  longer covers the gear, and with an auto-hidden taskbar the list no longer covers the strip.
- **A nearly empty device is visible**: the red fill stays noticeable even at 1–7 %.
- **Light theme has more contrast** in its numbers and fill colours.
- **No devices, still reachable**: starting the app again from Start opens its window, and the empty list says how to
  report a device.
- **Robustness**: Alt+F4 on the list no longer breaks it; after an Explorer restart the strip returns at once; the
  strip no longer hides under the taskbar's own layer; a rare background error no longer closes the app; a lost device
  connection recovers by itself; start-up holds up the taskbar less.
- **Tray icons** (fallback mode) always respond to clicks, repaint on theme and scale changes, and show 100 % as "100".
- **Install and update**: an update no longer turns autostart back on; autostart turned off in Task Manager shows as
  off; uninstalling removes everything including the exe; a failed update starts the old version again; a download is
  also checked for its version; clear error messages; a second "Install" click does not start a second download;
  reinstall and downgrade are asked differently.
- **Less memory, fewer wake-ups**: WPF and GDI+ are no longer loaded (taskbar buttons are read through the UI
  Automation COM API, fonts through DirectWrite); a reading that changes nothing on screen wakes no window and writes
  no file; the G435's audio is sampled less often during long playback and not at all in full-screen games; the
  strip's watchdog wakes every 5 s instead of 2; known HID devices are not reopened on every Windows device event;
  Bluetooth properties are read selectively; the location hook covers only the taskbar, not every Explorer window.
  A growing handle count now shows up in the log.
- **Small things**: low-battery notifications replace each other and expire after 2 hours ("20% or less"); charges are
  saved when Windows shuts down; the README settings example works when copied; the report dialog warns that it can
  hold Bluetooth names and addresses.

## 0.3.0-beta.3 — 2026-11-12

- **A new, iPhone-style battery.** The number is cut out of a solid battery and no longer splits into two colours
  where the fill ends ("47" used to read as a dark 4 and a white 7). "100" fits with room to spare. A sleeping
  device is a hollow outline.
- **A new charging bolt**: small and solid, next to the battery.
- **The settings menu no longer hides behind the device list** and is dark in dark mode. A reading arriving while the
  menu was open used to raise the list above it.
- **A mouse on its cable no longer jumps between values** (30 → 95 → 35 → 100). On the cable it answers both by
  cable and through its receiver, and the receiver repeats a stale value; while the cable answers, only it counts.
- **Game mode.** While a full-screen game or video is in front, the app sends no requests to devices and does not
  look at the taskbar.
- **PlayStation controllers on Bluetooth** are no longer switched to full reports: that made games reading the
  controller directly lose its input until it reconnected. A false "5%" read from the short report is fixed too.
- **The update check** uses the GitHub releases feed, which has no 60-requests-an-hour limit per IP address (it failed
  for people behind a VPN), and retries after 2 hours instead of a day.
- **Lighter**: the handle count no longer creeps up for hours, the strip is not repainted when nothing visible
  changed, the taskbar is checked less often when there is room, and the G435 audio state every 2 s instead of 1 s.
- **Safer**: Windows DLLs load from System32 only.

## 0.3.0-beta.2 — 2026-11-11

- **Report a device** under the gear: the app collects the device report itself and shows the file, ready to send
  on Telegram or attach to a GitHub device request. No command prompt needed.

## 0.3.0-beta.1 — 2026-11-11

First public beta.

- **English interface** next to Russian, chosen by the Windows language (or `language = ru|en` in settings).
- **Logitech G435 reading needs your consent.** The app asks once when it finds the receiver, because reading pauses
  the headset's audio for about a second. It can be turned on or off under the gear.
- **Optional update check**, once a day against GitHub releases. Downloads are checked against the published SHA-256
  before a silent install. Off until you agree.
- **A crowded taskbar no longer hides app buttons.** When the app buttons reach the strip, it drops its device icons;
  when even the batteries do not fit, the devices move to tray icons.
- After agreeing to G435 reading, the first read waits for silence instead of interrupting audio.

## 0.2.0 — 2026-11-11

- Bluetooth devices whose charge Windows knows (headphones, speakers, mice, keyboards).
- Logitech HID++ devices on Unifying, Bolt and Lightspeed receivers, by cable and Bluetooth.
- DualSense, DualSense Edge and DualShock 4, by cable and Bluetooth.
- Xbox and compatible controllers through Windows.Gaming.Input.
- A device seen by two sources is shown once.
- Speaker icon. Fixed a memory leak while waiting for silence in the headset. Remembered charges are written at most every two minutes.

## 0.1.2 — 2026-11-11

- Text and icons rendered with DirectWrite, like the Windows 11 taskbar.
- Icons and numbers in the taskbar's own colour, 12 px numbers, brighter outline, cleaner look for sleeping devices.

## 0.1.1 — 2026-11-11

- The G435 is read in any 20-second pause in audio; "Refresh now" reads it at once.
- Clear status when the headset cannot be read for a while; a settling delay and quick retry after sleep.

## 0.1.0 — 2026-11-10

First version: the strip, Lamzu (COMPX) mice and the Logitech G435, the device list, notifications at 20% and 10%,
recovery after Explorer restarts and sleep, tray-icon fallback, light and dark theme, self-installer.
