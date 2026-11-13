using System;
using System.Collections.Generic;
using System.IO;

namespace TempAutoCleaner.Tests
{
    internal sealed class TestSandbox : IDisposable
    {
        internal string Root { get; }

        internal TestSandbox(string parent)
        {
            if (parent.Length < 4 || parent[1] != ':' || parent[2] != Path.DirectorySeparatorChar)
                throw new ArgumentException("Supply an absolute path to an existing isolated sandbox parent.");

            string fullPath = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar);
            if (fullPath.Length < 4 || !string.Equals(fullPath, parent.TrimEnd(Path.DirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Supply a canonical sandbox path below the volume root.");

            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData,
                Environment.SpecialFolderOption.DoNotVerify);
            string currentTemp = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar);
            if (IsAtOrBelow(fullPath, currentTemp) ||
                (!string.IsNullOrEmpty(localAppData) && IsAtOrBelow(fullPath, Path.Combine(localAppData, "Temp"))))
                throw new ArgumentException("The test sandbox must be outside the current user's Temp directory.");

            VerifyDirectoryChain(new DirectoryInfo(fullPath));

            Root = Directory.CreateDirectory(Path.Combine(fullPath,
                "TempAutoCleaner.Tests-" + Guid.NewGuid().ToString("N"))).FullName;
        }

        public void Dispose()
        {
            VerifyDirectoryChain(new DirectoryInfo(Root).Parent);
            RemoveFixtureTree(Root);
        }

        private static void VerifyDirectoryChain(DirectoryInfo leaf)
        {
            var directories = new Stack<string>();
            for (DirectoryInfo directory = leaf; directory != null; directory = directory.Parent)
                directories.Push(directory.FullName);

            foreach (string path in directories)
                VerifyOrdinaryDirectory(path);
        }

        private static void VerifyOrdinaryDirectory(string path)
        {
            FileAttributes attributes = File.GetAttributes(path);
            if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != FileAttributes.Directory)
                throw new ArgumentException("The test sandbox path must contain only ordinary directories.");
        }

        private static bool IsAtOrBelow(string path, string directory)
        {
            return path.Equals(directory, StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }

        private static void RemoveFixtureTree(string path)
        {
            FileAttributes attributes = File.GetAttributes(path);
            bool directory = (attributes & FileAttributes.Directory) != 0;
            bool reparsePoint = (attributes & FileAttributes.ReparsePoint) != 0;
            if (directory)
            {
                if (!reparsePoint)
                {
                    foreach (string child in Directory.GetFileSystemEntries(path))
                        RemoveFixtureTree(child);
                }

                Directory.Delete(path, recursive: false);
                return;
            }

            if (!reparsePoint && (attributes & FileAttributes.ReadOnly) != 0)
                File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);

            File.Delete(path);
        }
    }
}
