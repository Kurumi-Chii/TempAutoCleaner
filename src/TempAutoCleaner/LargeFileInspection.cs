using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.Serialization;

namespace TempAutoCleaner
{
    [DataContract]
    internal sealed class LargeFileInspection
    {
        private const long MinimumSize = 100L * 1024 * 1024;
        private const int MaximumEntries = 20;
        private readonly long start;
        private readonly long creationCutoff;
        private readonly long lastWriteCutoff;

        internal LargeFileInspection(DateTimeOffset now)
        {
            DateTime utc = now.UtcDateTime;
            start = utc.ToFileTimeUtc();
            creationCutoff = utc.AddHours(-48).ToFileTimeUtc();
            lastWriteCutoff = utc.AddHours(-24).ToFileTimeUtc();
        }

        [DataMember(Name = "total", Order = 0)]
        private long Total { get; set; }

        [DataMember(Name = "files", Order = 1)]
        private List<Finding> Files { get; } = new List<Finding>(MaximumEntries);

        internal bool Includes(long size, long created, long lastWrite) =>
            size >= MinimumSize && created > 0 && created < creationCutoff &&
            lastWrite >= lastWriteCutoff && lastWrite <= start;

        internal void Record(string relativePath, NativeFiles.FileInfoByHandle info)
        {
            Total++;
            int index = Files.FindIndex(file => file.Size < info.Size);
            if (index < 0)
                index = Files.Count;
            if (index >= MaximumEntries)
                return;
            if (Files.Count == MaximumEntries)
                Files.RemoveAt(MaximumEntries - 1);
            Files.Insert(index, new Finding(relativePath, info));
        }

        [DataContract]
        private sealed class Finding
        {
            internal Finding(string relativePath, NativeFiles.FileInfoByHandle info)
            {
                RelativePath = relativePath;
                Size = info.Size;
                Created = FormatFileTime(info.Created);
                LastWrite = FormatFileTime(info.LastWrite);
            }

            [DataMember(Name = "relative_path", Order = 0)]
            private string RelativePath { get; set; }

            [DataMember(Name = "size_bytes", Order = 1)]
            internal long Size { get; private set; }

            [DataMember(Name = "created_utc", Order = 2)]
            private string Created { get; set; }

            [DataMember(Name = "last_write_utc", Order = 3)]
            private string LastWrite { get; set; }

            private static string FormatFileTime(long value) =>
                DateTime.FromFileTimeUtc(value).ToString("O", CultureInfo.InvariantCulture);
        }
    }
}
