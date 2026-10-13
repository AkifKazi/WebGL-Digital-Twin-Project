using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

/// <summary>
/// Answers the Ask window's questions about the machine.
///
/// By default the twin answers itself, with <see cref="TwinAssistantAnswers"/>'
/// rules over its live figures. With <see cref="PageAnswers"/> on, a question
/// goes to the hosting page instead, which can put it to a language model with
/// the AI data table as context and send the answer back; if no answer comes
/// within the timeout, the twin answers itself.
///
/// Page API (the component sits on the Digital Twin Runtime object):
///
///   window.addEventListener("digitaltwin:ask", e => {
///     const q = JSON.parse(e.detail);   // { id, question, view }
///     // context: window.DigitalTwinAi.latestJson (the AI data table)
///     unityInstance.SendMessage("Digital Twin Runtime", "PushAssistantReply", JSON.stringify({
///       id: q.id, text: "...", sensorIds: ["FEED-01.SLIP"], parts: ["Drive Roller"],
///       check: "...", followUps: ["..."] }));
///   });
///   unityInstance.SendMessage("Digital Twin Runtime", "SetAssistantSource", "page"); // or "twin"
///
/// Answers are advice only: nothing here changes the machine.
/// </summary>
[DisallowMultipleComponent]
public sealed class TwinAssistant : MonoBehaviour
{
    public const string QuestionEventName = "digitaltwin:ask";

    [Header("Sources (found in the scene when empty)")]
    [SerializeField] private TelemetryRegistry registry;
    [SerializeField] private TelemetryServiceSchedule schedule;
    [Tooltip("Gives each reading its trend over the last hour.")]
    [SerializeField] private TelemetryAiDataTable aiTable;

    [Header("Answers")]
    [Tooltip("The hosting page answers (a language model behind it). Off: the twin's own rules answer.")]
    [SerializeField] private bool pageAnswers;

    [Tooltip("Seconds to wait for the page's answer before the twin answers itself.")]
    [SerializeField, Min(1f)] private float pageTimeoutSeconds = 20f;

    [Tooltip("Seconds the twin's own answers take, so the reply reads as a reply rather than a flicker.")]
    [SerializeField, Min(0f)] private float localAnswerDelay = 0.6f;

    private const int TrendWindowMinutes = 60;
    private const double MinimumTrendCoverage = 0.5;

    private readonly List<TwinSensorFact> sensorFacts = new();
    private readonly List<TwinServiceFact> serviceFacts = new();
    private readonly List<ServiceItemStatus> statuses = new();
    private readonly List<Pending> pending = new();
    private int nextId = 1;

    private sealed class Pending
    {
        public int Id;
        public string Question;
        public string RuleQuestion;
        public float Due;
        public bool FromPage;
        public Action<TwinAssistantReply> Reply;
    }

    [Serializable]
    private sealed class ReplyDto
    {
        public int id;
        public string text;
        public string[] sensorIds;
        public string[] parts;
        public string check;
        public string[] followUps;
    }

    [Serializable]
    private sealed class QuestionDto
    {
        public int id;
        public string question;
        public string view;
    }

    /// <summary>True while questions go to the hosting page.</summary>
    public bool PageAnswers
    {
        get => pageAnswers;
        set => pageAnswers = value;
    }

    /// <summary>True while a question waits for its answer.</summary>
    public bool IsAnswering => pending.Count > 0;

    /// <summary>The assistant on the runtime object, added there if the scene has none.</summary>
    public static TwinAssistant Find()
    {
        TwinAssistant assistant = FindAnyObjectByType<TwinAssistant>(FindObjectsInactive.Include);
        if (assistant != null)
            return assistant;
        GameObject host = GameObject.Find(UITextTableLoader.RuntimeObjectName);
        if (host == null)
            host = new GameObject(UITextTableLoader.RuntimeObjectName);
        return host.AddComponent<TwinAssistant>();
    }

    // -----------------------------------------------------------------------
    // Asking
    // -----------------------------------------------------------------------

    /// <summary>The opening message: what to ask, with questions that fit the machine now.</summary>
    public TwinAssistantReply Greeting()
    {
        GatherFacts();
        return TwinAssistantAnswers.Greeting(sensorFacts, serviceFacts);
    }

