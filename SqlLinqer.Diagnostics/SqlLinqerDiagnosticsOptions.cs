namespace SqlLinqer.Diagnostics
{
    /// <summary>Controls optional diagnostic details. Configure before sharing the observer.</summary>
    public sealed class SqlLinqerDiagnosticsOptions
    {
        /// <summary>Includes SQL text in Debug logs and recorded activity tags. Defaults to false.</summary>
        public bool IncludeSql { get; set; }
        /// <summary>Includes parameter values in Debug logs. Defaults to false. Values are never added to activity tags.</summary>
        public bool IncludeParameterValues { get; set; }
        /// <summary>Passes execution exceptions to the logger. Defaults to false because provider error messages can contain application values.</summary>
        public bool IncludeExceptionDetails { get; set; }
    }
}
