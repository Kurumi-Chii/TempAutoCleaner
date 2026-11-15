using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace TempAutoCleaner.Tests
{
    internal sealed class FixtureUnavailableException : Exception
    {
        internal FixtureUnavailableException(string message) : base(message) { }

        internal FixtureUnavailableException(string operation, int errorCode)
            : base(operation + " could not create its fixture (Windows error " + errorCode + ").")
        {
        }
    }

    internal static class FixtureLinks
    {
        private const uint IoReparseTagMountPoint = 0xA0000003;
        private const uint FsctlSetReparsePoint = 0x000900A4;
        private const uint FsctlSetSparse = 0x000900C4;
        private const uint GenericWrite = 0x40000000;
        private const uint OpenExisting = 3;
        private const uint BackupSemantics = 0x02000000;
        private const uint OpenReparsePoint = 0x00200000;
        private const uint AllowUnprivilegedCreate = 2;

        internal static void CreateHardLink(string path, string target)
        {
            if (!CreateHardLinkW(path, target, IntPtr.Zero))
                ThrowFixtureError("Hard link", Marshal.GetLastWin32Error());
        }

        internal static void MarkSparse(SafeFileHandle file)
        {
            if (!DeviceIoControl(file, FsctlSetSparse, null, 0, IntPtr.Zero, 0, out _, IntPtr.Zero))
                ThrowFixtureError("Sparse file", Marshal.GetLastWin32Error());
        }

        internal static void CreateFileSymbolicLink(string path, string target)
        {
            if (CreateSymbolicLinkW(path, target, AllowUnprivilegedCreate))
                return;

            int error = Marshal.GetLastWin32Error();
            if (error == 87)
            {
                if (CreateSymbolicLinkW(path, target, 0))
                    return;

                error = Marshal.GetLastWin32Error();
            }

            ThrowFixtureError("Symbolic link", error);
        }

        internal static void CreateJunction(string path, string target)
        {
            Directory.CreateDirectory(path);
            using (SafeFileHandle handle = CreateFileW(path, GenericWrite, 0, IntPtr.Zero, OpenExisting,
                BackupSemantics | OpenReparsePoint, IntPtr.Zero))
            {
                if (handle.IsInvalid)
                    ThrowFixtureError("Junction handle", Marshal.GetLastWin32Error());

                byte[] buffer = JunctionData(target);
                if (!DeviceIoControl(handle, FsctlSetReparsePoint, buffer, (uint)buffer.Length,
                    IntPtr.Zero, 0, out _, IntPtr.Zero))
                    ThrowFixtureError("Junction", Marshal.GetLastWin32Error());
            }
        }

        private static byte[] JunctionData(string target)
        {
            byte[] substituteName = Encoding.Unicode.GetBytes(@"\??\" + target);
            byte[] printName = Encoding.Unicode.GetBytes(target);
            int printNameOffset = substituteName.Length + sizeof(char);
            int pathBufferLength = printNameOffset + printName.Length + sizeof(char);

            using (var bytes = new MemoryStream())
            using (var writer = new BinaryWriter(bytes))
            {
                writer.Write(IoReparseTagMountPoint);
                writer.Write(checked((ushort)(8 + pathBufferLength)));
                writer.Write((ushort)0); // Reserved
                writer.Write((ushort)0); // SubstituteNameOffset
                writer.Write(checked((ushort)substituteName.Length));
                writer.Write(checked((ushort)printNameOffset));
                writer.Write(checked((ushort)printName.Length));
                writer.Write(substituteName);
                writer.Write((ushort)0);
                writer.Write(printName);
                writer.Write((ushort)0);
                return bytes.ToArray();
            }
        }

        private static void ThrowFixtureError(string operation, int error)
        {
            if (error == 1 || error == 5 || error == 50 || error == 1314)
                throw new FixtureUnavailableException(operation, error);

            throw new Win32Exception(error, operation + " fixture failed with Windows error " + error + ".");
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CreateHardLinkW(string fileName, string existingFileName, IntPtr securityAttributes);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.U1)]
        private static extern bool CreateSymbolicLinkW(string linkName, string targetName, uint flags);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
        private static extern SafeFileHandle CreateFileW(string name, uint desiredAccess, uint shareMode,
            IntPtr securityAttributes, uint creationDisposition, uint flags, IntPtr template);

        [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DeviceIoControl(SafeFileHandle handle, uint code, byte[] input, uint inputLength,
            IntPtr output, uint outputLength, out uint bytesReturned, IntPtr overlapped);
    }
}
