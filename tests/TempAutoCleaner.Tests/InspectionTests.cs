using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace TempAutoCleaner.Tests
{
    internal static class InspectionTests
    {
        private const long MiB = 1024 * 1024;

        internal static void InspectionReportsOnlyMatchingFiles(string localAppData)
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            DateTime old = now.UtcDateTime.AddHours(-49);
            DateTime recent = now.UtcDateTime.AddHours(-24);
            string root = Directory.CreateDirectory(Path.Combine(localAppData, "Temp")).FullName;
            string subdir = Directory.CreateDirectory(Path.Combine(root, "cache")).FullName;
            string matching = PutFile(subdir, "growing.bin", 100 * MiB, old, recent);
            PutFile(root, "too-small.bin", 100 * MiB - 1, old, recent);
            PutFile(root, "at-creation-cutoff.bin", 100 * MiB, now.UtcDateTime.AddHours(-48), recent);
            string stale = PutFile(root, "stale.bin", 100 * MiB, old, recent.AddSeconds(-1));
            PutFile(root, "future.bin", 100 * MiB, old, now.UtcDateTime.AddHours(1));
            string empty = Directory.CreateDirectory(Path.Combine(subdir, "empty")).FullName;
            Directory.SetCreationTimeUtc(empty, old);
            Directory.SetLastWriteTimeUtc(empty, old);
            string outside = Directory.CreateDirectory(Path.Combine(localAppData, "outside")).FullName;
            string sentinel = PutFile(outside, "sentinel.bin", 200 * MiB, old, recent);
            FixtureLinks.CreateJunction(Path.Combine(root, "junction"), outside);
            string logs = Directory.CreateDirectory(Path.Combine(localAppData, "TempAutoCleaner", "logs")).FullName;
            string expired = Path.Combine(logs, "run-" + Guid.NewGuid().ToString("N") + ".json");
            File.WriteAllText(expired, "{}");
            File.SetCreationTimeUtc(expired, now.UtcDateTime.AddDays(-45));
            File.SetLastWriteTimeUtc(expired, now.UtcDateTime.AddDays(-45));

            // The inspection must still report metadata while another process can write.
            using (var writer = File.Open(matching, FileMode.Open, FileAccess.Write, FileShare.ReadWrite))
            {
                Assert(Inspect(localAppData, now) == 0, "Inspection failed on a readable tree.");
                Assert(writer.Length == 100 * MiB, "Inspection changed the reported file.");
            }

            string json = ReadReport(logs, expired);
            Assert(json.Contains("\"large_recent_files\"") && json.Contains("\"total\":1") &&
                json.Contains("\"relative_path\":\"cache\\\\growing.bin\"") &&
                json.Contains("\"size_bytes\":104857600") && json.Contains("\"last_write_utc\""),
                "Inspection did not report the matching file and its metadata.");
            Assert(!json.Contains("too-small.bin") && !json.Contains("at-creation-cutoff.bin") &&
                !json.Contains("stale.bin") && !json.Contains("future.bin") && !json.Contains("sentinel.bin") &&
                !json.Contains(localAppData.Replace("\\", "\\\\")), "Inspection disclosed an excluded path.");
            Assert(json.Contains("\"deleted_files\":0,") && json.Contains("\"deleted_directories\":0,") &&
                File.Exists(stale) && Directory.Exists(empty) && File.Exists(expired) && File.Exists(sentinel),
                "Inspection deleted a fixture or rotated an old log.");

            string inspectionReport = Directory.GetFiles(logs, "run-*.json").Single(path => path != expired);
            Assert(global::TempAutoCleaner.Program.RunOnce(localAppData, true, now) == 0, "Dry Run failed.");
            string dryRunJson = ReadReport(logs, expired, inspectionReport);
            Assert(!dryRunJson.Contains("\"large_recent_files\"") && !dryRunJson.Contains("growing.bin"),
                "A regular Dry Run disclosed the inspection finding.");
        }

        internal static void InspectionLimitsReportToLargestFiles(string localAppData)
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            string root = Directory.CreateDirectory(Path.Combine(localAppData, "Temp")).FullName;
            for (int i = 0; i <= 20; i++)
                PutFile(root, i.ToString("D2") + ".bin", 100 * MiB + i,
                    now.UtcDateTime.AddDays(-3), now.UtcDateTime.AddHours(-1));

            Assert(Inspect(localAppData, now) == 0, "Inspection failed.");
            string logs = Path.Combine(localAppData, "TempAutoCleaner", "logs");
            string json = ReadReport(logs);
            Assert(json.Contains("\"total\":21") && Regex.Matches(json, "\"relative_path\"").Count == 20 &&
                !json.Contains("\"00.bin\"") &&
                json.IndexOf("\"20.bin\"", StringComparison.Ordinal) < json.IndexOf("\"01.bin\"", StringComparison.Ordinal),
                "Inspection did not report the 20 largest files in descending order.");
        }

        private static int Inspect(string localAppData, DateTimeOffset now) =>
            global::TempAutoCleaner.Program.RunOnce(localAppData, false, now, inspectLargeFiles: true);

        private static string PutFile(string directory, string name, long size, DateTime created, DateTime lastWrite)
        {
            string path = Path.Combine(directory, name);
            using (FileStream file = File.Create(path))
            {
                FixtureLinks.MarkSparse(file.SafeFileHandle);
                file.SetLength(size);
            }
            File.SetCreationTimeUtc(path, created);
            File.SetLastWriteTimeUtc(path, lastWrite);
            return path;
        }

        private static string ReadReport(string logs, params string[] ignored) =>
            File.ReadAllText(Directory.GetFiles(logs, "run-*.json").Single(path => !ignored.Contains(path)));

        private static void Assert(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
        }
    }
}
