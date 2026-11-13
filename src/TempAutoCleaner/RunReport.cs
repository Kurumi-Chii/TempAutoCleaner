using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace TempAutoCleaner
{
    [DataContract]
    internal sealed class RunReport
    {
        private const int MaxErrors = 64;
        private const string PartialSuccess = "partial_success";
        private const string Failed = "failed";

        internal RunReport(DateTimeOffset startedUtc, bool dryRun)
        {
            StartedUtc = FormatUtc(startedUtc);
            DryRun = dryRun;
            Result = "success";
            Errors = new List<ReportError>();
        }

        [DataMember(Name = "started_utc", Order = 0)]
        private string StartedUtc { get; set; }

        [DataMember(Name = "finished_utc", Order = 1)]
        private string FinishedUtc { get; set; }

        [DataMember(Name = "dry_run", Order = 2)]
        public bool DryRun { get; private set; }

        [DataMember(Name = "result", Order = 3)]
        public string Result { get; private set; }

        [DataMember(Name = "deleted_files", Order = 4)]
        public long DeletedFiles { get; set; }

        [DataMember(Name = "skipped_files", Order = 5)]
        public long SkippedFiles { get; set; }

        [DataMember(Name = "estimated_deleted_bytes", Order = 6)]
        public long EstimatedDeletedBytes { get; set; }

        [DataMember(Name = "deleted_directories", Order = 7)]
        public long DeletedDirectories { get; set; }

        [DataMember(Name = "skipped_directories", Order = 8)]
        public long SkippedDirectories { get; set; }

        [DataMember(Name = "eligible_files", Order = 9)]
        public long EligibleFiles { get; set; }

        [DataMember(Name = "eligible_bytes", Order = 10)]
        public long EligibleBytes { get; set; }

        [DataMember(Name = "eligible_directories", Order = 11)]
        public long EligibleDirectories { get; set; }

        [DataMember(Name = "errors", Order = 12)]
        private List<ReportError> Errors { get; set; }

        [DataMember(Name = "errors_truncated", Order = 13)]
        private bool ErrorsTruncated { get; set; }

        internal bool HasFailure { get { return Result == Failed; } }
        internal bool HasPartial { get { return Result == PartialSuccess; } }

        // Callers supply fixed operation labels, never paths or exception messages.
        internal void RecordError(string operation, int code, bool fatal = false)
        {
            bool firstFatal = fatal && !HasFailure;
            if (!HasFailure)
                Result = fatal ? Failed : PartialSuccess;

            if (!IsOperationLabel(operation))
                operation = "unknown";

            if (Errors.Exists(error => error.Operation == operation && error.Code == code))
                return;

            if (Errors.Count < MaxErrors)
            {
                Errors.Add(new ReportError(operation, code));
                return;
            }

            ErrorsTruncated = true;
            if (firstFatal)
                Errors[MaxErrors - 1] = new ReportError(operation, code);
        }

        internal void WriteJson(Stream output, DateTimeOffset finishedUtc)
        {
            FinishedUtc = FormatUtc(finishedUtc);
            var serializer = new DataContractJsonSerializer(typeof(RunReport));
            serializer.WriteObject(output, this);
        }

        private static string FormatUtc(DateTimeOffset timestamp)
        {
            return timestamp.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);
        }

        private static bool IsOperationLabel(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length > 32)
                return false;

            foreach (char character in value)
            {
                if ((character < 'a' || character > 'z') && character != '_')
                    return false;
            }

            return true;
        }

        [DataContract]
        private sealed class ReportError
        {
            internal ReportError(string operation, int code)
            {
                Operation = operation;
                Code = code;
            }

            [DataMember(Name = "operation", Order = 0)]
            public string Operation { get; private set; }

            [DataMember(Name = "code", Order = 1)]
            public int Code { get; private set; }
        }
    }
}
