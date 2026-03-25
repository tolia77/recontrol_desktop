using System.Collections.Generic;

namespace ReControl.Desktop.Models;

/// <summary>
/// Static mapping table from Windows Virtual Key codes to X11 keysyms.
/// Covers all keys sent by the frontend's mapToVirtualKey() function (~118 entries).
/// Keysym values from X11/keysymdef.h.
/// </summary>
public static class VkToKeysymMap
{
    private static readonly Dictionary<ushort, ulong> Map = new()
    {
        // --- Letters A-Z (VK 0x41-0x5A -> XK_a-XK_z, lowercase keysyms) ---
        { 0x41, 0x0061 }, // A -> a
        { 0x42, 0x0062 }, // B -> b
        { 0x43, 0x0063 }, // C -> c
        { 0x44, 0x0064 }, // D -> d
        { 0x45, 0x0065 }, // E -> e
        { 0x46, 0x0066 }, // F -> f
        { 0x47, 0x0067 }, // G -> g
        { 0x48, 0x0068 }, // H -> h
        { 0x49, 0x0069 }, // I -> i
        { 0x4A, 0x006A }, // J -> j
        { 0x4B, 0x006B }, // K -> k
        { 0x4C, 0x006C }, // L -> l
        { 0x4D, 0x006D }, // M -> m
        { 0x4E, 0x006E }, // N -> n
        { 0x4F, 0x006F }, // O -> o
        { 0x50, 0x0070 }, // P -> p
        { 0x51, 0x0071 }, // Q -> q
        { 0x52, 0x0072 }, // R -> r
        { 0x53, 0x0073 }, // S -> s
        { 0x54, 0x0074 }, // T -> t
        { 0x55, 0x0075 }, // U -> u
        { 0x56, 0x0076 }, // V -> v
        { 0x57, 0x0077 }, // W -> w
        { 0x58, 0x0078 }, // X -> x
        { 0x59, 0x0079 }, // Y -> y
        { 0x5A, 0x007A }, // Z -> z

        // --- Digits 0-9 (VK 0x30-0x39 -> XK_0-XK_9) ---
        { 0x30, 0x0030 }, // 0
        { 0x31, 0x0031 }, // 1
        { 0x32, 0x0032 }, // 2
        { 0x33, 0x0033 }, // 3
        { 0x34, 0x0034 }, // 4
        { 0x35, 0x0035 }, // 5
        { 0x36, 0x0036 }, // 6
        { 0x37, 0x0037 }, // 7
        { 0x38, 0x0038 }, // 8
        { 0x39, 0x0039 }, // 9

        // --- F-keys F1-F24 (VK 0x70-0x87 -> XK_F1-XK_F24) ---
        { 0x70, 0xFFBE }, // F1
        { 0x71, 0xFFBF }, // F2
        { 0x72, 0xFFC0 }, // F3
        { 0x73, 0xFFC1 }, // F4
        { 0x74, 0xFFC2 }, // F5
        { 0x75, 0xFFC3 }, // F6
        { 0x76, 0xFFC4 }, // F7
        { 0x77, 0xFFC5 }, // F8
        { 0x78, 0xFFC6 }, // F9
        { 0x79, 0xFFC7 }, // F10
        { 0x7A, 0xFFC8 }, // F11
        { 0x7B, 0xFFC9 }, // F12
        { 0x7C, 0xFFCA }, // F13
        { 0x7D, 0xFFCB }, // F14
        { 0x7E, 0xFFCC }, // F15
        { 0x7F, 0xFFCD }, // F16
        { 0x80, 0xFFCE }, // F17
        { 0x81, 0xFFCF }, // F18
        { 0x82, 0xFFD0 }, // F19
        { 0x83, 0xFFD1 }, // F20
        { 0x84, 0xFFD2 }, // F21
        { 0x85, 0xFFD3 }, // F22
        { 0x86, 0xFFD4 }, // F23
        { 0x87, 0xFFD5 }, // F24

        // --- Arrow keys ---
        { 0x25, 0xFF51 }, // Left
        { 0x26, 0xFF52 }, // Up
        { 0x27, 0xFF53 }, // Right
        { 0x28, 0xFF54 }, // Down

        // --- Modifiers (11 entries) ---
        { 0x10, 0xFFE1 }, // VK_SHIFT (generic) -> Shift_L
        { 0x11, 0xFFE3 }, // VK_CONTROL (generic) -> Control_L
        { 0x12, 0xFFE9 }, // VK_MENU (generic Alt) -> Alt_L
        { 0xA0, 0xFFE1 }, // VK_LSHIFT -> Shift_L
        { 0xA1, 0xFFE2 }, // VK_RSHIFT -> Shift_R
        { 0xA2, 0xFFE3 }, // VK_LCONTROL -> Control_L
        { 0xA3, 0xFFE4 }, // VK_RCONTROL -> Control_R
        { 0xA4, 0xFFE9 }, // VK_LMENU -> Alt_L
        { 0xA5, 0xFFEA }, // VK_RMENU -> Alt_R
        { 0x5B, 0xFFEB }, // VK_LWIN -> Super_L
        { 0x5C, 0xFFEC }, // VK_RWIN -> Super_R

        // --- Navigation (6 entries) ---
        { 0x24, 0xFF50 }, // Home
        { 0x23, 0xFF57 }, // End
        { 0x21, 0xFF55 }, // Page_Up
        { 0x22, 0xFF56 }, // Page_Down
        { 0x2D, 0xFF63 }, // Insert
        { 0x2E, 0xFFFF }, // Delete

        // --- Special keys (11 entries) ---
        { 0x1B, 0xFF1B }, // Escape
        { 0x0D, 0xFF0D }, // Return
        { 0x09, 0xFF09 }, // Tab
        { 0x08, 0xFF08 }, // BackSpace
        { 0x20, 0x0020 }, // space
        { 0x14, 0xFFE5 }, // Caps_Lock
        { 0x2C, 0xFF61 }, // Print (VK_SNAPSHOT)
        { 0x13, 0xFF13 }, // Pause
        { 0x91, 0xFF14 }, // Scroll_Lock
        { 0x90, 0xFF7F }, // Num_Lock
        { 0x5D, 0xFF67 }, // Menu / ContextMenu

        // --- Numpad digits 0-9 (VK 0x60-0x69 -> XK_KP_0-XK_KP_9) ---
        { 0x60, 0xFFB0 }, // KP_0
        { 0x61, 0xFFB1 }, // KP_1
        { 0x62, 0xFFB2 }, // KP_2
        { 0x63, 0xFFB3 }, // KP_3
        { 0x64, 0xFFB4 }, // KP_4
        { 0x65, 0xFFB5 }, // KP_5
        { 0x66, 0xFFB6 }, // KP_6
        { 0x67, 0xFFB7 }, // KP_7
        { 0x68, 0xFFB8 }, // KP_8
        { 0x69, 0xFFB9 }, // KP_9

        // --- Numpad operators (5 entries) ---
        { 0x6A, 0xFFAA }, // KP_Multiply
        { 0x6B, 0xFFAB }, // KP_Add
        { 0x6D, 0xFFAD }, // KP_Subtract
        { 0x6E, 0xFFAE }, // KP_Decimal
        { 0x6F, 0xFFAF }, // KP_Divide

        // --- Punctuation (11 entries, decimal VK codes from browser keyCode) ---
        { 186, 0x003B }, // semicolon
        { 187, 0x003D }, // equal
        { 188, 0x002C }, // comma
        { 189, 0x002D }, // minus
        { 190, 0x002E }, // period
        { 191, 0x002F }, // slash
        { 192, 0x0060 }, // grave accent
        { 219, 0x005B }, // bracketleft
        { 220, 0x005C }, // backslash
        { 221, 0x005D }, // bracketright
        { 222, 0x0027 }, // apostrophe
    };

    /// <summary>
    /// Attempts to translate a Windows VK code to an X11 keysym.
    /// Returns false for unmapped VK codes (caller should log and drop).
    /// </summary>
    public static bool TryGetKeysym(ushort vk, out ulong keysym) => Map.TryGetValue(vk, out keysym);
}
