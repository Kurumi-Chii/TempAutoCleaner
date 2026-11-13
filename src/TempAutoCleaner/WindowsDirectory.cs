using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace TempAutoCleaner
{
    internal sealed class WindowsDirectory : IDisposable
    {
        private static readonly char[] InvalidNameChars = Path.GetInvalidFileNameChars();
        private readonly List<SafeFileHandle> handles = new List<SafeFileHandle>();

        internal SafeFileHandle Handle => handles[handles.Count - 1];

        internal static bool IsSafeAbsolutePath(string path)
        {
            if (string.IsNullOrEmpty(path) || path.Length < 4 || path.Length > 32766 ||
                !IsDriveLetter(path[0]) || path[1] != ':' || path[2] != '\\')
                return false;

            foreach (string segment in path.Substring(3).Split('\\'))
                if (!IsSafeComponent(segment))
                    return false;
            return true;
        }

        private static bool IsDriveLetter(char letter) =>
            (letter >= 'A' && letter <= 'Z') || (letter >= 'a' && letter <= 'z');

        internal static bool IsSafeComponent(string name) =>
            !string.IsNullOrEmpty(name) && name.Length <= 255 && name != "." && name != ".." &&
            name[name.Length - 1] != '.' && name[name.Length - 1] != ' ' && name.IndexOfAny(InvalidNameChars) < 0;

        // Keep every ancestor open without delete sharing until its descendant handles are closed.
        internal static WindowsDirectory Open(string path, uint finalAccess)
        {
            if (!IsSafeAbsolutePath(path))
                throw new Win32Exception(NativeFiles.InvalidName);

            var chain = new WindowsDirectory();
            try
            {
                chain.handles.Add(NativeFiles.OpenVolumeRoot(path.Substring(0, 3)));
                string[] segments = path.Substring(3).Split('\\');
                for (int i = 0; i < segments.Length; i++)
                    chain.Append(segments[i], i == segments.Length - 1 ? finalAccess : 0);
                return chain;
            }
            catch
            {
                chain.Dispose();
                throw;
            }
        }

        internal void Append(string name, uint access, bool create = false)
        {
            SafeFileHandle handle = NativeFiles.OpenChild(Handle, name,
                access | NativeFiles.Traverse | NativeFiles.ReadAttributes, NativeFiles.ShareReadWrite,
                NativeFiles.Directory, create ? NativeFiles.OpenOrCreate : NativeFiles.OpenExisting);
            try
            {
                if (!NativeFiles.ReadInfo(handle).IsOrdinaryDirectory)
                    throw new Win32Exception(NativeFiles.ReparsePointEncountered);
                handles.Add(handle);
            }
            catch
            {
                handle.Dispose();
                throw;
            }
        }

        internal static IEnumerable<DirectoryEntry> ReadEntries(SafeFileHandle handle)
        {
            const int bufferSize = 64 * 1024;
            const int headerSize = 104; // offsetof(FILE_ID_BOTH_DIR_INFO, FileName)
            IntPtr buffer = Marshal.AllocHGlobal(bufferSize);
            try
            {
                int infoClass = 11; // FileIdBothDirectoryRestartInfo
                while (NativeFiles.GetFileInformationByHandleEx(handle, infoClass, buffer, bufferSize))
                {
                    infoClass = 10; // FileIdBothDirectoryInfo
                    int offset = 0;
                    int next;
                    do
                    {
                        IntPtr entry = IntPtr.Add(buffer, offset);
                        next = Marshal.ReadInt32(entry, 0);
                        int nameSize = Marshal.ReadInt32(entry, 60);
                        int available = next == 0 ? bufferSize - offset : next;
                        if (next < 0 || (next != 0 && ((next & 7) != 0 ||
                            next > bufferSize - offset - headerSize)) || nameSize <= 0 ||
                            (nameSize & 1) != 0 || nameSize > available - headerSize)
                            throw new Win32Exception(NativeFiles.InvalidData);

                        string name = Marshal.PtrToStringUni(IntPtr.Add(entry, headerSize), nameSize / 2);
                        if (name != "." && name != "..")
                        {
                            yield return new DirectoryEntry(name, Marshal.ReadInt64(entry, 96),
                                (FileAttributes)Marshal.ReadInt32(entry, 56),
                                Marshal.ReadInt64(entry, 8), Marshal.ReadInt64(entry, 24));
                        }
                        offset += next;
                    } while (next != 0);
                }

                Win32Exception error = NativeFiles.LastError();
                if (error.NativeErrorCode != NativeFiles.NoMoreFiles)
                    throw error;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        public void Dispose()
        {
            for (int i = handles.Count - 1; i >= 0; i--)
                handles[i].Dispose();
            handles.Clear();
        }

        internal readonly struct DirectoryEntry
        {
            internal readonly string Name;
            internal readonly long Id;
            internal readonly FileAttributes Attributes;
            internal readonly long Created;
            internal readonly long LastWrite;

            internal DirectoryEntry(string name, long id, FileAttributes attributes, long created, long lastWrite)
            {
                Name = name;
                Id = id;
                Attributes = attributes;
                Created = created;
                LastWrite = lastWrite;
            }
        }
    }
}
