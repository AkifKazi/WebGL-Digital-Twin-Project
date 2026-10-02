using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Keeps the language table (Assets/StreamingAssets/Localization/ui_text.csv)
/// complete. Collects every key the views use, with its English copy: the
/// UIText calls in the runtime code, the sensor labels of the default machine
/// configuration, and the part names, service texts and control copy in the
/// open scene. Keys missing from the table are added with their English copy;
/// rows already there, and every translation, are left exactly as they are,
/// so a customised table is never overwritten.
/// </summary>
public static class LanguageTableTool
{
    public const string TablePath = "Assets/StreamingAssets/Localization/ui_text.csv";
    private const string RuntimeScripts = "Assets/Scripts/Runtime";
    private const string ScenePath = "Assets/Scenes/SampleScene.unity";

    private static readonly string[] Months =
        { "JAN", "FEB", "MAR", "APR", "MAY", "JUN", "JUL", "AUG", "SEP", "OCT", "NOV", "DEC" };

    private static readonly string[] Days = { "SUN", "MON", "TUE", "WED", "THU", "FRI", "SAT" };

    // UIText.Get("key", "fallback") and UIText.Format("key", "fallback", ...).
    private static readonly Regex GetCall = new(
        @"UIText\.(?:Get|Format)\(\s*""(?<key>[a-z0-9_.]+)""\s*,\s*""(?<text>(?:[^""\\]|\\.)*)""",
        RegexOptions.Compiled);

    // UITextBinding.Bind(label, "key", "fallback").
    private static readonly Regex BindCall = new(
        @"UITextBinding\.Bind\([^;]*?,\s*""(?<key>[a-z0-9_.]+)""\s*,\s*""(?<text>(?:[^""\\]|\\.)*)""\s*\)",
        RegexOptions.Compiled);

    // Builders that take the English caption, then its key: AddFigure(..., "RUN", "service.figure.run").
    private static readonly Regex CaptionThenKey = new(
        @"(?:AddLegendKey|AddFigure|AddNote|AddDataRow|CreateLegendItem)\([^;]*?""(?<text>(?:[^""\\]|\\.)*)""\s*,\s*""(?<key>[a-z0-9_]+\.[a-z0-9_.]+)""",
        RegexOptions.Compiled);

    [MenuItem("Tools/Digital Twin/Update Language Table")]
    public static void UpdateFromMenu()
    {
        UpdateTable(out int added, out List<string> unused);
        EditorUtility.DisplayDialog(
            "Language table",
            $"{added} key(s) added to {TablePath}.\n" +
            (unused.Count > 0 ? $"{unused.Count} row(s) are not used by the code or scene (kept)." : "Every row is in use."),
            "OK");
    }

    /// <summary>Batch entry point: opens the sample scene, then updates the table.</summary>
    public static void UpdateInBatch()
    {
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        UpdateTable(out int added, out List<string> unused);
        Debug.Log($"LANGUAGE_TABLE_UPDATE added={added} unused={unused.Count} {string.Join(",", unused)}");
        EditorApplication.Exit(0);
    }

    public static void UpdateTable(out int added, out List<string> unused)
    {
        SortedDictionary<string, (string text, string note)> wanted = CollectKeys();

        List<List<string>> records = File.Exists(TablePath)
            ? UIText.ParseCsv(File.ReadAllText(TablePath))
            : new List<List<string>>();

        List<string> header = records.FirstOrDefault(r => r.Count > 0 && r[0].Trim().Equals("key", StringComparison.OrdinalIgnoreCase));
        if (header == null)
        {
            header = new List<string> { "key", "en", "notes" };
            records.Insert(0, new List<string> { "# Language table for the digital twin. Edit the copy in any column; add a column per language (de, fr, es...)." });
            records.Insert(1, new List<string> { "# Empty cells fall back to en, then to the copy built into the code. Keep {0} placeholders. Reload the page to see changes." });
            records.Add(header);
        }

        int english = header.FindIndex(c => c.Trim().Equals("en", StringComparison.OrdinalIgnoreCase));
        if (english < 0)
        {
            header.Insert(1, "en");
            english = 1;
            foreach (List<string> record in records)
            {
                if (record != header && record.Count > 1 && !IsComment(record))
                    record.Insert(1, string.Empty);
            }
        }
        int notes = header.FindIndex(c => c.Trim().Equals("notes", StringComparison.OrdinalIgnoreCase));

        HashSet<string> present = new(StringComparer.Ordinal);
        foreach (List<string> record in records)
        {
            if (record != header && record.Count > 0 && !IsComment(record) && record[0].Trim().Length > 0)
                present.Add(record[0].Trim());
        }

        added = 0;
        foreach (KeyValuePair<string, (string text, string note)> entry in wanted)
        {
            if (present.Contains(entry.Key))
                continue;
            List<string> row = Enumerable.Repeat(string.Empty, header.Count).ToList();
            row[0] = entry.Key;
            row[english] = entry.Value.text;
            if (notes >= 0)
                row[notes] = entry.Value.note;
            records.Add(row);
            added++;
        }

        unused = present.Where(key => !wanted.ContainsKey(key)).OrderBy(key => key).ToList();

        Directory.CreateDirectory(Path.GetDirectoryName(TablePath));
        StringBuilder builder = new();
        foreach (List<string> record in records)
        {
            if (IsComment(record))
            {
                builder.Append(string.Join(",", record)).Append('\n');
                continue;
            }
            builder.Append(string.Join(",", record.Select(UIText.EscapeCsv))).Append('\n');
        }
        // UTF-8 with a byte order mark: spreadsheet programs then read ° and · correctly.
        File.WriteAllText(TablePath, builder.ToString(), new UTF8Encoding(true));
        AssetDatabase.ImportAsset(TablePath);
    }

