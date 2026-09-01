using System.Buffers;
using System.Collections.Frozen;
using System.Text;
using System.Text.RegularExpressions;

namespace AsynCUDA13.Shared.Utils
{
    /// <summary>
    /// Provides highly optimized, allocation-free helper methods for working with character sets.
    /// Useful for validating DTO strings against specific character rules (e.g., file names, HTML, SQL, etc.).
    /// </summary>
    public static partial class UniversalHelper
    {
        // --- Constant Building Blocks (Compile-Time) ---
        private static readonly string Numeric = "0123456789";
        private static readonly string AlphaLower = "abcdefghijklmnopqrstuvwxyz";
        private static readonly string AlphaUpper = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        private static readonly string Alpha = AlphaLower + AlphaUpper;
        private static readonly string Alphanumeric = Alpha + Numeric;

        // Language-specific & Extended
        private static readonly string GermanUmlauts = "äöüÄÖÜß";
        private static readonly string AlphaUmlauts = Alpha + GermanUmlauts;
        private static readonly string AlphanumericUmlauts = Alphanumeric + GermanUmlauts;

        // Special Characters
        private static readonly string Space = " ";
        private static readonly string Underscore = "_";
        private static readonly string Dash = "-";
        private static readonly string Apostrophe = "'";
        private static readonly string EmailSpecial = "@.-_";

        // --- Precompiled FrozenSets (Zero-Allocation at Runtime) ---

        // Invalid Sets
        private static readonly FrozenSet<char> InvalidNone = Array.Empty<char>().ToFrozenSet();
        private static readonly FrozenSet<char> InvalidControl = Enumerable.Range(0, 32).Append(127).Select(c => (char) c).ToFrozenSet();
        private static readonly FrozenSet<char> InvalidFileName = Path.GetInvalidFileNameChars().ToFrozenSet();
        private static readonly FrozenSet<char> InvalidPath = Path.GetInvalidPathChars().ToFrozenSet();
        private static readonly FrozenSet<char> InvalidHtmlDangerous = "<>\"'&".ToFrozenSet();
        private static readonly FrozenSet<char> InvalidSqlDangerous = "';-".ToFrozenSet();

        // Valid Sets
        private static readonly FrozenSet<char> ValidNone = Array.Empty<char>().ToFrozenSet();
        private static readonly FrozenSet<char> ValidNumeric = Numeric.ToFrozenSet();
        private static readonly FrozenSet<char> ValidAlpha = Alpha.ToFrozenSet();
        private static readonly FrozenSet<char> ValidAlphaUmlauts = AlphaUmlauts.ToFrozenSet();
        private static readonly FrozenSet<char> ValidAlphanumeric = Alphanumeric.ToFrozenSet();
        private static readonly FrozenSet<char> ValidAlphanumericUmlauts = AlphanumericUmlauts.ToFrozenSet();

        // Alphanumeric Combinations
        private static readonly FrozenSet<char> ValidAlphaNumSpace = (Alphanumeric + Space).ToFrozenSet();
        private static readonly FrozenSet<char> ValidAlphaNumUnderscore = (Alphanumeric + Underscore).ToFrozenSet();
        private static readonly FrozenSet<char> ValidAlphaNumDash = (Alphanumeric + Dash).ToFrozenSet();
        private static readonly FrozenSet<char> ValidAlphaNumSpaceUnderscore = (Alphanumeric + Space + Underscore).ToFrozenSet();
        private static readonly FrozenSet<char> ValidAlphaNumSpaceDash = (Alphanumeric + Space + Dash).ToFrozenSet();
        private static readonly FrozenSet<char> ValidAlphaNumUnderscoreDash = (Alphanumeric + Underscore + Dash).ToFrozenSet();
        private static readonly FrozenSet<char> ValidAlphaNumSpaceUnderscoreDash = (Alphanumeric + Space + Underscore + Dash).ToFrozenSet();
        private static readonly FrozenSet<char> ValidAlphaNumUmlautsSpaceDash = (AlphanumericUmlauts + Space + Dash).ToFrozenSet();

