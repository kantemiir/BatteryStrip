using System;
using System.Threading;
using System.Windows.Automation;

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
    private AutomationElement? _frame;
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

    public void Dispose() => _timer.Dispose();

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
        catch (Exception exception) when (exception is ElementNotAvailableException or InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
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
        if (_frame == null || _frameTaskbar != taskbar)
        {
            _frame = null;
            var condition = new PropertyCondition(AutomationElement.ClassNameProperty, ButtonClass);
            AutomationElement? button = AutomationElement.FromHandle(taskbar).FindFirst(TreeScope.Descendants, condition);
            if (button == null) return null;
            _frame = TreeWalker.ControlViewWalker.GetParent(button);
            _frameTaskbar = taskbar;
            if (_frame == null) return null;
        }

        // App buttons come after Start, Search and Task View; walking back from the end finds the last one quickly.
        for (AutomationElement? child = TreeWalker.ControlViewWalker.GetLastChild(_frame); child != null; child = TreeWalker.ControlViewWalker.GetPreviousSibling(child))
        {
            if (child.Current.ClassName == ButtonClass) return (int)Math.Ceiling(child.Current.BoundingRectangle.Right);
        }
        return null;
    }
}
