using System.Runtime.InteropServices;

namespace ReControl.Desktop.Native;

/// <summary>
/// X11 and XShm PInvoke declarations for Linux screen capture.
/// </summary>
internal static class X11Interop
{
    private const string LibX11 = "libX11.so.6";
    private const string LibXext = "libXext.so.6";
    private const string LibXtst = "libXtst.so.6";
    private const string LibXfixes = "libXfixes.so.3";
    private const string LibC = "libc";

    // --- X11 constants ---
    public const int ZPixmap = 2;
    public const ulong AllPlanes = unchecked((ulong)~0L);

    // --- POSIX shared memory constants ---
    public const int IPC_PRIVATE = 0;
    public const int IPC_CREAT = 0x200;
    public const int IPC_RMID = 0;
    public const int SHM_R = 0x100;
    public const int SHM_W = 0x80;

    // --- XImage structure ---
    [StructLayout(LayoutKind.Sequential)]
    public struct XImage
    {
        public int width;
        public int height;
        public int xoffset;
        public int format;
        public IntPtr data;
        public int byte_order;
        public int bitmap_unit;
        public int bitmap_bit_order;
        public int bitmap_pad;
        public int depth;
        public int bytes_per_line;
        public int bits_per_pixel;
        public ulong red_mask;
        public ulong green_mask;
        public ulong blue_mask;
        public IntPtr obdata;
    }

    // --- XShmSegmentInfo structure ---
    [StructLayout(LayoutKind.Sequential)]
    public struct XShmSegmentInfo
    {
        public ulong shmseg;    // ShmSeg (unsigned long)
        public int shmid;       // int
        public IntPtr shmaddr;  // char*
        public int readOnly;    // Bool (int)
    }

    // --- Core X11 functions (libX11.so.6) ---

    [DllImport(LibX11)]
    public static extern int XInitThreads();

    [DllImport(LibX11)]
    public static extern IntPtr XOpenDisplay(string? display);

    [DllImport(LibX11)]
    public static extern int XCloseDisplay(IntPtr display);

    [DllImport(LibX11)]
    public static extern int XDefaultScreen(IntPtr display);

    [DllImport(LibX11)]
    public static extern IntPtr XRootWindow(IntPtr display, int screen);

    [DllImport(LibX11)]
    public static extern int XDisplayWidth(IntPtr display, int screen);

    [DllImport(LibX11)]
    public static extern int XDisplayHeight(IntPtr display, int screen);

    [DllImport(LibX11)]
    public static extern IntPtr XGetImage(
        IntPtr display, IntPtr drawable,
        int x, int y, uint width, uint height,
        ulong planeMask, int format);

    [DllImport(LibX11)]
    public static extern int XDestroyImage(IntPtr image);

    [DllImport(LibX11)]
    public static extern IntPtr XDefaultVisual(IntPtr display, int screen);

    [DllImport(LibX11)]
    public static extern int XDefaultDepth(IntPtr display, int screen);

    // --- XShm extension functions (libXext.so.6) ---

    [DllImport(LibXext)]
    public static extern int XShmQueryExtension(IntPtr display);

    [DllImport(LibXext)]
    public static extern IntPtr XShmCreateImage(
        IntPtr display, IntPtr visual, uint depth,
        int format, IntPtr data,
        ref XShmSegmentInfo shminfo,
        uint width, uint height);

    [DllImport(LibXext)]
    public static extern int XShmAttach(IntPtr display, ref XShmSegmentInfo shminfo);

    [DllImport(LibXext)]
    public static extern int XShmDetach(IntPtr display, ref XShmSegmentInfo shminfo);

    [DllImport(LibXext)]
    public static extern int XShmGetImage(
        IntPtr display, IntPtr drawable,
        IntPtr image,
        int x, int y, ulong planeMask);

    // --- POSIX shared memory functions (libc) ---

    [DllImport(LibC, SetLastError = true)]
    public static extern int shmget(int key, nint size, int shmflg);

    [DllImport(LibC, SetLastError = true)]
    public static extern IntPtr shmat(int shmid, IntPtr shmaddr, int shmflg);

    [DllImport(LibC, SetLastError = true)]
    public static extern int shmdt(IntPtr shmaddr);

    [DllImport(LibC, SetLastError = true)]
    public static extern int shmctl(int shmid, int cmd, IntPtr buf);

