using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

/// <summary>
/// Batch-only play test of the telemetry detail panel and the control hover
/// states. Clicks through the behaviour (open, glide, release on an outside
/// click, pin into a second panel, a camera drag that must change nothing, the
/// range buttons, hover on idle, selected and pressed controls, two card
/// columns per side with the panel on its own detail rail beside them, rows in
/// a window taller than wide with the panel opening below a top-row card, the
/// panel switched off, reversed leader lines fading and the SD / HD control
/// sized like the zoom panel) and saves a capture of each step, UI included,
/// to Logs/DetailPanelTest. Exits the editor when done. Never saves the scene.
///
/// Run without -quit: the editor has to stay open while play mode runs.
/// </summary>
[InitializeOnLoad]
public static class TelemetryDetailPanelPlayTest
{
    private const string ScenePath = "Assets/Scenes/SampleScene.unity";
    private const string OutputDirectory = "Logs/DetailPanelTest";
    private const string ActiveKey = "TelemetryDetailPanelPlayTest.Active";
    private const string StartKey = "TelemetryDetailPanelPlayTest.Start";

    // Empty scene over the conveyor, clear of every rail, panel and control.
    private static readonly Vector2 OutsidePoint = new(1200f, 300f);
    // The same in the 1080x1920 capture: the model, between the rows.
    private static readonly Vector2 PortraitModelPoint = new(540f, 900f);

    private static readonly FieldInfo EmphasisField = typeof(PerformanceStatCardView)
        .GetField("connectionEmphasis", BindingFlags.Instance | BindingFlags.NonPublic);

    private static int step;
    private static float stepTime;
    private static Camera captureCamera;
    private static TelemetryDetailPanelController controller;
    private static TelemetryCardFocusReleaser releaser;
    private static StatRailManager rails;
    private static List<PerformanceStatCardView> leftCards = new();
    private static List<PerformanceStatCardView> rightCards = new();
    private static Button xray;
    private static Button section;
    private static int failures;
    private static StatLeaderLineView reversalLine;
    private static PerformanceStatCardView connectorCard;
    private static Vector3 reversalAnchorHome;

    // Entering play mode reloads scripts; this re-attaches the test afterwards.
    static TelemetryDetailPanelPlayTest()
    {
        if (!SessionState.GetBool(ActiveKey, false))
            return;
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
    }

    public static void Run()
    {
        Directory.CreateDirectory(OutputDirectory);
        foreach (string old in Directory.GetFiles(OutputDirectory, "*.png"))
            File.Delete(old);
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        SessionState.SetBool(ActiveKey, true);
        SessionState.SetFloat(StartKey, (float)EditorApplication.timeSinceStartup);
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
        EditorApplication.EnterPlaymode();
    }