        // Domain-Specific Sets
        private static readonly FrozenSet<char> ValidHexadecimal = (Numeric + "abcdefABCDEF").ToFrozenSet();
        private static readonly FrozenSet<char> ValidBase64 = (Alphanumeric + "+/=").ToFrozenSet();
        private static readonly FrozenSet<char> ValidBase64UrlSafe = (Alphanumeric + "-_").ToFrozenSet();
        private static readonly FrozenSet<char> ValidPersonName = (AlphaUmlauts + Space + Dash + Apostrophe).ToFrozenSet();
        private static readonly FrozenSet<char> ValidEmailBasic = (Alphanumeric + EmailSpecial).ToFrozenSet();

        /// <summary>
        /// Retrieves a frozen set of invalid characters based on the specified <see cref="InvalidCharSets"/> value.
        /// Used for blacklisting approaches where specific characters must be forbidden.
        /// </summary>
        /// <param name="charSet">The target invalid character set.</param>
        /// <returns>A highly optimized, immutable set of invalid characters.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when an unknown enum value is provided.</exception>
        public static FrozenSet<char> GetInvalidCharHashSet(InvalidCharSets charSet) => charSet switch
        {
            InvalidCharSets.None => InvalidNone,
            InvalidCharSets.ControlCharacters => InvalidControl,
            InvalidCharSets.InvalidFileNameChars => InvalidFileName,
            InvalidCharSets.InvalidPathChars => InvalidPath,
            InvalidCharSets.HtmlDangerousChars => InvalidHtmlDangerous,
            InvalidCharSets.SqlDangerousChars => InvalidSqlDangerous,
            _ => throw new ArgumentOutOfRangeException(nameof(charSet), charSet, "Unknown InvalidCharSets value provided.")
        };

        /// <summary>
        /// Retrieves a frozen set of valid characters based on the specified <see cref="ValidCharSets"/> value.
        /// Used for whitelisting approaches where only explicitly permitted characters are allowed.
        /// </summary>
        /// <param name="charSet">The target valid character set.</param>
        /// <returns>A highly optimized, immutable set of valid characters.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when an unknown enum value is provided.</exception>
        public static FrozenSet<char> GetValidCharHashSet(ValidCharSets charSet) => charSet switch
        {
            ValidCharSets.None => ValidNone,
            ValidCharSets.Numeric => ValidNumeric,
            ValidCharSets.Alpha => ValidAlpha,
            ValidCharSets.AlphaWithUmlauts => ValidAlphaUmlauts,
            ValidCharSets.Alphanumeric => ValidAlphanumeric,
            ValidCharSets.AlphanumericWithUmlauts => ValidAlphanumericUmlauts,
            ValidCharSets.AlphanumericWithSpaces => ValidAlphaNumSpace,
            ValidCharSets.AlphanumericWithUnderscores => ValidAlphaNumUnderscore,
            ValidCharSets.AlphanumericWithDashes => ValidAlphaNumDash,
            ValidCharSets.AlphanumericWithSpacesAndUnderscores => ValidAlphaNumSpaceUnderscore,
            ValidCharSets.AlphanumericWithSpacesAndDashes => ValidAlphaNumSpaceDash,
            ValidCharSets.AlphanumericWithUnderscoresAndDashes => ValidAlphaNumUnderscoreDash,
            ValidCharSets.AlphanumericWithSpacesUnderscoresAndDashes => ValidAlphaNumSpaceUnderscoreDash,
            ValidCharSets.AlphanumericWithUmlautsSpacesAndDashes => ValidAlphaNumUmlautsSpaceDash,
            ValidCharSets.Hexadecimal => ValidHexadecimal,
            ValidCharSets.Base64 => ValidBase64,
            ValidCharSets.Base64UrlSafe => ValidBase64UrlSafe,
            ValidCharSets.PersonNameBasic => ValidPersonName,
            ValidCharSets.EmailBasic => ValidEmailBasic,
            _ => throw new ArgumentOutOfRangeException(nameof(charSet), charSet, "Unknown ValidCharSets value provided.")
        };

