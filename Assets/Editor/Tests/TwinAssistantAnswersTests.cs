using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

/// <summary>
/// The assistant's own answers, without a scene. Run with
/// <c>unity test --mode EditMode</c>.
/// </summary>
public sealed class TwinAssistantAnswersTests
{
    private static readonly TelemetryLimits SlipLimits = new(TelemetryLimitMode.HighOnly, 0f, 0f, 0.8f, 1.2f);
    private static readonly TelemetryLimits SpeedLimits = new(TelemetryLimitMode.OutsideRange, 0.9f, 0.6f, 1.25f, 1.45f);
    private static readonly TelemetryLimits TempLimits = new(TelemetryLimitMode.HighOnly, 0f, 0f, 75f, 90f);

    private List<TwinSensorFact> sensors;
    private List<TwinServiceFact> services;

    [SetUp]
    public void SetUp()
    {
        sensors = new List<TwinSensorFact>
        {
            new("FEED-01.SLIP", "BELT SLIP", "%", 1.3f, "1.3", StatVisualState.Critical, SlipLimits, double.NaN, "part.drive_roller"),
            new("FEED-01.BELT.SPEED", "BELT SPEED", "m/s", 0.8f, "0.8", StatVisualState.Warning, SpeedLimits, double.NaN, "part.conveyor_belts"),
            new("FEED-01.BEARING.TEMP", "CONVEYOR BEARING TEMP", "°C", 44f, "44.0", StatVisualState.Normal, TempLimits, double.NaN, "part.drive_roller"),
            new("VIB-01.BEARING.TEMP", "BEARING TEMPERATURE", "°C", 56.4f, "56.4", StatVisualState.Normal, TempLimits, 6.0, "part.bearings")
        };
        services = new List<TwinServiceFact>
        {
            new("part.drive_roller", "Drive Roller", ServiceTask.Regrease, 740d, ServiceState.OnTrack, "Regrease the roller bearings"),
            new("part.bearings", "Bearings", ServiceTask.Regrease, 160d, ServiceState.DueSoon, "Regrease both bearings"),
            new("part.screen_deck", "Screen Deck", ServiceTask.Replace, 170d, ServiceState.DueSoon, "Replace the screen panels")
        };
    }

    [Test]
    public void ANamedReadingIsExplainedAgainstTheLimitItCrossed()
    {
        TwinAssistantReply reply = TwinAssistantAnswers.Answer("Why is belt slip red?", sensors, services);
        StringAssert.StartsWith("Belt slip is critical at 1.3 %", reply.Text);
        StringAssert.Contains("Its critical limit is 1.2 %.", reply.Text);
        Assert.AreEqual("FEED-01.SLIP", reply.SensorIds[0]);
        Assert.AreEqual("Regrease the roller bearings", reply.Check, "an alarm suggests the part's service fix");
    }

    [Test]
    public void AReadingJustPastItsLimitIsWrittenFinerThanTheLimit()
    {
        sensors[0] = new TwinSensorFact("FEED-01.SLIP", "BELT SLIP", "%", 1.204f, "1.2", StatVisualState.Critical,
            SlipLimits, double.NaN, "part.drive_roller");
        TwinAssistantReply reply = TwinAssistantAnswers.Answer("Why is belt slip red?", sensors, services);
        StringAssert.StartsWith("Belt slip is critical at 1.204 %. Its critical limit is 1.200 %.", reply.Text);
    }

    [Test]
    public void ALowAlarmQuotesTheLowLimit()
    {
        TwinAssistantReply reply = TwinAssistantAnswers.Answer("what about belt speed", sensors, services);
        StringAssert.Contains("in warning at 0.8 m/s", reply.Text);
        StringAssert.Contains("Its warning limit is 0.9 m/s.", reply.Text);
    }

    [Test]
    public void TheLongestMatchingLabelWins()
    {
        Assert.IsTrue(TwinAssistantAnswers.TryFindSensor(
            TwinAssistantAnswers.Normalise("Conveyor bearing temp?"), sensors, out TwinSensorFact found));
        Assert.AreEqual("FEED-01.BEARING.TEMP", found.Id);
        Assert.IsTrue(TwinAssistantAnswers.TryFindSensor(
            TwinAssistantAnswers.Normalise("bearing temperature now"), sensors, out found));
        Assert.AreEqual("VIB-01.BEARING.TEMP", found.Id);
    }

