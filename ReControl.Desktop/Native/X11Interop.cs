using System.Runtime.InteropServices;

namespace ReControl.Desktop.Native;

/// <summary>
/// X11 and XShm PInvoke declarations for Linux screen capture.
/// </summary>
internal static class X11Interop
{
    private const string LibX11 = "libX11.so.6";
    private const string LibXext = "libXext.so.6";
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
}
