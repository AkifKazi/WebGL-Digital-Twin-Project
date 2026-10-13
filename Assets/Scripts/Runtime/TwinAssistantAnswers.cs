using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

/// <summary>One sensor as the assistant sees it: a snapshot, so answers need no scene.</summary>
public readonly struct TwinSensorFact
{
    public readonly string Id;
    public readonly string Label;
    public readonly string Unit;
    public readonly float Value;
    public readonly string ValueText;
    public readonly StatVisualState State;
    public readonly TelemetryLimits Limits;
    /// <summary>Trend over the last hour, in units per hour; NaN when there is too little history.</summary>
    public readonly double SlopePerHour;
    /// <summary>Language table key of the part the sensor is read from (part.bearings); empty when none.</summary>
    public readonly string PartKey;

    public TwinSensorFact(string id, string label, string unit, float value, string valueText,
        StatVisualState state, TelemetryLimits limits, double slopePerHour, string partKey)
    {
        Id = id;
        Label = label;
        Unit = unit;
        Value = value;
        ValueText = valueText;
        State = state;
        Limits = limits;
        SlopePerHour = slopePerHour;
        PartKey = partKey ?? string.Empty;
    }
}

/// <summary>One part's next service as the assistant sees it.</summary>
public readonly struct TwinServiceFact
{
    public readonly string PartKey;
    public readonly string PartName;
    public readonly ServiceTask Task;
    public readonly double HoursLeft;
    public readonly ServiceState State;
    /// <summary>What to do at the service, as the plan words it.</summary>
    public readonly string Fix;

    public TwinServiceFact(string partKey, string partName, ServiceTask task, double hoursLeft, ServiceState state,
        string fix)
    {
        PartKey = partKey ?? string.Empty;
        PartName = partName;
        Task = task;
        HoursLeft = hoursLeft;
        State = state;
        Fix = fix ?? string.Empty;
    }
}

/// <summary>
/// A question worth asking next: as shown (in the viewer's language), and as
/// the twin's rules read it (in English), so a translated suggestion still
/// gets its answer.
/// </summary>
public readonly struct TwinFollowUp
{
    public readonly string Text;
    /// <summary>The question for the twin's own rules; the shown text when they are the same.</summary>
    public readonly string Question;

    public TwinFollowUp(string text, string question = null)
    {
        Text = text ?? string.Empty;
        Question = string.IsNullOrWhiteSpace(question) ? Text : question;
    }

    public override string ToString() => Text;
}

/// <summary>
/// An answer: a sentence or two, the readings and services it rests on (shown
/// as cards and rows the viewer can open), an optional suggested check, and
/// questions worth asking next.
/// </summary>
public sealed class TwinAssistantReply
{
    public string Text = string.Empty;
    public readonly List<string> SensorIds = new();
    public readonly List<string> PartKeys = new();
    public string Check = string.Empty;
    public readonly List<TwinFollowUp> FollowUps = new();
}

/// <summary>
/// The assistant's answers when no language model is connected: plain rules
/// over the twin's own figures, the same ones the AI data table publishes. It
/// recognises a sensor named in the question, and questions about alarms, the
/// machine's state and its services; anything else gets a pointer to what it
/// can answer. Questions are matched in English; answers come from the
/// language table (ask.*), so they translate.
///
/// Every number in an answer is read from the facts it is given: it never
/// guesses, and says so when a reading is unusable.
/// </summary>
public static class TwinAssistantAnswers
{
    /// <summary>Two services this close (operating hours) can share one stop.</summary>
    public const double ShareStopHours = 48d;

    private const int MaximumEvidence = 4;

    private static readonly string[] ServiceWords =
        { "service", "maintenance", "maintain", "due", "overdue", "regrease", "replace", "inspect", "schedule", "stop" };

    private static readonly string[] ShareWords =
        { "one stop", "same stop", "share", "combine", "together", "at once" };

    private static readonly string[] AlarmWords = { "why", "red", "critical", "warning", "amber" };

    private static readonly string[] OverviewWords =
    {
        "status", "summary", "overview", "how is", "how's", "how are", "doing", "wrong", "problem", "issue",
        "alarm", "healthy", "health", "ok", "okay", "anything"
    };

    // ---- greeting and suggestions -----------------------------------------------

