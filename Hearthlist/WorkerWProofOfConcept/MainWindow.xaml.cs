using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace WorkerWProofOfConcept;

public partial class MainWindow : Window
{
    private const uint ProgmanCreateWorkerW = 0x052C;
    private const int GwlExstyle = -20;
    private const int GwlStyle = -16;
    private const long WsChild = 0x40000000L;
    private const long WsPopup = unchecked((int)0x80000000);
    private const long WsExToolWindow = 0x00000080L;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SmCxScreen = 0;
    private const uint SmCyScreen = 1;

    public MainWindow()
    {
        InitializeComponent();
        SourceInitialized += AttachToDesktop;
    }

    private void AttachToDesktop(object? sender, EventArgs e)
    {
        var windowHandle = new WindowInteropHelper(this).Handle;
        var workerWindowHandle = FindDesktopWorkerWindow();

        if (workerWindowHandle == IntPtr.Zero)
        {
            MessageBox.Show(
                "Windows Explorer did not provide a WorkerW desktop host.",
                "WorkerW proof of concept",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Close();
            return;
        }

        SetWindowLongPtr(windowHandle, GwlStyle, GetWindowLongPtr(windowHandle, GwlStyle) & ~WsPopup | WsChild);
        SetWindowLongPtr(windowHandle, GwlExstyle, GetWindowLongPtr(windowHandle, GwlExstyle) | WsExToolWindow);
        SetParent(windowHandle, workerWindowHandle);

        SetWindowPos(
            windowHandle,
            IntPtr.Zero,
            0,
            0,
            GetSystemMetrics(SmCxScreen),
            GetSystemMetrics(SmCyScreen),
            SwpNoActivate | SwpNoZOrder);
    }

    private static IntPtr FindDesktopWorkerWindow()
    {
        var progmanHandle = FindWindow("Progman", null);
        if (progmanHandle == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        SendMessageTimeout(
            progmanHandle,
            ProgmanCreateWorkerW,
            IntPtr.Zero,
            IntPtr.Zero,
            0,
            1000,
            out _);

        var workerWindow = IntPtr.Zero;
        EnumWindows((topLevelWindow, _) =>
        {
            var shellView = FindWindowEx(topLevelWindow, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (shellView != IntPtr.Zero)
            {
                workerWindow = FindWindowEx(IntPtr.Zero, topLevelWindow, "WorkerW", null);
            }

            return workerWindow == IntPtr.Zero;
        }, IntPtr.Zero);

        return workerWindow;
    }

    private delegate bool EnumWindowsCallback(IntPtr windowHandle, IntPtr parameter);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr FindWindow(string? className, string? windowName);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr FindWindowEx(
        IntPtr parentHandle,
        IntPtr childAfterHandle,
        string? className,
        string? windowName);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr parameter);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(
        IntPtr windowHandle,
        uint message,
        IntPtr wParam,
        IntPtr lParam,
        uint flags,
        uint timeout,
        out IntPtr result);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetParent(IntPtr childHandle, IntPtr newParentHandle);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(
        IntPtr windowHandle,
        IntPtr insertAfterHandle,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(uint index);

    private static long GetWindowLongPtr(IntPtr windowHandle, int index)
    {
        return IntPtr.Size == 8
            ? GetWindowLongPtr64(windowHandle, index).ToInt64()
            : GetWindowLong32(windowHandle, index);
    }

    private static void SetWindowLongPtr(IntPtr windowHandle, int index, long value)
    {
        if (IntPtr.Size == 8)
        {
            SetWindowLongPtr64(windowHandle, index, new IntPtr(value));
        }
        else
        {
            SetWindowLong32(windowHandle, index, unchecked((int)value));
        }
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr64(IntPtr windowHandle, int index);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
    private static extern int GetWindowLong32(IntPtr windowHandle, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr64(IntPtr windowHandle, int index, IntPtr value);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static extern int SetWindowLong32(IntPtr windowHandle, int index, int value);
}