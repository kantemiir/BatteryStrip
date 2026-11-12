using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using BatteryStrip.App;
using BatteryStrip.Core;
using BatteryStrip.Rendering;

namespace BatteryStrip.Taskbar;

/// <summary>
/// Keeps the strip inside the taskbar, just left of the notification area, and redraws it when devices,
/// theme or display scale change. Re-attaches after Explorer restarts and checks itself every few seconds.
///
/// Runs entirely on one thread with a message loop. Because the strip is a child of the taskbar, Windows
/// joins this thread's input queue with Explorer's: nothing here may block, so device work happens elsewhere
/// and only finished snapshots arrive here.
/// </summary>
internal sealed class TaskbarStrip : IDisposable
{
    /// <summary>Gap between the strip and the notification area, at 100 % scale.</summary>
    private const float TrayGap = 8;

    /// <summary>Space kept free between the last app button and the strip, at 100 % scale.</summary>
    private const float AppGap = 8;

    private const int WatchdogMs = 2000;
    private const int LayoutDelayMs = 50;

    /// <summary>After this long without a working attachment the strip reports itself broken.</summary>
    private static readonly TimeSpan GiveUpAfter = TimeSpan.FromSeconds(20);

    private static readonly IntPtr WatchdogTimer = new(1);
    private static readonly IntPtr LayoutTimer = new(2);
    private const int WM_DEVICES_CHANGED = User32.WM_APP + 1;
    private const int WM_TASKLIST_CHANGED = User32.WM_APP + 2;

    private readonly DeviceStore _store;
    private readonly GlyphCache _glyphs = new();
    private readonly StripPainter _painter;
    private readonly BroadcastWindow _broadcasts = new();
    private readonly int _taskbarCreatedMessage = User32.RegisterWindowMessage("TaskbarCreated");
    private readonly User32.WinEventProc _winEventProc; // kept alive while the hook exists
    private readonly TaskListWatcher _taskList = new();

    private StripWindow? _strip;
    private TaskbarPlace? _place;
    private IntPtr _locationHook;
    private uint _hookedProcess;
    private Palette _palette = Theme.Current();
    private StripImage? _image;
    private string? _shownKey;
    private IReadOnlyList<DeviceSnapshot> _devices = Array.Empty<DeviceSnapshot>();
    private DateTime? _brokenSince;
    private bool _reportedBroken;
    private bool _fits = true;
    private string? _lastProblem;

    /// <summary>A device on the strip was clicked; the argument is its id, or null for a click between devices.</summary>
    public event Action<string?>? DeviceClicked;

    /// <summary>Right click on the strip, with the cursor position on screen.</summary>
    public event Action<int, int>? ContextMenuRequested;

    /// <summary>Attaching has failed for a while (true) or works again (false).</summary>
    public event Action<bool>? HealthChanged;

    /// <summary>
    /// The app buttons left too little room for the strip even without device icons (false), or there is
    /// room again (true). While it does not fit, the strip is hidden and the fallback shows the devices.
    /// </summary>
    public event Action<bool>? FitChanged;

    /// <summary>The taskbar was recreated or the computer woke up: device lists may be stale too.</summary>
    public event Action? EnvironmentChanged;

    /// <summary>Screen rectangle of the strip, for placing the flyout above it.</summary>
    public User32.RECT? ScreenBounds =>
        _strip != null && _strip.Handle != IntPtr.Zero && User32.GetWindowRect(_strip.Handle, out User32.RECT rect) ? rect : null;

    public bool IsHealthy => !_reportedBroken;

    public bool Fits => _fits;

    /// <summary>A top-level window of this thread that can own pop-up menus.</summary>
    public IntPtr MenuOwner => _broadcasts.Handle;

