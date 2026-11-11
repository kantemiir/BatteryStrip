# Changelog

[Русская версия](CHANGELOG.ru.md)

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