    public static TwinAssistantReply Greeting(IReadOnlyList<TwinSensorFact> sensors, IReadOnlyList<TwinServiceFact> services)
    {
        TwinAssistantReply reply = new()
        {
            Text = UIText.Get("ask.greeting",
                "Ask me about this machine: a reading, an alarm, or what needs service next.")
        };
        AddFollowUps(reply, sensors, services, null);
        return reply;
    }

    // ---- answering ----------------------------------------------------------------

    public static TwinAssistantReply Answer(string question, IReadOnlyList<TwinSensorFact> sensors,
        IReadOnlyList<TwinServiceFact> services)
    {
        sensors ??= Array.Empty<TwinSensorFact>();
        services ??= Array.Empty<TwinServiceFact>();
        string q = Normalise(question);

        if (ContainsAny(q, ShareWords) && services.Count > 1)
            return ShareStop(sensors, services);
        if (TryFindSensor(q, sensors, out TwinSensorFact named))
            return ExplainSensor(named, sensors, services);
        if (ContainsAny(q, ServiceWords) && services.Count > 0)
            return NextServices(sensors, services);
        if (ContainsAny(q, AlarmWords))
        {
            if (TryWorst(sensors, out TwinSensorFact worst))
                return ExplainSensor(worst, sensors, services);
            return Overview(sensors, services);
        }
        if (q.Length == 0 || ContainsAny(q, OverviewWords))
            return Overview(sensors, services);

        TwinAssistantReply unknown = new()
        {
            Text = UIText.Get("ask.unknown",
                "I can answer about readings, alarms and services on this machine. Try one of these.")
        };
        AddFollowUps(unknown, sensors, services, null);
        return unknown;
    }

    /// <summary>A reading: where it is against its limits, its trend, the part around it.</summary>
    private static TwinAssistantReply ExplainSensor(in TwinSensorFact sensor, IReadOnlyList<TwinSensorFact> sensors,
        IReadOnlyList<TwinServiceFact> services)
    {
        TwinAssistantReply reply = new();
        reply.SensorIds.Add(sensor.Id);
        StringBuilder text = new();

        string reading = Reading(sensor);
        switch (sensor.State)
        {
            case StatVisualState.Unavailable:
                text.Append(UIText.Format("ask.sensor.unavailable",
                    "{0} has no usable reading right now, so I can't judge it. Check its connection first.",
                    Name(sensor.Label, true)));
                break;
            case StatVisualState.Critical:
            {
                ReadingAgainstLimit(sensor, true, out reading, out string limit);
                text.Append(UIText.Format("ask.sensor.critical", "{0} is critical at {1}. Its critical limit is {2}.",
                    Name(sensor.Label, true), reading, limit));
                break;
            }
            case StatVisualState.Warning:
            {
                ReadingAgainstLimit(sensor, false, out reading, out string limit);
                text.Append(UIText.Format("ask.sensor.warning", "{0} is in warning at {1}. Its warning limit is {2}.",
                    Name(sensor.Label, true), reading, limit));
                break;
            }
            default:
                text.Append(UIText.Format("ask.sensor.normal", "{0} reads {1}, inside its normal band.",
                    Name(sensor.Label, true), reading));
                break;
        }

        if (sensor.State != StatVisualState.Unavailable && TryTrend(sensor, out string trend))
            text.Append(' ').Append(trend);

        // Other readings from the same part that need attention.
        foreach (TwinSensorFact other in sensors)
        {
            if (other.Id == sensor.Id || string.IsNullOrEmpty(sensor.PartKey) || other.PartKey != sensor.PartKey ||
                !IsAlarm(other.State) || reply.SensorIds.Count >= MaximumEvidence)
                continue;
            reply.SensorIds.Add(other.Id);
            text.Append(' ').Append(UIText.Format("ask.sensor.related", "{0} on the same part is also {1}.",
                Name(other.Label, true), StateWord(other.State)));
        }
        reply.Text = text.ToString();

        if (IsAlarm(sensor.State) && TryService(services, sensor.PartKey, out TwinServiceFact service))
        {
            reply.Check = service.Fix;
            reply.PartKeys.Add(service.PartKey);
        }
        AddFollowUps(reply, sensors, services, sensor.Id);
        return reply;
    }

