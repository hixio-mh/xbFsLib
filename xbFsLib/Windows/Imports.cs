using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace xbFsLib.Windows
{
    public static class Imports
    {
        [DllImport("Shell32.dll", SetLastError = true)]
        public static extern int SHChangeNotify(int wEventId, int uFlags, int dwItem1, int dwItem2);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern long ReleaseCapture();

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern SafeFileHandle CreateFile(string lpFileName,
            FileAccess dwDesiredAccess, FileShare dwShareMode,
            IntPtr lpSecurityAttributes,
            CreationDisposition dwCreationDisposition,
            FileAttributes dwFlagsAndAttributes, IntPtr hTemplateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool CloseHandle(SafeHandle hObject);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool DeviceIoControl(SafeHandle hDevice, uint dwIoControlCode,
            IntPtr lpInBuffer, uint nInBufferSize, ref DiskGeometry lpOutBuffer,
            uint nOutBufferSize, out uint lpBytesReturned, IntPtr lpOverlapped);

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        public static extern int SetWindowTheme(IntPtr hWnd, string appName, string partList);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern IntPtr SendMessage
                    (IntPtr hWnd, int msg, int wParam, int lParam);

        [DllImport("kernel32", SetLastError = true)]
        public static extern bool FlushFileBuffers(IntPtr handle);

        [DllImport("shlwapi.dll")]
        static extern bool PathCompactPathEx([Out] StringBuilder pszOut, string szPath, int cchMax, int dwFlags);

        [DllImport("kernel32.dll")]
        public static extern uint GetFileSize(SafeHandle hFile, ref uint lpFileSizeHigh);

        [DllImport("dwmapi.dll", CallingConvention = CallingConvention.Winapi, CharSet = CharSet.Auto)]
        public static extern int DwmExtendFrameIntoClientArea(IntPtr hWnd, ref Margins pMarinset);

        [DllImport("dwmapi.dll", CallingConvention = CallingConvention.Winapi, CharSet = CharSet.Auto)]
        public static extern void DwmIsCompositionEnabled(ref int enabledptr);

        [DllImport("user32.dll")]
        public static extern int SetWindowText(IntPtr hWnd, string text);

        public enum FileAccess : uint
        {
            GenericRead = 0x80000000,
            GenericWrite = 0x40000000,
            GenericExecute = 0x20000000,
            GenericAll = 0x10000000
        }

        public enum FileShare : uint
        {
            None = 0x00000000,
            Read = 0x00000001,
            Write = 0x00000002,
            Delete = 0x00000004,
            All = Read | Write | Delete
        }

        public enum CreationDisposition : uint
        {
            New = 1,
            CreateAlways = 2,
            OpenExisting = 3,
            OpenAlways = 4,
            TruncateExisting = 5
        }

        public struct Margins
        {
            public int cxLeftWidth;
            public int cxRightWidth;
            public int cyTopHeight;
            public int cyBottomHeight;
        }

        public enum FileAttributes : uint
        {
            Readonly = 0x00000001,
            Hidden = 0x00000002,
            System = 0x00000004,
            Directory = 0x00000010,
            Archive = 0x00000020,
            Device = 0x00000040,
            Normal = 0x00000080,
            Temporary = 0x00000100,
            SparseFile = 0x00000200,
            ReparsePoint = 0x00000400,
            Compressed = 0x00000800,
            Offline = 0x00001000,
            NotContentIndexed = 0x00002000,
            Encrypted = 0x00004000,
            WriteThrough = 0x80000000,
            Overlapped = 0x40000000,
            NoBuffering = 0x20000000,
            RandomAccess = 0x10000000,
            SequentialScan = 0x08000000,
            DeleteOnClose = 0x04000000,
            BackupSemantics = 0x02000000,
            PosixSemantics = 0x01000000,
            OpenReparsePoint = 0x00200000,
            OpenNoRecall = 0x00100000,
            FirstPipeInstance = 0x00080000
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct DiskGeometry
        {
            private long cylinders;
            private uint mediaType;
            private uint tracksPerCylinder;
            private uint sectorsPerTrack;
            private uint bytesPerSector;


            public long DiskSize
            {
                get
                {
                    return cylinders * tracksPerCylinder *
                        sectorsPerTrack * bytesPerSector;
                }
            }
        }

        public static long GetDriveSize(SafeFileHandle sfh)
        {
            uint sizeHigh = 0;
            uint sizeLow = GetFileSize(sfh, ref sizeHigh);
            if (sizeLow != 0xFFFFFFFF)
                return sizeHigh | sizeLow;

            DiskGeometry geo = new DiskGeometry();
            uint returnedBytes;
            DeviceIoControl(sfh, 0x70000, IntPtr.Zero, 0, ref geo,
                (uint)Marshal.SizeOf(typeof(DiskGeometry)),
                out returnedBytes, IntPtr.Zero);
            return geo.DiskSize;
        }

        public static string TruncatePath(string path, int length)
        {
            StringBuilder sb = new StringBuilder();
            PathCompactPathEx(sb, path, length, 0);
            return sb.ToString();
        }

    }
}