using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Batch-only stress test of the language table, the AI data table, the
/// history backfill and the process simulator (edit mode, no play mode):
///
///   Unity -batchmode -projectPath . -executeMethod FeedbackStressTest.Run
///
/// Writes Logs/FeedbackTest/*.csv|json for inspection and exits 0 when every
/// check passes. Never saves a scene.
/// </summary>
public static class FeedbackStressTest
{
    private const string ScenePath = "Assets/Scenes/SampleScene.unity";
    public const string OutputDirectory = "Logs/FeedbackTest";

    private static int failures;
    private static int checks;

    public static void Run()
    {
        failures = 0;
        checks = 0;
        Directory.CreateDirectory(OutputDirectory);

        Section("csv parser and language switching", TestLanguageTable);
        Section("url language parameter", TestUrlLanguage);
        Section("shipped language table", TestShippedTable);
        Section("simulator 24 h run", TestSimulator);
        Section("history backfill", TestBackfill);
        Section("ai data table", TestAiTable);

        UIText.Clear();
        string result = failures == 0 ? "passed" : $"failed ({failures} of {checks})";
        Debug.Log($"FEEDBACK_TEST_RESULT {result} checks={checks}");
        EditorApplication.Exit(failures == 0 ? 0 : 1);
    }

    private static void Section(string name, Action test)
    {
        Debug.Log($"FEEDBACK_TEST section: {name}");
        try
        {
            test();
        }
        catch (Exception exception)
        {
            Expect(false, $"{name} threw {exception}");
        }
    }

    private static void Expect(bool condition, string description)
    {
        checks++;
        if (!condition)
            failures++;
        Debug.Log($"FEEDBACK_TEST {(condition ? "ok" : "FAIL")}: {description}");
    }

    // -----------------------------------------------------------------------
    // Language table
    // -----------------------------------------------------------------------

