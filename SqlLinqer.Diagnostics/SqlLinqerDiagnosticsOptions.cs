using System;

namespace SqlLinqer.Diagnostics
{
    /// <summary>Controls diagnostic details. The observer captures a validated snapshot for each execution.</summary>
    public sealed class SqlLinqerDiagnosticsOptions
    {
        /// <summary>Includes parameterized SQL text in Trace logs. Defaults to false.</summary>
        public bool IncludeSqlInLogs { get; set; }
        /// <summary>Legacy alias for IncludeSqlInLogs. It no longer enables SQL in activity tags.</summary>
        public bool IncludeSql { get => IncludeSqlInLogs; set => IncludeSqlInLogs = value; }
        /// <summary>Includes SQL text in sampled activities requesting data, independently of logger thresholds. Defaults to false.</summary>
        public bool IncludeSqlInActivities { get; set; }
        /// <summary>Includes typed parameter metadata in Trace logs. Defaults to true. Values require their separate opt-in.</summary>
        public bool IncludeParameterMetadata { get; set; } = true;
        /// <summary>Includes bounded parameter values in Trace logs. Defaults to false. Values never become activity tags.</summary>
        public bool IncludeParameterValues { get; set; }
        /// <summary>Passes execution exceptions to the logger. Defaults to false. Provider messages can contain application values.</summary>
        public bool IncludeExceptionDetails { get; set; }
        /// <summary>Logs successful completion at Information instead of Debug. Cancellation and early disposal remain Debug. Defaults to false.</summary>
        public bool LogSuccessfulQueriesAtInformation { get; set; }
        /// <summary>Optional positive provider Execute duration threshold for Warning event 1004. Excludes reader iteration and consumer pauses. Defaults to disabled.</summary>
        public TimeSpan? SlowCommandThreshold { get; set; }
        /// <summary>Maximum characters per displayed parameter value, including its truncation marker. Defaults to 256. Allowed range is 16 through 65536.</summary>
        public int MaxParameterValueLength { get; set; } = 256;
        /// <summary>Maximum parameter entries in a Trace record. Defaults to 32. Allowed range is 1 through 1024. Omitted entries are counted.</summary>
        public int MaxParameterCount { get; set; } = 32;

        internal SqlLinqerDiagnosticsOptions Snapshot()
        {
            var copy = (SqlLinqerDiagnosticsOptions)MemberwiseClone();
            if (copy.MaxParameterValueLength < 16 || copy.MaxParameterValueLength > 65536)
                throw new ArgumentOutOfRangeException(nameof(MaxParameterValueLength));
            if (copy.MaxParameterCount < 1 || copy.MaxParameterCount > 1024)
                throw new ArgumentOutOfRangeException(nameof(MaxParameterCount));
            if (copy.SlowCommandThreshold.HasValue && copy.SlowCommandThreshold.Value <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(SlowCommandThreshold));
            return copy;
        }
    }
}
