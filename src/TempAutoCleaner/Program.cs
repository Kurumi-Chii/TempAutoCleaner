using System;
using System.ComponentModel;
using System.IO;
using System.Security;
using System.Security.Principal;
using System.Threading;

namespace TempAutoCleaner
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            if (args.Length > 1 || (args.Length == 1 && args[0] != "--dry-run"))
                return 64;

            try
            {
                using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
                {
                    if (identity.User == null)
                        return 2;

                    using (var mutex = new Mutex(false, @"Global\TempAutoCleaner-" + identity.User.Value))
                    {
                        if (!TryAcquire(mutex))
                            return 3;
                        try
                        {
                            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData,
                                Environment.SpecialFolderOption.DoNotVerify);
                            return RunOnce(localAppData, args.Length == 1, DateTimeOffset.UtcNow);
                        }
                        finally
                        {
                            mutex.ReleaseMutex();
                        }
                    }
                }
            }
            catch (Exception error) when (error is UnauthorizedAccessException || error is IOException ||
                error is SecurityException || error is WaitHandleCannotBeOpenedException || error is Win32Exception)
            {
                return 2;
            }
        }

        private static bool TryAcquire(Mutex mutex)
        {
            try
            {
                return mutex.WaitOne(0);
            }
            catch (AbandonedMutexException)
            {
                // Windows granted ownership after the previous process exited without releasing it.
                return true;
            }
        }

        internal static int RunOnce(string localAppData, bool dryRun, DateTimeOffset now)
        {
            if (!WindowsDirectory.IsSafeAbsolutePath(localAppData))
                return 2;

            var report = new RunReport(now, dryRun);
            try
            {
                // Establish a safe report destination before doing any destructive work.
                using (LogStore logs = LogStore.Open(localAppData))
                using (FileStream stream = logs.CreatePendingReport(out string finalName))
                {
                    logs.Rotate(now, report);
                    Cleaner.Run(Path.Combine(localAppData, "Temp"), now, report);
                    report.WriteJson(stream, DateTimeOffset.UtcNow);
                    stream.Flush(true);
                    NativeFiles.RenameInSameDirectory(stream.SafeFileHandle, finalName);
                }
            }
            catch (Exception error) when (error is Win32Exception || error is IOException ||
                error is UnauthorizedAccessException || error is SecurityException ||
                error is System.Runtime.Serialization.SerializationException)
            {
                return 2;
            }

            return report.HasFailure ? 2 : report.HasPartial ? 1 : 0;
        }
    }
}
