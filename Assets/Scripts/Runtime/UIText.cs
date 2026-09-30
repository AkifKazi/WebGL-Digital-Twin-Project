using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

/// <summary>
/// The language table behind every piece of on-screen copy. Text is looked up
/// by key, in the current language, from a CSV table the site owner can edit
/// without touching code (see <see cref="UITextTableLoader"/>):
///
///   key,en,de,notes
///   service.title,SERVICE SCHEDULE,WARTUNGSPLAN,Service panel heading
///
/// The first column is the key, a column named notes, note, comment, context
/// or description is ignored, and every other column is a language code. An
/// empty cell falls back to the table's default language, then to the English
/// copy compiled into the code, so a half-translated table never shows a blank
/// label. Lines starting with # are comments.
///
/// Translated copy can be longer or shorter than the English: the views that
/// show it size, wrap, scroll or shorten their labels to fit.
/// </summary>
public static class UIText
{
    public const string DefaultLanguage = "en";

    private static readonly HashSet<string> IgnoredColumns = new(StringComparer.OrdinalIgnoreCase)
    {
        "notes", "note", "comment", "comments", "context", "description"
    };

    private static readonly string[] EnglishMonths =
    {
        "JAN", "FEB", "MAR", "APR", "MAY", "JUN", "JUL", "AUG", "SEP", "OCT", "NOV", "DEC"
    };

    private static readonly string[] EnglishDays = { "SUN", "MON", "TUE", "WED", "THU", "FRI", "SAT" };

    // key -> one cell per language column, in column order.
    private static readonly Dictionary<string, string[]> rows = new(StringComparer.Ordinal);
    private static readonly List<string> languages = new();
    private static int languageColumn = -1;
    private static int defaultColumn = -1;

    /// <summary>Raised after the table or the language changes. Views re-apply their copy.</summary>
    public static event Action Changed;

    /// <summary>The language shown now, as its column code.</summary>
    public static string Language { get; private set; } = DefaultLanguage;

    /// <summary>Language codes the loaded table has columns for.</summary>
    public static IReadOnlyList<string> Languages => languages;

    /// <summary>Keys the loaded table defines.</summary>
    public static IEnumerable<string> Keys => rows.Keys;

    /// <summary>Counts every change, so a view can tell its copy is out of date.</summary>
    public static int Version { get; private set; }

    public static bool HasTable => rows.Count > 0;

