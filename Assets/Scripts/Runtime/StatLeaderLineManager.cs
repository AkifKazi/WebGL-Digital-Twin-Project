using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Draws a leader line from every card on one layout's rails to its sensor.
/// Each sensor keeps its own line across rail rebuilds: while its card stays on
/// screen the line is the same object, still bound to the same card, so its
/// fades, focus and direction carry on rather than starting over. Only a new
/// sensor gets a fresh line, and one leaving the screen gives its line back.
/// </summary>
public class StatLeaderLineManager : MonoBehaviour
{
    private const string RuntimeLayerName = "Runtime Leader Line Layer";

    [Header("References")]
    [SerializeField] private StatRailManager railManager;
    [SerializeField] private StatLeaderLineView linePrefab;
    [SerializeField] private RectTransform lineLayer;
    [SerializeField] private Camera worldCamera;
    [SerializeField] private Canvas canvas;

    // Every card in this layout, and its translucent controls; each line fades
    // where it crosses one of them.
    private readonly List<PerformanceStatCardView> layoutCards = new();
    private readonly List<LeaderLineObstacle> layoutObstacles = new();
    private readonly List<StatLeaderLineView> generatedLines = new();
    private readonly Dictionary<PerformanceStatSource, StatLeaderLineView> linesBySource = new();
    private readonly Dictionary<PerformanceStatSource, StatLeaderLineView> previousLines = new();
    private readonly Stack<StatLeaderLineView> linePool = new();
    private readonly List<PerformanceStatSource> watchedSources = new();

    private bool restingOpacityInitialized;
    private float previousRestingOpacity;
    private bool drawOrderDirty;

    private void OnEnable()
    {
        if (railManager != null)
            railManager.LayoutRebuilt += RebuildLines;
    }

    private IEnumerator Start()
    {
        ResolveLineLayer();
        // Wait for the rail manager to place its cards.
        yield return null;
        RebuildLines();
    }

    private void OnDisable()
    {
        if (railManager != null)
            railManager.LayoutRebuilt -= RebuildLines;
        UnwatchSources();
    }

    public void RebuildLines()
    {
        ResolveLineLayer();
        UnwatchSources();

        if (railManager == null || linePrefab == null || lineLayer == null || worldCamera == null || canvas == null)
        {
            Debug.LogWarning($"{name} has missing references.", this);
            return;
        }

        previousLines.Clear();
        foreach (KeyValuePair<PerformanceStatSource, StatLeaderLineView> entry in linesBySource)
            previousLines[entry.Key] = entry.Value;
        linesBySource.Clear();
        generatedLines.Clear();

        HashSet<PerformanceStatCardView> linkedCards = new();
        float desiredRestingOpacity = railManager.HasSecondaryRailCards
            ? linePrefab.RestingLineOpacity
            : linePrefab.EmptySecondaryRailsRestingOpacity;
        float startingRestingOpacity = restingOpacityInitialized ? previousRestingOpacity : desiredRestingOpacity;

        foreach (KeyValuePair<PerformanceStatSource, PerformanceStatCardView> binding in railManager.ActiveCards)
        {
            PerformanceStatSource source = binding.Key;
            PerformanceStatCardView card = binding.Value;
            if (source == null || card == null || !linkedCards.Add(card))
                continue;

            if (previousLines.Remove(source, out StatLeaderLineView line) && line != null && line.Card == card)
            {
                // The same card stays on screen: its line carries on as it was.
                line.SetAdaptiveRestingLineOpacity(
                    desiredRestingOpacity, linePrefab.SecondaryRailOpacityTransitionDuration);
            }
            else
            {
                if (line != null)
                    PoolLine(line);
                line = AcquireLine();
                line.SetAdaptiveRestingLineOpacity(startingRestingOpacity, 0f, true);
                StretchInsideParent(line.transform as RectTransform);
                line.Bind(source, card, worldCamera, canvas, lineLayer);
                line.SetAdaptiveRestingLineOpacity(
                    desiredRestingOpacity,
                    linePrefab.SecondaryRailOpacityTransitionDuration,
                    !restingOpacityInitialized);
            }

            linesBySource[source] = line;
            generatedLines.Add(line);
            WatchState(source);
        }

        // Sensors that left the screen give their lines back.
        foreach (StatLeaderLineView line in previousLines.Values)
            PoolLine(line);
        previousLines.Clear();

        layoutCards.Clear();
        layoutCards.AddRange(linkedCards);
        FindObstacles();
        foreach (StatLeaderLineView line in generatedLines)
            line.SetUiToAvoid(layoutCards, layoutObstacles);

        previousRestingOpacity = desiredRestingOpacity;
        restingOpacityInitialized = true;
        ApplyDrawOrder();
    }