    public TaskbarStrip(DeviceStore store)
    {
        _store = store;
        _painter = new StripPainter(_glyphs);
        _winEventProc = OnWinEvent;
        _broadcasts.Message += OnBroadcast;
        _store.Changed += () => User32.PostMessage(_broadcasts.Handle, WM_DEVICES_CHANGED, IntPtr.Zero, IntPtr.Zero);
        _taskList.Changed += () => User32.PostMessage(_broadcasts.Handle, WM_TASKLIST_CHANGED, IntPtr.Zero, IntPtr.Zero);
        User32.SetTimer(_broadcasts.Handle, WatchdogTimer, WatchdogMs, IntPtr.Zero);
        _devices = _store.Snapshot;
        Attach("start");
    }

    public void Dispose()
    {
        User32.KillTimer(_broadcasts.Handle, WatchdogTimer);
        _taskList.Dispose();
        Unhook();
        _strip?.Dispose();
        _broadcasts.Dispose();
        _glyphs.Dispose();
    }

    private bool OnBroadcast(int message, IntPtr wParam, IntPtr lParam)
    {
        if (message == _taskbarCreatedMessage)
        {
            Log.Info("Explorer recreated the taskbar.");
            _taskList.CheckSoon();
            Attach("taskbar recreated");
            EnvironmentChanged?.Invoke();
            return true;
        }

        switch (message)
        {
            case WM_DEVICES_CHANGED:
                _devices = _store.Snapshot;
                Render();
                return true;

            case WM_TASKLIST_CHANGED:
                Render();
                return true;

            case User32.WM_TIMER when wParam == WatchdogTimer:
                Watchdog();
                return true;

            case User32.WM_TIMER when wParam == LayoutTimer:
                User32.KillTimer(_broadcasts.Handle, LayoutTimer);
                Render();
                return true;

            case User32.WM_SETTINGCHANGE:
                if (lParam != IntPtr.Zero && Marshal.PtrToStringUni(lParam) == "ImmersiveColorSet")
                {
                    Palette palette = Theme.Current();
                    if (palette != _palette)
                    {
                        _palette = palette;
                        Log.Info($"Theme changed to {(palette.IsLight ? "light" : "dark")}.");
                        Render();
                    }
                }
                ScheduleLayout();
                return false;

            case User32.WM_DISPLAYCHANGE:
            case User32.WM_DPICHANGED:
                ScheduleLayout();
                return false;

            case User32.WM_POWERBROADCAST when (int)wParam == User32.PBT_APMRESUMEAUTOMATIC:
                Log.Info("Resumed from sleep.");
                Attach("resume");
                EnvironmentChanged?.Invoke();
                return false;

            default:
                return false;
        }
    }

    /// <summary>Finds the taskbar and makes the strip its child. Safe to call again at any time.</summary>
    private void Attach(string reason)
    {
        TaskbarPlace? place = TaskbarLocator.Find(out string? problem);
        if (place == null)
        {
            MarkBroken(problem ?? "taskbar unavailable");
            return;
        }

        if (_strip == null || _strip.Handle == IntPtr.Zero)
        {
            _strip = new StripWindow();
            _strip.LeftClick += (x, _) => DeviceClicked?.Invoke(DeviceAt(x));
            _strip.RightClick += (_, _) =>
            {
                if (User32.GetCursorPos(out User32.POINT cursor)) ContextMenuRequested?.Invoke(cursor.X, cursor.Y);
            };
            _strip.Destroyed += () => Log.Info("The strip window was destroyed; it will be recreated.");
        }

        IntPtr hwnd = _strip.Handle;
        if (User32.GetParent(hwnd) != place.Taskbar)
        {
            long style = (long)User32.GetWindowLongPtr(hwnd, User32.GWL_STYLE);
            style = (style & ~0x80000000L) | User32.WS_CHILD | User32.WS_CLIPSIBLINGS;
            User32.SetWindowLongPtr(hwnd, User32.GWL_STYLE, new IntPtr(style));
            User32.SetParent(hwnd, place.Taskbar);
            int error = Marshal.GetLastWin32Error();
            if (User32.GetParent(hwnd) != place.Taskbar)
            {
                // Start over with a fresh window next time: this one may be stuck to a dead parent.
                _strip.Dispose();
                _strip = null;
                MarkBroken($"SetParent did not take (error {error})");
                return;
            }
            Log.Info($"Strip attached to the taskbar ({reason}): taskbar {place.TaskbarRect}, tray at {place.TrayLeft}, {place.Dpi} dpi.");
        }

        if (_place != null && _place.Dpi != place.Dpi) _glyphs.Clear();
        _place = place;
        Hook(place.Taskbar);
        Render();
    }

