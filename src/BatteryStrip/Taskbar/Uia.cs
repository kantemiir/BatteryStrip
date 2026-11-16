using System;
using System.Runtime.InteropServices;

namespace BatteryStrip.Taskbar;

/// <summary>
/// The few UI Automation calls the task-list watcher needs, straight through the COM API. The managed wrapper
/// (System.Windows.Automation) would load WPF and Direct3D into this small program for nothing; this keeps it out.
/// Element properties are read with GetCurrentPropertyValue, so only the first slots of each interface are used.
/// </summary>
internal static class Uia
{
    public const int TreeScopeDescendants = 4;
    public const int ClassNameProperty = 30012;
    public const int BoundingRectangleProperty = 30001;

    /// <summary>A new automation object; COM objects of UI Automation are free-threaded, so any thread may use it.</summary>
    public static IUIAutomation Create() => (IUIAutomation)new CUIAutomation();

    public static string? ClassName(IUIAutomationElement element) =>
        element.GetCurrentPropertyValue(ClassNameProperty) as string;

    /// <summary>The element's right edge in screen pixels, or null when it has no rectangle.</summary>
    public static double? Right(IUIAutomationElement element) =>
        element.GetCurrentPropertyValue(BoundingRectangleProperty) is double[] { Length: 4 } rect ? rect[0] + rect[2] : null;

    public static void Release(object? comObject)
    {
        if (comObject != null && Marshal.IsComObject(comObject)) Marshal.ReleaseComObject(comObject);
    }

    [ComImport, Guid("FF48DBA4-60EF-4201-AA87-54103EEF594E")]
    private class CUIAutomation
    {
    }

    // Declared in UIAutomationClient.h order. Methods after the last one used are left out.
    [ComImport, Guid("30CBE57D-D9D0-452A-AB13-7AC5AC4825EE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IUIAutomation
    {
        void CompareElements();
        void CompareRuntimeIds();
        void GetRootElement();
        IUIAutomationElement ElementFromHandle(IntPtr hwnd);
        void ElementFromPoint();
        void GetFocusedElement();
        void GetRootElementBuildCache();
        void ElementFromHandleBuildCache();
        void ElementFromPointBuildCache();
        void GetFocusedElementBuildCache();
        void CreateTreeWalker();
        IUIAutomationTreeWalker ControlViewWalker { get; }
        void ContentViewWalker();
        void RawViewWalker();
        void RawViewCondition();
        void ControlViewCondition();
        void ContentViewCondition();
        void CreateCacheRequest();
        void CreateTrueCondition();
        void CreateFalseCondition();
        IUIAutomationCondition CreatePropertyCondition(int propertyId, [MarshalAs(UnmanagedType.Struct)] object value);
    }

    [ComImport, Guid("D22108AA-8AC5-49A5-837B-37BBB3D7591E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IUIAutomationElement
    {
        void SetFocus();
        void GetRuntimeId();
        IUIAutomationElement? FindFirst(int scope, IUIAutomationCondition condition);
        void FindAll();
        void FindFirstBuildCache();
        void FindAllBuildCache();
        void BuildUpdatedCache();
        [return: MarshalAs(UnmanagedType.Struct)]
        object GetCurrentPropertyValue(int propertyId);
    }

    [ComImport, Guid("4042C624-389C-4AFC-A630-9DF854A541FC"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IUIAutomationTreeWalker
    {
        IUIAutomationElement? GetParentElement(IUIAutomationElement element);
        IUIAutomationElement? GetFirstChildElement(IUIAutomationElement element);
        IUIAutomationElement? GetLastChildElement(IUIAutomationElement element);
        IUIAutomationElement? GetNextSiblingElement(IUIAutomationElement element);
        IUIAutomationElement? GetPreviousSiblingElement(IUIAutomationElement element);
    }

    [ComImport, Guid("352FFBA8-0973-437C-A61F-F64CAFD81DF9"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IUIAutomationCondition
    {
    }
}