    private static void TestLanguageTable()
    {
        UIText.Clear();
        Expect(UIText.Get("missing.key", "Fallback") == "Fallback", "no table: the code's English copy shows");
        Expect(UIText.Get("missing.key", null) == "missing.key", "no table and no fallback: the key shows, never null");
        Expect(UIText.SetLanguage("en") && !UIText.SetLanguage("de"), "no table: only en can be chosen");

        int changes = 0;
        void Count() => changes++;
        UIText.Changed += Count;

        string basic = "key,en,de,notes\nhello,Hello,Hallo,a greeting\nonly_en,Only English,,\nspaced ,  padded  ,x,\n";
        Expect(UIText.LoadCsv(basic, out string error) && error == null, "a plain table loads");
        Expect(UIText.Languages.SequenceEqual(new[] { "en", "de" }), "the notes column is not a language");
        Expect(UIText.Get("hello", "x") == "Hello", "the default language is en");
        Expect(UIText.SetLanguage("de") && UIText.Get("hello", "x") == "Hallo", "switching to de shows the de copy");
        Expect(UIText.Get("only_en", "x") == "Only English", "an empty cell falls back to en");
        Expect(UIText.Get("not.there", "Code copy") == "Code copy", "a key missing from the table falls back to the code");
        Expect(UIText.Get("spaced", "?") == "x", "a trimmed key finds its row and keeps the cell as written");
        int before = changes;
        Expect(UIText.SetLanguage("DE") && changes == before, "choosing the language already shown changes nothing");
        Expect(!UIText.SetLanguage("fr") && UIText.Language == "de", "an unknown language is refused and the current one kept");
        Expect(!UIText.SetLanguage("") && !UIText.SetLanguage(null) && !UIText.SetLanguage("   "), "empty language codes are refused");

        // Quotes, commas, line breaks, CRLF, a byte order mark, comments and blank lines.
        string tricky = "﻿# comment, with, commas\r\n\r\nkey,en\r\n" +
                        "comma,\"one, two\"\r\n" +
                        "quote,\"he said \"\"stop\"\"\"\r\n" +
                        "multi,\"line one\nline two\"\r\n" +
                        "escaped,first\\nsecond\r\n" +
                        "dup,first\r\n" +
                        "dup,second\r\n" +
                        "short\r\n" +
                        "extra,a,b,c,d\r\n" +
                        "last,no newline at the end";
        Expect(UIText.LoadCsv(tricky, out error), $"a table with quoting, comments and CRLF loads ({error})");
        Expect(UIText.Language == "en", "a table without the current language falls back to the default");
        Expect(UIText.Get("comma", "") == "one, two", "a quoted comma stays in its cell");
        Expect(UIText.Get("quote", "") == "he said \"stop\"", "doubled quotes become one");
        Expect(UIText.Get("multi", "") == "line one\nline two", "a quoted line break stays in its cell");
        Expect(UIText.Get("escaped", "") == "first\nsecond", "a typed \\n becomes a line break");
        Expect(UIText.Get("dup", "") == "second", "a key listed twice keeps its last row");
        Expect(UIText.Get("short", "fallback") == "fallback", "a row with no copy falls back");
        Expect(UIText.Get("extra", "") == "a", "cells past the header are ignored");
        Expect(UIText.Get("last", "") == "no newline at the end", "the last row needs no line end");
        Expect(!UIText.Has("# comment"), "comment lines are not keys");

        Expect(UIText.LoadCsv("key;en;de\nsemi;Semi;Halb\nwith;\"a;b\";x", out error) && UIText.Get("semi", "") == "Semi" &&
               UIText.Get("with", "") == "a;b", "a semicolon-separated table (spreadsheet export) loads");

        // Bad tables are refused and the table in use is kept.
        UIText.LoadCsv("key,en,de\nkeep,Kept,Behalten", out _);
        UIText.SetLanguage("de");
        foreach ((string csv, string what) in new[]
                 {
                     ("", "an empty table"),
                     ("   \n\n", "a blank table"),
                     ("# only a comment", "a table of comments"),
                     ("id,en\na,b", "a header without key"),
                     ("key,notes\na,b", "a table without language columns"),
                     ("key,en\na,\"never closed", "an unclosed quote"),
                     (null, "a null table")
                 })
        {
            Expect(!UIText.LoadCsv(csv, out error) && !string.IsNullOrEmpty(error) &&
                   UIText.Get("keep", "") == "Behalten" && UIText.Language == "de",
                $"{what} is refused ({error}) and the table in use kept");
        }

        // Language codes: case, dashes and regional fallback.
        Expect(UIText.LoadCsv("key,EN,De,pt-BR,en\nk,E,D,P,second en", out _), "a table with mixed-case codes loads");
        Expect(UIText.Languages.SequenceEqual(new[] { "en", "de", "pt_br" }), "codes are normalised and a repeated column ignored");
        Expect(UIText.SetLanguage("PT_br") && UIText.Get("k", "") == "P", "pt-BR, pt_br and PT_br name one column");
        Expect(UIText.SetLanguage("de-AT") && UIText.Language == "de", "de-AT falls back to de");
        Expect(!UIText.SetLanguage("pt") && UIText.Language == "de", "pt does not match pt_br");

        // Placeholders: a broken translation falls back to the English format.
        UIText.LoadCsv("key,en,de\nfmt,{0} of {1},{0} von {1}\nbroken,{0} ok,{0 kaputt\nmore,{0},{0} {2}", out _);
        UIText.SetLanguage("de");
        Expect(UIText.Format("fmt", "{0} of {1}", 3, 4) == "3 von 4", "placeholders fill in the translation");
        Expect(UIText.Format("broken", "{0} ok", 5) == "5 ok", "a broken placeholder falls back to the English copy");
        Expect(UIText.Format("more", "{0}", 7) == "7", "a placeholder with no value falls back to the English copy");
        Expect(UIText.Format("absent", "{0} GOOD · {1} ISSUE", 1, 2) == "1 GOOD · 2 ISSUE", "formatting works with no row");
        Expect(UIText.Format("absent", "{0", 1) == "{0", "a broken code format returns the text rather than throwing");

        // One failing listener must not stop the rest.
        bool secondCalled = false;
        void Throw() => throw new InvalidOperationException("test listener");
        void Second() => secondCalled = true;
        UIText.Changed += Throw;
        UIText.Changed += Second;
        UIText.SetLanguage("en");
        UIText.Changed -= Throw;
        UIText.Changed -= Second;
        Expect(secondCalled, "a listener that throws does not stop the others");

        UIText.LoadCsv("key,en\nview.full_body,WHOLE MACHINE", out _);
        Expect(UIText.GetAuthored("view", "FULL BODY") == "WHOLE MACHINE", "authored copy is keyed by its slug");
        Expect(UIText.GetAuthored("view", "CROSS SECTION") == "CROSS SECTION", "authored copy without a row keeps the authored text");
        Expect(UIText.GetAuthored("view", null) == string.Empty && UIText.GetAuthored("view", " ") == " ", "empty authored copy passes through");

        Expect(UIText.Slug("FULL BODY") == "full_body" && UIText.Slug("X-RAY") == "x_ray" &&
               UIText.Slug("  Oil  Change ") == "oil_change" && UIText.Slug("°C / temp") == "c_temp" &&
               UIText.Slug("") == string.Empty && UIText.Slug(null) == string.Empty, "slugs are stable");
        Expect(UIText.ShortMonth(0) == "JAN" && UIText.ShortMonth(13) == "DEC" && UIText.ShortMonth(7) == "JUL",
            "month names clamp out-of-range months");
        Expect(UIText.ShortDay(DayOfWeek.Wednesday) == "WED", "day names");

        string[] awkward = { "a,b", "say \"hi\"", "two\nlines", " lead", "trail ", "#hash", "semi;colon", "", "°C · µm" };
        string row = "key," + string.Join(",", awkward.Select(UIText.EscapeCsv));
        List<string> parsed = UIText.ParseCsv(row)[0];
        Expect(parsed.Skip(1).SequenceEqual(awkward), "escaped cells read back exactly");

        UIText.Changed -= Count;
        Expect(changes > 5, $"listeners hear every table load and language change ({changes})");
        UIText.Clear();
    }