    /// <summary>
    /// Asks <paramref name="question"/>; <paramref name="reply"/> is called once
    /// with the answer. <paramref name="ruleQuestion"/>, when given, is what the
    /// twin's own rules read instead (a suggestion's English form); the page
    /// gets the question as the viewer saw it.
    /// </summary>
    public void Ask(string question, Action<TwinAssistantReply> reply, string ruleQuestion = null)
    {
        if (reply == null)
            return;
        // Waiting answers are delivered from Update: switched off, the twin
        // answers at once rather than leaving the question unanswered.
        if (!isActiveAndEnabled)
        {
            reply(AnswerNow(string.IsNullOrWhiteSpace(ruleQuestion) ? question : ruleQuestion));
            return;
        }
        Pending entry = new()
        {
            Id = nextId++,
            Question = question ?? string.Empty,
            RuleQuestion = string.IsNullOrWhiteSpace(ruleQuestion) ? question ?? string.Empty : ruleQuestion,
            Reply = reply,
            FromPage = pageAnswers,
            Due = Time.unscaledTime + (pageAnswers ? pageTimeoutSeconds : localAnswerDelay)
        };
        pending.Add(entry);
        if (entry.FromPage)
            SendQuestionToPage(entry);
    }

    /// <summary>The twin's own answer, now: for tests and for the page's fallback.</summary>
    public TwinAssistantReply AnswerNow(string question)
    {
        GatherFacts();
        return TwinAssistantAnswers.Answer(question, sensorFacts, serviceFacts);
    }

    // Switched off with questions waiting: the twin answers them now.
    private void OnDisable() => AnswerDue(float.PositiveInfinity);

    private void Update() => AnswerDue(Time.unscaledTime);

    private void AnswerDue(float now)
    {
        for (int i = pending.Count - 1; i >= 0; i--)
        {
            Pending entry = pending[i];
            if (now < entry.Due)
                continue;
            pending.RemoveAt(i);
            TwinAssistantReply answer = AnswerNow(entry.RuleQuestion);
            if (entry.FromPage && !float.IsPositiveInfinity(now))
                Debug.LogWarning($"Twin assistant: the page did not answer question {entry.Id} in time; the twin answered.", this);
            entry.Reply(answer);
        }
    }

    // -----------------------------------------------------------------------
    // Page API (unityInstance.SendMessage)
    // -----------------------------------------------------------------------

    /// <summary>The page's answer to a question it was sent, as JSON (see the class summary).</summary>
    public void PushAssistantReply(string json)
    {
        ReplyDto dto;
        try
        {
            dto = JsonUtility.FromJson<ReplyDto>(json ?? string.Empty);
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"Twin assistant: rejected reply JSON: {exception.Message}", this);
            return;
        }
        if (dto == null)
            return;

        int index = pending.FindIndex(p => p.Id == dto.id);
        if (index < 0)
            return;
        Pending entry = pending[index];
        pending.RemoveAt(index);