    private static void Tick()
    {
        if (EditorApplication.timeSinceStartup - SessionState.GetFloat(StartKey, 0f) > 300f)
        {
            Finish("timeout");
            return;
        }

        if (!EditorApplication.isPlaying)
            return;
        // A batch editor opens behind other windows; keep play mode running
        // there. Only this session's flag, not the project setting.
        Application.runInBackground = true;
        float t = Time.time;
        if (step == 0 && t <= 3f)
            return;
        if (step > 0 && t < stepTime)
            return;

        switch (step)
        {
            case 0:
                PrepareCapture();
                // Telemetry cards only show in the cross-section view.
                Object.FindAnyObjectByType<ViewModeSegmentedControl>().SelectSection();
                Next(4.2f);
                break;

            case 1:
                FindCards();
                Expect(rails.DetailRailActive && rails.GetDetailRail(StatRailSide.Left) != null &&
                       rails.GetDetailRail(StatRailSide.Right) != null,
                    "the panel has a detail rail of its own on each side");
                ExpectNoCardOnDetailRails();
                Capture("01-rest");
                Click(leftCards[0]);
                Next(1.2f);
                break;

            case 2:
                Capture("02-open");
                Expect(controller.OpenPanels.Count == 1 && controller.FloatingPanel != null, "one unpinned panel after the first click");
                Click(leftCards[Mathf.Min(3, leftCards.Count - 1)]);
                Next(0.12f);
                break;

            case 3:
                Capture("03-gliding");
                Next(0.9f);
                break;

            case 4:
                Capture("04-glided");
                Expect(controller.OpenPanels.Count == 1, "the click on another card moved the panel instead of opening a second");
                Expect(!leftCards[0].IsFocused && leftCards[Mathf.Min(3, leftCards.Count - 1)].IsFocused,
                    "only the newly clicked card is focused");
                // Queued input events do not reach input actions in this batch
                // play mode, so the gesture is classified directly here. The
                // real click is checked in the browser build instead.
                releaser.HandlePointerGesture(OutsidePoint, OutsidePoint);
                Expect(controller.OpenPanels.Count == 0, "an outside click closed the panel");
                Expect(CardFocus.Focused == null && AllCards().All(card => !card.IsFocused),
                    "an outside click released every card (none left in the focused state)");
                Next(1f);
                break;

            case 5:
                Capture("05-after-dismiss");
                Expect(AllCards().Where(card => card.VisualState is StatVisualState.Normal or StatVisualState.Unavailable)
                        .All(card => (float)EmphasisField.GetValue(card) < 0.05f),
                    "every resting card's trail faded back to rest");
                Click(leftCards[0]);
                Next(0.8f);
                break;

            case 6:
                PressButton(controller.FloatingPanel, "Pin");
                Click(leftCards[1]);
                Next(1.2f);
                break;

            case 7:
                Capture("06-pinned-and-new");
                Expect(controller.OpenPanels.Count == 2 && controller.OpenPanels.Count(p => p.IsPinned) == 1,
                    "pinning kept the panel and the next click opened a new one");
                releaser.HandlePointerGesture(OutsidePoint, OutsidePoint + new Vector2(140f, 30f));
                Expect(controller.OpenPanels.Count == 2, "a camera drag closed nothing");
                releaser.HandlePointerGesture(OutsidePoint, OutsidePoint);
                Expect(controller.OpenPanels.Count == 1 && controller.OpenPanels[0].IsPinned,
                    "an outside click closed only the unpinned panel");
                Next(1f);
                break;

            case 8:
                Capture("07-after-outside-click");
                if (rightCards.Count > 0)
                    Click(rightCards[0]);
                Next(1.2f);
                break;

            case 9:
                Capture("08-right-side");
                PressButton(controller.OpenPanels[0], "D");
                Next(0.6f);
                break;

            case 10:
            {
                TelemetryDetailPanel panel = controller.OpenPanels[0];
                Enter(Control(panel, "W"));
                Enter(Control(panel, "D"));
                Enter(Control(panel, "Pin"));
                Next(0.3f);
                break;
            }

            case 11:
            {
                TelemetryDetailPanel panel = controller.OpenPanels[0];
                Capture("09-panel-hover");
                Expect(IsHovering(Control(panel, "W")), "an idle range button shows the hover");
                Expect(!IsHovering(Control(panel, "D")), "the selected range button never shows the hover");
                Expect(!IsHovering(Control(panel, "Pin")), "the pinned pin never shows the hover");
                Exit(Control(panel, "W"));
                Exit(Control(panel, "D"));
                Exit(Control(panel, "Pin"));
                PressButton(panel, "Y");
                Next(0.8f);
                break;
            }

            case 12:
                Capture("10-year-range");
                Expect(!IsHovering(Control(controller.OpenPanels[0], "W")), "the hover goes when the pointer leaves");
                xray = FindControl("X-Ray Button");
                section = FindControl("Cross-Section Button");
                Enter(xray.gameObject);
                Enter(section.gameObject);
                Next(0.3f);
                break;

            case 13:
                Capture("11-hover-xray");
                Expect(IsHovering(xray.gameObject) && Shown(xray) == HoverSprite(xray),
                    "hovering the idle X-Ray segment shows the hover sprite");
                Expect(!IsHovering(section.gameObject), "hovering the selected Cross Section segment shows no hover");
                Down(xray.gameObject);
                Next(0.2f);
                break;

            case 14:
                Capture("12-pressed-xray");
                Expect(!IsHovering(xray.gameObject) && Shown(xray) != HoverSprite(xray) && Shown(xray) != IdleSprite(xray),
                    "pressing a segment swaps the hover for the pressed look");
                Up(xray.gameObject);
                Exit(xray.gameObject);
                Exit(section.gameObject);
                CheckOptionalHover(FindControl("Zoom Out Button", required: false), "zoom out");
                CheckOptionalHover(FindControl("Zoom In Button", required: false), "zoom in");
                Next(0.3f);
                break;

            case 15:
            {
                Expect(!IsHovering(xray.gameObject) && Shown(xray) == IdleSprite(xray), "the X-Ray segment is idle again");
                Button zoom = FindControl("Zoom Out Button", required: false);
                if (zoom != null && zoom.IsInteractable())
                {
                    Capture("13-hover-zoom");
                    Expect(IsHovering(zoom.gameObject), "hovering an available zoom button shows the hover sprite");
                }
                Exit(zoom != null ? zoom.gameObject : null);
                Exit(FindControl("Zoom In Button", required: false)?.gameObject);
                Button next = FindControl("Next Telemetry Page", required: false);
                Debug.Log($"DETAIL_PANEL_TEST pages={rails.PageCount} nextInteractable={(next != null && next.IsInteractable())}");
                if (next != null && next.IsInteractable())
                    Enter(next.gameObject);
                Next(0.3f);
                break;
            }

            case 16:
            {
                Button next = FindControl("Next Telemetry Page", required: false);
                if (next != null && next.IsInteractable())
                {
                    Capture("14-hover-pagination");
                    Expect(IsHovering(next.gameObject), "hovering the next-page button shows the hover sprite");
                    Exit(next.gameObject);
                }
                // Two card columns a side, with the panel's rail beside them.
                SetProfile(false, TelemetryRailCount.Two, true);
                Next(1.5f);
                break;
            }

            case 17:
            {
                // An outer card level with a card on the inner column: its
                // connector to the panel must find that column clear.
                FindCards();
                connectorCard = OuterCardBesideInnerCard();
                Debug.Log($"DETAIL_PANEL_TEST connector card={(connectorCard != null ? connectorCard.BoundSource.MetricName : "none")}");
                if (connectorCard != null)
                    Click(connectorCard);
                Next(1.6f);
                break;
            }

            case 18:
            {
                Capture("15-connector-to-panel");
                if (connectorCard != null)
                    ExpectConnectorClear(connectorCard);
                releaser.HandlePointerGesture(OutsidePoint, OutsidePoint);

                FindCards();
                Expect(rails.RailsPerSide == 2 && rails.DetailRailActive, "two columns a side and the detail rail");
                ExpectNoCardOnDetailRails();
                PerformanceStatCardView inner = AllCards().FirstOrDefault(card =>
                    rails.OwnsCard(card) && card.transform.parent.parent == rails.GetRail(card.RailSide, 1));
                Debug.Log($"DETAIL_PANEL_TEST two columns: secondaryCards={rails.HasSecondaryRailCards} " +
                          $"inner card={(inner != null ? inner.BoundSource.MetricName : "none")} pages={rails.PageCount}");
                Click(inner != null ? inner : leftCards[0]);
                Next(1f);
                break;
            }

            case 19:
            {
                Capture("15-two-columns-with-panel");
                TelemetryDetailPanel panel = controller.FloatingPanel;
                Expect(panel != null, "a card on the second column opens its panel");
                if (panel != null)
                {
                    Expect(InsideDetailRail(panel), "the panel sits on the detail rail, clear of every card rail");
                    Expect(!OverlapsAnyCard(panel), "the panel covers no card");
                }
                releaser.HandlePointerGesture(OutsidePoint, OutsidePoint);
                // A window taller than wide: rows above and below the model.
                UsePortraitCapture(true);
                SetProfile(true, TelemetryRailCount.Auto, true);
                Next(1.5f);
                break;
            }

            case 20:
            {
                // The capture's canvas took its new size after the first rebuild.
                rails.RebuildLayout();
                IEnumerable<PerformanceStatCardView> rowCards = AllCards().Where(card => rails.OwnsCard(card));
                PerformanceStatCardView top = rowCards.Where(card => card.RailSide == StatRailSide.Top)
                    .OrderBy(card => card.transform.position.x).FirstOrDefault();
                Expect(rails.RailsAreRows && top != null && rowCards.All(card => card.RailSide is StatRailSide.Top or StatRailSide.Bottom),
                    "a tall window runs the cards in rows");
                Expect(rails.RailsPerSide >= 2, "Auto runs more than one row an edge in a 1080x1920 window");
                Debug.Log($"DETAIL_PANEL_TEST rows: perSide={rails.RailsPerSide} top={rowCards.Count(c => c.RailSide == StatRailSide.Top)} " +
                          $"bottom={rowCards.Count(c => c.RailSide == StatRailSide.Bottom)} pages={rails.PageCount}");
                if (top != null)
                    Click(top);
                Next(1f);
                break;
            }

            case 21:
            {
                Capture("16-rows-with-panel");
                TelemetryDetailPanel panel = controller.FloatingPanel;
                Expect(panel != null && panel.Side == StatRailSide.Top,
                    "in rows a click on a top card opens its panel (it used to open nothing)");
                if (panel != null)
                {
                    Expect(InsideDetailRail(panel), "the row panel hangs from the top rows, on the detail rail");
                    Expect(!OverlapsAnyCard(panel), "the row panel covers no card");
                }
                releaser.HandlePointerGesture(PortraitModelPoint, PortraitModelPoint);
                SetProfile(true, TelemetryRailCount.Auto, false);
                Next(1.5f);
                break;
            }

            case 22:
            {
                Capture("17-rows-panel-off");
                PerformanceStatCardView top = AllCards().FirstOrDefault(card =>
                    rails.OwnsCard(card) && card.RailSide == StatRailSide.Top);
                Expect(!rails.DetailRailActive && rails.GetDetailRail(StatRailSide.Top) == null,
                    "with the panel off there is no detail rail");
                if (top != null)
                    Click(top);
                Expect(controller.OpenPanels.Count == 0, "with the panel off a click opens no panel");
                releaser.HandlePointerGesture(PortraitModelPoint, PortraitModelPoint);
                UsePortraitCapture(false);
                SetProfile(false, TelemetryRailCount.Auto, true);
                Next(1.5f);
                break;
            }

            case 23:
                rails.RebuildLayout();
                FindCards();
                Expect(!rails.RailsAreRows && rails.RailsPerSide == 2,
                    "back in the wide window Auto runs two columns a side again");
                CheckQualityControlSize();
                // Lines fade over 0.7 s as they start or stop crossing a card.
                Next(1.5f);
                break;

            case 24:
            {
                Capture("18-reversed-lines");
                StatLeaderLineView[] lines = Object.FindObjectsByType<StatLeaderLineView>()
                    .Where(line => line.Card != null)
                    .ToArray();
                int reversed = lines.Count(line => line.IsReversed);
                Debug.Log($"DETAIL_PANEL_TEST leader lines={lines.Length} reversed={reversed}: " +
                          string.Join(", ", lines.Where(line => line.IsReversed).Select(line => line.Source.MetricName)));
                Expect(lines.Where(line => line.IsReversed).All(line => line.DirectionOpacity <= line.FadedDirectionOpacity + 0.001f),
                    "lines turning back across their card fade");
                foreach (StatLeaderLineView line in lines.Where(line => !line.IsReversed && !line.IsCrossingUi && line.DirectionOpacity < 0.999f))
                    Debug.Log($"DETAIL_PANEL_TEST faded but clear: '{line.Source.MetricName}' opacity={line.DirectionOpacity:F3}");
                Expect(lines.Where(line => !line.IsReversed && !line.IsCrossingUi).All(line => line.DirectionOpacity >= 0.999f),
                    "lines leaving their card, clear of other cards, keep their opacity");

                // Pull a left-rail sensor behind its card (play mode only; the
                // scene is never saved) so the line has to turn back across it.
                reversalLine = lines.FirstOrDefault(line => line.Card.RailSide == StatRailSide.Left &&
                                                            line.Card.VisualState is StatVisualState.Warning or StatVisualState.Critical)
                               ?? lines.First(line => line.Card.RailSide == StatRailSide.Left);
                Transform anchor = reversalLine.Card.WorldAnchor;
                reversalAnchorHome = anchor.position;
                Vector3 onScreen = captureCamera.WorldToScreenPoint(reversalAnchorHome);
                anchor.position = captureCamera.ScreenToWorldPoint(new Vector3(60f, onScreen.y, onScreen.z));
                Next(0.35f);
                break;
            }

            case 25:
                Debug.Log($"DETAIL_PANEL_TEST reversing '{reversalLine.Source.MetricName}' state={reversalLine.Card.VisualState} " +
                          $"reversed={reversalLine.IsReversed} opacity={reversalLine.DirectionOpacity:F3}");
                Expect(reversalLine.IsReversed && reversalLine.DirectionOpacity < 0.95f &&
                       reversalLine.DirectionOpacity > reversalLine.FadedDirectionOpacity + 0.01f,
                    "a line turning back across its card is part way through its fade");
                Next(0.6f);
                break;

            case 26:
                Capture("19-reversed-line-faded");
                Expect(reversalLine.IsReversed && Mathf.Abs(reversalLine.DirectionOpacity - reversalLine.FadedDirectionOpacity) < 0.001f,
                    $"after 0.7 s the reversed line is at its faded opacity ({reversalLine.FadedDirectionOpacity:P0})");
                reversalLine.Card.WorldAnchor.position = reversalAnchorHome;
                Next(1f);
                break;

            case 27:
                Capture("20-reversed-line-restored");
                Expect(!reversalLine.IsReversed && reversalLine.DirectionOpacity >= 0.999f,
                    "back in front of its card the line returns to its usual opacity");
                Debug.Log("DETAIL_PANEL_TEST crossing at rest: " + string.Join(", ",
                    Object.FindObjectsByType<StatLeaderLineView>()
                        .Where(line => line.Card != null && line.IsCrossingUi)
                        .Select(line => line.Source.MetricName)));

                // As in the reported case: a left card's sensor just past a right
                // card, so the line runs straight across that card.
                FindCards();
                RectTransform across = (RectTransform)rightCards[rightCards.Count - 1].transform;
                Vector2 acrossCentre = RectTransformUtility.WorldToScreenPoint(
                    captureCamera, across.TransformPoint(across.rect.center));
                Vector3 home = captureCamera.WorldToScreenPoint(reversalAnchorHome);
                reversalLine.Card.WorldAnchor.position = captureCamera.ScreenToWorldPoint(
                    new Vector3(1910f, acrossCentre.y, home.z));
                Next(1f);
                break;

            case 28:
                Capture("21-line-across-card-faded");
                Debug.Log($"DETAIL_PANEL_TEST across '{reversalLine.Source.MetricName}' crossing={reversalLine.IsCrossingUi} " +
                          $"reversed={reversalLine.IsReversed} opacity={reversalLine.DirectionOpacity:F3}");
                Expect(reversalLine.IsCrossingUi && !reversalLine.IsReversed &&
                       Mathf.Abs(reversalLine.DirectionOpacity - reversalLine.FadedDirectionOpacity) < 0.001f,
                    "a line running across another card fades");
                reversalLine.Card.WorldAnchor.position = reversalAnchorHome;
                Next(1f);
                break;

            case 29:
                Capture("22-line-across-card-restored");
                Expect(!reversalLine.IsCrossingUi && reversalLine.DirectionOpacity >= 0.999f,
                    "clear of the card the line returns to its usual opacity");
                Finish(failures == 0 ? "passed" : $"failed={failures}");
                break;
        }
    }

