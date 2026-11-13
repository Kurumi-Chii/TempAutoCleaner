using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace TempAutoCleaner
{
    internal static class NativeFiles
    {
        internal const uint ListDirectory = 0x0001;
        internal const uint WriteData = 0x0002;
        internal const uint Traverse = 0x0020;
        internal const uint ReadAttributes = 0x0080;
        internal const uint Delete = 0x00010000;
        private const uint Synchronize = 0x00100000;
        internal const uint ShareReadWrite = 0x0003;
        internal const uint Directory = 0x0001;
        internal const uint NonDirectory = 0x0040;
        internal const uint OpenExisting = 1;
        internal const uint CreateNew = 2;
        internal const uint OpenOrCreate = 3;
        internal const int NoMoreFiles = 18;
        internal const int InvalidData = 13;
        internal const int NotSupported = 50;
        internal const int InvalidName = 123;
        internal const int ReparsePointEncountered = 4395;
        private const uint SynchronousIo = 0x0020;
        private const uint OpenReparsePoint = 0x00200000;
        private const uint BackupSemantics = 0x02000000;
        private const uint CaseInsensitive = 0x0040;
        private const uint DontReparse = 0x1000;
        private const FileAttributes ProtectedAttributes = FileAttributes.ReparsePoint |
            FileAttributes.ReadOnly | FileAttributes.System | FileAttributes.Device | FileAttributes.Offline |
            (FileAttributes)0x00440000; // RECALL_ON_DATA_ACCESS | RECALL_ON_OPEN

        internal static bool IsProtected(FileAttributes attributes) => (attributes & ProtectedAttributes) != 0;

        internal static SafeFileHandle OpenVolumeRoot(string drive)
        {
            if (GetDriveTypeW(drive) != 3) // DRIVE_FIXED
                throw new Win32Exception(NotSupported);

            var handle = CreateFileW(@"\\?\" + drive, ReadAttributes | Traverse, ShareReadWrite,
                IntPtr.Zero, 3, BackupSemantics | OpenReparsePoint, IntPtr.Zero);
            try
            {
                if (handle.IsInvalid)
                    throw LastError();

                var filesystem = new StringBuilder(32);
                if (!GetVolumeInformationByHandleW(handle, null, 0, out _, out _, out _, filesystem, 32))
                    throw LastError();
                if (!string.Equals(filesystem.ToString(), "NTFS", StringComparison.OrdinalIgnoreCase))
                    throw new Win32Exception(NotSupported);

                // A SUBST drive may report NTFS yet resolve to a subdirectory of the volume.
                var path = new StringBuilder(64);
                uint length = GetFinalPathNameByHandleW(handle, path, (uint)path.Capacity, 1); // VOLUME_NAME_GUID
                if (length == 0)
                    throw LastError();
                const string prefix = @"\\?\Volume";
                if (length >= path.Capacity || length != prefix.Length + 39 ||
                    !path.ToString().StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
                    path[(int)length - 1] != '\\' ||
                    !Guid.TryParseExact(path.ToString(prefix.Length, 38), "B", out _))
                    throw new Win32Exception(NotSupported);
                if (!ReadInfo(handle).IsOrdinaryDirectory)
                    throw new Win32Exception(InvalidData);
                return handle;
            }
            catch
            {
                handle.Dispose();
                throw;
            }
        }

        internal static unsafe SafeFileHandle OpenChild(SafeFileHandle parent, string name, uint access,
            uint share, uint options, uint disposition = OpenExisting)
        {
            if (!WindowsDirectory.IsSafeComponent(name))
                throw new Win32Exception(InvalidName);

            fixed (char* chars = name)
            {
                var unicodeName = new UnicodeString
                {
                    Length = (ushort)(name.Length * sizeof(char)),
                    MaximumLength = (ushort)(name.Length * sizeof(char)),
                    Buffer = (IntPtr)chars
                };
                bool referenceAdded = false;
                try
                {
                    parent.DangerousAddRef(ref referenceAdded);
                    var attributes = new ObjectAttributes
                    {
                        Length = (uint)Marshal.SizeOf<ObjectAttributes>(),
                        RootDirectory = parent.DangerousGetHandle(),
                        ObjectName = (IntPtr)(&unicodeName),
                        Attributes = CaseInsensitive | DontReparse
                    };
                    int status = NtCreateFile(out IntPtr file, access | Synchronize, ref attributes,
                        out _, IntPtr.Zero, 0, share, disposition,
                        options | OpenReparsePoint | SynchronousIo, IntPtr.Zero, 0);
                    if (status < 0)
                        throw new Win32Exception((int)RtlNtStatusToDosError(status));
                    var handle = new SafeFileHandle(file, true);
                    if (handle.IsInvalid)
                    {
                        handle.Dispose();
                        throw new Win32Exception(InvalidData);
                    }
                    return handle;
                }
                finally
                {
                    if (referenceAdded)
                        parent.DangerousRelease();
                }
            }
        }

        internal static FileInfoByHandle ReadInfo(SafeFileHandle handle)
        {
            if (!GetFileInformationByHandle(handle, out FileInfoByHandle info))
                throw LastError();
            return info;
        }

        internal static void DeleteOnClose(SafeFileHandle handle)
        {
            byte delete = 1;
            if (!SetFileInformationByHandle(handle, 4, ref delete, 1))
                throw LastError();
        }

        internal static unsafe bool HasOnlyDefaultStream(SafeFileHandle handle)
        {
            // FILE_STREAM_INFO has a 24-byte header followed by a UTF-16 stream name.
            // Multiple streams or an oversized response make this object ineligible.
            const string defaultStream = "::$DATA";
            const int headerSize = 24;
            const int nameSize = sizeof(char) * 7;
            byte* buffer = stackalloc byte[128];
            if (!GetFileInformationByHandleEx(handle, 7, (IntPtr)buffer, 128)) // FileStreamInfo
            {
                int error = Marshal.GetLastWin32Error();
                if (error == 38 || error == 122 || error == 234) // EOF / insufficient buffer / more data
                    return false;
                throw new Win32Exception(error);
            }
            return *(int*)buffer == 0 && *(int*)(buffer + 4) == nameSize &&
                new string((char*)(buffer + headerSize), 0, nameSize / sizeof(char)) == defaultStream;
        }

        internal static unsafe void RenameInSameDirectory(SafeFileHandle handle, string name)
        {
            if (!WindowsDirectory.IsSafeComponent(name))
                throw new Win32Exception(InvalidName);

            // FILE_RENAME_INFORMATION on x64. Zero means no replacement and no RootDirectory.
            const int nameOffset = 20;
            int size = 24 + name.Length * sizeof(char);
            byte* info = stackalloc byte[size];
            for (int i = 0; i < size; i++)
                info[i] = 0;
            *(uint*)(info + 16) = (uint)(name.Length * sizeof(char));
            for (int i = 0; i < name.Length; i++)
                ((char*)(info + nameOffset))[i] = name[i];
            int status = NtSetInformationFile(handle, out _, (IntPtr)info, (uint)size, 10);
            if (status < 0)
                throw new Win32Exception((int)RtlNtStatusToDosError(status));
        }

        internal static Win32Exception LastError()
        {
            return new Win32Exception(Marshal.GetLastWin32Error());
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct UnicodeString
        {
            internal ushort Length;
            internal ushort MaximumLength;
            internal IntPtr Buffer;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ObjectAttributes
        {
            internal uint Length;
            internal IntPtr RootDirectory;
            internal IntPtr ObjectName;
            internal uint Attributes;
            internal IntPtr SecurityDescriptor;
            internal IntPtr SecurityQualityOfService;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct IoStatusBlock
        {
            internal IntPtr Status;
            internal UIntPtr Information;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct FileInfoByHandle
        {
            internal FileAttributes Attributes;
            private uint creationLow, creationHigh;
            private uint accessLow, accessHigh;
            private uint writeLow, writeHigh;
            internal uint VolumeSerial;
            private uint sizeHigh, sizeLow;
            internal uint Links;
            private uint idHigh, idLow;

            internal long Created => Combine(creationHigh, creationLow);
            internal long LastWrite => Combine(writeHigh, writeLow);
            internal long Size => Combine(sizeHigh, sizeLow);
            internal long Id => Combine(idHigh, idLow);
            internal bool IsOrdinaryDirectory =>
                (Attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) == FileAttributes.Directory;

            private static long Combine(uint high, uint low) => ((long)high << 32) | low;
        }

        [DllImport("ntdll.dll")]
        private static extern int NtCreateFile(out IntPtr file, uint access, ref ObjectAttributes attributes,
            out IoStatusBlock status, IntPtr allocationSize, uint fileAttributes, uint share,
            uint disposition, uint options, IntPtr eaBuffer, uint eaLength);

        [DllImport("ntdll.dll")]
        private static extern uint RtlNtStatusToDosError(int status);

        [DllImport("ntdll.dll")]
        private static extern int NtSetInformationFile(SafeFileHandle file, out IoStatusBlock status,
            IntPtr information, uint length, int infoClass);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
        private static extern SafeFileHandle CreateFileW(string path, uint access, uint share,
            IntPtr attributes, uint disposition, uint flags, IntPtr template);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern uint GetDriveTypeW(string root);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
        private static extern bool GetVolumeInformationByHandleW(SafeFileHandle file, StringBuilder volumeName,
            uint volumeNameSize, out uint volumeSerial, out uint maxComponentLength, out uint flags,
            StringBuilder filesystem, uint filesystemSize);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
        private static extern uint GetFinalPathNameByHandleW(SafeFileHandle file, StringBuilder path, uint size, uint flags);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetFileInformationByHandle(SafeFileHandle file, out FileInfoByHandle info);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern bool GetFileInformationByHandleEx(SafeFileHandle file, int infoClass,
            IntPtr buffer, uint bufferSize);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetFileInformationByHandle(SafeFileHandle file, int infoClass, ref byte info, uint size);
    }
}
