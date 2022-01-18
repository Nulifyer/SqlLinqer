namespace SqlLinqer
{
    /// <summary>
    /// SQL Function identifiers
    /// </summary>
    public enum SQLFunc
    {
        /*
         * Custom
         */

        /// <summary>
        /// No Function
        /// </summary>
        NONE,
        /// <summary>
        /// Return the unix timestamp for date in seconds
        /// </summary>
        DATE_TO_UNIXTIMESTAMP_SEC,
        /// <summary>
        /// Return the unix timestamp for date in milliseconds
        /// </summary>
        DATE_TO_UNIXTIMESTAMP_MS,


        /*
         * Navtive
         */

        /// <summary>
        /// Absolute value
        /// </summary>
        ABS,
        /// <summary>
        /// Average value
        /// </summary>
        AVG,
        /// <summary>
        /// This function returns the checksum of the values in a group
        /// </summary>
        CHECKSUM_AGG,
        /// <summary>
        /// Count based on value
        /// </summary>
        COUNT,
        /// <summary>
        /// Count based on value, return long
        /// </summary>
        COUNT_BIG,
        /// <summary>
        /// Date of date type value
        /// </summary>
        DAY,
        /// <summary>
        /// Max value
        /// </summary>
        MAX,
        /// <summary>
        /// Lowercase
        /// </summary>
        LOWER,
        /// <summary>
        /// Trim left
        /// </summary>
        LTRIM,
        /// <summary>
        /// Minimum value
        /// </summary>
        MIN,
        /// <summary>
        /// Month of date type value
        /// </summary>
        MONTH,
        /// <summary>
        /// Reverse value
        /// </summary>
        REVERSE,
        /// <summary>
        /// Extracts a number of characters from a string, starting from right side
        /// </summary>
        RIGHT,
        /// <summary>
        /// Trim right
        /// </summary>
        RTRIM,
        /// <summary>
        /// SoundDex of value
        /// </summary>
        SOUNDDEX,
        /// <summary>
        /// Statistical standard deviation 
        /// </summary>
        STDEV,
        /// <summary>
        /// Statistical standard deviation for the population
        /// </summary>
        STDEVP,
        /// <summary>
        /// Concatenates the values of string expressions and places separator values between them
        /// </summary>
        STRING_AGG,
        /// <summary>
        /// Substring of a value
        /// </summary>
        SUBSTRING,
        /// <summary>
        /// Sum of values
        /// </summary>
        SUM,
        /// <summary>
        /// Trim value
        /// </summary>
        TRIM,
        /// <summary>
        /// Uppercase
        /// </summary>
        UPPER,
        /// <summary>
        /// Returns the statistical variance of all values in the specified expression
        /// </summary>
        VAR,
        /// <summary>
        /// Returns the statistical variance for the population for all values in the specified expression
        /// </summary>
        VARP,
        /// <summary>
        /// Year of date type value
        /// </summary>
        YEAR
    }
}