    private static void TestUrlLanguage()
    {
        (string url, string expected)[] cases =
        {
            ("https://site/index.html?lang=de", "de"),
            ("https://site/?a=1&lang=fr#top", "fr"),
            ("https://site/?LANG=pt-BR", "pt-BR"),
            ("https://site/?lang=%64e", "de"),
            ("https://site/?lang=", null),
            ("https://site/?lang", null),
            ("https://site/?language=de", null),
            ("https://site/index.html", null),
            ("https://site/?lang=<script>", null),
            ("https://site/?lang=de%20fr", null),
            ("https://site/?lang=abcdefghijklmnopqrstuvwxyz", null),
            ("", null),
            (null, null)
        };
        foreach ((string url, string expected) in cases)
        {
            bool found = UITextTableLoader.TryReadUrlLanguage(url, "lang", out string language);
            Expect(found == (expected != null) && language == expected, $"url '{url}' gives '{language ?? "nothing"}'");
        }
    }

    private static void TestShippedTable()
    {
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        string path = LanguageTableTool.TablePath;
        Expect(File.Exists(path), $"the table ships at {path}");
        string csv = File.ReadAllText(path);
        Expect(UIText.LoadCsv(csv, out string error), $"the shipped table loads ({error})");
        Expect(UIText.Languages.Contains("en"), "the shipped table has an en column");

        SortedDictionary<string, (string text, string note)> used = LanguageTableTool.CollectKeys();
        List<string> missing = used.Keys.Where(key => !UIText.Has(key)).ToList();
        Expect(missing.Count == 0, $"every key the views use is in the table ({used.Count} keys; missing: {string.Join(", ", missing)})");

        List<List<string>> records = UIText.ParseCsv(csv);
        List<string> header = records.First(r => r.Count > 0 && r[0] == "key");
        int en = header.IndexOf("en");
        List<List<string>> rows = records.Where(r => r != header && r.Count > 0 && r[0].Length > 0 && !r[0].StartsWith("#")).ToList();
        List<string> duplicates = rows.GroupBy(r => r[0]).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        Expect(duplicates.Count == 0, $"no key is listed twice ({string.Join(", ", duplicates)})");
        List<string> emptyEnglish = rows.Where(r => r.Count <= en || string.IsNullOrWhiteSpace(r[en])).Select(r => r[0]).ToList();
        Expect(emptyEnglish.Count == 0, $"every row has English copy ({string.Join(", ", emptyEnglish)})");

        // Every translation keeps the English placeholders.
        Regex placeholder = new(@"\{\d+\}");
        List<string> mismatched = new();
        foreach (List<string> row in rows)
        {
            string[] english = placeholder.Matches(row[en]).Select(m => m.Value).OrderBy(v => v).ToArray();
            for (int column = 1; column < header.Count && column < row.Count; column++)
            {
                if (column == en || header[column] == "notes" || string.IsNullOrEmpty(row[column]))
                    continue;
                string[] other = placeholder.Matches(row[column]).Select(m => m.Value).OrderBy(v => v).ToArray();
                if (!other.SequenceEqual(english))
                    mismatched.Add($"{row[0]}[{header[column]}]");
            }
        }
        Expect(mismatched.Count == 0, $"translations keep their placeholders ({string.Join(", ", mismatched)})");

        // The English table must reproduce the compiled copy exactly, so shipping
        // it changes nothing on screen.
        List<string> differs = used.Where(pair => UIText.Get(pair.Key, "\u0000") != pair.Value.text)
            .Select(pair => pair.Key).ToList();
        Expect(differs.Count == 0, $"the en column matches the built-in copy ({string.Join(", ", differs)})");
        UIText.Clear();
    }

    // -----------------------------------------------------------------------
    // Simulator
    // -----------------------------------------------------------------------

    private sealed class SignalLog
    {
        public PerformanceStatSource Source;
        public double MinuteSum;
        public int MinuteCount;
        public readonly List<double> MinuteMeans = new();
        public readonly Dictionary<StatVisualState, int> States = new();
        public bool NonFinite;
        public int Steps;
    }

