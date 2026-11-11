using System;
using System.Runtime.InteropServices;
using System.Threading;
using BatteryStrip.Core;
using BatteryStrip.Devices;
using BatteryStrip.Flyout;
using BatteryStrip.Taskbar;

namespace BatteryStrip.App;

/// <summary>The running program: devices on worker threads, the strip on the calling thread's message loop.</summary>
internal sealed class AppHost : IDisposable
{
    /// <summary>Signalled by the installer or a second copy to ask this copy to exit.</summary>
    public const string ExitEventName = @"Local\BatteryStrip.Exit";

    /// <summary>Remembered charges are written at most this often (and on exit), not on every reading.</summary>
    private static readonly TimeSpan SaveDelay = TimeSpan.FromMinutes(2);

    private readonly DeviceStore _store = new(() => DateTime.UtcNow);
    private readonly Settings _settings;
    private readonly UpdateChecker _updates;
    private readonly FlyoutWindow _flyout;
    private readonly DeviceManager _devices;
    private readonly TaskbarStrip _strip;
    private readonly TrayIcons _tray;
    private readonly Timer _saveTimer;
    private readonly EventWaitHandle _exitEvent;
    private readonly RegisteredWaitHandle _exitWait;
    private readonly int _mainThreadId = GetCurrentThreadId();
    private int _savePending;
    private int _askedG435;

    /// <param name="forceTray">Show tray icons even while the strip works (for testing the fallback).</param>
    public AppHost(Settings settings, bool forceTray = false)
    {
        _settings = settings;
        _store.Import(ChargeMemory.Load(ChargeMemory.DefaultPath));
        Notifier.Register();
        _store.LowBattery += (device, threshold) =>
        {
            if (_settings.Warnings) Notifier.LowBattery(device, threshold);
        };
        _saveTimer = new Timer(_ =>
        {
            Interlocked.Exchange(ref _savePending, 0);
            Save();
        });
        _store.Changed += () =>
        {
            if (Interlocked.Exchange(ref _savePending, 1) == 0) _saveTimer.Change(SaveDelay, Timeout.InfiniteTimeSpan);
        };

        _strip = new TaskbarStrip(_store);
        _strip.ContextMenuRequested += ShowMenu;

        _updates = new UpdateChecker(() => _settings.UpdateChecks == true);
        _updates.Found += update => Notifier.Show(Text.Current.UpdateAvailable(update.Version.ToString()), Text.Current.UpdateAvailableBody);

        _flyout = FlyoutWindow.StartThread(_store, new FlyoutActions(
            GetWarnings: () => _settings.Warnings,
            SetWarnings: value => _settings.Warnings = value,
            GetG435: () => _settings.G435Reading,
            SetG435: value =>
            {
                _settings.G435Reading = value;
                _devices?.RequestRefresh(); // the first read waits for silence
            },
            GetUpdates: () => AppInfo.HasRepository ? _settings.UpdateChecks == true : null,
            SetUpdates: value => _settings.UpdateChecks = value,
            AvailableUpdate: () => _updates.Available?.Version.ToString(),
            InstallUpdate: InstallUpdate,
            RefreshDevices: () => _devices?.RequestRefresh(WakeReason.UserRefresh),
            OpenLog: OpenLog,
            Exit: RequestExit));
        _strip.DeviceClicked += _ =>
        {
            if (_strip.ScreenBounds is User32.RECT bounds) _flyout.Toggle(bounds);
        };

        _tray = new TrayIcons(_store);
        _tray.ContextMenuRequested += ShowMenu;
        _tray.Clicked += _ =>
        {
            if (User32.GetCursorPos(out User32.POINT cursor)) _flyout.Toggle(new User32.RECT { Left = cursor.X, Top = cursor.Y, Right = cursor.X, Bottom = cursor.Y });
        };
        _tray.Enabled = forceTray;
        _strip.HealthChanged += _ => UpdateTray(forceTray);
        _strip.FitChanged += _ => UpdateTray(forceTray);

        var options = new DeviceOptions(() => _settings.G435Reading, AskAboutG435);
        _devices = new DeviceManager(DeviceSources.Create(_store, new DeviceNaming(_settings.DeviceNames), options));
        _strip.EnvironmentChanged += () => _devices.RequestRefresh(WakeReason.Resumed);
        _devices.Start();

        if (AppInfo.HasRepository && _settings.UpdateChecks == null) InBackground(() => _settings.UpdateChecks = Dialogs.Ask(Text.Current.AskUpdates));

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
        _updates.Dispose();
        _tray.Dispose();
        _flyout.Dispose();
        _strip.Dispose();
        _devices.Dispose();
        _saveTimer.Dispose();
        Save();
    }

    private void Save() => ChargeMemory.Save(ChargeMemory.DefaultPath, _store.Export());

    /// <summary>Asks once per run whether the G435 may be read; the question is a message box on its own thread.</summary>
    private void AskAboutG435()
    {
        if (Interlocked.Exchange(ref _askedG435, 1) != 0) return;
        InBackground(() =>
        {
            _settings.G435Reading = Dialogs.Ask(Text.Current.AskG435);
            _devices.RequestRefresh(); // the first read waits for silence
        });
    }

    private void InstallUpdate()
    {
        if (_updates.Available is not AvailableUpdate update) return;
        InBackground(() =>
        {
            if (UpdateChecker.Install(update) is string error) Dialogs.Error(Text.Current.UpdateFailed(error));
        });
    }

    /// <summary>Message boxes and downloads must never run on the strip's thread, which the taskbar shares.</summary>
    private static void InBackground(Action action) =>
        new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                Log.Error("Background task failed", exception);
            }
        })
        { IsBackground = true }.Start();

    /// <summary>Tray icons stand in for the strip while it is broken or has no room.</summary>
    private void UpdateTray(bool forceTray) => _tray.Enabled = forceTray || !_strip.IsHealthy || !_strip.Fits;

    private static void OpenLog()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(System.IO.Path.Combine(AppInfo.LogDirectory, "app.log")) { UseShellExecute = true });
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or System.IO.FileNotFoundException)
        {
            Log.Warn($"Cannot open the log: {exception.Message}");
        }
    }

    private void ShowMenu(int x, int y)
    {
        const int Refresh = 1, Exit = 2;
        IntPtr menu = CreatePopupMenu();
        try
        {
            AppendMenu(menu, 0, new IntPtr(Refresh), Text.Current.MenuRefresh);
            AppendMenu(menu, 0x800, IntPtr.Zero, null); // separator
            AppendMenu(menu, 0, new IntPtr(Exit), Text.Current.MenuExit);

            // A menu needs a foreground owner to close when the user clicks elsewhere.
            IntPtr owner = _strip.MenuOwner;
            User32.SetForegroundWindow(owner);
            int choice = TrackPopupMenuEx(menu, 0x0100 /* TPM_RETURNCMD */ | 0x0020 /* TPM_BOTTOMALIGN */, x, y, owner, IntPtr.Zero);
            User32.PostMessage(owner, 0, IntPtr.Zero, IntPtr.Zero);

            if (choice == Refresh) _devices.RequestRefresh(WakeReason.UserRefresh);
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