        /// <summary>
        /// Sanitizes the input string by replacing invalid characters or strings with a specified replacement string. Optionally, it can also separate camel case words with spaces.
        /// </summary>
        /// <param name="input">The input string to sanitize.</param>
        /// <param name="invalidStrings">A collection of strings that are considered invalid and should be replaced.</param>
        /// <param name="invalidChars">A collection of characters that are considered invalid and should be replaced.</param>
        /// <param name="replacement">The string to replace invalid characters or strings with.</param>
        /// <param name="caseSensitive">Indicates whether the comparison should be case-sensitive.</param>
        /// <param name="separateCamelCase">Indicates whether camel case words should be separated with spaces.</param>
        /// <returns>The sanitized string.</returns>
        public static string SanitizeString(this string input, IEnumerable<string>? invalidStrings = null, IEnumerable<char>? invalidChars = null, string replacement = " ", bool caseSensitive = true, bool separateCamelCase = false)
        {
            if (string.IsNullOrEmpty(input))
            {
                return input;
            }

            // 2. Zeichen sammeln (ohne massig LINQ-Overhead)
            var charSet = new HashSet<char>();

            if (invalidStrings != null)
            {
                foreach (var str in invalidStrings)
                {
                    foreach (var c in str)
                    {
                        charSet.Add(c);
                    }
                }
            }

            if (invalidChars != null)
            {
                foreach (var c in invalidChars)
                {
                    charSet.Add(c);
                }
            }

            string replaced = input;

            if (charSet.Count > 0)
            {
                // 3. Den StringComparer-Bug elegant lösen: 
                // Bei Case-Insensitive fügen wir einfach die Upper/Lower-Varianten zum Set hinzu.
                if (!caseSensitive)
                {
                    var caseInsensitiveSet = new HashSet<char>(charSet.Count * 2);
                    foreach (var c in charSet)
                    {
                        caseInsensitiveSet.Add(char.ToLowerInvariant(c));
                        caseInsensitiveSet.Add(char.ToUpperInvariant(c));
                    }
                    charSet = caseInsensitiveSet;
                }

                // 4. .NET 8+ Magie: Vectorized SearchValues. 
                // Sucht Hardware-beschleunigt in O(N) mit minimalsten Konstanten.
                var searchValues = SearchValues.Create(charSet.ToArray());

                // 5. Erst prüfen, ob überhaupt ein invalidChar drin ist (vermeidet unnötige Loops)
                if (input.AsSpan().IndexOfAny(searchValues) >= 0)
                {
                    // StringBuilder nutzen! Verhindert das LOH-Zumüllen durch string.Concat(c.ToString())
                    var sb = new StringBuilder(input.Length); // Capacity direkt setzen = keine Re-Allokation

                    foreach (char c in input)
                    {
                        if (searchValues.Contains(c)) // Contains auf SearchValues ist ultraschnell
                        {
                            sb.Append(replacement);
                        }
                        else
                        {
                            sb.Append(c);
                        }
                    }
                    replaced = sb.ToString();
                }
            }

            // 6. CamelCase splitten
            if (separateCamelCase)
            {
                replaced = CamelCaseRegex().Replace(replaced, " ");
            }

            return replaced;
        }

        /// <summary>
        /// Verifies whether the provided format string is valid for formatting DateTime or TimeSpan objects. If a reference object is provided, it will attempt to format that object using the specified format string. If the format string is invalid, an error message will be returned, and a default format string will be used instead.
        /// </summary>
        /// <param name="formatString">The format string to verify.</param>
        /// <param name="referenceObj">An optional reference object to test the format string against.</param>
        /// <param name="errorMessage">An output parameter that will contain an error message if the format string is invalid.</param>
        /// <returns>The original format string if it is valid; otherwise, a default format string.</returns>
        public static string VerifyFormatString(this string formatString, out string? errorMessage, object? referenceObj = null)
        {
            errorMessage = null;
            try
            {
                if (referenceObj != null)
                {
                    var testValue = referenceObj switch
                    {
                        DateTime dt => dt.ToString(formatString),
                        TimeSpan ts => ts.ToString(formatString),
                        IFormattable formattable => formattable.ToString(formatString, null),
                        _ => throw new ArgumentException("Reference object must be DateTime, TimeSpan, or implement IFormattable.")
                    };
                }
                else
                {
                    // Test with current DateTime if no reference object is provided
                    var testValue = DateTime.Now.ToString(formatString);
                }
                return formatString;
            }
            catch (Exception ex)
            {
                errorMessage = $"Invalid format string '{formatString}': {ex.Message}";
                return "HH-mm-ss.fff";
            }
        }

        [GeneratedRegex(@"(?<=[a-z])(?=[A-Z])")]
        private static partial Regex CamelCaseRegex();
    }