    private static void Next(float delay)
    {
        step++;
        stepTime = Time.time + delay;
    }

    private static void PrepareCapture()
    {
        captureCamera = Camera.main;
        RenderTexture target = new(1920, 1080, 24, RenderTextureFormat.ARGB32);
        captureCamera.targetTexture = target;
        if (captureCamera.TryGetComponent(out UniversalAdditionalCameraData cameraData))
            cameraData.renderPostProcessing = false; // UI colours as authored

        // Screen-space-overlay UI is not part of a camera render; move the main
        // canvas into the camera so the captures include it.
        Canvas canvas = Object.FindObjectsByType<Canvas>()
            .First(c => c.name == "Main UI Canvas");
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = captureCamera;
        canvas.planeDistance = captureCamera.nearClipPlane + 1f;

        controller = Object.FindAnyObjectByType<TelemetryDetailPanelController>();
        releaser = Object.FindAnyObjectByType<TelemetryCardFocusReleaser>();
        rails = StatRailManagers.Wide();
        // The batch window can be any shape; the steps choose the layout themselves.
        SetProfile(false, TelemetryRailCount.Auto, true);
        // Batch play mode presents no frames, so the rails never finish their own
        // first build (it waits for the end of a frame), and a requested rebuild
        // only follows a first build. Build them here, at the capture's size.
        Canvas.ForceUpdateCanvases();
        if (rails != null)
            rails.RebuildLayout();
        Debug.Log($"DETAIL_PANEL_TEST screen={Screen.width}x{Screen.height} controller={(controller != null)} " +
                  $"ready={(controller != null && controller.IsReady)} releaser={(releaser != null)}");
    }

