using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace BatteryStrip.Taskbar;

/// <summary>
/// Tracks where the taskbar's app buttons end, so the strip never covers them. Windows 11 draws the buttons
/// with XAML and exposes them only through UI Automation; Windows 10 still has the legacy task-list window.
/// The lookup crosses into Explorer and may be slow, so it runs on its own thread, never on the strip's.
/// </summary>
internal sealed class TaskListWatcher : IDisposable
{
    /// <summary>Look this often while the app buttons are close to the strip: one new button could reach it.</summary>
    private static readonly TimeSpan NearInterval = TimeSpan.FromSeconds(10);

    /// <summary>Look this often while there is room for several more buttons, or while nothing is shown.</summary>
    private static readonly TimeSpan FarInterval = TimeSpan.FromSeconds(30);

    /// <summary>Room, at 100 % scale, that counts as "several more buttons" (a button is about 44 px wide).</summary>
    private const int FarRoom = 200;

    private const string ButtonClass = "Taskbar.TaskListButtonAutomationPeer";

    private readonly Timer _timer;
    private readonly object _gate = new();
    private int _right = int.MinValue;
    private int _room = int.MinValue;
    private Uia.IUIAutomation? _automation;
    private Uia.IUIAutomationCondition? _isButton;
    private Uia.IUIAutomationElement? _frame;
    private IntPtr _frameTaskbar;

    /// <summary>Raised on a worker thread when the right edge of the app buttons moved.</summary>
    public event Action? Changed;

    public TaskListWatcher()
    {
        _timer = new Timer(_ => Check(), null, TimeSpan.Zero, Timeout.InfiniteTimeSpan);
    }

    /// <summary>Right edge of the last app button in screen pixels; <see cref="int.MinValue"/> while unknown.</summary>
    public int Right => Volatile.Read(ref _right);

    /// <summary>
    /// Free space between the last app button and the strip, in pixels at 100 % scale, as the strip last laid
    /// itself out; <see cref="int.MaxValue"/> while the strip shows nothing. Sets how often to look.
    /// </summary>
    public int Room
    {
        set => Volatile.Write(ref _room, value);
    }

    /// <summary>Looks again soon, for example after the taskbar was recreated.</summary>
    public void CheckSoon() => _timer.Change(TimeSpan.FromMilliseconds(500), Timeout.InfiniteTimeSpan);

    public void Dispose()
    {
        _timer.Dispose();
        lock (_gate)
        {
            Uia.Release(_frame);
            Uia.Release(_isButton);
            Uia.Release(_automation);
            _frame = null;
            _isButton = null;
            _automation = null;
        }
    }

    private void Check()
    {
        if (!Monitor.TryEnter(_gate)) return;
        try
        {
            // The taskbar is hidden behind a full-screen program; looking would only take time from the game.
            if (!App.FullScreen.IsActive())
            {
                int right = Measure();
                if (Interlocked.Exchange(ref _right, right) != right) Changed?.Invoke();
            }
        }
        catch (Exception exception)
        {
            // UI Automation fails in many ways while Explorer is busy or restarting (timeouts, vanished elements);
            // on a timer thread any of them would end the program. Start from the taskbar again next time.
            if (exception is not (COMException or InvalidCastException or InvalidOperationException))
                App.Log.Warn($"Measuring the app buttons failed: {exception.GetType().Name}: {exception.Message}");
            Uia.Release(_frame);
            _frame = null;
        }
        finally
        {
            Monitor.Exit(_gate);
            int room = Volatile.Read(ref _room);
            try { _timer.Change(room > FarRoom ? FarInterval : NearInterval, Timeout.InfiniteTimeSpan); }
            catch (ObjectDisposedException) { }
        }
    }

    private int Measure()
    {
        IntPtr taskbar = User32.FindWindow(TaskbarLocator.TaskbarClass, null);
        if (taskbar == IntPtr.Zero) return int.MinValue;

        if (MeasureXaml(taskbar) is int xaml) return xaml;

        // Windows 10: the legacy task list window spans the buttons.
        IntPtr rebar = User32.FindWindowEx(taskbar, IntPtr.Zero, "ReBarWindow32", null);
        IntPtr tasks = rebar == IntPtr.Zero ? IntPtr.Zero : User32.FindWindowEx(rebar, IntPtr.Zero, "MSTaskSwWClass", null);
        return tasks != IntPtr.Zero && User32.GetWindowRect(tasks, out User32.RECT rect) ? rect.Right : int.MinValue;
    }

    /// <summary>Windows 11: the last app button among the children of the taskbar frame.</summary>
    private int? MeasureXaml(IntPtr taskbar)
    {
        _automation ??= Uia.Create();
        _isButton ??= _automation.CreatePropertyCondition(Uia.ClassNameProperty, ButtonClass);
        Uia.IUIAutomationTreeWalker walker = _automation.ControlViewWalker;
        try
        {
            if (_frame == null || _frameTaskbar != taskbar)
            {
                Uia.Release(_frame);
                _frame = null;
                Uia.IUIAutomationElement root = _automation.ElementFromHandle(taskbar);
                Uia.IUIAutomationElement? button = root.FindFirst(Uia.TreeScopeDescendants, _isButton);
                Uia.Release(root);
                if (button == null) return null;
                _frame = walker.GetParentElement(button);
                Uia.Release(button);
                _frameTaskbar = taskbar;
                if (_frame == null) return null;
            }

            // App buttons come after Start, Search and Task View; walking back from the end finds the last one quickly.
            Uia.IUIAutomationElement? child = walker.GetLastChildElement(_frame);
            while (child != null)
            {
                if (Uia.ClassName(child) == ButtonClass)
                {
                    double? right = Uia.Right(child);
                    Uia.Release(child);
                    return right is double r ? (int)Math.Ceiling(r) : null;
                }
                Uia.IUIAutomationElement? previous = walker.GetPreviousSiblingElement(child);
                Uia.Release(child);
                child = previous;
            }
            return null;
        }
        finally
        {
            Uia.Release(walker);
        }
    }
}
