using System;
using System.Runtime.InteropServices;

namespace ReControl.Desktop.Native;

/// <summary>
/// PInvoke declarations for the Win32 clipboard change-listener path
/// (HWND_MESSAGE window + WM_CLIPBOARDUPDATE + AddClipboardFormatListener).
/// Consumed by Services.Clipboard.Platforms.WindowsClipboardWatcher. Per CONTEXT D-01,
/// this avoids Avalonia's Win32Properties.AddWndProcHookCallback hook chain (Pitfall 7).
/// </summary>
internal static class Win32ClipboardInterop
{
    private const string LibUser32 = "user32.dll";
    private const string LibKernel32 = "kernel32.dll";

    // --- Window message constants ---
    public const uint WM_DESTROY = 0x0002;
    public const uint WM_CLIPBOARDUPDATE = 0x031D;

    // --- Clipboard format constants ---
    public const int CF_UNICODETEXT = 13;

    // --- Special HWND values ---
    public static readonly IntPtr HWND_MESSAGE = new IntPtr(-3);

    // --- WndProc delegate (must be rooted via GCHandle.Alloc per Pitfall A) ---
    public delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    // --- WNDCLASSEX (sequential layout, Unicode chars) ---
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct WNDCLASSEX
    {
        public uint cbSize;
        public uint style;
        [MarshalAs(UnmanagedType.FunctionPtr)] public WndProcDelegate lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpszMenuName;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpszClassName;
        public IntPtr hIconSm;
    }

    // --- Class registration / window lifecycle ---
    [DllImport(LibUser32, CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern ushort RegisterClassEx(ref WNDCLASSEX lpwcx);

    [DllImport(LibUser32, CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool UnregisterClass(string lpClassName, IntPtr hInstance);

    [DllImport(LibUser32, CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr CreateWindowEx(
        uint dwExStyle,
        string lpClassName,
        string? lpWindowName,
        uint dwStyle,
        int x, int y, int nWidth, int nHeight,
        IntPtr hWndParent,
        IntPtr hMenu,
        IntPtr hInstance,
        IntPtr lpParam);

    [DllImport(LibUser32, SetLastError = true)]
    public static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport(LibUser32)]
    public static extern IntPtr DefWindowProc(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    // --- Clipboard listener registration ---
    [DllImport(LibUser32, SetLastError = true)]
    public static extern bool AddClipboardFormatListener(IntPtr hWnd);

    [DllImport(LibUser32, SetLastError = true)]
    public static extern bool RemoveClipboardFormatListener(IntPtr hWnd);

    // --- Clipboard read ---
    [DllImport(LibUser32, SetLastError = true)]
    public static extern bool OpenClipboard(IntPtr hWndNewOwner);

    [DllImport(LibUser32, SetLastError = true)]
    public static extern bool CloseClipboard();

    [DllImport(LibUser32, SetLastError = true)]
    public static extern IntPtr GetClipboardData(uint uFormat);

    // --- Global memory locking (CF_UNICODETEXT pointer is HGLOBAL) ---
    [DllImport(LibKernel32)]
    public static extern IntPtr GlobalLock(IntPtr hMem);

    [DllImport(LibKernel32)]
    public static extern bool GlobalUnlock(IntPtr hMem);

    // --- Process / module handle ---
    [DllImport(LibKernel32, CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr GetModuleHandle(string? lpModuleName);
}
