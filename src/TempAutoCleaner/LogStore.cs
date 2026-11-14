using System;
using System.ComponentModel;
using System.IO;
using Microsoft.Win32.SafeHandles;
using static TempAutoCleaner.NativeFiles;

namespace TempAutoCleaner
{
    internal sealed class LogStore : IDisposable
    {
        private const string NamePrefix = "run-";
        private const string Extension = ".json";
        private const string PendingExtension = ".pending";
        private readonly WindowsDirectory directory;

        private LogStore(WindowsDirectory directory) => this.directory = directory;

        internal static LogStore Open(string localAppData)
        {
            WindowsDirectory directory = WindowsDirectory.Open(localAppData, 0);
            try
            {
                directory.Append("TempAutoCleaner", 0, create: true);
                directory.Append("logs", ListDirectory, create: true);
                return new LogStore(directory);
            }
            catch
            {
                directory.Dispose();
                throw;
            }
        }

        internal FileStream CreatePendingReport(out string finalName)
        {
            string name = NamePrefix + Guid.NewGuid().ToString("N");
            finalName = name + Extension;
            var handle = OpenChild(directory.Handle, name + PendingExtension,
                WriteData | Delete, 0, NonDirectory, CreateNew);
            try
            {
                return new FileStream(handle, FileAccess.Write);
            }
            catch
            {
                handle.Dispose();
                throw;
            }
        }

        internal void Rotate(DateTimeOffset now, RunReport report)
        {
            if (report.DryRun)
                return;

            long cutoff = now.UtcDateTime.AddDays(-30).ToFileTimeUtc();
            try
            {
                uint volume = ReadInfo(directory.Handle).VolumeSerial;
                foreach (WindowsDirectory.DirectoryEntry entry in WindowsDirectory.ReadEntries(directory.Handle))
                {
                    if (entry.Id == 0 || !IsReportName(entry.Name) ||
                        !Cleaner.IsOld(entry.Created, entry.LastWrite, cutoff) ||
                        !IsOrdinaryFile(entry.Attributes))
                        continue;

                    try
                    {
                        using (SafeFileHandle file = OpenChild(directory.Handle, entry.Name, ReadAttributes | Delete,
                            0, NonDirectory))
                        {
                            if (!IsOrdinaryFile(ReadInfo(file).Attributes) || !HasNoNamedDataStreams(file))
                                continue;
                            FileInfoByHandle current = ReadInfo(file);
                            if (current.Id == entry.Id && current.VolumeSerial == volume && current.Links == 1 &&
                                IsOrdinaryFile(current.Attributes) &&
                                Cleaner.IsOld(current.Created, current.LastWrite, cutoff))
                                DeleteOnClose(file);
                        }
                    }
                    catch (Win32Exception error)
                    {
                        report.RecordError("rotate_report", error.NativeErrorCode);
                    }
                }
            }
            catch (Win32Exception error)
            {
                report.RecordError("enumerate_reports", error.NativeErrorCode);
            }
        }

        private static bool IsReportName(string name) =>
            name.StartsWith(NamePrefix, StringComparison.Ordinal) &&
            (HasExtension(name, Extension) || HasExtension(name, PendingExtension)) &&
            Guid.TryParseExact(name.Substring(NamePrefix.Length, 32), "N", out _);

        private static bool HasExtension(string name, string extension) =>
            name.Length == NamePrefix.Length + 32 + extension.Length &&
            name.EndsWith(extension, StringComparison.Ordinal);

        private static bool IsOrdinaryFile(FileAttributes attributes) =>
            (attributes & FileAttributes.Directory) == 0 && !IsProtected(attributes);

        public void Dispose() => directory.Dispose();
    }
}
