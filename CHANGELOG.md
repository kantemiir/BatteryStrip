# Changelog

[Русская версия](CHANGELOG.ru.md)

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
