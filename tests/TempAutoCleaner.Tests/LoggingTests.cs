using System;
using System.IO;
using System.Linq;

namespace TempAutoCleaner.Tests
{
    internal static class LoggingTests
    {
        internal static void DryRunWritesReportWithoutRotating(string root)
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            string target = CreateTempFixture(root, now);
            string logs = Directory.CreateDirectory(LogPath(root)).FullName;
            string expired = PutFile(logs, ReportName(1), "expired", now.AddDays(-45).UtcDateTime);

            int exitCode = global::TempAutoCleaner.Program.RunOnce(root, true, now);

            Assert(exitCode == 0, "Dry Run failed.");
            Assert(File.ReadAllText(target) == "data", "Dry Run changed the cleanup target.");
            Assert(File.ReadAllText(expired) == "expired", "Dry Run rotated an expired report.");
            string json = ReadNewReport(logs, expired);
            Assert(json.Contains("\"result\":\"success\"") && json.Contains("\"dry_run\":true"),
                "The report did not identify a successful Dry Run.");
            Assert(json.Contains("\"eligible_files\":1,") && json.Contains("\"eligible_bytes\":4,") &&
                json.Contains("\"deleted_files\":0,") && json.Contains("\"estimated_deleted_bytes\":0,"),
                "Dry Run reported incorrect eligibility or deletion totals.");
            Assert(!json.Contains(Path.GetFileName(target)), "The report exposed a fixture file name.");
        }

        internal static void CleanupRotatesOnlyExpiredOwnedReports(string root)
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            DateTime expiredTime = now.AddDays(-45).UtcDateTime;
            string target = CreateTempFixture(root, now);
            string logs = Directory.CreateDirectory(LogPath(root)).FullName;
            string expired = PutFile(logs, ReportName(1), "expired", expiredTime);
            string expiredPending = PutFile(logs, "run-" + 4.ToString("x32") + ".pending", "incomplete", expiredTime);
            string recent = PutFile(logs, ReportName(2), "recent", now.AddDays(-2).UtcDateTime);
            string unrelated = PutFile(logs, "run-unrelated.json", "unrelated", expiredTime);
            string hardLinkSource = PutFile(root, "log-sentinel", "linked", expiredTime);
            string hardLink = Path.Combine(logs, ReportName(3));
            FixtureLinks.CreateHardLink(hardLink, hardLinkSource);

            int exitCode = global::TempAutoCleaner.Program.RunOnce(root, false, now);

            Assert(exitCode == 0, "Cleanup with report rotation failed.");
            Assert(!File.Exists(target), "Cleanup retained an eligible fixture file.");
            Assert(!File.Exists(expired), "An expired report survived rotation.");
            Assert(!File.Exists(expiredPending), "An expired pending report survived rotation.");
            Assert(File.ReadAllText(recent) == "recent" && File.ReadAllText(unrelated) == "unrelated",
                "Rotation changed a recent report or unrelated log file.");
            Assert(File.ReadAllText(hardLink) == "linked" && File.ReadAllText(hardLinkSource) == "linked",
                "Rotation changed a hard-linked report or its source.");
            string json = ReadNewReport(logs, recent, unrelated, hardLink);
            Assert(json.Contains("\"result\":\"success\"") && json.Contains("\"dry_run\":false") &&
                json.Contains("\"deleted_files\":1,") && json.Contains("\"estimated_deleted_bytes\":4,"),
                "The report did not record the successful deletion and estimated bytes.");
            Assert(!json.Contains(Path.GetFileName(target)), "The report exposed a fixture file name.");
        }

        internal static void RejectsJunctionAtLogDestination(string root)
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            string target = CreateTempFixture(root, now);
            string appData = Directory.CreateDirectory(Path.Combine(root, "TempAutoCleaner")).FullName;
            string outside = Directory.CreateDirectory(Path.Combine(root, "external-log-fixture")).FullName;
            string sentinel = PutFile(outside, ReportName(1), "outside", now.AddDays(-45).UtcDateTime);
            string link = Path.Combine(appData, "logs");
            FixtureLinks.CreateJunction(link, outside);

            int exitCode = global::TempAutoCleaner.Program.RunOnce(root, false, now);

            Assert(exitCode == 2, "The run accepted a junction as its report destination.");
            Assert(File.ReadAllText(target) == "data", "Cleanup started with an unsafe report destination.");
            Assert(File.ReadAllText(sentinel) == "outside" && Directory.GetFileSystemEntries(outside).Length == 1,
                "The run wrote to the junction target or rotated its sentinel.");
            Assert((File.GetAttributes(link) & FileAttributes.ReparsePoint) != 0,
                "The run changed the log junction.");
        }

        private static string CreateTempFixture(string root, DateTimeOffset now)
        {
            string target = Directory.CreateDirectory(Path.Combine(root, "Temp")).FullName;
            return PutFile(target, "private-雪-fixture.bin", "data", now.AddDays(-2).UtcDateTime);
        }

        private static string ReadNewReport(string logs, params string[] existing)
        {
            string[] reports = Directory.GetFiles(logs, "run-*.json")
                .Where(path => !existing.Contains(path, StringComparer.OrdinalIgnoreCase)).ToArray();
            Assert(reports.Length == 1, "Expected exactly one new JSON report.");
            Assert(Directory.GetFiles(logs, "*.pending").Length == 0, "A completed run left a pending report.");
            string json = File.ReadAllText(reports[0]);
            Assert(json.StartsWith("{", StringComparison.Ordinal) && json.EndsWith("}", StringComparison.Ordinal) &&
                json.Contains("\"started_utc\":") && json.Contains("\"finished_utc\":"),
                "The JSON report was incomplete.");
            return json;
        }

        private static string PutFile(string directory, string name, string content, DateTime timestamp)
        {
            string file = Path.Combine(directory, name);
            File.WriteAllText(file, content);
            File.SetCreationTimeUtc(file, timestamp);
            File.SetLastWriteTimeUtc(file, timestamp);
            return file;
        }

        private static string LogPath(string root) => Path.Combine(root, "TempAutoCleaner", "logs");

        private static string ReportName(int id) => "run-" + id.ToString("x32") + ".json";

        private static void Assert(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
        }
    }
}