    // Play mode without a domain reload keeps statics: start each run clean,
    // with no handlers left over from views the last run destroyed.
    [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetForPlayMode()
    {
        Changed = null;
        rows.Clear();
        languages.Clear();
        languageColumn = -1;
        defaultColumn = -1;
        Language = DefaultLanguage;
    }

    /// <summary>
    /// The copy for <paramref name="key"/> in the current language. Falls back
    /// to the table's default language, then to <paramref name="fallback"/>.
    /// </summary>
    public static string Get(string key, string fallback)
    {
        if (!string.IsNullOrEmpty(key) && rows.TryGetValue(key, out string[] cells))
        {
            string text = Cell(cells, languageColumn);
            if (string.IsNullOrEmpty(text))
                text = Cell(cells, defaultColumn);
            if (!string.IsNullOrEmpty(text))
                return text;
        }

        return fallback ?? key ?? string.Empty;
    }

    /// <summary>True when the table has non-empty copy for <paramref name="key"/> in any language column.</summary>
    public static bool Has(string key)
    {
        if (string.IsNullOrEmpty(key) || !rows.TryGetValue(key, out string[] cells))
            return false;
        foreach (string cell in cells)
        {
            if (!string.IsNullOrEmpty(cell))
                return true;
        }
        return false;
    }

    /// <summary>
    /// <see cref="Get"/> with {0}-style placeholders filled in. A translation
    /// with a broken placeholder falls back to the English copy rather than
    /// throwing, so one bad cell cannot stop a view from drawing.
    /// </summary>
    public static string Format(string key, string fallback, params object[] args)
    {
        string template = Get(key, fallback);
        if (TryFormat(template, args, out string text))
            return text;
        if (fallback != null && TryFormat(fallback, args, out text))
            return text;
        return template;
    }

    /// <summary>
    /// Copy for text authored in the inspector or a data asset: the key is
    /// <paramref name="prefix"/> and a slug of the authored text, so
    /// "FULL BODY" under "view" reads view.full_body. The authored text is
    /// the fallback.
    /// </summary>
    public static string GetAuthored(string prefix, string authored)
    {
        if (string.IsNullOrWhiteSpace(authored))
            return authored ?? string.Empty;
        return Get(prefix + "." + Slug(authored), authored);
    }

    /// <summary>Upper-case three-letter month name, January = 1.</summary>
    public static string ShortMonth(int month)
    {
        int index = Math.Clamp(month, 1, 12) - 1;
        return Get("date.month." + (index + 1).ToString(CultureInfo.InvariantCulture), EnglishMonths[index]);
    }

    /// <summary>Upper-case three-letter day name.</summary>
    public static string ShortDay(DayOfWeek day)
    {
        int index = (int)day;
        return Get("date.day." + EnglishDays[index].ToLowerInvariant(), EnglishDays[index]);
    }

    /// <summary>
    /// Lower case, letters and digits kept, every run of anything else one
    /// underscore: "Oil Change" becomes oil_change, "X-RAY" x_ray.
    /// </summary>
    public static string Slug(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        StringBuilder builder = new(text.Length);
        bool pendingSeparator = false;
        foreach (char c in text.Trim())
        {
            if (char.IsLetterOrDigit(c))
            {
                if (pendingSeparator && builder.Length > 0)
                    builder.Append('_');
                builder.Append(char.ToLowerInvariant(c));
                pendingSeparator = false;
            }
            else
            {
                pendingSeparator = true;
            }
        }
        return builder.ToString();
    }

    /// <summary>
    /// Replaces the table with <paramref name="csv"/>. On failure the table in
    /// use is kept and <paramref name="error"/> says why. The current language
    /// is kept when the new table has it, otherwise the default is used.
    /// </summary>
    public static bool LoadCsv(string csv, out string error) => LoadCsv(csv, DefaultLanguage, out error);

    public static bool LoadCsv(string csv, string defaultLanguage, out string error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(csv))
        {
            error = "the table is empty";
            return false;
        }

        List<List<string>> records;
        try
        {
            records = ParseCsv(csv);
        }
        catch (FormatException exception)
        {
            error = exception.Message;
            return false;
        }

        int headerIndex = records.FindIndex(record => !IsSkippable(record));
        if (headerIndex < 0)
        {
            error = "the table has no header row";
            return false;
        }

        List<string> header = records[headerIndex];
        if (!string.Equals(header[0].Trim(), "key", StringComparison.OrdinalIgnoreCase))
        {
            error = "the first column of the header must be 'key'";
            return false;
        }

        // Header column index -> language slot, or -1 for ignored columns.
        List<string> newLanguages = new();
        int[] slots = new int[header.Count];
        slots[0] = -1;
        for (int column = 1; column < header.Count; column++)
        {
            string code = NormaliseLanguage(header[column]);
            if (string.IsNullOrEmpty(code) || IgnoredColumns.Contains(code) || newLanguages.Contains(code))
            {
                slots[column] = -1;
                continue;
            }
            slots[column] = newLanguages.Count;
            newLanguages.Add(code);
        }

        if (newLanguages.Count == 0)
        {
            error = "the table has no language columns";
            return false;
        }

        Dictionary<string, string[]> newRows = new(StringComparer.Ordinal);
        for (int r = headerIndex + 1; r < records.Count; r++)
        {
            List<string> record = records[r];
            if (IsSkippable(record))
                continue;

            string key = record[0].Trim();
            if (key.Length == 0)
                continue;

            string[] cells = new string[newLanguages.Count];
            for (int column = 1; column < record.Count && column < slots.Length; column++)
            {
                if (slots[column] >= 0)
                    cells[slots[column]] = Unescape(record[column]);
            }

            // A key listed twice keeps its last row, as a spreadsheet reads top to bottom.
            newRows[key] = cells;
        }

        rows.Clear();
        foreach (KeyValuePair<string, string[]> row in newRows)
            rows.Add(row.Key, row.Value);
        languages.Clear();
        languages.AddRange(newLanguages);

        string preferredDefault = NormaliseLanguage(defaultLanguage);
        defaultColumn = languages.IndexOf(preferredDefault);
        if (defaultColumn < 0)
            defaultColumn = languages.IndexOf(DefaultLanguage);
        if (defaultColumn < 0)
            defaultColumn = 0;

        int current = languages.IndexOf(Language);
        languageColumn = current >= 0 ? current : defaultColumn;
        Language = languages[languageColumn];
        RaiseChanged();
        return true;
    }

    /// <summary>
    /// Shows <paramref name="language"/> (a column code such as en or de_ch;
    /// case and a dash or underscore do not matter). False, and nothing
    /// changes, when the table has no such column.
    /// </summary>
    public static bool SetLanguage(string language)
    {
        string code = NormaliseLanguage(language);
        if (string.IsNullOrEmpty(code))
            return false;

        int column = languages.IndexOf(code);
        if (column < 0)
        {
            // de-AT falls back to de when the table has no regional column.
            int dash = code.IndexOf('_');
            if (dash > 0)
                column = languages.IndexOf(code.Substring(0, dash));
        }

        if (column < 0)
        {
            // Without a table only the compiled English exists.
            if (languages.Count == 0 && code == DefaultLanguage)
                return true;
            return false;
        }

        if (column == languageColumn)
            return true;

        languageColumn = column;
        Language = languages[column];
        RaiseChanged();
        return true;
    }

    /// <summary>Forgets the table and returns to the compiled English copy.</summary>
    public static void Clear()
    {
        rows.Clear();
        languages.Clear();
        languageColumn = -1;
        defaultColumn = -1;
        Language = DefaultLanguage;
        RaiseChanged();
    }