    /// <summary>How the machine is doing: the alarms, worst first.</summary>
    private static TwinAssistantReply Overview(IReadOnlyList<TwinSensorFact> sensors, IReadOnlyList<TwinServiceFact> services)
    {
        TwinAssistantReply reply = new();
        int critical = 0, warning = 0, unavailable = 0;
        foreach (TwinSensorFact sensor in sensors)
        {
            if (sensor.State == StatVisualState.Critical) critical++;
            else if (sensor.State == StatVisualState.Warning) warning++;
            else if (sensor.State == StatVisualState.Unavailable) unavailable++;
        }

        if (sensors.Count == 0)
            reply.Text = UIText.Get("ask.overview.none", "No sensors are reporting yet.");
        else if (unavailable == sensors.Count)
            // Nothing to judge: never say the machine is fine when no reading says so.
            reply.Text = UIText.Format("ask.overview.no_data",
                "None of the {0} sensors has a usable reading, so I can't tell how the machine is doing. Check the connection first.",
                Count(sensors.Count));
        else if (critical + warning == 0)
            reply.Text = UIText.Format("ask.overview.calm", "All {0} sensors are inside their normal bands.",
                Count(sensors.Count - unavailable));
        else
            reply.Text = UIText.Format("ask.overview.alarms", "Of {0} sensors, {1} are critical and {2} in warning.",
                Count(sensors.Count), Count(critical), Count(warning));
        if (unavailable > 0 && unavailable < sensors.Count)
            reply.Text += " " + UIText.Format("ask.overview.unavailable", "{0} have no usable reading.", Count(unavailable));

        List<TwinSensorFact> alarms = new();
        foreach (TwinSensorFact sensor in sensors)
        {
            if (IsAlarm(sensor.State))
                alarms.Add(sensor);
        }
        alarms.Sort((a, b) => Severity(b.State).CompareTo(Severity(a.State)));
        for (int i = 0; i < alarms.Count && i < MaximumEvidence; i++)
            reply.SensorIds.Add(alarms[i].Id);

        AddFollowUps(reply, sensors, services, null);
        return reply;
    }

    /// <summary>The services coming up soonest, overdue first.</summary>
    private static TwinAssistantReply NextServices(IReadOnlyList<TwinSensorFact> sensors, IReadOnlyList<TwinServiceFact> services)
    {
        TwinAssistantReply reply = new();
        List<TwinServiceFact> soonest = Soonest(services);
        TwinServiceFact first = soonest[0];
        reply.Text = first.HoursLeft <= 0d
            ? UIText.Format("ask.service.overdue", "{0} ({1}) is {2} h overdue.", first.PartName, TaskWord(first.Task),
                Hours(-first.HoursLeft))
            : UIText.Format("ask.service.next", "Next up is {0} ({1}) in {2} h.", first.PartName, TaskWord(first.Task),
                Hours(first.HoursLeft));

        for (int i = 0; i < soonest.Count && i < 3; i++)
            reply.PartKeys.Add(soonest[i].PartKey);
        if (soonest.Count > 1 && Math.Abs(soonest[1].HoursLeft - first.HoursLeft) <= ShareStopHours)
            reply.Text += " " + UIText.Format("ask.service.close", "{0} follows {1} h later.", soonest[1].PartName,
                Hours(Math.Abs(soonest[1].HoursLeft - first.HoursLeft)));
        reply.Check = first.Fix;

        AddFollowUps(reply, sensors, services, null);
        return reply;
    }

    /// <summary>Whether the next two services can be done in one stop.</summary>
    private static TwinAssistantReply ShareStop(IReadOnlyList<TwinSensorFact> sensors, IReadOnlyList<TwinServiceFact> services)
    {
        TwinAssistantReply reply = new();
        List<TwinServiceFact> soonest = Soonest(services);
        TwinServiceFact a = soonest[0];
        TwinServiceFact b = soonest[1];
        double apart = Math.Abs(b.HoursLeft - a.HoursLeft);
        reply.PartKeys.Add(a.PartKey);
        reply.PartKeys.Add(b.PartKey);
        reply.Text = apart <= ShareStopHours
            ? UIText.Format("ask.share.yes",
                "Yes. {0} and {1} are due {2} h apart, so one stop in {3} h covers both and saves a second shutdown.",
                a.PartName, b.PartName, Hours(apart), Hours(Math.Max(a.HoursLeft, 0d)))
            : UIText.Format("ask.share.no",
                "Not without waste: {0} is due in {1} h but {2} only in {3} h, so one stop would service {2} {4} h early.",
                a.PartName, Hours(Math.Max(a.HoursLeft, 0d)), b.PartName, Hours(Math.Max(b.HoursLeft, 0d)), Hours(apart));
        AddFollowUps(reply, sensors, services, null);
        return reply;
    }

