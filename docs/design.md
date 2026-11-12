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
thread looks for the last `Taskbar.TaskListButtonAutomationPeer` every 10 s while the buttons are close to the strip
and every 30 s while there is room for several more, and not at all while a full-screen program is in front. On
Windows 10 the legacy `MSTaskSwWClass` window gives it directly.

## Rendering

A small software canvas (`Surface`) in premultiplied BGRA. Shapes are antialiased analytically; glyphs come from
DirectWrite through a Direct2D DC render target as coverage masks (grayscale antialiasing, as on the taskbar).

The battery follows the iPhone: a solid body (the empty part in the text colour at about half opacity, the filled part
in the level colour) with the number cut out of both parts, so the number is one colour whatever the level; with a
plain number on top, the edge of the fill used to split digits into two colours. A sleeping device or one without
data is a hollow outline instead. The charging bolt is a small vector shape beside the battery (8×8 supersampled),
not a font glyph. Fonts: Segoe UI Variable and Segoe Fluent Icons. The flyout runs on its own thread and uses the
same painter; the strip repaints only when something visible changed. Pop-up menus follow the taskbar's light or
dark mode through uxtheme's `SetPreferredAppMode`.

## Devices

Each family is an `IDeviceSource`; each connected device gets a `DeviceSession` with its own task. Sources are
refreshed on Windows device-interface notifications (`CM_Register_Notification`), never by polling the device list.
A shared `DeviceStore` keeps the visible state and hands immutable snapshots to the UI threads.

Safety rules for talking to devices:

1. Prefer passive data: input reports the device sends anyway, values Windows already has.
2. Send only documented read requests. Never probe with arbitrary reports.
3. Anything with side effects (the G435 bridge pauses audio and switches the receiver's mode) needs the user's consent,
   always restores the previous state, and is rate-limited.
4. Never change a device's mode that other programs depend on (for example a PlayStation controller's Bluetooth
   report mode: games reading the plain DirectInput report would lose it).
5. While a full-screen program is in front (`SHQueryUserNotificationState`), send nothing; keep listening.
6. A mouse on its cable answers on both links; the cable is the truth, the receiver repeats stale values.

Protocol details and the hardware tests behind them: [research/devices.md](../research/devices.md).

## Resource use

Idle CPU is close to zero: no polling timers on the strip's thread, device sessions sleep between reads, the
G435 samples audio state every 2 s only while waiting for a quiet moment. About 30–35 MB private memory.

The thread pool starts and retires threads around timers and device replies; a retired thread keeps its handle and a
few events until its `Thread` object is finalized, and the app allocates too little for the garbage collector to run
on its own for hours. A background collection every 10 minutes keeps the handle count flat.