    private static void TestSimulator()
    {
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        HopperProcessSimulator simulator = UnityEngine.Object.FindAnyObjectByType<HopperProcessSimulator>(FindObjectsInactive.Include);
        TelemetryRegistry registry = UnityEngine.Object.FindAnyObjectByType<TelemetryRegistry>(FindObjectsInactive.Include);
        Expect(simulator != null && registry != null, "the scene has the simulator and the registry");
        if (simulator == null || registry == null)
            return;

        registry.RebuildIndex();
        Invoke(simulator, "Awake");
        MethodInfo step = typeof(HopperProcessSimulator).GetMethod("StepModel", BindingFlags.Instance | BindingFlags.NonPublic);

        // Varied production demand, as shipped.
        Dictionary<string, SignalLog> logs = RunSimulator(simulator, registry, step, 24 * 3600, 1f, 100f);
        WriteMinuteTable(logs, Path.Combine(OutputDirectory, "simulator_24h_per_minute.csv"));

        List<string> silent = logs.Values.Where(l => l.Source.SourceTimestampUnixMs <= 0 || l.Source.ProviderId != "sim.process-model")
            .Select(l => l.Source.StatId).ToList();
        Expect(silent.Count == 0, $"every sensor receives simulated readings ({logs.Count} sensors; silent: {string.Join(", ", silent)})");
        Expect(logs.Values.All(l => !l.NonFinite), "no reading is NaN or infinite over 24 h");

        // Not flat: every hour of every sensor moves.
        foreach (SignalLog log in logs.Values)
        {
            List<double> hourRanges = HourRanges(log.MinuteMeans);
            double dayRange = log.MinuteMeans.Max() - log.MinuteMeans.Min();
            double mean = log.MinuteMeans.Average();
            int flatHours = hourRanges.Count(r => r <= Math.Max(1e-4, Math.Abs(mean) * 1e-4));
            Debug.Log($"FEEDBACK_TEST signal {log.Source.StatId,-30} mean={mean,10:0.###} 24h-range={dayRange,9:0.###} " +
                      $"median-hour-range={Median(hourRanges),8:0.###} flat-hours={flatHours} " +
                      $"states={string.Join(" ", log.States.Select(s => $"{s.Key}:{100.0 * s.Value / log.Steps:0.#}%"))}");
            Expect(flatHours == 0, $"{log.Source.StatId} moves in every hour of the day");
        }

        SignalLog temperature = logs["VIB-01.MOTOR.TEMP"];
        List<double> temperatureHours = HourRanges(temperature.MinuteMeans);
        Expect(temperatureHours.Count(r => r >= 1.0) >= temperatureHours.Count * 0.75,
            $"motor temperature swings at least 1 °C in most hours (median {Median(temperatureHours):0.00} °C) - the flat chart the mentor saw");
        Expect(temperature.MinuteMeans.Max() < 80f, $"motor temperature stays below its 80 °C warning (max {temperature.MinuteMeans.Max():0.0})");

        // No alarm the model did not raise before: only the conveyor demonstration alarms.
        string[] demonstration = { "FEED-01.SLIP", "FEED-01.BELT.SPEED" };
        foreach (SignalLog log in logs.Values.Where(l => !demonstration.Contains(l.Source.StatId)))
        {
            int alarms = log.States.Where(s => s.Key is StatVisualState.Warning or StatVisualState.Critical).Sum(s => s.Value);
            Expect(alarms <= log.Steps * 0.001, $"{log.Source.StatId} raises no new alarms ({100.0 * alarms / log.Steps:0.###}% of the time)");
        }
        Expect(Share(logs["FEED-01.SLIP"], StatVisualState.Critical) > 0.9, "the belt slip demonstration still reads Critical");
        Expect(Share(logs["FEED-01.BELT.SPEED"], StatVisualState.Warning) > 0.9, "the belt underspeed demonstration still reads Warning");

        SignalLog level = logs["HOP-01.LEVEL"];
        Expect(level.MinuteMeans.Min() > 20f && level.MinuteMeans.Max() < 85f,
            $"the hopper level stays in its normal band ({level.MinuteMeans.Min():0.0} to {level.MinuteMeans.Max():0.0} %)");

        // Long sessions: the unscaled clock at 30 days still gives finite, moving values.
        Dictionary<string, SignalLog> late = RunSimulator(simulator, registry, step, 2 * 3600, 1f, 30f * 24f * 3600f);
        Expect(late.Values.All(l => !l.NonFinite) && HourRanges(late["VIB-01.MOTOR.TEMP"].MinuteMeans).All(r => r > 0.05),
            "after 30 days of page time the model still moves and stays finite");

        // With the demand swing off the model settles flat: the variation is what fixes the chart.
        SerializedObject data = new(simulator);
        data.FindProperty("demandVariation").floatValue = 0f;
        data.ApplyModifiedPropertiesWithoutUndo();
        Invoke(simulator, "ResetModel");
        Dictionary<string, SignalLog> flat = RunSimulator(simulator, registry, step, 3 * 3600, 1f, 200f);
        double flatRange = Median(HourRanges(flat["VIB-01.MOTOR.TEMP"].MinuteMeans));
        Debug.Log($"FEEDBACK_TEST motor temperature median hourly range without demand variation: {flatRange:0.000} °C");
        Expect(flatRange < 0.5, "without the demand swing the motor temperature is flat, as in the screenshot");
        WriteMinuteTable(flat, Path.Combine(OutputDirectory, "simulator_3h_without_variation.csv"));
    }

    private static Dictionary<string, SignalLog> RunSimulator(
        HopperProcessSimulator simulator,
        TelemetryRegistry registry,
        MethodInfo step,
        int seconds,
        float stepSeconds,
        float clockStart)
    {
        Dictionary<string, SignalLog> logs = registry.Sources.Where(s => s != null)
            .ToDictionary(s => s.StatId, s => new SignalLog { Source = s });
        object[] arguments = new object[2];
        int stepsPerMinute = Mathf.RoundToInt(60f / stepSeconds);
        int total = Mathf.RoundToInt(seconds / stepSeconds);

        for (int i = 0; i < total; i++)
        {
            arguments[0] = stepSeconds;
            arguments[1] = clockStart + i * stepSeconds;
            step.Invoke(simulator, arguments);

            foreach (SignalLog log in logs.Values)
            {
                float value = log.Source.CurrentValue;
                if (!float.IsFinite(value))
                    log.NonFinite = true;
                log.MinuteSum += value;
                log.MinuteCount++;
                log.Steps++;
                log.States.TryGetValue(log.Source.VisualState, out int count);
                log.States[log.Source.VisualState] = count + 1;
                if (log.MinuteCount == stepsPerMinute)
                {
                    log.MinuteMeans.Add(log.MinuteSum / log.MinuteCount);
                    log.MinuteSum = 0d;
                    log.MinuteCount = 0;
                }
            }
        }
        return logs;
    }

    private static List<double> HourRanges(List<double> minuteMeans)
    {
        List<double> ranges = new();
        for (int start = 0; start + 60 <= minuteMeans.Count; start += 60)
        {
            List<double> hour = minuteMeans.GetRange(start, 60);
            ranges.Add(hour.Max() - hour.Min());
        }
        return ranges;
    }

    private static double Median(List<double> values)
    {
        if (values.Count == 0)
            return 0d;
        List<double> sorted = values.OrderBy(v => v).ToList();
        return sorted[sorted.Count / 2];
    }

    private static double Share(SignalLog log, StatVisualState state) =>
        log.States.TryGetValue(state, out int count) ? (double)count / log.Steps : 0d;

    private static void WriteMinuteTable(Dictionary<string, SignalLog> logs, string path)
    {
        List<SignalLog> ordered = logs.Values.OrderBy(l => l.Source.StatId).ToList();
        StringBuilder builder = new();
        builder.Append("minute,").Append(string.Join(",", ordered.Select(l => l.Source.StatId))).Append('\n');
        int minutes = ordered.Min(l => l.MinuteMeans.Count);
        for (int m = 0; m < minutes; m++)
        {
            builder.Append(m.ToString(CultureInfo.InvariantCulture));
            foreach (SignalLog log in ordered)
                builder.Append(',').Append(log.MinuteMeans[m].ToString("0.####", CultureInfo.InvariantCulture));
            builder.Append('\n');
        }
        File.WriteAllText(path, builder.ToString());
    }

    // -----------------------------------------------------------------------
    // Backfill
    // -----------------------------------------------------------------------

    private static void TestBackfill()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        TelemetryHistoryRecorder recorder = new GameObject("Recorder").AddComponent<TelemetryHistoryRecorder>();
        PerformanceStatSource[] sources =
        {
            // Nominal 20.8 kW with the warning at 21: the case that drew history past the line.
            MakeSource("POWER", "MOTOR POWER", "kW", TelemetryLimitMode.OutsideRange, 8f, 4f, 21f, 23f, 20.8f),
            MakeSource("TEMP", "MOTOR TEMPERATURE", "°C", TelemetryLimitMode.HighOnly, 0f, 0f, 80f, 95f, 65f),
            MakeSource("LOW", "PRESSURE", "bar", TelemetryLimitMode.LowOnly, 2f, 1f, 0f, 0f, 2.2f),
            MakeSource("NEG", "AMBIENT", "°C", TelemetryLimitMode.OutsideRange, -5f, -15f, 40f, 48f, -4f),
            MakeSource("OFF", "COUNTER", "", TelemetryLimitMode.Disabled, 0f, 0f, 0f, 0f, 0f),
            MakeSource("TIGHT", "TIGHT BAND", "", TelemetryLimitMode.OutsideRange, 9.99f, 9f, 10.01f, 11f, 10f)
        };

        List<TelemetryHistoryBucket> buckets = new();
        System.Random random = new(7);
        int bucketsChecked = 0;
        List<string> problems = new();
        foreach (PerformanceStatSource source in sources)
        {
            TelemetryLimits limits = source.Limits;
            for (int trial = 0; trial < 40; trial++)
            {
                long end = 1_600_000_000_000L + (long)(random.NextDouble() * 400_000_000_000L);
                foreach (TelemetryHistoryRange range in TelemetryHistoryRanges.All)
                {
                    recorder.TryGetHistory(source, range, end, buckets);
                    if (buckets.Count != TelemetryHistoryRanges.BucketCount(range))
                        problems.Add($"{source.StatId} {range}: {buckets.Count} buckets");
                    for (int i = 0; i < buckets.Count; i++)
                    {
                        TelemetryHistoryBucket bucket = buckets[i];
                        bucketsChecked++;
                        if (!bucket.Generated)
                            problems.Add($"{source.StatId} {range}: bucket not flagged generated");
                        if (!float.IsFinite(bucket.Mean) || !float.IsFinite(bucket.Min) || !float.IsFinite(bucket.Max))
                            problems.Add($"{source.StatId} {range}: non-finite bucket");
                        if (bucket.Min > bucket.Mean + 1e-4f || bucket.Max < bucket.Mean - 1e-4f)
                            problems.Add($"{source.StatId} {range}: min/max do not contain the mean");
                        if (i > 0 && bucket.StartUnixMs - buckets[i - 1].StartUnixMs != TelemetryHistoryRanges.BucketMilliseconds(range))
                            problems.Add($"{source.StatId} {range}: buckets not contiguous");

                        bool alarm = bucket.WarningSeconds > 0f || bucket.CriticalSeconds > 0f;
                        StatVisualState drawn = limits.Evaluate(bucket.Mean);
                        if (!alarm && limits.Mode != TelemetryLimitMode.Disabled &&
                            (drawn != StatVisualState.Normal || limits.Evaluate(bucket.Min) != StatVisualState.Normal ||
                             limits.Evaluate(bucket.Max) != StatVisualState.Normal))
                            problems.Add($"{source.StatId} {range}: a normal bucket drawn at {bucket.Mean:0.###} past a limit");
                    }
                }
            }
        }
        Expect(problems.Count == 0, $"generated history is consistent over {bucketsChecked} buckets ({string.Join("; ", problems.Distinct().Take(8))})");

        // Generated history must still move, even hard against a limit.
        recorder.TryGetHistory(sources[0], TelemetryHistoryRange.Hour, 1_790_000_000_000L, buckets);
        List<float> normal = buckets.Where(b => b.WarningSeconds <= 0f && b.CriticalSeconds <= 0f).Select(b => b.Mean).ToList();
        Expect(normal.Max() - normal.Min() > 0.1f, $"history near a limit is not flattened ({normal.Min():0.00} to {normal.Max():0.00} kW)");
    }

    // -----------------------------------------------------------------------
    // AI data table
    // -----------------------------------------------------------------------

    private static void TestAiTable()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        GameObject runtime = new("Digital Twin Runtime");
        TelemetryRegistry registry = runtime.AddComponent<TelemetryRegistry>();
        FeedbackTestHistoryProvider history = new();

        PerformanceStatSource trending = MakeSource("TREND", "MOTOR TEMP, \"hot\"\nside", "°C", TelemetryLimitMode.HighOnly, 0f, 0f, 80f, 95f, 60f);
        PerformanceStatSource empty = MakeSource("EMPTY", "NO HISTORY", "bar", TelemetryLimitMode.OutsideRange, 2f, 1f, 8f, 9f, 5f);
        PerformanceStatSource past = MakeSource("PAST", "PAST LIMIT", "A", TelemetryLimitMode.HighOnly, 0f, 0f, 40f, 50f, 45f);
        PerformanceStatSource falling = MakeSource("FALL", "FALLING", "%", TelemetryLimitMode.LowOnly, 20f, 10f, 0f, 0f, 50f);
        registry.RebuildIndex();

        const long now = 1_790_000_030_000L; // 30 s into a minute
        long hourNow = TelemetryHistoryRanges.AlignDown(now, 60_000L);
        history.Bucket = (source, range, start, bucketMs) =>
        {
            if (source == empty)
                return null;
            double hours = (start + bucketMs * 0.5 - now) / 3_600_000d;
            double slope = source == falling ? -30d : 12d;
            double value = (source == falling ? 50d : 60d) + slope * hours;
            // The oldest ten minutes of the hour are generated backfill.
            bool generated = range == TelemetryHistoryRange.Hour && start < hourNow - 49 * 60_000L;
            return new TelemetryHistoryBucket
            {
                SampleCount = 60,
                Sum = value * 60d,
                Min = (float)value - 1f,
                Max = (float)value + 1f,
                Last = (float)value,
                NormalSeconds = generated ? 60f : 30f,
                WarningSeconds = generated ? 0f : 20f,
                CriticalSeconds = generated ? 0f : 10f,
                Generated = generated
            };
        };

        TelemetryAiDataTable table = runtime.AddComponent<TelemetryAiDataTable>();
        SerializedObject data = new(table);
        data.FindProperty("registry").objectReferenceValue = registry;
        data.ApplyModifiedPropertiesWithoutUndo();
        table.UseHistoryProvider(history);
        table.SetAiWindows("15,60");

        TelemetryAiTable sensors = table.BuildSensorTable(now);
        Expect(sensors.Rows.Count == 4, "one row per sensor");
        object Cell(PerformanceStatSource source, string column) =>
            sensors.Rows.First(r => (string)r[sensors.IndexOf("sensor_id")] == source.StatId)[sensors.IndexOf(column)];
        double Number(PerformanceStatSource source, string column) => Convert.ToDouble(Cell(source, column), CultureInfo.InvariantCulture);

        Expect(Math.Abs(Number(trending, "w15_slope_per_h") - 12d) < 1e-6, $"the 15 min trend is 12 per hour ({Number(trending, "w15_slope_per_h")})");
        Expect(Math.Abs(Number(trending, "w60_slope_per_h") - 12d) < 1e-6, "the 60 min trend is 12 per hour");
        Expect(Math.Abs(Number(trending, "w60_coverage") - 50d / 60d) < 1e-3, $"generated buckets do not count as coverage ({Number(trending, "w60_coverage")})");
        Expect(Math.Abs(Number(trending, "w15_coverage") - 1d) < 1e-9, "a fully recorded window has coverage 1");
        double expectedMean = Enumerable.Range(0, 15).Select(i => 60d + 12d * ((hourNow - i * 60_000L + 30_000L - now) / 3_600_000d)).Average();
        Expect(Math.Abs(Number(trending, "w15_mean") - expectedMean) < 1e-4, $"the 15 min mean is exact ({Number(trending, "w15_mean"):0.0000} vs {expectedMean:0.0000})");
        Expect(Math.Abs(Number(trending, "w60_warning_s") - 50 * 20d) < 1e-6 && Math.Abs(Number(trending, "w60_critical_s") - 50 * 10d) < 1e-6,
            "alarm time counts recorded buckets only");
        Expect(Math.Abs(Number(trending, "minutes_to_warning") - 100d) < 0.1, $"60 to 80 at 12 per hour is 100 minutes ({Cell(trending, "minutes_to_warning")})");
        Expect(Math.Abs(Number(trending, "margin_to_warning_pct") - 25d) < 1e-6, "the margin is a quarter of the band");
        Expect(Number(past, "minutes_to_warning") == 0d, "a reading already past its warning reads 0 minutes");
        Expect(Math.Abs(Number(falling, "minutes_to_warning") - 60d) < 0.1, "a falling reading projects to its low limit (50 to 20 at 30 per hour)");
        Expect(Cell(empty, "w15_mean") == null && Cell(empty, "w60_slope_per_h") == null && Cell(empty, "minutes_to_warning") == null &&
               Number(empty, "w15_coverage") == 0d, "a sensor without history has empty figures, not zeros");

        string csv = sensors.ToCsv();
        List<List<string>> parsed = UIText.ParseCsv(csv);
        Expect(parsed[0].Count == sensors.Columns.Count && parsed.Count(r => r.Count > 1) == sensors.Rows.Count + 1,
            "the CSV has one header and one line per row, whatever the labels hold");
        Expect(parsed.Any(r => r.Contains("MOTOR TEMP, \"hot\"\nside")), "a label with a comma, quotes and a line break survives the CSV");
        Expect(!csv.Contains("NaN") && !csv.Contains("Infinity"), "no NaN or Infinity in the CSV");

        data.Update();
        data.FindProperty("includeGeneratedHistory").boolValue = true;
        data.ApplyModifiedPropertiesWithoutUndo();
        Expect(Math.Abs(NumberIn(table.BuildSensorTable(now), "w60_coverage", "TREND") - 1d) < 1e-9, "generated history counts only when asked for");
        data.FindProperty("includeGeneratedHistory").boolValue = false;
        data.ApplyModifiedPropertiesWithoutUndo();

        TelemetryAiTable series = table.BuildSeriesTable(now);
        int generatedColumn = series.IndexOf("generated");
        Expect(series.Rows.Count == 3 * 50 && series.Rows.All(r => (bool)r[generatedColumn] == false),
            $"the series holds recorded buckets only ({series.Rows.Count} rows)");

        string json = table.BuildJson(now, true);
        File.WriteAllText(Path.Combine(OutputDirectory, "ai_table_logic.json"), json);
        File.WriteAllText(Path.Combine(OutputDirectory, "ai_sensors_logic.csv"), csv);
        Expect(!json.Contains("NaN") && !json.Contains("Infinity") && json.StartsWith("{") && json.EndsWith("}"), "the JSON has no NaN and is one object");

        // Windows: invalid, repeated and too many.
        table.SetAiWindows("abc, -5, 0, 99999999, 60, 15, 15");
        Expect(table.Windows.SequenceEqual(new[] { 15, 60 }), $"bad windows are dropped and the rest sorted ({string.Join(",", table.Windows)})");
        table.SetAiWindows("");
        Expect(table.Windows.SequenceEqual(new[] { 15, 60 }), "an empty window list keeps the windows in use");
        table.SetAiWindows("1,2,3,4,5,6,7,8,9,10");
        Expect(table.Windows.Count == 8, "at most eight windows");
        table.SetAiWindows("525600");
        Expect(table.BuildSensorTable(now).Columns.Any(c => c.Name == "w525600_mean"), "a year-long window uses the year range");

        // Insights sent back by the page.
        table.PushAiInsight("{\"sensorId\":\"TREND\",\"text\":\"Bearing <b>hot</b>\",\"ttlSeconds\":-4}");
        Expect(table.TryGetInsight(trending, TelemetryHistoryRange.Hour, null, out string insight) &&
               insight.Contains("<noparse><</noparse>b>"), $"an insight shows, with markup made harmless ({insight})");
        table.PushAiInsight("{\"insights\":[{\"sensorId\":\"PAST\",\"text\":\"Check load\"},{\"sensorId\":\"\",\"text\":\"x\"},{\"text\":\"no id\"}]}");
        Expect(table.TryGetInsight(past, TelemetryHistoryRange.Hour, null, out _), "a batch of insights shows");
        table.PushAiInsight("{\"sensorId\":\"TREND\",\"text\":\"\"}");
        Expect(!table.TryGetInsight(trending, TelemetryHistoryRange.Hour, null, out _), "empty text removes an insight");
        foreach (string bad in new[] { "", "not json", "{", "null", "[]", "{\"sensorId\":5}" })
            table.PushAiInsight(bad);
        table.PushAiInsight("{\"sensorId\":\"FALL\",\"text\":\"" + new string('x', 5000) + "\"}");
        Expect(table.TryGetInsight(falling, TelemetryHistoryRange.Hour, null, out string longText) && longText.Length < 700,
            "a very long insight is cut short");
        table.ClearAiInsights();
        Expect(!table.TryGetInsight(past, TelemetryHistoryRange.Hour, null, out _), "insights clear");

        // Without a history provider or schedule, the table still builds.
        table.UseHistoryProvider(null);
        TelemetryAiTable bare = table.BuildSensorTable(now);
        Expect(bare.Rows.Count == 4 && table.BuildMaintenanceTable(now).Rows.Count == 0 && table.BuildSeriesTable(now).Rows.Count == 0,
            "with no history and no schedule the tables build, empty where they must be");
    }