    private static bool OnRail(PerformanceStatCardView card, int depth)
    {
        RectTransform rail = rails.GetRail(card.RailSide, depth);
        return rail != null && rails.OwnsCard(card) && card.transform.IsChildOf(rail);
    }

    /// <summary>The outer-column card most nearly level with a card on the inner column of its side.</summary>
    private static PerformanceStatCardView OuterCardBesideInnerCard()
    {
        List<PerformanceStatCardView> cards = AllCards().Where(card => rails.OwnsCard(card)).ToList();
        PerformanceStatCardView best = null;
        float bestDistance = float.MaxValue;
        foreach (PerformanceStatCardView outer in cards.Where(card => OnRail(card, 0)))
        {
            foreach (PerformanceStatCardView inner in cards.Where(card => card.RailSide == outer.RailSide && OnRail(card, 1)))
            {
                float distance = Mathf.Abs(outer.transform.position.y - inner.transform.position.y);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = outer;
                }
            }
        }
        return best;
    }

    /// <summary>
    /// The card's panel is open beside the rails, its leader line draws the
    /// connector to it, the inner column has stepped clear of that connector,
    /// and the inner column holds fewer cards than the outer one.
    /// </summary>
    private static void ExpectConnectorClear(PerformanceStatCardView card)
    {
        Expect(card.ConnectsFromPanel, "the outer card's panel opened beside the rails");
        StatLeaderLineView line = Object.FindObjectsByType<StatLeaderLineView>().FirstOrDefault(l => l.Card == card);
        RectTransform connector = line != null
            ? line.GetComponentsInChildren<RectTransform>(true).FirstOrDefault(t => t.name == "Panel Connector")
            : null;
        Expect(connector != null && connector.gameObject.activeInHierarchy && connector.rect.width > 4f,
            "a connector runs from the card to its panel");

        float lineY = RectTransformUtility.WorldToScreenPoint(captureCamera, card.OwnConnectionPoint.position).y;
        List<PerformanceStatCardView> inner = AllCards()
            .Where(other => other.RailSide == card.RailSide && OnRail(other, 1)).ToList();
        List<PerformanceStatCardView> crossed = inner
            .Where(other => { Rect rect = LayerRect((RectTransform)other.transform); return lineY > rect.yMin + 1f && lineY < rect.yMax - 1f; })
            .ToList();
        Debug.Log($"DETAIL_PANEL_TEST connector y={lineY:0} inner={inner.Count} outer={AllCards().Count(other => other.RailSide == card.RailSide && OnRail(other, 0))} " +
                  $"crossed=[{string.Join(",", crossed.Select(other => other.BoundSource.MetricName))}]");
        Expect(crossed.Count == 0, "the cards on the inner column stepped clear of the connector");
    }

    private static void FindCards()
    {
        IEnumerable<PerformanceStatCardView> cards = AllCards()
            .Where(card => rails.OwnsCard(card))
            .OrderByDescending(card => card.transform.position.y);
        leftCards = cards.Where(card => card.RailSide == StatRailSide.Left).ToList();
        rightCards = cards.Where(card => card.RailSide == StatRailSide.Right).ToList();
        Debug.Log($"DETAIL_PANEL_TEST cards left={leftCards.Count} right={rightCards.Count} perSide={rails.RailsPerSide} " +
                  $"detail={rails.DetailRailActive} secondaryCards={rails.HasSecondaryRailCards} pages={rails.PageCount}");
    }

    private static IEnumerable<PerformanceStatCardView> AllCards() =>
        Object.FindObjectsByType<PerformanceStatCardView>()
            .Where(card => card.isActiveAndEnabled);

    private static void Click(PerformanceStatCardView card)
    {
        Vector2 position = RectTransformUtility.WorldToScreenPoint(captureCamera, card.transform.position);
        PointerEventData data = new(EventSystem.current)
        {
            button = PointerEventData.InputButton.Left,
            position = position,
            pressPosition = position,
            clickCount = 1
        };
        ExecuteEvents.Execute(card.gameObject, data, ExecuteEvents.pointerClickHandler);
        Debug.Log($"DETAIL_PANEL_TEST click '{card.BoundSource?.MetricName}' focused={card.IsFocused} panels={controller.OpenPanels.Count}");
    }

    private static void PressButton(TelemetryDetailPanel panel, string name)
    {
        Button button = panel.GetComponentsInChildren<Button>(true).First(b => b.name == name);
        button.onClick.Invoke();
    }

    private static GameObject Control(TelemetryDetailPanel panel, string name) =>
        panel.GetComponentsInChildren<Button>(true).First(b => b.name == name).gameObject;

    private static Button FindControl(string name, bool required = true)
    {
        Button button = Object.FindObjectsByType<Button>()
            .FirstOrDefault(b => b.name == name && b.isActiveAndEnabled);
        if (button == null && required)
            Expect(false, $"control '{name}' exists");
        return button;
    }

    private static void CheckOptionalHover(Button button, string label)
    {
        if (button == null)
            return;
        Debug.Log($"DETAIL_PANEL_TEST {label} interactable={button.IsInteractable()} hover={(button.TryGetComponent(out UIHoverSprite hover) && hover != null)}");
        if (button.IsInteractable())
            Enter(button.gameObject);
    }

    private static void CheckQualityControlSize()
    {
        RectTransform hd = Object.FindObjectsByType<QualityToggleControl>()
            .Select(control => (RectTransform)control.transform)
            .FirstOrDefault(rect => rect.gameObject.activeInHierarchy);
        Transform zoom = hd != null ? hd.parent.Find("Camera Zoom Controls") : null;
        if (hd == null || zoom == null)
        {
            Debug.Log("DETAIL_PANEL_TEST HD control not shown here; size not checked");
            return;
        }
        Debug.Log($"DETAIL_PANEL_TEST HD {hd.rect.size} zoom {((RectTransform)zoom).rect.size}");
        Expect(Mathf.Abs(hd.rect.height - ((RectTransform)zoom).rect.height) < 0.5f &&
               Mathf.Abs(hd.position.y - zoom.position.y) < 0.5f,
            "the SD / HD control is as tall as the zoom panel and level with it");
    }

    /// <summary>Lays the wide rails out as asked, as the responsive shell would.</summary>
    private static void SetProfile(bool rowsLayout, TelemetryRailCount count, bool detail)
    {
        if (rails == null)
            return;
        rails.SetLayoutProfile(new TelemetryLayoutProfile(rowsLayout, count, detail));
        controller?.SetFeatureEnabled(detail);
        // On the next frame, as the shell does, once the canvas has taken any new size.
        rails.RequestRebuild();
    }

    /// <summary>Renders into a 1080x1920 target, a window taller than wide, or back to 1920x1080.</summary>
    private static void UsePortraitCapture(bool portrait)
    {
        RenderTexture old = captureCamera.targetTexture;
        captureCamera.targetTexture = portrait
            ? new RenderTexture(1080, 1920, 24, RenderTextureFormat.ARGB32)
            : new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32);
        if (old != null)
            old.Release();
        Canvas.ForceUpdateCanvases();
    }

    private static Rect LayerRect(RectTransform rect)
    {
        Vector3[] corners = new Vector3[4];
        rect.GetWorldCorners(corners);
        Vector2 min = RectTransformUtility.WorldToScreenPoint(captureCamera, corners[0]);
        Vector2 max = RectTransformUtility.WorldToScreenPoint(captureCamera, corners[2]);
        return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
    }

    private static bool InsideDetailRail(TelemetryDetailPanel panel)
    {
        RectTransform rail = rails.GetDetailRail(panel.Side);
        if (rail == null)
            return false;
        Rect railRect = LayerRect(rail);
        Rect panelRect = LayerRect(panel.RectTransform);
        // Across the rail the panel stays inside it; along it the panel follows its card.
        bool inside = panel.OnRow
            ? panelRect.yMin >= railRect.yMin - 2f && panelRect.yMax <= railRect.yMax + 2f
            : panelRect.xMin >= railRect.xMin - 2f && panelRect.xMax <= railRect.xMax + 2f;
        Debug.Log($"DETAIL_PANEL_TEST panel {panelRect} detail rail {railRect} inside={inside}");
        return inside;
    }

    private static bool OverlapsAnyCard(TelemetryDetailPanel panel)
    {
        Rect panelRect = LayerRect(panel.RectTransform);
        // The glow round the frame may touch a card; the frame itself may not.
        panelRect = Rect.MinMaxRect(panelRect.xMin + 8f, panelRect.yMin + 8f, panelRect.xMax - 8f, panelRect.yMax - 8f);
        PerformanceStatCardView hit = AllCards().FirstOrDefault(card =>
            rails.OwnsCard(card) && LayerRect((RectTransform)card.transform).Overlaps(panelRect));
        if (hit != null)
            Debug.Log($"DETAIL_PANEL_TEST panel overlaps '{hit.BoundSource.MetricName}'");
        return hit != null;
    }

    private static void ExpectNoCardOnDetailRails()
    {
        foreach (StatRailSide side in new[] { StatRailSide.Left, StatRailSide.Right })
        {
            RectTransform rail = rails.GetDetailRail(side);
            if (rail == null)
                continue;
            Rect railRect = LayerRect(rail);
            Rect inner = Rect.MinMaxRect(railRect.xMin + 2f, railRect.yMin, railRect.xMax - 2f, railRect.yMax);
            Expect(!AllCards().Any(card => rails.OwnsCard(card) && LayerRect((RectTransform)card.transform).Overlaps(inner)),
                $"no card sits on the {side.ToString().ToLowerInvariant()} detail rail");
        }
    }

    private static bool IsHovering(GameObject control) =>
        control != null && control.TryGetComponent(out UIHoverSprite hover) && hover.IsShowingHover;

    private static Sprite Shown(Button button) => ((Image)button.targetGraphic).overrideSprite;

    private static Sprite HoverSprite(Button button) => Field<Sprite>(button, "hoverSprite");

    private static Sprite IdleSprite(Button button) => Field<Sprite>(button, "idleSprite");

    private static T Field<T>(Button button, string name) where T : Object =>
        new SerializedObject(button.GetComponent<UIHoverSprite>()).FindProperty(name).objectReferenceValue as T;

    private static PointerEventData Pointer() =>
        new(EventSystem.current) { button = PointerEventData.InputButton.Left };

    private static void Enter(GameObject target)
    {
        if (target != null)
            ExecuteEvents.Execute(target, Pointer(), ExecuteEvents.pointerEnterHandler);
    }

    private static void Exit(GameObject target)
    {
        if (target != null)
            ExecuteEvents.Execute(target, Pointer(), ExecuteEvents.pointerExitHandler);
    }

    private static void Down(GameObject target) =>
        ExecuteEvents.Execute(target, Pointer(), ExecuteEvents.pointerDownHandler);

    private static void Up(GameObject target) =>
        ExecuteEvents.Execute(target, Pointer(), ExecuteEvents.pointerUpHandler);

    private static void Expect(bool condition, string description)
    {
        if (!condition)
            failures++;
        Debug.Log($"DETAIL_PANEL_TEST {(condition ? "ok" : "FAIL")}: {description}");
    }

    private static void Capture(string name)
    {
        RenderTexture target = captureCamera.targetTexture;
        Canvas.ForceUpdateCanvases();
        captureCamera.Render();
        RenderTexture.active = target;
        Texture2D image = new(target.width, target.height, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
        image.Apply();
        RenderTexture.active = null;
        File.WriteAllBytes(Path.Combine(OutputDirectory, name + ".png"), image.EncodeToPNG());
        Object.DestroyImmediate(image);
    }

    private static void Finish(string result)
    {
        EditorApplication.update -= Tick;
        SessionState.EraseBool(ActiveKey);
        Debug.Log($"DETAIL_PANEL_TEST_RESULT {result}");
        EditorApplication.Exit(result == "passed" ? 0 : 1);
    }
}