    // ---- follow-up questions ----------------------------------------------------

    // The suggestions as the rules read them: English, whatever the language on show.
    private const string ServiceQuestion = "What needs service next?";
    private const string ShareQuestion = "Can the next two services share one stop?";
    private const string OverviewQuestion = "How is the machine doing?";

    /// <summary>Up to three questions worth asking next, not repeating the one just answered.</summary>
    private static void AddFollowUps(TwinAssistantReply reply, IReadOnlyList<TwinSensorFact> sensors,
        IReadOnlyList<TwinServiceFact> services, string answeredSensorId)
    {
        sensors ??= Array.Empty<TwinSensorFact>();
        services ??= Array.Empty<TwinServiceFact>();
        if (TryWorst(sensors, out TwinSensorFact worst, answeredSensorId))
            reply.FollowUps.Add(new TwinFollowUp(
                UIText.Format("ask.suggest.why", "Why is {0} {1}?", Name(worst.Label, false), StateWord(worst.State)),
                "Why is " + worst.Label + "?"));
        if (answeredSensorId != null || reply.PartKeys.Count == 0)
        {
            if (services.Count > 0)
                reply.FollowUps.Add(new TwinFollowUp(UIText.Get("ask.suggest.service", ServiceQuestion), ServiceQuestion));
        }
        else if (services.Count > 1)
        {
            reply.FollowUps.Add(new TwinFollowUp(UIText.Get("ask.suggest.share", ShareQuestion), ShareQuestion));
        }
        if (reply.SensorIds.Count == 0 || answeredSensorId != null)
            reply.FollowUps.Add(new TwinFollowUp(UIText.Get("ask.suggest.overview", OverviewQuestion), OverviewQuestion));
        while (reply.FollowUps.Count > 3)
            reply.FollowUps.RemoveAt(reply.FollowUps.Count - 1);
    }

    // ---- matching -----------------------------------------------------------------