    // --- XTEST extension functions (libXtst.so.6) ---

    [DllImport(LibXtst)]
    public static extern bool XTestQueryExtension(
        IntPtr display, out int event_base, out int error_base,
        out int major_version, out int minor_version);

    [DllImport(LibXtst)]
    public static extern void XTestFakeKeyEvent(
        IntPtr display, uint keycode, bool is_press, ulong delay);

    [DllImport(LibXtst)]
    public static extern void XTestFakeButtonEvent(
        IntPtr display, uint button, bool is_press, ulong delay);

    [DllImport(LibXtst)]
    public static extern void XTestFakeMotionEvent(
        IntPtr display, int screen_number, int x, int y, ulong delay);

    // --- Keysym conversion (libX11.so.6) ---

    [DllImport(LibX11)]
    public static extern uint XKeysymToKeycode(IntPtr display, ulong keysym);

    [DllImport(LibX11)]
    public static extern int XFlush(IntPtr display);

    // --- Phase 14: clipboard change detection (XFixes + selection management) ---

    // XFixes selection-owner-notify mask
    public const uint XFixesSetSelectionOwnerNotifyMask = 1u << 0;
    // XFixesSelectionNotify is event type 0 within the XFixes event range; the
    // actual numeric event code is (eventBase + 0) where eventBase comes from
    // XFixesQueryExtension. We compare with that sum at the call site.

    // Standard X event opcodes we dispatch on
    public const int SelectionNotify = 31;
    public const int ClientMessage = 33;

    // For XGetWindowProperty(reqType: IntPtr.Zero) -> AnyPropertyType
    public static readonly IntPtr AnyPropertyType = IntPtr.Zero;

    // X event union -- we only read the `type` field for dispatch; pad out to 24 longs
    // to cover the largest member of the C union safely on 64-bit platforms.
    [StructLayout(LayoutKind.Sequential)]
    public struct XEvent
    {
        public int type;
        public long pad0; public long pad1; public long pad2; public long pad3;
        public long pad4; public long pad5; public long pad6; public long pad7;
        public long pad8; public long pad9; public long pad10; public long pad11;
        public long pad12; public long pad13; public long pad14; public long pad15;
        public long pad16; public long pad17; public long pad18; public long pad19;
        public long pad20; public long pad21; public long pad22;
    }

    // --- libXfixes ---

    [DllImport(LibXfixes)]
    public static extern int XFixesQueryExtension(IntPtr display, out int eventBase, out int errorBase);

    [DllImport(LibXfixes)]
    public static extern int XFixesQueryVersion(IntPtr display, out int major, out int minor);

    [DllImport(LibXfixes)]
    public static extern void XFixesSelectSelectionInput(IntPtr display, IntPtr window, IntPtr selection, uint mask);

    // --- libX11: atoms, windows, selections ---

    [DllImport(LibX11, CharSet = CharSet.Ansi)]
    public static extern IntPtr XInternAtom(IntPtr display, string name, bool onlyIfExists);

    [DllImport(LibX11)]
    public static extern IntPtr XCreateSimpleWindow(
        IntPtr display, IntPtr parent,
        int x, int y, uint width, uint height,
        uint borderWidth, ulong border, ulong background);

    [DllImport(LibX11)]
    public static extern int XDestroyWindow(IntPtr display, IntPtr window);

    [DllImport(LibX11)]
    public static extern int XConvertSelection(
        IntPtr display, IntPtr selection, IntPtr target,
        IntPtr property, IntPtr requestor, IntPtr time);

    [DllImport(LibX11)]
    public static extern int XGetWindowProperty(
        IntPtr display, IntPtr w, IntPtr property,
        IntPtr offset, IntPtr length, bool delete, IntPtr reqType,
        out IntPtr actualType, out int actualFormat,
        out IntPtr nItems, out IntPtr bytesAfter,
        out IntPtr prop);

    [DllImport(LibX11)]
    public static extern int XFree(IntPtr data);

    [DllImport(LibX11)]
    public static extern int XPending(IntPtr display);

    [DllImport(LibX11)]
    public static extern int XNextEvent(IntPtr display, out XEvent eventOut);

    // --- libX11: synthetic event delivery (used by Stop() to wake XNextEvent) ---

    [DllImport(LibX11)]
    public static extern int XSendEvent(IntPtr display, IntPtr w, bool propagate, long eventMask, ref XEvent eventSend);
}