    [Test]
    public void ASteepTrendIsMentionedAndAFlatOneIsNot()
    {
        TwinAssistantReply rising = TwinAssistantAnswers.Answer("bearing temperature", sensors, services);
        StringAssert.Contains("rising by about 6.0 °C an hour", rising.Text);
        TwinAssistantReply flat = TwinAssistantAnswers.Answer("conveyor bearing temp", sensors, services);
        StringAssert.DoesNotContain("rising", flat.Text);
        StringAssert.DoesNotContain("falling", flat.Text);
    }

    [Test]
    public void WhyWithoutANameExplainsTheWorstAlarm()
    {
        TwinAssistantReply reply = TwinAssistantAnswers.Answer("Why is it red?", sensors, services);
        Assert.AreEqual("FEED-01.SLIP", reply.SensorIds[0]);
    }

    [Test]
    public void TheOverviewCountsAlarmsAndListsThemWorstFirst()
    {
        TwinAssistantReply reply = TwinAssistantAnswers.Answer("How is the machine doing?", sensors, services);
        Assert.AreEqual("Of 4 sensors, 1 are critical and 1 in warning.", reply.Text);
        CollectionAssert.AreEqual(new[] { "FEED-01.SLIP", "FEED-01.BELT.SPEED" }, reply.SensorIds);
    }

    [Test]
    public void NextServicesComeSoonestFirst()
    {
        TwinAssistantReply reply = TwinAssistantAnswers.Answer("What needs service next?", sensors, services);
        StringAssert.StartsWith("Next up is Bearings (regrease) in 160 h.", reply.Text);
        StringAssert.Contains("Screen Deck follows 10 h later.", reply.Text);
        CollectionAssert.AreEqual(new[] { "part.bearings", "part.screen_deck", "part.drive_roller" }, reply.PartKeys);
        Assert.That(reply.FollowUps.Exists(f => f.Text == "Can the next two services share one stop?"));
    }

    [Test]
    public void TwoServicesCloseTogetherShareAStop()
    {
        TwinAssistantReply reply = TwinAssistantAnswers.Answer("Can the next two services share one stop?", sensors, services);
        StringAssert.StartsWith("Yes. Bearings and Screen Deck are due 10 h apart", reply.Text);
    }

    [Test]
    public void ServicesFarApartDoNot()
    {
        services[2] = new TwinServiceFact("part.screen_deck", "Screen Deck", ServiceTask.Replace, 600d, ServiceState.OnTrack, "");
        TwinAssistantReply reply = TwinAssistantAnswers.Answer("can they share one stop", sensors, services);
        StringAssert.StartsWith("Not without waste", reply.Text);
    }

    [Test]
    public void AnUnusableReadingIsNotJudged()
    {
        sensors[0] = new TwinSensorFact("FEED-01.SLIP", "BELT SLIP", "%", 0f, "0.0", StatVisualState.Unavailable,
            SlipLimits, double.NaN, "part.drive_roller");
        TwinAssistantReply reply = TwinAssistantAnswers.Answer("belt slip?", sensors, services);
        StringAssert.Contains("no usable reading", reply.Text);
        Assert.IsEmpty(reply.Check, "no fix is suggested on a reading that cannot be trusted");
    }

    [Test]
    public void AnUnknownQuestionPointsToWhatCanBeAsked()
    {
        TwinAssistantReply reply = TwinAssistantAnswers.Answer("what's the weather like", sensors, services);
        StringAssert.StartsWith("I can answer about readings", reply.Text);
        Assert.IsNotEmpty(reply.FollowUps);
        Assert.LessOrEqual(reply.FollowUps.Count, 3);
    }