    /// <summary>
    /// Normalises a language code: trimmed, lower case, with a dash turned
    /// into an underscore so pt-BR and pt_br name the same column.
    /// </summary>
    public static string NormaliseLanguage(string code) =>
        string.IsNullOrWhiteSpace(code) ? string.Empty : code.Trim().ToLowerInvariant().Replace('-', '_');

    // -----------------------------------------------------------------------
    // CSV
    // -----------------------------------------------------------------------

    /// <summary>
    /// RFC 4180 CSV: commas separate cells, a cell in double quotes may hold
    /// commas, line breaks and doubled quotes. A byte order mark and CRLF or
    /// LF line ends are accepted. Semicolon-separated files, as some
    /// spreadsheet locales save them, are detected from the header row.
    /// </summary>
    public static List<List<string>> ParseCsv(string csv)
    {
        List<List<string>> records = new();
        if (string.IsNullOrEmpty(csv))
            return records;

        int start = csv[0] == '﻿' ? 1 : 0;
        char separator = DetectSeparator(csv, start);
        List<string> record = new();
        StringBuilder cell = new();
        bool quoted = false;
        bool cellStarted = false;
        int line = 1;

        for (int i = start; i < csv.Length; i++)
        {
            char c = csv[i];
            if (quoted)
            {
                if (c == '"')
                {
                    if (i + 1 < csv.Length && csv[i + 1] == '"')
                    {
                        cell.Append('"');
                        i++;
                    }
                    else
                    {
                        quoted = false;
                    }
                }
                else
                {
                    if (c == '\n')
                        line++;
                    cell.Append(c);
                }
                continue;
            }

            if (c == '"' && !cellStarted)
            {
                quoted = true;
                cellStarted = true;
            }
            else if (c == separator)
            {
                record.Add(cell.ToString());
                cell.Clear();
                cellStarted = false;
            }
            else if (c == '\r' || c == '\n')
            {
                if (c == '\r' && i + 1 < csv.Length && csv[i + 1] == '\n')
                    i++;
                record.Add(cell.ToString());
                records.Add(record);
                record = new List<string>();
                cell.Clear();
                cellStarted = false;
                line++;
            }
            else
            {
                // A stray quote inside an unquoted cell is kept as text, as
                // spreadsheets do.
                cell.Append(c);
                cellStarted = true;
            }
        }

        if (quoted)
            throw new FormatException($"a quoted cell opened before line {line} is never closed");

        if (cell.Length > 0 || record.Count > 0)
        {
            record.Add(cell.ToString());
            records.Add(record);
        }

        return records;
    }

    /// <summary>Quotes a cell when it needs it, for writing a table back out.</summary>
    public static string EscapeCsv(string value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;
        bool needsQuotes = value.IndexOfAny(new[] { ',', '"', '\n', '\r', ';' }) >= 0 ||
                           value[0] == ' ' || value[value.Length - 1] == ' ' || value[0] == '#';
        return needsQuotes ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
    }

    private static char DetectSeparator(string csv, int start)
    {
        // The first line that is not a comment decides.
        int i = start;
        while (i < csv.Length)
        {
            int end = csv.IndexOf('\n', i);
            if (end < 0)
                end = csv.Length;
            string lineText = csv.Substring(i, end - i).Trim();
            if (lineText.Length > 0 && lineText[0] != '#')
            {
                int commas = Count(lineText, ',');
                int semicolons = Count(lineText, ';');
                return semicolons > commas ? ';' : ',';
            }
            i = end + 1;
        }
        return ',';
    }

    private static int Count(string text, char c)
    {
        int count = 0;
        foreach (char x in text)
        {
            if (x == c)
                count++;
        }
        return count;
    }

    private static bool IsSkippable(List<string> record)
    {
        if (record.Count == 0)
            return true;
        string first = record[0].TrimStart();
        if (first.StartsWith("#", StringComparison.Ordinal))
            return true;
        foreach (string cell in record)
        {
            if (!string.IsNullOrWhiteSpace(cell))
                return false;
        }
        return true;
    }

    /// <summary>A literal \n in a cell is a line break, for editors that cannot type one.</summary>
    private static string Unescape(string cell) =>
        string.IsNullOrEmpty(cell) ? string.Empty : cell.Replace("\\n", "\n");

    private static string Cell(string[] cells, int column) =>
        column >= 0 && column < cells.Length ? cells[column] : null;

    private static bool TryFormat(string template, object[] args, out string text)
    {
        try
        {
            text = string.Format(CultureInfo.InvariantCulture, template, args ?? Array.Empty<object>());
            return true;
        }
        catch (FormatException)
        {
            text = null;
            return false;
        }
    }

    private static void RaiseChanged()
    {
        Version++;
        Action handlers = Changed;
        if (handlers == null)
            return;

        // One failing view must not stop the others updating.
        foreach (Delegate handler in handlers.GetInvocationList())
        {
            try
            {
                ((Action)handler)();
            }
            catch (Exception exception)
            {
                UnityEngine.Debug.LogException(exception);
            }
        }
    }
}
