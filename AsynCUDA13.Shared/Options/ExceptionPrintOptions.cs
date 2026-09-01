namespace AsynCUDA13.Shared.Options
{
    /// <summary>
    /// Represents configuration options for printing exceptions, including settings for inner exception depth, stack trace inclusion, and formatting. This class allows customization of how exceptions and their inner exceptions are logged or displayed, providing control over the level of detail and formatting used in exception messages.
    /// </summary>
    public class ExceptionPrintOptions
    {
        /// <summary>
        /// Gets or sets the maximum depth of inner exceptions to include when logging exceptions. If set to null, all inner exceptions will be included. If set to a specific integer value, only that many levels of inner exceptions will be included in the log output.
        /// </summary>
        public int? InnerExceptionMaxDepth { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether to append the stack trace of inner exceptions when logging exceptions.
        /// </summary>
        public bool InnerExceptionAppendStackTrace { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether to format inner exceptions as a single line when logging exceptions.
        /// </summary>
        public bool InnerExceptionAsSingleLine { get; set; }

        /// <summary>
        /// The opening bracket used when formatting inner exception messages in the log. This can be customized to change how inner exceptions are displayed in the log output.
        /// </summary>
        public string InnerExceptionOpeningBracket { get; set; } = "(";

        /// <summary>
        /// The closing bracket used when formatting inner exception messages in the log. This can be customized to change how inner exceptions are displayed in the log output.
        /// </summary>
        public string InnerExceptionClosingBracket { get; set; } = ")";

        /// <summary>
        /// The separator used when formatting inner exception messages in the log. This can be customized to change how inner exceptions are displayed in the log output.
        /// </summary>
        public string InnerExceptionSeparator { get; set; } = " ";
    }
}