        TwinAssistantReply reply = new()
        {
            // Plain text only: rich-text tags from a model are shown as written.
            Text = PlainText(dto.text, 1200),
            Check = PlainText(dto.check, 300)
        };
        if (dto.sensorIds != null)
        {
            foreach (string id in dto.sensorIds)
            {
                if (TryGetSource(id, out PerformanceStatSource source))
                    reply.SensorIds.Add(source.StatId);
            }
        }
        if (dto.parts != null)
        {
            foreach (string part in dto.parts)
            {
                if (TryGetPart(part, out MachinePartGroup found))
                    reply.PartKeys.Add(ServicePanelView.PartKey(found));
            }
        }
        if (dto.followUps != null)
        {
            foreach (string followUp in dto.followUps)
            {
                if (!string.IsNullOrWhiteSpace(followUp) && reply.FollowUps.Count < 3)
                    reply.FollowUps.Add(new TwinFollowUp(Truncate(followUp, 120)));
            }
        }
        if (string.IsNullOrEmpty(reply.Text))
            reply = AnswerNow(entry.RuleQuestion);
        entry.Reply(reply);
    }

    /// <summary>"page": the hosting page answers; "twin": the twin's own rules do.</summary>
    public void SetAssistantSource(string source) =>
        pageAnswers = string.Equals((source ?? string.Empty).Trim(), "page", StringComparison.OrdinalIgnoreCase);

    private void SendQuestionToPage(Pending entry)
    {
        QuestionDto dto = new()
        {
            id = entry.Id,
            question = entry.Question,
            view = DigitalTwinModeState.Get(DigitalTwinMode.Telemetry) == DigitalTwinMode.Service ? "service" : "telemetry"
        };
        PublishToPage(QuestionEventName, JsonUtility.ToJson(dto));
    }

    /// <summary>
    /// Text from the page, cut to <paramref name="limit"/> characters and shown
    /// as written. Cut first, then escaped: an escape cut in half would show as
    /// tag text or swallow what follows.
    /// </summary>
    public static string PlainText(string text, int limit) => EscapeRichText(Truncate(text, limit));

    // A follow-up stays raw: it is sent as the viewer's message when pressed,
    // and the window escapes it wherever it shows it.
    private static string Truncate(string text, int limit)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;
        string plain = text.Trim();
        return plain.Length > limit ? plain.Substring(0, limit) + "..." : plain;
    }

    /// <summary><paramref name="text"/> shown as written in a rich-text field: its tags are not read as tags.</summary>
    public static string EscapeRichText(string text) =>
        string.IsNullOrEmpty(text) ? string.Empty : text.Replace("<", "<noparse><</noparse>");

    // -----------------------------------------------------------------------
    // The twin's figures
    // -----------------------------------------------------------------------

    /// <summary>The sensor with <paramref name="id"/>.</summary>
    public bool TryGetSource(string id, out PerformanceStatSource source)
    {
        source = null;
        ResolveSources();
        return registry != null && !string.IsNullOrWhiteSpace(id) && registry.TryGetSource(id.Trim(), out source);
    }

    /// <summary>
    /// The sensor with <paramref name="id"/> as the answers see it: its limits
    /// and its trend over the last hour, among the rest.
    /// </summary>
    public bool TryGetFact(string id, out TwinSensorFact fact)
    {
        fact = default;
        if (!TryGetSource(id, out PerformanceStatSource source))
            return false;
        fact = SensorFact(source, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        return true;
    }

    /// <summary>A serviced part by its language key (part.bearings) or its name.</summary>
    public bool TryGetPart(string keyOrName, out MachinePartGroup part)
    {
        part = null;
        ResolveSources();
        if (schedule == null || string.IsNullOrWhiteSpace(keyOrName))
            return false;
        string wanted = keyOrName.Trim();
        string slug = "part." + UIText.Slug(wanted);
        schedule.GetStatuses(statuses);
        foreach (ServiceItemStatus status in statuses)
        {
            string key = ServicePanelView.PartKey(status.Part);
            if (key == wanted || key == slug)
            {
                part = status.Part;
                return true;
            }
        }
        return false;
    }

    /// <summary>A serviced part's position now, by its language key.</summary>
    public bool TryGetService(string partKey, out ServiceItemStatus status)
    {
        status = default;
        ResolveSources();
        if (schedule == null)
            return false;
        schedule.GetStatuses(statuses);
        foreach (ServiceItemStatus candidate in statuses)
        {
            if (ServicePanelView.PartKey(candidate.Part) == partKey)
            {
                status = candidate;
                return true;
            }
        }
        return false;
    }

    private void ResolveSources()
    {
        if (registry == null)
            registry = FindAnyObjectByType<TelemetryRegistry>();
        if (schedule == null)
            schedule = FindAnyObjectByType<TelemetryServiceSchedule>();
        if (aiTable == null)
            aiTable = FindAnyObjectByType<TelemetryAiDataTable>();
    }

    private void GatherFacts()
    {
        ResolveSources();
        sensorFacts.Clear();
        serviceFacts.Clear();
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        if (registry != null)
        {
            foreach (PerformanceStatSource source in registry.Sources)
            {
                if (source != null && source.isActiveAndEnabled)
                    sensorFacts.Add(SensorFact(source, now));
            }
        }

        if (schedule != null)
        {
            schedule.GetStatuses(statuses);
            foreach (ServiceItemStatus status in statuses)
            {
                schedule.TryGetPlan(status.Part, out TelemetryServiceSchedule.ServicePlan plan);
                string key = ServicePanelView.PartKey(status.Part);
                serviceFacts.Add(new TwinServiceFact(key, ServicePanelView.PartName(status.Part), status.Task,
                    status.HoursLeft, status.State, ServicePanelView.SuggestedFix(key, plan, status.Task)));
            }
        }
    }

    private TwinSensorFact SensorFact(PerformanceStatSource source, long now)
    {
        double slope = double.NaN;
        if (aiTable != null)
        {
            // A trend over a few minutes, scaled up to an hour, is mostly noise:
            // only an hour with enough real data in it gives one.
            TelemetryAiDataTable.WindowStats stats = aiTable.ComputeWindow(source, TrendWindowMinutes, now);
            if (stats.HasSlope && stats.Coverage >= MinimumTrendCoverage)
                slope = stats.SlopePerHour;
        }
        string partKey = schedule != null && schedule.TryGetServicedPart(source, out MachinePartGroup part)
            ? ServicePanelView.PartKey(part)
            : string.Empty;
        return new TwinSensorFact(source.StatId, source.MetricName, source.Unit, source.CurrentValue,
            source.FormattedValue, source.VisualState, source.Limits, slope, partKey);
    }

#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")]
    private static extern void DigitalTwin_PublishEvent(string eventName, string json);

    private static void PublishToPage(string eventName, string json) => DigitalTwin_PublishEvent(eventName, json);
#else
    private static void PublishToPage(string eventName, string json)
    {
    }
#endif
}
