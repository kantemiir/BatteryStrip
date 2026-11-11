# Design notes

## The strip

Windows 11 has no API for taskbar extensions (DeskBands are gone). BatteryStrip makes its strip a child window of
`Shell_TrayWnd`:

- A layered (`WS_EX_LAYERED`) window with per-pixel alpha, created as a pop-up and re-parented with `SetParent`.
  A layered *child* window needs the Windows 8+ `supportedOS` entry in the manifest.
- Placed 8 px left of `TrayNotifyWnd`. Its position is re-read before every layout, followed in real time through a
  `EVENT_OBJECT_LOCATIONCHANGE` WinEvent hook on Explorer, and checked every 2 s by a watchdog.
- Re-attached on `TaskbarCreated` (Explorer restart) and after resume. `SetParent` is verified by reading the parent
  back; a window stuck to a dead taskbar is destroyed and recreated.
- Fully transparent pixels get alpha 1, so the whole strip is clickable while staying invisible.
- Being a child of the taskbar, it hides with it in full-screen apps and with auto-hide, for free.
- A child window shares its input queue with Explorer's thread. If the strip's thread stalled, the taskbar would
  stall too. So that thread only paints; device work, UI Automation, dialogs and downloads run elsewhere.
- If attaching fails for 20 s, or there is no room next to the app buttons, tray icons stand in.

Where the app buttons end is known only through UI Automation on Windows 11 (the buttons are XAML); a background
thread looks for the last `Taskbar.TaskListButtonAutomationPeer` every 10 s. On Windows 10 the legacy
`MSTaskSwWClass` window gives it directly.

## Rendering

A small software canvas (`Surface`) in premultiplied BGRA. Shapes are antialiased analytically; glyphs come from
DirectWrite through a Direct2D DC render target as coverage masks (grayscale antialiasing, as on the taskbar). The
number inside a battery is cut out of the fill. Fonts: Segoe UI Variable and Segoe Fluent Icons. The flyout runs on
its own thread and uses the same painter.

## Devices

Each family is an `IDeviceSource`; each connected device gets a `DeviceSession` with its own task. Sources are
refreshed on Windows device-interface notifications (`CM_Register_Notification`), never by polling the device list.
A shared `DeviceStore` keeps the visible state and hands immutable snapshots to the UI threads.

Safety rules for talking to devices:

1. Prefer passive data: input reports the device sends anyway, values Windows already has.
2. Send only documented read requests. Never probe with arbitrary reports.
3. Anything with side effects (the G435 bridge pauses audio and switches the receiver's mode) needs the user's consent,
   always restores the previous state, and is rate-limited.

Protocol details and the hardware tests behind them: [research/devices.md](../research/devices.md).

## Resource use

Idle CPU is close to zero: no polling timers on the strip's thread, device sessions sleep between reads, the
G435 samples audio state once a second only while waiting for a quiet moment. About 25–30 MB private memory.
