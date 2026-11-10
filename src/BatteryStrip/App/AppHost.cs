using System;
using System.Runtime.InteropServices;
using System.Threading;
using BatteryStrip.Core;
using BatteryStrip.Devices;
using BatteryStrip.Taskbar;

namespace BatteryStrip.App;

/// <summary>The running program: devices on worker threads, the strip on the calling thread's message loop.</summary>
internal sealed class AppHost : IDisposable
{
    /// <summary>Signalled by the installer or a second copy to ask this copy to exit.</summary>
    public const string ExitEventName = @"Local\BatteryStrip.Exit";

    private static readonly TimeSpan SaveDelay = TimeSpan.FromSeconds(10);

    private readonly DeviceStore _store = new(() => DateTime.UtcNow);
    private readonly DeviceManager _devices;
    private readonly TaskbarStrip _strip;
    private readonly Timer _saveTimer;
    private readonly EventWaitHandle _exitEvent;
    private readonly RegisteredWaitHandle _exitWait;
    private readonly int _mainThreadId = GetCurrentThreadId();

    public AppHost()
    {
        _store.Import(ChargeMemory.Load(ChargeMemory.DefaultPath));
        _saveTimer = new Timer(_ => Save());
        _store.Changed += () => _saveTimer.Change(SaveDelay, Timeout.InfiniteTimeSpan);

        _strip = new TaskbarStrip(_store);
        _strip.ContextMenuRequested += ShowMenu;
        _strip.HealthChanged += broken => Log.Info(broken ? "Fallback: tray icons are not implemented yet." : "Strip is back.");

        _devices = new DeviceManager(DeviceSources.Create(_store, DeviceNaming.Default));
        _strip.EnvironmentChanged += _devices.RequestRefresh;
        _devices.Start();

        _exitEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ExitEventName);
        _exitWait = ThreadPool.RegisterWaitForSingleObject(_exitEvent, (_, _) => RequestExit(), null, Timeout.Infinite, executeOnlyOnce: true);
    }

    /// <summary>Runs the message loop until the program is asked to exit.</summary>
    public void Run()
    {
        Log.Info($"{AppInfo.DisplayName} {AppInfo.Version} started.");
        while (User32.GetMessage(out User32.MSG message, IntPtr.Zero, 0, 0) > 0)
        {
            User32.TranslateMessage(ref message);
            User32.DispatchMessage(ref message);
        }
        Log.Info("Exiting.");
    }

    /// <summary>Ends the message loop. Safe to call from any thread.</summary>
    public void RequestExit() => PostThreadMessage(_mainThreadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);

    public void Dispose()
    {
        _exitWait.Unregister(null);
        _exitEvent.Dispose();
        _strip.Dispose();
        _devices.Dispose();
        _saveTimer.Dispose();
        Save();
    }

    private void Save() => ChargeMemory.Save(ChargeMemory.DefaultPath, _store.Export());

    private void ShowMenu(int x, int y)
    {
        const int Refresh = 1, Exit = 2;
        IntPtr menu = CreatePopupMenu();
        try
        {
            AppendMenu(menu, 0, new IntPtr(Refresh), "Обновить устройства");
            AppendMenu(menu, 0x800, IntPtr.Zero, null); // separator
            AppendMenu(menu, 0, new IntPtr(Exit), "Выход");

            // A menu needs a foreground owner to close when the user clicks elsewhere.
            IntPtr owner = _strip.MenuOwner;
            User32.SetForegroundWindow(owner);
            int choice = TrackPopupMenuEx(menu, 0x0100 /* TPM_RETURNCMD */ | 0x0020 /* TPM_BOTTOMALIGN */, x, y, owner, IntPtr.Zero);
            User32.PostMessage(owner, 0, IntPtr.Zero, IntPtr.Zero);

            if (choice == Refresh) _devices.RequestRefresh();
            else if (choice == Exit) RequestExit();
        }
        finally
        {
            DestroyMenu(menu);
        }
    }

    private const int WM_QUIT = 0x0012;

    [DllImport("kernel32.dll")]
    private static extern int GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern bool PostThreadMessage(int threadId, int message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool AppendMenu(IntPtr menu, int flags, IntPtr id, string? text);

    [DllImport("user32.dll")]
    private static extern int TrackPopupMenuEx(IntPtr menu, int flags, int x, int y, IntPtr owner, IntPtr parameters);

    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(IntPtr menu);
}