    /// <summary>Lower case, letters and digits only, single spaces.</summary>
    public static string Normalise(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;
        StringBuilder builder = new(text.Length);
        bool space = false;
        foreach (char c in text.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c) || c == '\'')
            {
                if (space && builder.Length > 0)
                    builder.Append(' ');
                builder.Append(c);
                space = false;
            }
            else
            {
                space = true;
            }
        }
        return builder.ToString();
    }

    /// <summary>
    /// The sensor the question names: every word of its label in the question,
    /// the longest label winning, so "conveyor bearing temp" beats "bearing temperature";
    /// between labels that match as well, the reading in the worse state.
    /// </summary>
    public static bool TryFindSensor(string normalisedQuestion, IReadOnlyList<TwinSensorFact> sensors, out TwinSensorFact found)
    {
        found = default;
        int best = 0;
        string padded = " " + normalisedQuestion + " ";
        foreach (TwinSensorFact sensor in sensors)
        {
            string[] words = Normalise(sensor.Label).Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0)
                continue;
            int matched = 0;
            foreach (string word in words)
            {
                if (padded.Contains(" " + word + " ") || (word.Length > 4 && padded.Contains(" " + word.Substring(0, 4))))
                    matched++;
            }
            if (matched < words.Length)
                continue;
            int score = words.Length * 100 + Severity(sensor.State);
            if (score > best)
            {
                best = score;
                found = sensor;
            }
        }
        return best > 0;
    }

    private static bool ContainsAny(string normalised, string[] words)
    {
        string padded = " " + normalised + " ";
        foreach (string word in words)
        {
            if (padded.Contains(" " + word + " ") || padded.Contains(" " + word + "s "))
                return true;
        }
        return false;
    }

    // ---- facts ------------------------------------------------------------------

    private static bool TryWorst(IReadOnlyList<TwinSensorFact> sensors, out TwinSensorFact worst, string except = null)
    {
        worst = default;
        int best = 0;
        foreach (TwinSensorFact sensor in sensors)
        {
            int severity = Severity(sensor.State);
            if (sensor.Id == except || severity <= best)
                continue;
            best = severity;
            worst = sensor;
        }
        return best > 0;
    }

    private static bool TryService(IReadOnlyList<TwinServiceFact> services, string partKey, out TwinServiceFact service)
    {
        service = default;
        if (string.IsNullOrEmpty(partKey))
            return false;
        foreach (TwinServiceFact candidate in services)
        {
            if (candidate.PartKey == partKey)
            {
                service = candidate;
                return true;
            }
        }
        return false;
    }

    private static List<TwinServiceFact> Soonest(IReadOnlyList<TwinServiceFact> services)
    {
        List<TwinServiceFact> sorted = new(services);
        sorted.Sort((a, b) => a.HoursLeft.CompareTo(b.HoursLeft));
        return sorted;
    }

    private static bool IsAlarm(StatVisualState state) => state is StatVisualState.Warning or StatVisualState.Critical;

    private static int Severity(StatVisualState state) => state switch
    {
        StatVisualState.Critical => 2,
        StatVisualState.Warning => 1,
        _ => 0
    };

    // ---- wording ------------------------------------------------------------------

    private static string Reading(in TwinSensorFact sensor) =>
        string.IsNullOrEmpty(sensor.Unit) ? sensor.ValueText : sensor.ValueText + " " + sensor.Unit;

    /// <summary>The limit the reading has crossed, on whichever side it crossed it.</summary>
    private static float LimitValue(in TwinSensorFact sensor, bool critical)
    {
        TelemetryLimits limits = sensor.Limits;
        bool high = limits.HasHigh && (!limits.HasLow || sensor.Value >= (limits.WarningAbove + limits.WarningBelow) * 0.5f);
        return critical
            ? high ? limits.CriticalAbove : limits.CriticalBelow
            : high ? limits.WarningAbove : limits.WarningBelow;
    }

    /// <summary>
    /// The reading and the limit it crossed, with their units, written so they
    /// differ: a reading just past its limit (1.204 % against 1.2 %) would
    /// read the same as the limit at the card's precision, so both take as
    /// many more decimals as it needs, up to three.
    /// </summary>
    private static void ReadingAgainstLimit(in TwinSensorFact sensor, bool critical, out string reading, out string limit)
    {
        float limitValue = LimitValue(sensor, critical);
        string format = DecimalsFormat(sensor.ValueText);
        reading = sensor.ValueText;
        limit = limitValue.ToString(format, CultureInfo.InvariantCulture);
        if (sensor.Value.ToString(format, CultureInfo.InvariantCulture) == limit)
        {
            int decimals = format.Length > 1 ? format.Length - 2 : 0;
            for (int more = decimals + 1; more <= 3; more++)
            {
                string finer = "0." + new string('0', more);
                string value = sensor.Value.ToString(finer, CultureInfo.InvariantCulture);
                string bound = limitValue.ToString(finer, CultureInfo.InvariantCulture);
                if (value == bound)
                    continue;
                reading = value;
                limit = bound;
                break;
            }
        }
        string unit = string.IsNullOrEmpty(sensor.Unit) ? string.Empty : " " + sensor.Unit;
        reading += unit;
        limit += unit;
    }

    /// <summary>A trend worth mentioning: more than a twentieth of the normal band an hour.</summary>
    private static bool TryTrend(in TwinSensorFact sensor, out string trend)
    {
        trend = null;
        if (!TryTrendAmount(sensor, out bool rising, out string amount))
            return false;
        trend = rising
            ? UIText.Format("ask.trend.rising", "Over the last hour it has been rising by about {0} an hour.", amount)
            : UIText.Format("ask.trend.falling", "Over the last hour it has been falling by about {0} an hour.", amount);
        return true;
    }

    /// <summary>
    /// The hour's trend in a few words, for a reading's row: RISING 0.1 %/H, or
    /// STEADY when it moves less than is worth mentioning. False while there is
    /// too little real data in the hour to tell.
    /// </summary>
    public static bool TryShortTrend(in TwinSensorFact sensor, out string trend)
    {
        trend = null;
        if (!HasSlope(sensor))
            return false;
        if (!TryTrendAmount(sensor, out bool rising, out string amount))
        {
            trend = UIText.Get("ask.trend.steady", "STEADY");
            return true;
        }
        string perHour = amount + "/" + UIText.Get("unit.hours", "H");
        trend = rising
            ? UIText.Format("ask.trend.rising.short", "RISING {0}", perHour)
            : UIText.Format("ask.trend.falling.short", "FALLING {0}", perHour);
        return true;
    }

    /// <summary>
    /// A reading's normal band in a few words, for its row: NORMAL &lt; 0.8 %,
    /// NORMAL &gt; 20 KW or NORMAL 0.9–1.25 M/S. False without limits.
    /// </summary>
    public static bool TryNormalBand(in TwinSensorFact sensor, out string band)
    {
        band = null;
        TelemetryLimits limits = sensor.Limits;
        string format = DecimalsFormat(sensor.ValueText);
        string unit = string.IsNullOrEmpty(sensor.Unit) ? string.Empty : " " + sensor.Unit;
        string Number(float value) => value.ToString(format, CultureInfo.InvariantCulture);
        switch (limits.Mode)
        {
            case TelemetryLimitMode.HighOnly:
                band = UIText.Format("ask.normal.below", "NORMAL < {0}", Number(limits.WarningAbove) + unit);
                return true;
            case TelemetryLimitMode.LowOnly:
                band = UIText.Format("ask.normal.above", "NORMAL > {0}", Number(limits.WarningBelow) + unit);
                return true;
            case TelemetryLimitMode.OutsideRange:
                band = UIText.Format("ask.normal.range", "NORMAL {0}",
                    Number(limits.WarningBelow) + "–" + Number(limits.WarningAbove) + unit);
                return true;
            default:
                return false;
        }
    }

    private static bool HasSlope(in TwinSensorFact sensor) =>
        !double.IsNaN(sensor.SlopePerHour) && !double.IsInfinity(sensor.SlopePerHour);

    /// <summary>The hour's change with its unit, when it is more than a twentieth of the normal band.</summary>
    private static bool TryTrendAmount(in TwinSensorFact sensor, out bool rising, out string amount)
    {
        rising = false;
        amount = null;
        if (!HasSlope(sensor))
            return false;
        double slope = sensor.SlopePerHour;
        TelemetryLimits limits = sensor.Limits;
        double band = limits.Mode switch
        {
            TelemetryLimitMode.OutsideRange => Math.Abs(limits.WarningAbove - limits.WarningBelow),
            TelemetryLimitMode.HighOnly => Math.Abs(limits.CriticalAbove - limits.WarningAbove) * 4d,
            TelemetryLimitMode.LowOnly => Math.Abs(limits.WarningBelow - limits.CriticalBelow) * 4d,
            _ => 0d
        };
        if (band <= 0d || Math.Abs(slope) < band / 20d)
            return false;
        rising = slope > 0d;
        amount = Math.Abs(slope).ToString(DecimalsFormat(sensor.ValueText), CultureInfo.InvariantCulture) +
                 (string.IsNullOrEmpty(sensor.Unit) ? string.Empty : " " + sensor.Unit);
        return true;
    }

    private static string DecimalsFormat(string valueText)
    {
        int separator = valueText?.IndexOf('.') ?? -1;
        int decimals = separator < 0 ? 0 : Math.Min(valueText.Length - separator - 1, 3);
        return decimals == 0 ? "0" : "0." + new string('0', decimals);
    }

    private static string StateWord(StatVisualState state) => state switch
    {
        StatVisualState.Critical => UIText.Get("ask.state.critical", "critical"),
        StatVisualState.Warning => UIText.Get("ask.state.warning", "in warning"),
        StatVisualState.Unavailable => UIText.Get("ask.state.unavailable", "without data"),
        _ => UIText.Get("ask.state.normal", "normal")
    };

    private static string TaskWord(ServiceTask task) => task switch
    {
        ServiceTask.Regrease => UIText.Get("ask.task.regrease", "regrease"),
        ServiceTask.OilChange => UIText.Get("ask.task.oil_change", "oil change"),
        ServiceTask.Replace => UIText.Get("ask.task.replace", "replace"),
        _ => UIText.Get("ask.task.inspect", "inspect")
    };

    /// <summary>
    /// A label for a sentence: the cards write labels in capitals, prose does
    /// not. A label already in mixed case is kept as written.
    /// </summary>
    public static string Name(string label, bool startsSentence)
    {
        if (string.IsNullOrEmpty(label) || label != label.ToUpperInvariant())
            return label;
        string lower = label.ToLowerInvariant();
        return startsSentence ? char.ToUpperInvariant(lower[0]) + lower.Substring(1) : lower;
    }

    private static string Hours(double hours) =>
        Math.Ceiling(Math.Max(hours, 0d)).ToString("#,0", CultureInfo.InvariantCulture);

    private static string Count(int count) => count.ToString(CultureInfo.InvariantCulture);
}
