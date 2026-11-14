using System;
using System.Diagnostics;

namespace TempAutoCleaner.Tests
{
    internal static class ProcessTests
    {
        internal const string ProbeFlag = "--mutex-probe";
        private const int ConcurrentInstance = 3;
        private const int CallbackExecuted = 91;
        private const int ProbeTimeoutMilliseconds = 15000;

        internal static void PreventsConcurrentProcess(string root)
        {
            Guid id = Guid.NewGuid();
            int ownerExitCode = Probe(id, () =>
            {
                int contenderExitCode = StartProbe(id, root);
                if (contenderExitCode != ConcurrentInstance)
                    throw new InvalidOperationException(
                        "The contender did not exit with code 3 while the owner held the mutex. Exit code: " + contenderExitCode);
                return CallbackExecuted;
            });

            if (ownerExitCode != CallbackExecuted || StartProbe(id, root) != CallbackExecuted)
                throw new InvalidOperationException("The mutex did not allow a process to enter when free.");
        }

        internal static int RunChild(string id)
        {
            return Guid.TryParseExact(id, "N", out Guid parsed) ? Probe(parsed, () => CallbackExecuted) : 2;
        }

        private static int Probe(Guid id, Func<int> callback)
        {
            return global::TempAutoCleaner.Program.RunWithMutex(
                @"Global\TempAutoCleaner-test-" + id.ToString("N"), callback);
        }

        private static int StartProbe(Guid id, string root)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = typeof(ProcessTests).Assembly.Location,
                Arguments = ProbeFlag + " " + id.ToString("N"),
                WorkingDirectory = root,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using (Process child = Process.Start(startInfo))
            {
                if (child.WaitForExit(ProbeTimeoutMilliseconds))
                    return child.ExitCode;

                try
                {
                    child.Kill();
                }
                catch (InvalidOperationException) when (child.HasExited)
                {
                    // The child exited between the timeout and the kill request.
                }
                child.WaitForExit(ProbeTimeoutMilliseconds);
                throw new TimeoutException("The mutex probe did not exit within 15 seconds.");
            }
        }
    }
}
