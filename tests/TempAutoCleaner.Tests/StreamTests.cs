using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace TempAutoCleaner.Tests
{
    internal static class StreamTests
    {
        private const uint GenericRead = 0x80000000;
        private const uint GenericWrite = 0x40000000;
        private const uint CreateAlways = 2;
        private const uint OpenExisting = 3;
        private const uint NormalAttributes = 0x80;

        internal static void PreservesFileWithOpenNamedStream(string root)
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            DateTime old = now.UtcDateTime.AddDays(-2);
            string protectedFile = Path.Combine(root, "named-stream.bin");
            string ordinaryFile = Path.Combine(root, "ordinary.bin");
            File.WriteAllText(protectedFile, "primary stream");
            File.WriteAllText(ordinaryFile, "ordinary");

            string streamPath = protectedFile + ":fixture:$DATA";
            using (SafeFileHandle handle = OpenStream(streamPath, GenericWrite, CreateAlways))
            using (var stream = new FileStream(handle, FileAccess.Write))
                stream.WriteByte(0x5A);

            foreach (string file in new[] { protectedFile, ordinaryFile })
            {
                File.SetCreationTimeUtc(file, old);
                File.SetLastWriteTimeUtc(file, old);
            }

            var report = new RunReport(now, dryRun: false);
            using (SafeFileHandle handle = OpenStream(streamPath, GenericRead, OpenExisting))
            using (var held = new FileStream(handle, FileAccess.Read))
            {
                Cleaner.Run(root, now, report);
                if (held.ReadByte() != 0x5A)
                    throw new InvalidOperationException("The contents of the open named stream changed.");
            }

            if (report.DeletedFiles != 1 || report.SkippedFiles != 1 || File.Exists(ordinaryFile) ||
                File.ReadAllText(protectedFile) != "primary stream")
                throw new InvalidOperationException("Cleanup did not preserve the file with an open named stream.");
        }

        private static SafeFileHandle OpenStream(string path, uint access, uint disposition)
        {
            const uint shareReadWriteDelete = 7;
            SafeFileHandle handle = CreateFileW(path, access, shareReadWriteDelete, IntPtr.Zero,
                disposition, NormalAttributes, IntPtr.Zero);
            if (!handle.IsInvalid)
                return handle;

            int error = Marshal.GetLastWin32Error();
            handle.Dispose();
            if (error == 1 || error == 50)
                throw new FixtureUnavailableException("Named stream", error);

            throw new Win32Exception(error, "Could not open the test fixture's named stream.");
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
        private static extern SafeFileHandle CreateFileW(string path, uint access, uint share,
            IntPtr attributes, uint disposition, uint flags, IntPtr template);
    }
}