    [Test]
    public void ARowStatesTheNormalBandOnTheReadingsOwnSide()
    {
        Assert.IsTrue(TwinAssistantAnswers.TryNormalBand(sensors[0], out string high));
        Assert.AreEqual("NORMAL < 0.8 %", high);
        Assert.IsTrue(TwinAssistantAnswers.TryNormalBand(sensors[1], out string range));
        Assert.AreEqual("NORMAL 0.9–1.3 m/s", range, "the band is written to the reading's own decimals");
        TelemetryLimits disabled = new(TelemetryLimitMode.Disabled, 0f, 0f, 0f, 0f);
        TwinSensorFact noLimits = new("X", "X", "", 1f, "1", StatVisualState.Normal, disabled, double.NaN, "");
        Assert.IsFalse(TwinAssistantAnswers.TryNormalBand(noLimits, out _));
    }

    [Test]
    public void ARowsTrendIsShortAndSteadyWhenFlat()
    {
        Assert.IsTrue(TwinAssistantAnswers.TryShortTrend(sensors[3], out string rising));
        Assert.AreEqual("RISING 6.0 °C/H", rising);
        TwinSensorFact flat = new("T", "T", "°C", 44f, "44.0", StatVisualState.Normal, TempLimits, 0.1, "");
        Assert.IsTrue(TwinAssistantAnswers.TryShortTrend(flat, out string steady));
        Assert.AreEqual("STEADY", steady);
        Assert.IsFalse(TwinAssistantAnswers.TryShortTrend(sensors[0], out _), "no trend without an hour of data");
    }

    [Test]
    public void BetweenEquallyMatchingLabelsTheWorseReadingIsExplained()
    {
        sensors.Add(new TwinSensorFact("FEED-02.SLIP", "BELT SLIP", "%", 0.2f, "0.2", StatVisualState.Normal,
            SlipLimits, double.NaN, "part.drive_roller"));
        sensors.Reverse();
        TwinAssistantReply reply = TwinAssistantAnswers.Answer("why is belt slip red?", sensors, services);
        Assert.AreEqual("FEED-01.SLIP", reply.SensorIds[0]);
    }

    [Test]
    public void AnOverviewWithNoUsableReadingDoesNotSayAllIsWell()
    {
        for (int i = 0; i < sensors.Count; i++)
        {
            TwinSensorFact s = sensors[i];
            sensors[i] = new TwinSensorFact(s.Id, s.Label, s.Unit, 0f, "0", StatVisualState.Unavailable, s.Limits,
                double.NaN, s.PartKey);
        }
        TwinAssistantReply reply = TwinAssistantAnswers.Answer("how is the machine doing?", sensors, services);
        StringAssert.StartsWith("None of the 4 sensors has a usable reading", reply.Text);
        StringAssert.DoesNotContain("normal bands", reply.Text);
    }

    [Test]
    public void EverySuggestionIsAnsweredByTheRulesItsQuestionFor()
    {
        TwinAssistantReply greeting = TwinAssistantAnswers.Greeting(sensors, services);
        TwinAssistantReply asked = TwinAssistantAnswers.Answer("what needs service next?", sensors, services);
        foreach (TwinFollowUp followUp in greeting.FollowUps.Concat(asked.FollowUps))
        {
            TwinAssistantReply reply = TwinAssistantAnswers.Answer(followUp.Question, sensors, services);
            StringAssert.DoesNotStartWith("I can answer about readings", reply.Text, followUp.Question);
        }
    }

    [Test]
    public void PageTextIsCutBeforeItIsEscaped()
    {
        string text = new string('a', 9) + "<b>";
        string plain = TwinAssistant.PlainText(text, 10);
        Assert.AreEqual(new string('a', 9) + "<noparse><</noparse>...", plain);
        Assert.AreEqual("x <noparse><</noparse> y", TwinAssistant.EscapeRichText("x < y"));
    }

    [Test]
    public void LabelsInCapitalsReadAsSentences()
    {
        Assert.AreEqual("Belt slip", TwinAssistantAnswers.Name("BELT SLIP", true));
        Assert.AreEqual("belt slip", TwinAssistantAnswers.Name("BELT SLIP", false));
        Assert.AreEqual("Ölstand", TwinAssistantAnswers.Name("Ölstand", true), "mixed case is kept as written");
    }
}
