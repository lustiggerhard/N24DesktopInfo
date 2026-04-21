using System;
using System.Runtime.InteropServices;

namespace N24DesktopInfo
{
    internal static class NativeMethods
    {
        // === Window styles ===
        public const int GWL_EXSTYLE = -20;
        public const int WS_EX_TRANSPARENT   = 0x00000020;
        public const int WS_EX_TOOLWINDOW    = 0x00000080;
        public const int WS_EX_NOACTIVATE    = 0x08000000;
        public const int WS_EX_APPWINDOW     = 0x00040000;

        public static readonly IntPtr HWND_BOTTOM = new(1);
        public const uint SWP_NOSIZE          = 0x0001;
        public const uint SWP_NOMOVE          = 0x0002;
        public const uint SWP_NOACTIVATE      = 0x0010;
        public const uint SWP_NOSENDCHANGING  = 0x0400;

        public const int WM_WINDOWPOSCHANGING = 0x0046;
        public const int WM_DISPLAYCHANGE     = 0x007E;
        public const int WM_SETTINGCHANGE     = 0x001A;

        [StructLayout(LayoutKind.Sequential)]
        public struct WINDOWPOS
        {
            public IntPtr hwnd, hwndInsertAfter;
            public int x, y, cx, cy;
            public uint flags;
        }

        [DllImport("user32.dll")] public static extern int  GetWindowLong(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll")] public static extern int  SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
        [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        // === Memory ===
        [StructLayout(LayoutKind.Sequential)]
        public struct MEMORYSTATUSEX
        {
            public uint dwLength, dwMemoryLoad;
            public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile;
            public ulong ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
        }

        [DllImport("kernel32.dll")] public static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

        // === CPU ===
        [DllImport("kernel32.dll")] public static extern bool GetSystemTimes(out long idle, out long kernel, out long user);

        // === Icon cleanup ===
        [DllImport("user32.dll")] public static extern bool DestroyIcon(IntPtr handle);

        // === Network Traffic (v1 API - GetIfTable/MIB_IFROW) ===
        // This is the SAME API that netlib.dll (Network Meter gadget) uses.
        // It works correctly on vmxnet3 where the v2 API (GetIfEntry2)
        // and ALL WMI performance counters return broken values.
        [DllImport("iphlpapi.dll")] public static extern int GetIfTable(byte[] pIfTable, ref int pdwSize, bool bOrder);

        // MIB_IFROW layout (860 bytes, stable since Windows 2000):
        //   0..511   WCHAR[256] wszName
        //   512      DWORD dwIndex
        //   516      DWORD dwType
        //   520      DWORD dwMtu
        //   524      DWORD dwSpeed
        //   528      DWORD dwPhysAddrLen
        //   532      BYTE[8] bPhysAddr  (MAXLEN_PHYSADDR=8)
        //   540      DWORD dwAdminStatus
        //   544      DWORD dwOperStatus
        //   548      DWORD dwLastChange
        //   552      DWORD dwInOctets
        //   556      DWORD dwInUcastPkts
        //   560      DWORD dwInNUcastPkts
        //   564      DWORD dwInDiscards
        //   568      DWORD dwInErrors
        //   572      DWORD dwInUnknownProtos
        //   576      DWORD dwOutOctets
        //   580      DWORD dwOutUcastPkts
        //   584      DWORD dwOutNUcastPkts
        //   588      DWORD dwOutDiscards
        //   592      DWORD dwOutErrors
        //   596      DWORD dwOutQLen
        //   600      DWORD dwDescrLen
        //   604      BYTE[256] bDescr   (MAXLEN_IFDESCR=256)
        // Total: 860 bytes per row
        public const int MIB_IFROW_SIZE = 860;
        public const int IFROW_OFFSET_TYPE = 516;
        public const int IFROW_OFFSET_SPEED = 524;
        public const int IFROW_OFFSET_OPERSTATUS = 544;
        public const int IFROW_OFFSET_IN_OCTETS = 552;
        public const int IFROW_OFFSET_OUT_OCTETS = 576;
        public const int IFROW_OFFSET_DESCRLEN = 600;
        public const int IFROW_OFFSET_DESCR = 604;

        // Adapter types to include
        public const uint IF_TYPE_ETHERNET_CSMACD = 6;
        public const uint IF_TYPE_IEEE80211 = 71;       // WiFi
        public const uint IF_TYPE_PROP_VIRTUAL = 53;     // vmxnet3 may report this
        public const uint IF_TYPE_OTHER = 1;

        // OperStatus v1: 1=non-operational, 2..5=various connected states
        // Connected if >= 2 (unlike v2 where Up=1)
        public const int ERROR_INSUFFICIENT_BUFFER = 122;
    }
}