    /// <summary>Paints the devices and puts the strip in its place. Hides it when there is nothing to show.</summary>
    private void Render()
    {
        if (_strip == null || _strip.Handle == IntPtr.Zero || _place == null) return;

        // The place can move at any time (the tray grows or shrinks), so it is re-read before every layout.
        TaskbarPlace? place = TaskbarLocator.Find(out _);
        if (place == null || place.Taskbar != _place.Taskbar)
        {
            Attach("layout");
            return;
        }
        if (place.Dpi != _place.Dpi) _glyphs.Clear();
        _place = place;

        IntPtr hwnd = _strip.Handle;
        if (_devices.Count == 0)
        {
            User32.ShowWindow(hwnd, User32.SW_HIDE);
            _image = null;
            _shownKey = null;
            _taskList.Room = int.MaxValue;
            MarkHealthy();
            return;
        }

        int right = place.TrayLeft - (int)Math.Round(TrayGap * place.Scale);
        int? appsRight = _taskList.Right == int.MinValue ? null : _taskList.Right - place.TaskbarRect.Left;
        float fullWidth = _painter.Measure(_devices, place.Scale);
        _taskList.Room = appsRight is int apps ? (int)((right - fullWidth - apps) / place.Scale) : int.MinValue;
        StripFit fit = StripLayout.Choose(right, appsRight, (int)Math.Round(AppGap * place.Scale),
            fullWidth, _painter.Measure(_devices, place.Scale, compact: true));
        bool compact = fit == StripFit.Compact;
        if (fit == StripFit.None)
        {
            User32.ShowWindow(hwnd, User32.SW_HIDE);
            _image = null;
            MarkHealthy();
            SetFits(false);
            return;
        }
        SetFits(true);

        // A new reading with the same look (same percent, same state) needs no new picture, and most readings are such.
        string key = VisualKey(_devices, _palette, place, compact);
        if (key == _shownKey && _image != null && User32.IsWindowVisible(hwnd) && IsWhereExpected(place))
        {
            MarkHealthy();
            return;
        }

        StripImage image = _painter.Paint(_devices, _palette, place.Scale, place.Height, compact);
        int width = image.Surface.Width;
        int x = right - width;

        if (!LayeredBitmap.Show(hwnd, image.Surface))
        {
            _shownKey = null;
            MarkBroken($"UpdateLayeredWindow failed ({Marshal.GetLastWin32Error()})");
            return;
        }
        if (!User32.SetWindowPos(hwnd, IntPtr.Zero, x, 0, width, place.Height, User32.SWP_NOACTIVATE | User32.SWP_SHOWWINDOW))
        {
            _shownKey = null;
            MarkBroken($"SetWindowPos failed ({Marshal.GetLastWin32Error()})");
            return;
        }
        _image = image;
        _shownKey = key;
        MarkHealthy();
    }

    /// <summary>Everything the strip's picture depends on; equal keys give equal pictures.</summary>
    private static string VisualKey(IReadOnlyList<DeviceSnapshot> devices, Palette palette, TaskbarPlace place, bool compact)
    {
        var key = new System.Text.StringBuilder();
        key.Append(palette.IsLight).Append('|').Append(place.Dpi).Append('|').Append(place.Height).Append('|').Append(place.TrayLeft).Append('|').Append(compact);
        foreach (DeviceSnapshot d in devices)
        {
            key.Append('|').Append(d.Id).Append(':').Append(d.Kind).Append(':').Append(d.Percent).Append(':')
                .Append(d.IsCharging).Append(':').Append(d.IsAsleep).Append(':').Append(d.Level);
        }
        return key.ToString();
    }