    private static double NumberIn(TelemetryAiTable table, string column, string sensor) =>
        Convert.ToDouble(table.Rows.First(r => (string)r[table.IndexOf("sensor_id")] == sensor)[table.IndexOf(column)], CultureInfo.InvariantCulture);

    // -----------------------------------------------------------------------

    private static PerformanceStatSource MakeSource(
        string id, string label, string unit, TelemetryLimitMode mode,
        float warningBelow, float criticalBelow, float warningAbove, float criticalAbove, float value)
    {
        PerformanceStatSource source = new GameObject(id).AddComponent<PerformanceStatSource>();
        SerializedObject data = new(source);
        data.FindProperty("statId").stringValue = id;
        data.FindProperty("metricName").stringValue = label;
        data.FindProperty("unit").stringValue = unit;
        data.FindProperty("useAutomaticThresholds").boolValue = true;
        data.FindProperty("limitMode").enumValueIndex = (int)mode;
        data.FindProperty("warningBelow").floatValue = warningBelow;
        data.FindProperty("criticalBelow").floatValue = criticalBelow;
        data.FindProperty("warningAbove").floatValue = warningAbove;
        data.FindProperty("criticalAbove").floatValue = criticalAbove;
        data.FindProperty("currentValue").floatValue = value;
        data.ApplyModifiedPropertiesWithoutUndo();
        return source;
    }

    private static void Invoke(object target, string method) =>
        target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            .Invoke(target, null);
}