    /// <summary>Every key the views ask for, with its English copy and a note for translators.</summary>
    public static SortedDictionary<string, (string text, string note)> CollectKeys()
    {
        SortedDictionary<string, (string text, string note)> keys = new(StringComparer.Ordinal);

        foreach (string file in Directory.GetFiles(RuntimeScripts, "*.cs", SearchOption.AllDirectories))
        {
            string code = File.ReadAllText(file);
            string where = Area(Path.GetFileNameWithoutExtension(file));
            foreach (Regex pattern in new[] { GetCall, BindCall, CaptionThenKey })
            {
                foreach (Match match in pattern.Matches(code))
                    Add(keys, match.Groups["key"].Value, Regex.Unescape(match.Groups["text"].Value), where);
            }
        }

        foreach (TelemetryHistoryRange range in TelemetryHistoryRanges.All)
            Add(keys, TelemetryHistoryRanges.ShortLabelKey(range), TelemetryHistoryRanges.ShortLabel(range), $"Range button: one {range.ToString().ToLowerInvariant()}");
        for (int i = 0; i < Months.Length; i++)
            Add(keys, $"date.month.{i + 1}", Months[i], "Short month name on chart axes and service dates");
        foreach (string day in Days)
            Add(keys, $"date.day.{day.ToLowerInvariant()}", day, "Short day name on the week chart");

        DigitalTwinMachineConfiguration configuration = DigitalTwinMachineConfigurationLoader.LoadDefault();
        if (configuration != null)
        {
            foreach (MachineSensorDefinition sensor in configuration.sensors)
            {
                if (sensor != null && !string.IsNullOrWhiteSpace(sensor.id))
                    Add(keys, "sensor." + sensor.id, sensor.label, $"Sensor label ({sensor.unit})");
            }
        }

        foreach (MachinePartGroup part in UnityEngine.Object.FindObjectsByType<MachinePartGroup>(FindObjectsInactive.Include))
            Add(keys, ServicePanelView.PartKey(part), part.DisplayName, "Machine part name");

        foreach (TelemetryServiceSchedule schedule in UnityEngine.Object.FindObjectsByType<TelemetryServiceSchedule>(FindObjectsInactive.Include))
        {
            foreach (TelemetryServiceSchedule.ServicePlan plan in schedule.Plans)
            {
                if (plan.part == null)
                    continue;
                string key = ServicePanelView.PartKey(plan.part);
                Add(keys, key + ".cause", plan.likelyCause, $"Likely cause shown for {plan.part.DisplayName}");
                Add(keys, key + ".fix", plan.suggestedFix, $"Suggested fix shown for {plan.part.DisplayName}");
                Add(keys, key + ".materials", plan.materials, $"Materials listed for {plan.part.DisplayName}");
            }
        }

        foreach (ViewModeSegmentedControl control in UnityEngine.Object.FindObjectsByType<ViewModeSegmentedControl>(FindObjectsInactive.Include))
        {
            SerializedObject data = new(control);
            foreach (string field in new[] { "exteriorText", "sectionText", "xrayText" })
            {
                string text = data.FindProperty(field)?.stringValue;
                if (!string.IsNullOrWhiteSpace(text))
                    Add(keys, "view." + UIText.Slug(text), text, "View mode segment");
            }
        }

        foreach (DigitalTwinModeSwitch modeSwitch in UnityEngine.Object.FindObjectsByType<DigitalTwinModeSwitch>(FindObjectsInactive.Include))
        {
            AddButtonLabel(keys, modeSwitch.TelemetryButton, "mode.telemetry");
            AddButtonLabel(keys, modeSwitch.ServiceButton, "mode.service");
        }

        return keys;
    }

    /// <summary>Where a script's copy appears, for the notes column.</summary>
    private static string Area(string script) => script switch
    {
        "ConnectionHealthView" => "Connection status bar",
        "TelemetryGraphView" or "TelemetryStatusTimelineGraph" or "TelemetryLineChartGraph" => "Telemetry detail panel: graphs",
        "TelemetryKeyFiguresGraph" => "Telemetry detail panel: key figures",
        "TelemetryAiDataTable" => "Telemetry detail panel: AI insight text",
        "ServicePanelView" => "Service panel: list",
        "ServicePanelView.Detail" => "Service panel: part detail",
        "PortraitTelemetrySheetController" => "Telemetry sheet on a phone: second-tap hint",
        _ => script
    };

    private static void AddButtonLabel(SortedDictionary<string, (string, string)> keys, Button button, string key)
    {
        TMP_Text label = button != null ? button.GetComponentInChildren<TMP_Text>(true) : null;
        if (label != null)
            Add(keys, key, label.text, "Telemetry / Service switch");
    }

    private static void Add(SortedDictionary<string, (string text, string note)> keys, string key, string text, string note)
    {
        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(text) || keys.ContainsKey(key))
            return;
        keys[key] = (text, note);
    }

    private static bool IsComment(List<string> record) =>
        record.Count > 0 && record[0].TrimStart().StartsWith("#", StringComparison.Ordinal);
}
