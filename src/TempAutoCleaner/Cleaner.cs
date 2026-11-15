using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using Microsoft.Win32.SafeHandles;
using static TempAutoCleaner.NativeFiles;
using static TempAutoCleaner.WindowsDirectory;

namespace TempAutoCleaner
{
    internal static class Cleaner
    {
        private const int MaxDepth = 256;
        internal static void Run(string root, DateTimeOffset now, RunReport report, Action<string> beforeOpen = null)
        {
            try
            {
                using (WindowsDirectory directory = Open(root, ListDirectory))
                {
                    long cutoff = now.UtcDateTime.AddHours(-24).ToFileTimeUtc();
                    Scan(directory.Handle, ReadInfo(directory.Handle).VolumeSerial, cutoff, report, beforeOpen,
                        depth: 0, relativePrefix: report.Inspection == null ? null : string.Empty);
                }
            }
            catch (Win32Exception error)
            {
                report.RecordError("open_root", error.NativeErrorCode, fatal: true);
            }
        }

        internal static bool IsOld(long created, long lastWrite, long cutoff) =>
            created > 0 && lastWrite > 0 && created < cutoff && lastWrite < cutoff;

        private static bool Matches(DirectoryEntry entry, FileInfoByHandle current, uint volume, bool directory) =>
            entry.Id != 0 && current.Id == entry.Id && current.VolumeSerial == volume &&
            ((current.Attributes & FileAttributes.Directory) != 0) == directory && !IsProtected(current.Attributes);

        private static bool Scan(SafeFileHandle parent, uint volume, long cutoff, RunReport report,
            Action<string> beforeOpen, int depth, string relativePrefix)
        {
            try
            {
                foreach (DirectoryEntry entry in ReadEntries(parent))
                {
                    if ((entry.Attributes & FileAttributes.Directory) != 0)
                    {
                        if (!ProcessDirectory(parent, entry, volume, cutoff, report, beforeOpen, depth, relativePrefix))
                            report.SkippedDirectories++;
                    }
                    else
                    {
                        bool matched = report.Inspection == null
                            ? ProcessFile(parent, entry, volume, cutoff, report, beforeOpen)
                            : InspectFile(parent, entry, volume, report, beforeOpen, relativePrefix);
                        if (!matched)
                            report.SkippedFiles++;
                    }
                }
                return true;
            }
            catch (Win32Exception error)
            {
                report.RecordError("enumerate", error.NativeErrorCode, fatal: depth == 0);
                return false;
            }
        }

        private static bool InspectFile(SafeFileHandle parent, DirectoryEntry entry, uint volume,
            RunReport report, Action<string> beforeOpen, string relativePrefix)
        {
            LargeFileInspection inspection = report.Inspection;
            if (!IsSafeComponent(entry.Name) || IsProtected(entry.Attributes) ||
                !inspection.Includes(entry.Size, entry.Created, entry.LastWrite))
                return false;

            try
            {
                beforeOpen?.Invoke(entry.Name);
                using (SafeFileHandle file = OpenChild(parent, entry.Name, ReadAttributes,
                    ShareReadWrite | ShareDelete, NonDirectory))
                {
                    FileInfoByHandle info = ReadInfo(file);
                    if (!Matches(entry, info, volume, false) || info.Links != 1 ||
                        !inspection.Includes(info.Size, info.Created, info.LastWrite))
                        return false;

                    inspection.Record(relativePrefix + entry.Name, info);
                    return true;
                }
            }
            catch (Win32Exception error)
            {
                report.RecordError("inspect_file", error.NativeErrorCode);
                return false;
            }
        }

        private static bool ProcessFile(SafeFileHandle parent, DirectoryEntry entry, uint volume,
            long cutoff, RunReport report, Action<string> beforeOpen)
        {
            if (!IsSafeComponent(entry.Name) || IsProtected(entry.Attributes) ||
                !IsOld(entry.Created, entry.LastWrite, cutoff))
                return false;

            try
            {
                beforeOpen?.Invoke(entry.Name);
                using (SafeFileHandle file = OpenChild(parent, entry.Name, ReadAttributes | Delete, 0, NonDirectory))
                {
                    if (!Matches(entry, ReadInfo(file), volume, false) || !HasNoNamedDataStreams(file))
                        return false;

                    FileInfoByHandle info = ReadInfo(file);
                    if (!Matches(entry, info, volume, false) || info.Links != 1 || info.Size < 0 ||
                        !IsOld(info.Created, info.LastWrite, cutoff))
                        return false;

                    if (!report.DryRun)
                    {
                        DeleteOnClose(file);
                        report.DeletedFiles++;
                        report.EstimatedDeletedBytes += info.Size;
                    }
                    report.EligibleFiles++;
                    report.EligibleBytes += info.Size;
                    return true;
                }
            }
            catch (Win32Exception error)
            {
                report.RecordError("process_file", error.NativeErrorCode);
                return false;
            }
        }

        private static bool ProcessDirectory(SafeFileHandle parent, DirectoryEntry entry, uint volume,
            long cutoff, RunReport report, Action<string> beforeOpen, int depth, string relativePrefix)
        {
            if (!IsSafeComponent(entry.Name) || IsProtected(entry.Attributes))
                return false;
            if (depth >= MaxDepth)
            {
                report.RecordError("directory_depth", NotSupported);
                return false;
            }

            try
            {
                beforeOpen?.Invoke(entry.Name);
                const uint access = ListDirectory | Traverse | ReadAttributes;
                using (SafeFileHandle child = OpenChild(parent, entry.Name, access, ShareReadWrite, NativeFiles.Directory))
                {
                    FileInfoByHandle info = ReadInfo(child);
                    if (!Matches(entry, info, volume, true))
                        return false;
                    string childPrefix = relativePrefix == null ? null : relativePrefix + entry.Name + "\\";
                    if (!Scan(child, volume, cutoff, report, beforeOpen, depth + 1, childPrefix))
                        return false;
                    if (report.Inspection != null)
                        return true;
                    if (!IsOld(entry.Created, entry.LastWrite, cutoff) || ReadEntries(child).Any())
                        return false;
                }

                // Reopen exclusively for the disposition. Verify the snapshot ID after the reopen.
                using (SafeFileHandle child = OpenChild(parent, entry.Name, access | Delete, 0, NativeFiles.Directory))
                {
                    if (!Matches(entry, ReadInfo(child), volume, true) || ReadEntries(child).Any() ||
                        !HasNoNamedDataStreams(child, isDirectory: true))
                        return false;
                    FileInfoByHandle info = ReadInfo(child);
                    if (!Matches(entry, info, volume, true) || !IsOld(info.Created, info.LastWrite, cutoff))
                        return false;

                    if (!report.DryRun)
                    {
                        DeleteOnClose(child); // NTFS rejects nonempty directories, including late arrivals.
                        report.DeletedDirectories++;
                    }
                    report.EligibleDirectories++;
                    return true;
                }
            }
            catch (Win32Exception error)
            {
                report.RecordError("process_directory", error.NativeErrorCode);
                return false;
            }
        }
    }
}
