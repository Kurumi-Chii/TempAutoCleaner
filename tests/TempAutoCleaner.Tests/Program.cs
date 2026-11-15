using System;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;

namespace TempAutoCleaner.Tests
{
    internal static class Program
    {
        private static readonly DateTimeOffset Now = new DateTimeOffset(
            DateTime.UtcNow.Date.AddHours(12), TimeSpan.Zero);

        private static readonly DateTime Old = Now.UtcDateTime.AddDays(-2);

        private static int Main(string[] args)
        {
            if (args.Length == 2 && args[0] == ProcessTests.ProbeFlag)
                return ProcessTests.RunChild(args[1]);

            bool strict = args.Length == 2 && args[0] == "--strict";
            if (!strict && (args.Length != 1 || args[0].StartsWith("--", StringComparison.Ordinal)))
            {
                Console.Error.WriteLine("Usage: TempAutoCleaner.Tests.exe [--strict] <existing isolated sandbox parent>");
                return 2;
            }

            try
            {
                using (var sandbox = new TestSandbox(args[strict ? 1 : 0]))
                {
                    var tests = new Action<string>[]
                    {
                        ChecksBothTimes,
                        SkipsLockedAndReadOnlyFiles,
                        SkipsFilesWithDeniedAccess,
                        RecursesAndRespectsDirectoryAge,
                        SkipsOccupiedEmptyDirectory,
                        DryRunPreservesTargets,
                        RefusesInvalidRoots,
                        SkipsJunctions,
                        SkipsSymbolicLinks,
                        SkipsHardLinks,
                        PreservesReplacedFileWithNewTime,
                        PreservesReplacedFileWithSameTimes,
                        PreservesTargetWhenDirectoryIsReplaced,
                        PreservesOrdinaryReplacementDirectory,
                        PreservesFileChangedAfterSnapshot,
                        ContinuesAfterFileDisappears,
                        EnumeratesAcrossBatchesWhileDeleting,
                        StreamTests.PreservesFileWithOpenNamedStream,
                        StreamTests.PreservesDirectoryWithNamedStream,
                        ProcessTests.PreventsConcurrentProcess,
                        InspectionTests.InspectionReportsOnlyMatchingFiles,
                        InspectionTests.InspectionLimitsReportToLargestFiles,
                        LoggingTests.DryRunWritesReportWithoutRotating,
                        LoggingTests.CleanupRotatesOnlyExpiredOwnedReports,
                        LoggingTests.RejectsJunctionAtLogDestination
                    };

                    int passed = 0;
                    int skipped = 0;
                    int failed = 0;
                    foreach (Action<string> test in tests)
                    {
                        string testDirectory = Directory.CreateDirectory(
                            Path.Combine(sandbox.Root, test.Method.Name)).FullName;

                        try
                        {
                            test(testDirectory);
                            passed++;
                            Console.WriteLine("PASS " + test.Method.Name);
                        }
                        catch (FixtureUnavailableException ex)
                        {
                            skipped++;
                            Console.WriteLine("SKIP " + test.Method.Name + " - " + ex.Message);
                        }
                        catch (Exception ex)
                        {
                            failed++;
                            Console.Error.WriteLine("FAIL " + test.Method.Name + " - " + ex);
                        }
                    }

                    Console.WriteLine("Passed: {0}, skipped: {1}, failed: {2}", passed, skipped, failed);
                    if (strict && (skipped > 0 || passed == 0))
                    {
                        Console.Error.WriteLine("Strict mode requires all tests to run.");
                        return 1;
                    }
                    return failed == 0 ? 0 : 1;
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
                return 2;
            }
        }

        private static void ChecksBothTimes(string root)
        {
            DateTime cutoff = Now.UtcDateTime.AddHours(-24);
            string old = PutFile(root, "已过期-🐱.bin", "old", cutoff.AddSeconds(-1), cutoff.AddSeconds(-1));
            string newCreation = PutFile(root, "new-creation", "keep", cutoff.AddSeconds(1), Old);
            string newWrite = PutFile(root, "new-write", "keep", Old, cutoff.AddSeconds(1));
            string exactCreation = PutFile(root, "exact-creation", "keep", cutoff, Old);
            string exactWrite = PutFile(root, "exact-write", "keep", Old, cutoff);
            string future = PutFile(root, "future", "keep", Old, Now.UtcDateTime.AddDays(1));

            RunReport report = Run(root);

            Assert(!File.Exists(old), "An old Unicode file survived.");
            foreach (string retained in new[] { newCreation, newWrite, exactCreation, exactWrite, future })
                Assert(File.ReadAllText(retained) == "keep", "A file outside the age rule was changed.");
            Assert(report.DeletedFiles == 1 && report.EstimatedDeletedBytes == 3,
                "Deleted file count or estimated bytes are incorrect.");
            Assert(report.SkippedFiles == 5, "The five retained files should be counted as skipped.");
        }

        private static void SkipsLockedAndReadOnlyFiles(string root)
        {
            string readOnly = PutOldFile(root, "read-only");
            File.SetAttributes(readOnly, File.GetAttributes(readOnly) | FileAttributes.ReadOnly);
            string exclusive = PutOldFile(root, "exclusive-lock");
            string permitsDelete = PutOldFile(root, "shared-read-handle");
            string ordinary = PutOldFile(root, "ordinary");

            using (File.Open(exclusive, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            using (File.Open(permitsDelete, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                RunReport report = Run(root);
                Assert(report.DeletedFiles == 1 && !File.Exists(ordinary), "Cleanup stopped at a protected file.");
                Assert(report.SkippedFiles >= 3, "Protected files did not all count as skipped.");
                Assert(File.Exists(permitsDelete), "A file with an open read handle was marked for deletion.");
            }

            Assert(File.Exists(exclusive) && File.Exists(permitsDelete) && File.Exists(readOnly),
                "A locked or read-only file was removed.");
            Assert((File.GetAttributes(readOnly) & FileAttributes.ReadOnly) != 0,
                "Cleanup cleared a read-only flag.");
        }

        private static void RecursesAndRespectsDirectoryAge(string root)
        {
            string oldEmpty = PutDirectory(root, "旧空目录", Old, Old);
            string newCreation = PutDirectory(root, "new-creation", Now.UtcDateTime, Old);
            string newWrite = PutDirectory(root, "new-write", Old, Now.UtcDateTime);
            string holder = PutDirectory(root, "holder", Old, Old);
            string oldChild = PutOldFile(holder, "old-child");
            string newChild = PutFile(holder, "new-child", "keep", Old, Now.UtcDateTime);
            SetDirectoryTimes(holder, Old, Old);
            SetDirectoryTimes(root, Old, Old);

            RunReport report = Run(root);

            Assert(!Directory.Exists(oldEmpty) && report.DeletedDirectories >= 1, "An old empty directory survived.");
            Assert(Directory.Exists(newCreation) && Directory.Exists(newWrite), "A recent empty directory was deleted.");
            Assert(Directory.Exists(root) && Directory.Exists(holder), "Root or a nonempty directory was deleted.");
            Assert(!File.Exists(oldChild) && File.ReadAllText(newChild) == "keep", "Recursive cleanup violated the age rule.");
        }

        private static void SkipsFilesWithDeniedAccess(string root)
        {
            string denied = PutOldFile(root, "denied");
            string ordinary = PutOldFile(root, "ordinary");
            DirectorySecurity originalParent = Directory.GetAccessControl(root, AccessControlSections.Access);
            FileSecurity originalFile = File.GetAccessControl(denied, AccessControlSections.Access);
            DirectorySecurity parent = Directory.GetAccessControl(root, AccessControlSections.Access);
            FileSecurity restrictive = File.GetAccessControl(denied, AccessControlSections.Access);
            using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
            {
                // NTFS can grant delete through the parent even when the file denies DELETE.
                parent.AddAccessRule(new FileSystemAccessRule(identity.User,
                    FileSystemRights.DeleteSubdirectoriesAndFiles, AccessControlType.Deny));
                restrictive.AddAccessRule(new FileSystemAccessRule(identity.User,
                    FileSystemRights.Delete, AccessControlType.Deny));
            }

            bool parentChanged = false;
            bool fileChanged = false;
            RunReport report;
            try
            {
                try
                {
                    Directory.SetAccessControl(root, parent);
                    parentChanged = true;
                    File.SetAccessControl(denied, restrictive);
                    fileChanged = true;
                }
                catch (UnauthorizedAccessException)
                {
                    throw new FixtureUnavailableException("The test environment denied changing fixture DACLs.");
                }
                report = Run(root);
            }
            finally
            {
                if (parentChanged)
                {
                    parent.SetSecurityDescriptorBinaryForm(originalParent.GetSecurityDescriptorBinaryForm(), AccessControlSections.Access);
                    Directory.SetAccessControl(root, parent);
                }
                if (fileChanged && File.Exists(denied))
                {
                    restrictive.SetSecurityDescriptorBinaryForm(originalFile.GetSecurityDescriptorBinaryForm(), AccessControlSections.Access);
                    File.SetAccessControl(denied, restrictive);
                }
            }

            Assert(report.DeletedFiles == 1 && report.SkippedFiles == 1 && report.Result == "partial_success",
                "A permission error was not reported or interrupted sibling cleanup.");
            Assert(File.ReadAllText(denied) == "old" && !File.Exists(ordinary), "Permission handling affected the wrong file.");
        }

        private static void DryRunPreservesTargets(string root)
        {
            string old = PutOldFile(root, "old-file");
            string oldEmpty = PutDirectory(root, "old-empty", Old, Old);
            string recent = PutFile(root, "recent", "keep", Now.UtcDateTime, Now.UtcDateTime);
            DateTime originalLastWrite = File.GetLastWriteTimeUtc(old);

            RunReport report = Run(root, dryRun: true);

            Assert(File.ReadAllText(old) == "old" && File.GetLastWriteTimeUtc(old) == originalLastWrite,
                "Dry Run changed the old candidate.");
            Assert(Directory.Exists(oldEmpty) && File.ReadAllText(recent) == "keep", "Dry Run changed other targets.");
            Assert(report.EligibleFiles == 1 && report.EligibleBytes == 3, "Dry Run candidate estimates are wrong.");
            Assert(report.DeletedFiles == 0 && report.DeletedDirectories == 0 && report.EstimatedDeletedBytes == 0,
                "Dry Run recorded deletion.");
        }

        private static void SkipsOccupiedEmptyDirectory(string root)
        {
            string path = PutDirectory(root, "occupied", Old, Old);
            using (var parent = WindowsDirectory.Open(root, NativeFiles.ListDirectory))
            using (var held = NativeFiles.OpenChild(parent.Handle, "occupied", NativeFiles.ListDirectory,
                7, NativeFiles.Directory)) // Allow the cleaner to request delete access.
            {
                RunReport report = Run(root);
                Assert(report.DeletedDirectories == 0, "An occupied directory was counted as deleted.");
            }
            Assert(Directory.Exists(path), "An occupied directory was removed.");
        }

        private static void RefusesInvalidRoots(string sandbox)
        {
            string validDirectory = Directory.CreateDirectory(Path.Combine(sandbox, "directory")).FullName;
            string sentinel = PutOldFile(validDirectory, "sentinel");
            string file = PutOldFile(sandbox, "ordinary-file");
            foreach (string badPath in new[] { null, "", "directory", Path.GetPathRoot(sandbox),
                @"\\localhost\C$\Temp", Path.Combine(sandbox, ".", "directory"),
                Path.Combine(sandbox, "directory", "..", "directory") })
                Assert(!WindowsDirectory.IsSafeAbsolutePath(badPath), "The lexical validator accepted an unsafe path.");

            string originalWorkingDirectory = Environment.CurrentDirectory;
            try
            {
                Environment.CurrentDirectory = sandbox;
                foreach (string badRoot in new[] { null, "directory", Path.Combine(sandbox, "missing"), file })
                {
                    RunReport report = Run(badRoot);
                    Assert(report.DeletedFiles == 0 && report.DeletedDirectories == 0,
                        "An invalid root reported deletions.");
                }
            }
            finally
            {
                Environment.CurrentDirectory = originalWorkingDirectory;
            }

            Assert(File.Exists(sentinel) && File.Exists(file), "An invalid root affected a fixture.");
        }

        private static void SkipsJunctions(string sandbox)
        {
            string root = Directory.CreateDirectory(Path.Combine(sandbox, "target-root")).FullName;
            string outside = Directory.CreateDirectory(Path.Combine(sandbox, "external-sentinel-dir")).FullName;
            string sentinel = PutOldFile(outside, "sentinel");
            string nested = Directory.CreateDirectory(Path.Combine(outside, "nested")).FullName;
            string nestedSentinel = PutOldFile(nested, "nested-sentinel");
            string link = Path.Combine(root, "junction");
            FixtureLinks.CreateJunction(link, outside);
            string ordinary = PutOldFile(root, "ordinary");

            RunReport rootReport = Run(link);
            RunReport nestedReport = Run(Path.Combine(link, "nested"));
            RunReport traversalReport = Run(root);

            Assert(rootReport.DeletedFiles == 0 && rootReport.DeletedDirectories == 0, "A junction root reported deletion.");
            Assert(nestedReport.DeletedFiles == 0 && File.ReadAllText(nestedSentinel) == "old",
                "The root opener followed a junction in the middle of a path.");
            Assert(traversalReport.DeletedFiles == 1 && !File.Exists(ordinary), "The ordinary sibling was not cleaned.");
            Assert(File.ReadAllText(sentinel) == "old" && Directory.Exists(link), "A junction or its target was changed.");
        }

        private static void SkipsSymbolicLinks(string sandbox)
        {
            string root = Directory.CreateDirectory(Path.Combine(sandbox, "target-root")).FullName;
            string outside = Directory.CreateDirectory(Path.Combine(sandbox, "external-sentinel-dir")).FullName;
            string sentinel = PutOldFile(outside, "sentinel");
            string link = Path.Combine(root, "file-link");
            FixtureLinks.CreateFileSymbolicLink(link, sentinel);

            RunReport report = Run(root);

            Assert(report.DeletedFiles == 0 && report.SkippedFiles == 1, "The symbolic link was not counted as skipped.");
            Assert((File.GetAttributes(link) & FileAttributes.ReparsePoint) != 0 && File.ReadAllText(sentinel) == "old",
                "The symbolic link or its target was changed.");
        }

        private static void SkipsHardLinks(string sandbox)
        {
            string root = Directory.CreateDirectory(Path.Combine(sandbox, "target-root")).FullName;
            string sentinel = PutOldFile(sandbox, "sentinel");
            string link = Path.Combine(root, "hard-link");
            FixtureLinks.CreateHardLink(link, sentinel);

            RunReport report = Run(root);

            Assert(report.DeletedFiles == 0 && report.SkippedFiles == 1, "A hard-linked file was not skipped.");
            Assert(File.Exists(link) && File.ReadAllText(sentinel) == "old", "Cleanup changed a hard-linked file.");
        }

        private static void PreservesReplacedFileWithNewTime(string sandbox)
        {
            CheckFileReplacement(sandbox, Now.UtcDateTime);
        }

        private static void PreservesReplacedFileWithSameTimes(string sandbox)
        {
            CheckFileReplacement(sandbox, Old);
        }

        private static void CheckFileReplacement(string sandbox, DateTime replacementTime)
        {
            string root = Directory.CreateDirectory(Path.Combine(sandbox, "target-root")).FullName;
            string original = PutOldFile(root, "replaceable");
            string moved = Path.Combine(sandbox, "moved-original");
            string ready = PutFile(sandbox, "replacement", "new", replacementTime, replacementTime);
            bool replaced = false;

            RunReport report = Run(root, beforeOpen: name =>
            {
                if (name != "replaceable" || replaced)
                    return;

                File.Move(original, moved);
                File.Move(ready, original);
                // NTFS can retain a previous entry's creation time when its name is reused.
                File.SetCreationTimeUtc(original, replacementTime);
                File.SetLastWriteTimeUtc(original, replacementTime);
                replaced = true;
            });

            Assert(replaced, "The replacement did not occur.");
            Assert(report.DeletedFiles == 0 && File.ReadAllText(original) == "new" && File.ReadAllText(moved) == "old",
                "Cleanup deleted a replacement or modified the displaced original.");
        }

        private static void PreservesTargetWhenDirectoryIsReplaced(string sandbox)
        {
            string root = Directory.CreateDirectory(Path.Combine(sandbox, "target-root")).FullName;
            string outside = Directory.CreateDirectory(Path.Combine(sandbox, "external-sentinel-dir")).FullName;
            string sentinel = PutOldFile(outside, "sentinel");
            string original = PutDirectory(root, "replaceable", Old, Old);
            string moved = Path.Combine(sandbox, "moved-directory");
            bool replaced = false;

            RunReport report = Run(root, beforeOpen: name =>
            {
                if (name != "replaceable" || replaced)
                    return;

                Directory.Move(original, moved);
                FixtureLinks.CreateJunction(original, outside);
                replaced = true;
            });

            Assert(replaced, "The directory replacement did not occur.");
            Assert(report.DeletedFiles == 0 && report.DeletedDirectories == 0 && File.ReadAllText(sentinel) == "old",
                "Cleanup crossed a directory replacement into the sentinel directory.");
            Assert((File.GetAttributes(original) & FileAttributes.ReparsePoint) != 0 && Directory.Exists(moved),
                "Cleanup changed the replacement junction or the displaced directory.");
        }

        private static void PreservesFileChangedAfterSnapshot(string root)
        {
            string file = PutOldFile(root, "changed");
            bool changed = false;

            RunReport report = Run(root, beforeOpen: name =>
            {
                if (name != "changed")
                    return;

                File.SetLastWriteTimeUtc(file, Now.UtcDateTime);
                changed = true;
            });

            Assert(changed, "The timestamp change did not occur.");
            Assert(report.DeletedFiles == 0 && File.ReadAllText(file) == "old", "A recently changed file was deleted.");
        }

        private static void PreservesOrdinaryReplacementDirectory(string sandbox)
        {
            string root = Directory.CreateDirectory(Path.Combine(sandbox, "target-root")).FullName;
            string original = PutDirectory(root, "replaceable", Old, Old);
            string moved = Path.Combine(sandbox, "moved-directory");
            string ready = PutDirectory(sandbox, "replacement", Old, Old);
            PutOldFile(ready, "old-sentinel");
            bool replaced = false;

            RunReport report = Run(root, beforeOpen: name =>
            {
                if (name != "replaceable" || replaced)
                    return;

                Directory.Move(original, moved);
                Directory.Move(ready, original);
                SetDirectoryTimes(original, Old, Old);
                replaced = true;
            });

            Assert(replaced, "The ordinary directory replacement did not occur.");
            Assert(report.DeletedFiles == 0 && report.DeletedDirectories == 0 && Directory.Exists(moved) &&
                File.ReadAllText(Path.Combine(original, "old-sentinel")) == "old",
                "Cleanup traversed a directory that replaced the snapshotted entry.");
        }

        private static void ContinuesAfterFileDisappears(string root)
        {
            string disappears = PutOldFile(root, "disappears");
            string sibling = PutOldFile(root, "sibling");
            bool removed = false;

            RunReport report = Run(root, beforeOpen: name =>
            {
                if (name != "disappears" || removed)
                    return;

                File.Delete(disappears);
                removed = true;
            });

            Assert(removed && !File.Exists(disappears), "The disappearance fixture did not run.");
            Assert(report.DeletedFiles == 1 && report.SkippedFiles == 1 && !File.Exists(sibling),
                "A disappearing file interrupted sibling cleanup or was counted as deleted.");
        }

        private static void EnumeratesAcrossBatchesWhileDeleting(string root)
        {
            const int candidateCount = 700;
            for (int index = 0; index < candidateCount; index++)
                PutOldFile(root, "old-candidate-" + index.ToString("D4"));

            string retained = PutFile(root, "retained", "keep", Old, Now.UtcDateTime);
            RunReport report = Run(root);

            Assert(report.DeletedFiles == candidateCount && report.EstimatedDeletedBytes == candidateCount * 3,
                "Cleanup lost directory entries across enumeration batches.");
            Assert(Directory.GetFileSystemEntries(root).Length == 1 && File.ReadAllText(retained) == "keep",
                "A retained file was modified or expired files survived the batched scan.");
        }

        private static RunReport Run(string root, bool dryRun = false, Action<string> beforeOpen = null)
        {
            var report = new RunReport(Now, dryRun);
            Cleaner.Run(root, Now, report, beforeOpen);
            return report;
        }

        private static string PutOldFile(string directory, string name)
        {
            return PutFile(directory, name, "old", Old, Old);
        }

        private static string PutFile(string directory, string name, string content, DateTime creation, DateTime write)
        {
            string path = Path.Combine(directory, name);
            File.WriteAllText(path, content);
            File.SetCreationTimeUtc(path, creation);
            File.SetLastWriteTimeUtc(path, write);
            return path;
        }

        private static string PutDirectory(string parent, string name, DateTime creation, DateTime write)
        {
            string path = Directory.CreateDirectory(Path.Combine(parent, name)).FullName;
            SetDirectoryTimes(path, creation, write);
            return path;
        }

        private static void SetDirectoryTimes(string path, DateTime creation, DateTime write)
        {
            Directory.SetCreationTimeUtc(path, creation);
            Directory.SetLastWriteTimeUtc(path, write);
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
        }
    }
}