    /// <summary>
    /// Draws the alarming lines and their anchors over the calm ones, critical
    /// above warning. Without this the anchors stack in whatever order the cards
    /// were built in, and a yellow anchor can sit over a red one.
    /// </summary>
    private void ApplyDrawOrder()
    {
        generatedLines.Sort((first, second) =>
        {
            int severity = Severity(first.Source).CompareTo(Severity(second.Source));
            // Ties keep a stable order, so equal lines never swap places.
            return severity != 0
                ? severity
                : string.CompareOrdinal(first.Source != null ? first.Source.StatId : null,
                    second.Source != null ? second.Source.StatId : null);
        });

        for (int i = 0; i < generatedLines.Count; i++)
        {
            if (generatedLines[i].transform.GetSiblingIndex() != i)
                generatedLines[i].transform.SetSiblingIndex(i);
        }
    }

    private static int Severity(PerformanceStatSource source) => source == null
        ? 0
        : source.VisualState switch
        {
            StatVisualState.Critical => 3,
            StatVisualState.Warning => 2,
            StatVisualState.Unavailable => 1,
            _ => 0
        };

    private void WatchState(PerformanceStatSource source)
    {
        if (source == null || watchedSources.Contains(source))
            return;
        watchedSources.Add(source);
        source.Changed += HandleSourceChanged;
    }

    private void UnwatchSources()
    {
        foreach (PerformanceStatSource source in watchedSources)
        {
            if (source != null)
                source.Changed -= HandleSourceChanged;
        }
        watchedSources.Clear();
    }

    // An alarm can start at any moment. Sorting waits for the end of the frame,
    // so a burst of readings reorders the lines once rather than per reading.
    private void HandleSourceChanged(PerformanceStatSource source) => drawOrderDirty = true;

    /// <summary>The translucent controls of this manager's layout: everything under its parent marked as a <see cref="LeaderLineObstacle"/>.</summary>
    private void FindObstacles()
    {
        layoutObstacles.Clear();
        Transform layout = transform.parent != null ? transform.parent : transform;
        layout.GetComponentsInChildren(true, layoutObstacles);
    }

    private void LateUpdate()
    {
        if (!drawOrderDirty)
            return;
        drawOrderDirty = false;
        ApplyDrawOrder();
    }

    private void PoolLine(StatLeaderLineView line)
    {
        if (line == null)
            return;
        line.Unbind();
        line.gameObject.SetActive(false);
        linePool.Push(line);
    }

    private StatLeaderLineView AcquireLine()
    {
        StatLeaderLineView line = null;
        while (line == null && linePool.Count > 0)
            line = linePool.Pop();

        if (line == null)
            return Instantiate(linePrefab, lineLayer);

        line.transform.SetParent(lineLayer, false);
        line.gameObject.SetActive(true);
        return line;
    }

    private void ResolveLineLayer()
    {
        Transform layoutRoot = transform.parent;
        if (layoutRoot == null)
            return;

        Transform expected = layoutRoot.Find(RuntimeLayerName);
        if (expected is RectTransform expectedRect && lineLayer != expectedRect)
            lineLayer = expectedRect;
    }

#if UNITY_EDITOR
    private void OnValidate() => ResolveLineLayer();
#endif

    private static void StretchInsideParent(RectTransform rectTransform)
    {
        if (rectTransform == null)
            return;
        rectTransform.anchorMin = Vector2.zero;
        rectTransform.anchorMax = Vector2.one;
        rectTransform.offsetMin = Vector2.zero;
        rectTransform.offsetMax = Vector2.zero;
        rectTransform.localScale = Vector3.one;
        rectTransform.localRotation = Quaternion.identity;
    }
}