    /// <summary>
    /// Defines contexts for characters that are explicitly forbidden (Blacklisting).
    /// </summary>
    public enum InvalidCharSets
    {
        /// <summary>
        /// No restrictions. This set is empty.
        /// </summary>
        None = 0,

        /// <summary>
        /// Non-printable control characters that affect text formatting or behavior (ASCII 0-31 and 127).
        /// </summary>
        ControlCharacters = 1,

        /// <summary>
        /// Characters rejected by most file systems for file names (e.g., \, /, :, *, ?, &quot;, &lt;, &gt;, |).
        /// </summary>
        InvalidFileNameChars = 10,

        /// <summary>
        /// Characters rejected by most file systems for directory paths.
        /// </summary>
        InvalidPathChars = 11,

        /// <summary>
        /// Characters commonly used in Cross-Site Scripting (XSS) attacks (&lt;, &gt;, &quot;, &apos;, &amp;).
        /// </summary>
        HtmlDangerousChars = 20,

        /// <summary>
        /// Characters commonly used in SQL injection attacks (&apos;, ;, -).
        /// </summary>
        SqlDangerousChars = 21
    }

    /// <summary>
    /// Defines contexts for characters that are explicitly allowed (Whitelisting).
    /// </summary>
    public enum ValidCharSets
    {
        /// <summary>
        /// No valid characters. This set is empty and rejects all input.
        /// </summary>
        None = 0,

        /// <summary>
        /// Allows only digits (0-9). Useful for phone numbers, IDs, or numeric codes.
        /// </summary>
        Numeric = 1,

        /// <summary>
        /// Allows only standard alphabetic letters (A-Z, a-z).
        /// </summary>
        Alpha = 2,

        /// <summary>
        /// Allows alphabetic letters including German umlauts (A-Z, a-z, ÄÖÜäöü, ß).
        /// </summary>
        AlphaWithUmlauts = 3,

        /// <summary>
        /// Allows letters and digits (A-Z, a-z, 0-9). Useful for standard usernames or codes.
        /// </summary>
        Alphanumeric = 4,

        /// <summary>
        /// Allows letters, digits, and umlauts.
        /// </summary>
        AlphanumericWithUmlauts = 5,

        /// <summary>
        /// Allows letters, digits, and spaces.
        /// </summary>
        AlphanumericWithSpaces = 10,

        /// <summary>
        /// Allows letters, digits, and underscores. Useful for programming identifiers.
        /// </summary>
        AlphanumericWithUnderscores = 11,

        /// <summary>
        /// Allows letters, digits, and dashes. Useful for URL slugs.
        /// </summary>
        AlphanumericWithDashes = 12,

        /// <summary>
        /// Allows letters, digits, spaces, and underscores.
        /// </summary>
        AlphanumericWithSpacesAndUnderscores = 13,

        /// <summary>
        /// Allows letters, digits, spaces, and dashes.
        /// </summary>
        AlphanumericWithSpacesAndDashes = 14,

        /// <summary>
        /// Allows letters, digits, underscores, and dashes.
        /// </summary>
        AlphanumericWithUnderscoresAndDashes = 15,

        /// <summary>
        /// Allows letters, digits, spaces, underscores, and dashes.
        /// </summary>
        AlphanumericWithSpacesUnderscoresAndDashes = 16,

        /// <summary>
        /// Allows letters, digits, umlauts, spaces, and dashes.
        /// </summary>
        AlphanumericWithUmlautsSpacesAndDashes = 20,

        /// <summary>
        /// Allows only valid hexadecimal digits (0-9, A-F, a-f). Useful for color codes or cryptography.
        /// </summary>
        Hexadecimal = 30,

        /// <summary>
        /// Allows standard Base64 characters (A-Z, a-z, 0-9, +, /, =).
        /// </summary>
        Base64 = 31,

        /// <summary>
        /// Allows URL-safe Base64 characters (A-Z, a-z, 0-9, -, _).
        /// </summary>
        Base64UrlSafe = 32,

        /// <summary>
        /// Allows letters, umlauts, spaces, hyphens, and apostrophes. Ideal for standard person names (e.g., René O'Connor-Müller).
        /// </summary>
        PersonNameBasic = 40,

        /// <summary>
        /// Allows letters, digits, and special characters commonly required in email routing (@, ., -, _).
        /// </summary>
        EmailBasic = 41
    }
}