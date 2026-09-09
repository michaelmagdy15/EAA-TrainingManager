using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace EAATrainingManager.Helpers;

/// <summary>
/// High-performance Arabic text normalization, search sanitization, and BiDi formatting engine
/// tailored for Egyptian Aviation Academy pilot records and aviation codes.
/// </summary>
public static class ArabicTextHelper
{
    // Left-to-Right Marker (LRM) to protect Latin aviation codes and slashes in RTL contexts
    public const char LRM = '\u200E';
    public const string LRM_STRING = "\u200E";

    // Regex for all Arabic diacritics (Fatha, Damma, Kasra, Sukun, Shadda, Tanween, superscript Alef)
    private static readonly Regex TashkeelRegex = new(
        @"[\u064B-\u065F\u0670\u06D6-\u06ED]",
        RegexOptions.Compiled);

    // Regex for Tatweel (Kashida: ـ)
    private static readonly Regex TatweelRegex = new(
        @"\u0640+",
        RegexOptions.Compiled);

    // Regex for multiple whitespace characters
    private static readonly Regex MultiSpaceRegex = new(
        @"\s+",
        RegexOptions.Compiled);

    // Known Latin aviation terms to safely wrap in LRM
    private static readonly Regex AviationCodeRegex = new(
        @"\b(PPL|CPL|IR|ATP|ATPL|CESSNA|FAR|ME|SE|PIC|SIC|CPL\/IR|CPL-IR|IR\/CPL|IR-CPL|PPL\/IR|PPL-IR)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Normalizes Arabic text for deduplication and indexing:
    /// 1. Strips Tashkeel (diacritics)
    /// 2. Strips Tatweel (Kashida)
    /// 3. Normalizes Hamza: [أ إ آ ٱ] -> ا
    /// 4. Normalizes Yaa & Alif Maqsura: ى -> ي
    /// 5. Normalizes Taa Marbuta: ة -> ه
    /// 6. Normalizes Persian/Urdu variants (e.g. ك, ى)
    /// 7. Collapses whitespaces and trims
    /// </summary>
    public static string Normalize(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return string.Empty;

        // 1. Strip diacritics
        string text = TashkeelRegex.Replace(input, string.Empty);

        // 2. Strip Tatweel (ـ)
        text = TatweelRegex.Replace(text, string.Empty);

        var sb = new StringBuilder(text.Length);
        foreach (char c in text)
        {
            switch (c)
            {
                // Hamza variants -> bare Alif
                case 'أ':
                case 'إ':
                case 'آ':
                case 'ٱ':
                    sb.Append('ا');
                    break;

                // Alif Maqsura -> Yaa
                case 'ى':
                    sb.Append('ي');
                    break;

                // Taa Marbuta -> Haa
                case 'ة':
                    sb.Append('ه');
                    break;

                // Farsi/Urdu Kaf -> Arabic Kaf
                case 'ک':
                    sb.Append('ك');
                    break;

                // Farsi/Urdu Yaa -> Arabic Yaa
                case 'ی':
                case 'ے':
                    sb.Append('ي');
                    break;

                // Normalize zero-width chars and quotation marks
                case '\u200B': // Zero-width space
                case '\u200C': // ZWNJ
                case '\u200D': // ZWJ
                case '\uFEFF': // BOM
                    break;

                default:
                    sb.Append(c);
                    break;
            }
        }

        // 3. Normalize common compound name spacing (e.g., عبد الرحمن -> عبدالرحمن)
        string result = sb.ToString();
        result = NormalizeCompoundNames(result);

        // 4. Collapse multi-spaces & trim
        result = MultiSpaceRegex.Replace(result, " ").Trim();

        return result;
    }

    /// <summary>
    /// Normalizes spaces in common Arabic compound prefixes such as "عبد الـ", "ابو ", etc.
    /// to guarantee that "عبد الرحمن" matches "عبدالرحمن".
    /// </summary>
    public static string NormalizeCompoundNames(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        // Standardize "عبد " + name to "عبد" + name for normalized comparison
        text = Regex.Replace(text, @"\bعبد\s+", "عبد", RegexOptions.Compiled);

        // Standardize "ابو " + name to "ابو" + name
        text = Regex.Replace(text, @"\bابو\s+", "ابو", RegexOptions.Compiled);

        // Standardize "ابن " + name to "ابن" + name
        text = Regex.Replace(text, @"\bابن\s+", "ابن", RegexOptions.Compiled);

        return text;
    }

    /// <summary>
    /// Checks if a candidate Arabic name matches a search query with prefix insensitivity
    /// (e.g. ignoring the definite article "الـ", matching with or without hamzas, etc.)
    /// </summary>
    public static bool IsFuzzyMatch(string? candidate, string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return true;
        if (string.IsNullOrWhiteSpace(candidate))
            return false;

        string normCandidate = Normalize(candidate);
        string normQuery = Normalize(query);

        if (normCandidate.Contains(normQuery, StringComparison.OrdinalIgnoreCase))
            return true;

        // Strip "ال" from individual tokens if not matched directly
        string[] queryTokens = normQuery.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        string[] candidateTokens = normCandidate.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        int matchedTokens = 0;
        foreach (var qToken in queryTokens)
        {
            string cleanQ = StripDefiniteArticle(qToken);
            bool tokenFound = false;

            foreach (var cToken in candidateTokens)
            {
                string cleanC = StripDefiniteArticle(cToken);
                if (cleanC.Contains(cleanQ, StringComparison.OrdinalIgnoreCase) ||
                    cleanQ.Contains(cleanC, StringComparison.OrdinalIgnoreCase))
                {
                    tokenFound = true;
                    break;
                }
            }

            if (tokenFound)
                matchedTokens++;
        }

        return matchedTokens == queryTokens.Length;
    }

    /// <summary>
    /// Strips the Arabic definite article "الـ" from the start of a token if length permits.
    /// </summary>
    public static string StripDefiniteArticle(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return string.Empty;

        if (token.StartsWith("ال") && token.Length > 3)
            return token[2..];

        return token;
    }

    /// <summary>
    /// Wraps Latin aviation codes, numbers, and slashes with Left-to-Right Markers (LRM: \u200E)
    /// to prevent bracket flipping, slash inversion, and punctuation displacement in RTL layouts.
    /// Example: "أحمد - (CPL/IR)" -> "أحمد - ‎(CPL/IR)‎"
    /// </summary>
    public static string WrapAviationBiDi(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        // Wrap recognized aviation codes
        string result = AviationCodeRegex.Replace(text, match => $"{LRM}{match.Value}{LRM}");

        // Ensure parentheses containing Latin characters are preserved
        result = Regex.Replace(result, @"\(([A-Za-z0-9\/\s\-]+)\)", match => $"{LRM}({match.Groups[1].Value}){LRM}");

        return result;
    }

    /// <summary>
    /// Sanitizes and cleans a display name for Arabic trainees: removes rogue symbols,
    /// trims excess spaces, but preserves correct Arabic letters and standard Hamzas.
    /// </summary>
    public static string CleanDisplayName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return string.Empty;

        // Remove diacritics and tatweel from display name for maximum readability
        string cleaned = TashkeelRegex.Replace(name, string.Empty);
        cleaned = TatweelRegex.Replace(cleaned, string.Empty);

        // Collapse whitespace
        cleaned = MultiSpaceRegex.Replace(cleaned, " ").Trim();

        return cleaned;
    }
}
