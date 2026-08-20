using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class WideStatLeaderLineManager : MonoBehaviour
{
    private const string RuntimeLayerName = "Runtime Leader Line Layer";

    [Header("References")]
    [SerializeField] private WideStatRailManager railManager;
    [SerializeField] private StatLeaderLineView linePrefab;
    [SerializeField] private RectTransform lineLayer;
    [SerializeField] private Camera worldCamera;
    [SerializeField] private Canvas canvas;

    private readonly List<StatLeaderLineView>
        generatedLines = new();

    private readonly Stack<StatLeaderLineView>
        linePool = new();

    private bool restingOpacityInitialized;
    private float previousRestingOpacity;

    private void OnEnable()
    {
        if (railManager != null)
            railManager.LayoutRebuilt += RebuildLines;
    }

    private IEnumerator Start()
    {
        ResolveLineLayer();
        // Wait for the rail manager to instantiate its cards.
        yield return null;
        RebuildLines();
    }

    private void OnDisable()
    {
        if (railManager != null)
            railManager.LayoutRebuilt -= RebuildLines;
    }

    public void RebuildLines()
    {
        ResolveLineLayer();
        ClearLines();

        if (railManager == null ||
            linePrefab == null ||
            lineLayer == null ||
            worldCamera == null ||
            canvas == null)
        {
            Debug.LogWarning(
                "WideStatLeaderLineManager has missing references.",
                this
            );

            return;
        }

        HashSet<PerformanceStatCardView> linkedCards = new();
        float desiredRestingOpacity = railManager.HasSecondaryRailCards
            ? linePrefab.RestingLineOpacity
            : linePrefab.EmptySecondaryRailsRestingOpacity;
        float startingRestingOpacity = restingOpacityInitialized
            ? previousRestingOpacity
            : desiredRestingOpacity;
        foreach (KeyValuePair<
                     PerformanceStatSource,
                     PerformanceStatCardView
                 > binding in railManager.ActiveCards)
        {
            PerformanceStatSource source = binding.Key;
            PerformanceStatCardView card = binding.Value;

            if (source == null || card == null)
                continue;
            if (!linkedCards.Add(card))
                continue;

            StatLeaderLineView line = AcquireLine();

            line.SetAdaptiveRestingLineOpacity(
                startingRestingOpacity,
                0f,
                true);

            StretchInsideParent(
                line.transform as RectTransform
            );

            line.Bind(
                source,
                card,
                worldCamera,
                canvas,
                lineLayer
            );

            line.SetAdaptiveRestingLineOpacity(
                desiredRestingOpacity,
                linePrefab.SecondaryRailOpacityTransitionDuration,
                !restingOpacityInitialized);

            generatedLines.Add(line);
        }

        previousRestingOpacity = desiredRestingOpacity;
        restingOpacityInitialized = true;
    }

    private void ClearLines()
    {
        foreach (StatLeaderLineView line in generatedLines)
        {
            if (line != null)
            {
                line.Unbind();
                line.gameObject.SetActive(false);
                linePool.Push(line);
            }
        }

        generatedLines.Clear();
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
    private void OnValidate()
    {
        ResolveLineLayer();
    }
#endif

    private static void StretchInsideParent(
        RectTransform rectTransform)
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