    /// <summary>Periodic self-check: still a child of the live taskbar, at the right place, with the right look.</summary>
    private void Watchdog()
    {
        TaskbarPlace? place = TaskbarLocator.Find(out string? problem);
        if (place == null)
        {
            MarkBroken(problem ?? "taskbar unavailable");
            return;
        }

        bool lost = _strip == null || _strip.Handle == IntPtr.Zero || !User32.IsWindow(_strip.Handle)
            || User32.GetParent(_strip.Handle) != place.Taskbar;
        if (lost)
        {
            if (_lastProblem == null) Log.Info($"Watchdog: the strip lost its place (window {(_strip?.Handle ?? IntPtr.Zero) != IntPtr.Zero}, parent ok {(_strip != null && User32.GetParent(_strip.Handle) == place.Taskbar)}).");
            Attach("watchdog");
            return;
        }

        if (_devices.Count > 0 && (_place == null || place.TrayLeft != _place.TrayLeft || place.Height != _place.Height
            || place.Dpi != _place.Dpi || !IsWhereExpected(place)))
        {
            Render();
        }
    }

    private bool IsWhereExpected(TaskbarPlace place)
    {
        if (_strip == null || _image == null || !User32.GetWindowRect(_strip.Handle, out User32.RECT rect)) return false;
        int expectedRight = place.TaskbarRect.Left + place.TrayLeft - (int)Math.Round(TrayGap * place.Scale);
        return rect.Right == expectedRight && rect.Width == _image.Surface.Width && rect.Top == place.TaskbarRect.Top;
    }

    private string? DeviceAt(int x)
    {
        if (_image == null) return null;
        foreach (DeviceSlot slot in _image.Slots)
        {
            if (x >= slot.Bounds.X && x < slot.Bounds.Right) return slot.DeviceId;
        }
        return null;
    }

    /// <summary>Follows moves of the notification area as they happen, instead of waiting for the watchdog.</summary>
    private void Hook(IntPtr taskbar)
    {
        User32.GetWindowThreadProcessId(taskbar, out uint process);
        if (process == _hookedProcess && _locationHook != IntPtr.Zero) return;
        Unhook();
        _locationHook = User32.SetWinEventHook(User32.EVENT_OBJECT_LOCATIONCHANGE, User32.EVENT_OBJECT_LOCATIONCHANGE,
            IntPtr.Zero, _winEventProc, process, 0, User32.WINEVENT_OUTOFCONTEXT | User32.WINEVENT_SKIPOWNPROCESS);
        _hookedProcess = process;
    }

    private void Unhook()
    {
        if (_locationHook != IntPtr.Zero) User32.UnhookWinEvent(_locationHook);
        _locationHook = IntPtr.Zero;
        _hookedProcess = 0;
    }

    private void OnWinEvent(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (idObject != User32.OBJID_WINDOW || _place == null) return;
        if (hwnd == _place.Tray || hwnd == _place.Taskbar) ScheduleLayout();
    }

    /// <summary>Coalesces bursts of geometry changes into one layout pass.</summary>
    private void ScheduleLayout() => User32.SetTimer(_broadcasts.Handle, LayoutTimer, LayoutDelayMs, IntPtr.Zero);

    private void SetFits(bool fits)
    {
        if (fits == _fits) return;
        _fits = fits;
        Log.Info(fits ? "The strip fits next to the app buttons again." : "No room next to the app buttons; the strip is hidden.");
        FitChanged?.Invoke(fits);
    }

    private void MarkBroken(string reason)
    {
        DateTime now = DateTime.UtcNow;
        _brokenSince ??= now;
        if (reason != _lastProblem)
        {
            _lastProblem = reason;
            Log.Warn($"Strip is not attached: {reason}.");
        }
        if (!_reportedBroken && now - _brokenSince.Value >= GiveUpAfter)
        {
            _reportedBroken = true;
            Log.Warn("Strip gave up for now; the fallback takes over.");
            HealthChanged?.Invoke(true);
        }
    }

    private void MarkHealthy()
    {
        _brokenSince = null;
        _lastProblem = null;
        if (!_reportedBroken) return;
        _reportedBroken = false;
        Log.Info("Strip is attached again.");
        HealthChanged?.Invoke(false);
    }
}
