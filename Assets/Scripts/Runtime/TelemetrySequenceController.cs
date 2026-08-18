using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class TelemetrySequenceController : MonoBehaviour
{
    [Header("References")]
    [SerializeField]
    private HybridHopperClipController hopperController;

    [SerializeField]
    private WideStatRailManager railManager;

    [SerializeField]
    private PortraitStatRailManager portraitRailManager;

    [Tooltip(
        "A parent containing the generated cards and leader lines."
    )]
    [SerializeField]
    private Transform telemetryRoot;

    [SerializeField]
    private Canvas canvas;

    [Header("Automatic Behaviour")]
    [SerializeField]
    private bool followCrossSectionState = true;

    [SerializeField]
    private bool initiallyVisible;

    [Header("Appearance Sequence")]
    [SerializeField, Min(0.01f)]
    private float anchorDuration = 0.12f;

    [SerializeField, Min(0f)]
    private float pauseAfterAnchor = 0.04f;

    [SerializeField, Min(0.01f)]
    private float lineRevealDuration = 0.5f;

    [SerializeField, Min(0.01f)]
    private float cardAppearDuration = 0.22f;

    [Tooltip(
        "Delay between the starting times of consecutive telemetry items."
    )]
    [SerializeField, Min(0f)]
    private float itemStagger = 0.12f;

    [SerializeField, Range(0.8f, 1f)]
    private float cardStartingScale = 0.96f;

    [Header("Hide Sequence")]
    [SerializeField, Min(0.01f)]
    private float cardHideDuration = 0.14f;

    [SerializeField, Min(0.01f)]
    private float lineRetractDuration = 0.28f;

    [SerializeField, Min(0.01f)]
    private float anchorHideDuration = 0.10f;

    [Header("Time")]
    [SerializeField]
    private bool useUnscaledTime = true;

    [Header("Debug")]
    [SerializeField]
    private bool showDebugLogs;

    private readonly List<AnimatedTelemetryItem>
        items = new();

    private readonly List<StatLeaderLineView>
        collectedLines = new();

    private Coroutine animationRoutine;
    private Coroutine refreshRoutine;

    private bool telemetryVisible;
    private bool initialised;

    private void OnEnable()
    {
        Subscribe();

        if (!initialised)
            return;

        bool shouldBeVisible =
            followCrossSectionState &&
            hopperController != null
                ? hopperController.IsCrossSection
                : initiallyVisible;

        telemetryVisible = shouldBeVisible;
        RefreshGeneratedItems();
    }

    private IEnumerator Start()
    {
        // Wait for cards and lines to be generated.
        yield return null;
        yield return null;

        CollectItems();

        bool shouldBeVisible =
            followCrossSectionState &&
            hopperController != null
                ? hopperController.IsCrossSection
                : initiallyVisible;

        telemetryVisible = shouldBeVisible;

        // Always begin hidden so no generated element flashes.
        SetPresentationInstant(false);

        initialised = true;
    

        if (shouldBeVisible)
        {
            SetTelemetryVisible(
                true,
                true
            );
        }
    }

    private void OnDisable()
    {
        Unsubscribe();

        StopActiveCoroutines();
    }

    private void Subscribe()
    {
        if (hopperController != null)
        {
            hopperController.ViewStateChanged -=
                HandleViewStateChanged;

            hopperController.ViewStateChanged +=
                HandleViewStateChanged;
        }

        if (railManager != null)
        {
            railManager.LayoutRebuilt -=
                HandleLayoutRebuilt;

            railManager.LayoutRebuilt +=
                HandleLayoutRebuilt;
        }

        if (portraitRailManager != null)
        {
            portraitRailManager.LayoutRebuilt -=
                HandleLayoutRebuilt;

            portraitRailManager.LayoutRebuilt +=
                HandleLayoutRebuilt;
        }
    }

    private void Unsubscribe()
    {
        if (hopperController != null)
        {
            hopperController.ViewStateChanged -=
                HandleViewStateChanged;
        }

        if (railManager != null)
        {
            railManager.LayoutRebuilt -=
                HandleLayoutRebuilt;
        }

        if (portraitRailManager != null)
        {
            portraitRailManager.LayoutRebuilt -=
                HandleLayoutRebuilt;
        }
    }

    private void HandleViewStateChanged(
        bool isCrossSection)
    {
        if (!followCrossSectionState)
            return;

        SetTelemetryVisible(
            isCrossSection,
            true
        );
    }

    private void HandleLayoutRebuilt()
    {
        RefreshGeneratedItems();
    }

    public void ShowTelemetry()
    {
        SetTelemetryVisible(true, true);
    }

    public void HideTelemetry()
    {
        SetTelemetryVisible(false, true);
    }

    public void ToggleTelemetry()
    {
        SetTelemetryVisible(
            !telemetryVisible,
            true
        );
    }

    public void SetTelemetryVisible(
        bool visible,
        bool animated)
    {
        telemetryVisible = visible;

        if (!initialised)
            return;

        if (animationRoutine != null)
        {
            StopCoroutine(animationRoutine);
            animationRoutine = null;
        }

        CollectItems();

        if (!animated)
        {
            SetPresentationInstant(visible);
            return;
        }

        animationRoutine = StartCoroutine(
            visible
                ? PlayShowSequence()
                : PlayHideSequence()
        );
    }

    public void RefreshGeneratedItems()
    {
        if (!isActiveAndEnabled)
            return;

        if (refreshRoutine != null)
            StopCoroutine(refreshRoutine);

        refreshRoutine = StartCoroutine(
            RefreshAfterGeneration()
        );
    }

    private IEnumerator RefreshAfterGeneration()
    {
        // Rail cards build first and leader lines build afterwards.
        yield return null;
        yield return null;

        CollectItems();
        SetPresentationInstant(telemetryVisible);

        refreshRoutine = null;
    }

    private void CollectItems()
    {
        items.Clear();

        if (telemetryRoot == null)
        {
            Debug.LogWarning(
                "TelemetrySequenceController has no Telemetry Root.",
                this
            );

            return;
        }

        collectedLines.Clear();
        telemetryRoot.GetComponentsInChildren(
            true,
            collectedLines
        );

        foreach (StatLeaderLineView line in collectedLines)
        {
            if (line == null || line.Card == null)
                continue;

            PerformanceStatCardView card =
                line.Card;

            CanvasGroup cardGroup =
                GetOrAddCanvasGroup(
                    card.gameObject
                );

            RectTransform cardRect =
                card.transform as RectTransform;

            // The shader controls line visibility.
            // Ensure an old CanvasGroup is not hiding it.
            CanvasGroup lineGroup =
                line.GetComponent<CanvasGroup>();

            if (lineGroup != null)
            {
                lineGroup.alpha = 1f;
                lineGroup.interactable = false;
                lineGroup.blocksRaycasts = false;
            }

            items.Add(
                new AnimatedTelemetryItem(
                    line,
                    card,
                    cardGroup,
                    cardRect
                )
            );
        }

        // Play from the highest card toward the lowest card.
        items.Sort((a, b) =>
            GetScreenY(b.CardRect).CompareTo(
                GetScreenY(a.CardRect)
            )
        );

        if (showDebugLogs)
        {
            Debug.Log(
                $"Telemetry sequence found {items.Count} linked items.",
                this
            );
        }
    }

    private IEnumerator PlayShowSequence()
    {
        PrepareHiddenState();

        float itemDuration =
            anchorDuration +
            pauseAfterAnchor +
            lineRevealDuration +
            cardAppearDuration;

        float totalDuration =
            items.Count == 0
                ? 0f
                : itemDuration +
                  itemStagger *
                  (items.Count - 1);

        float timer = 0f;

        while (timer < totalDuration)
        {
            timer += GetDeltaTime();

            for (int i = 0; i < items.Count; i++)
            {
                AnimatedTelemetryItem item =
                    items[i];

                float itemStart =
                    i * itemStagger;

                float anchorStart =
                    itemStart;

                float lineStart =
                    anchorStart +
                    anchorDuration +
                    pauseAfterAnchor;

                float cardStart =
                    lineStart +
                    lineRevealDuration;

                float anchorProgress =
                    CalculateProgress(
                        timer,
                        anchorStart,
                        anchorDuration
                    );

                float lineProgress =
                    CalculateProgress(
                        timer,
                        lineStart,
                        lineRevealDuration
                    );

                float cardProgress =
                    CalculateProgress(
                        timer,
                        cardStart,
                        cardAppearDuration
                    );

                item.Line.SetAnchorProgress(
                    EaseOutCubic(anchorProgress)
                );

                item.Line.SetRevealProgress(
                    EaseInOutCubic(lineProgress)
                );

                ApplyCardProgress(
                    item,
                    EaseOutCubic(cardProgress)
                );
            }

            yield return null;
        }

        SetPresentationInstant(true);
        animationRoutine = null;
    }

    private IEnumerator PlayHideSequence()
    {
        float timer = 0f;

        // Stop hover/tap ownership as soon as a hide begins. The group is
        // restored to an interactive state only after a complete reveal.
        foreach (AnimatedTelemetryItem item in items)
        {
            if (item.CardGroup == null)
                continue;
            item.CardGroup.interactable = false;
            item.CardGroup.blocksRaycasts = false;
        }

        float[] startingCardAlpha =
            new float[items.Count];

        float[] startingLineProgress =
            new float[items.Count];

        float[] startingAnchorProgress =
            new float[items.Count];

        Vector3[] startingCardScale =
            new Vector3[items.Count];

        for (int i = 0; i < items.Count; i++)
        {
            AnimatedTelemetryItem item =
                items[i];

            startingCardAlpha[i] =
                item.CardGroup != null
                    ? item.CardGroup.alpha
                    : 1f;

            startingCardScale[i] =
                item.CardRect != null
                    ? item.CardRect.localScale
                    : Vector3.one;

            startingLineProgress[i] =
                item.Line.RevealProgress;

            startingAnchorProgress[i] =
                item.Line.AnchorProgress;
        }

        // Phase 1: hide cards.
        while (timer < cardHideDuration)
        {
            timer += GetDeltaTime();

            float progress =
                Mathf.Clamp01(
                    timer / cardHideDuration
                );

            float eased =
                Mathf.SmoothStep(
                    0f,
                    1f,
                    progress
                );

            for (int i = 0; i < items.Count; i++)
            {
                AnimatedTelemetryItem item =
                    items[i];

                if (item.CardGroup != null)
                {
                    item.CardGroup.alpha =
                        Mathf.Lerp(
                            startingCardAlpha[i],
                            0f,
                            eased
                        );
                }

                if (item.CardRect != null)
                {
                    item.CardRect.localScale =
                        Vector3.Lerp(
                            startingCardScale[i],
                            Vector3.one *
                                cardStartingScale,
                            eased
                        );
                }
            }

            yield return null;
        }

        // Phase 2: retract lines toward their anchors.
        timer = 0f;

        while (timer < lineRetractDuration)
        {
            timer += GetDeltaTime();

            float progress =
                Mathf.Clamp01(
                    timer / lineRetractDuration
                );

            float eased =
                EaseInOutCubic(progress);

            for (int i = 0; i < items.Count; i++)
            {
                items[i].Line.SetRevealProgress(
                    Mathf.Lerp(
                        startingLineProgress[i],
                        0f,
                        eased
                    )
                );
            }

            yield return null;
        }

        // Phase 3: hide anchors.
        timer = 0f;

        while (timer < anchorHideDuration)
        {
            timer += GetDeltaTime();

            float progress =
                Mathf.Clamp01(
                    timer / anchorHideDuration
                );

            float eased =
                Mathf.SmoothStep(
                    0f,
                    1f,
                    progress
                );

            for (int i = 0; i < items.Count; i++)
            {
                items[i].Line.SetAnchorProgress(
                    Mathf.Lerp(
                        startingAnchorProgress[i],
                        0f,
                        eased
                    )
                );
            }

            yield return null;
        }

        SetPresentationInstant(false);
        animationRoutine = null;
    }

    private void PrepareHiddenState()
    {
        foreach (AnimatedTelemetryItem item in items)
        {
            item.Line.SetAnchorProgress(0f);
            item.Line.SetRevealProgress(0f);

            if (item.CardGroup != null)
            {
                item.CardGroup.alpha = 0f;
                item.CardGroup.interactable = false;
                item.CardGroup.blocksRaycasts = false;
            }

            if (item.CardRect != null)
            {
                item.CardRect.localScale =
                    Vector3.one *
                    cardStartingScale;
            }
        }
    }

    private void SetPresentationInstant(bool visible)
    {
        float progress = visible ? 1f : 0f;

        foreach (AnimatedTelemetryItem item in items)
        {
            item.Line.SetAnchorProgress(progress);
            item.Line.SetRevealProgress(progress);

            if (item.CardGroup != null)
            {
                item.CardGroup.alpha = progress;
                item.CardGroup.interactable = visible;
                item.CardGroup.blocksRaycasts = visible;
            }

            if (item.CardRect != null)
            {
                item.CardRect.localScale =
                    visible
                        ? Vector3.one
                        : Vector3.one *
                          cardStartingScale;
            }
        }
    }

    private void ApplyCardProgress(
        AnimatedTelemetryItem item,
        float progress)
    {
        if (item.CardGroup != null)
        {
            item.CardGroup.alpha = progress;
            bool fullyVisible = progress >= 0.999f;
            item.CardGroup.interactable = fullyVisible;
            item.CardGroup.blocksRaycasts = fullyVisible;
        }

        if (item.CardRect != null)
        {
            float scale =
                Mathf.Lerp(
                    cardStartingScale,
                    1f,
                    progress
                );

            item.CardRect.localScale =
                Vector3.one * scale;
        }
    }

    private float GetScreenY(
        RectTransform rectTransform)
    {
        if (rectTransform == null)
            return 0f;

        Camera uiCamera =
            canvas != null &&
            canvas.renderMode !=
            RenderMode.ScreenSpaceOverlay
                ? canvas.worldCamera
                : null;

        return RectTransformUtility.WorldToScreenPoint(
            uiCamera,
            rectTransform.position
        ).y;
    }

    private static float CalculateProgress(
        float currentTime,
        float startTime,
        float duration)
    {
        if (duration <= 0f)
            return currentTime >= startTime ? 1f : 0f;

        return Mathf.Clamp01(
            (currentTime - startTime) /
            duration
        );
    }

    private float GetDeltaTime()
    {
        return useUnscaledTime
            ? Time.unscaledDeltaTime
            : Time.deltaTime;
    }

    private static float EaseOutCubic(float value)
    {
        float inverse = 1f - value;

        return 1f -
               inverse *
               inverse *
               inverse;
    }

    private static float EaseInOutCubic(float value)
    {
        value = Mathf.Clamp01(value);

        return value < 0.5f
            ? 4f * value * value * value
            : 1f -
              Mathf.Pow(
                  -2f * value + 2f,
                  3f
              ) / 2f;
    }

    private static CanvasGroup GetOrAddCanvasGroup(
        GameObject target)
    {
        if (target.TryGetComponent(
                out CanvasGroup canvasGroup))
        {
            return canvasGroup;
        }

        return target.AddComponent<CanvasGroup>();
    }

    private void StopActiveCoroutines()
    {
        if (animationRoutine != null)
        {
            StopCoroutine(animationRoutine);
            animationRoutine = null;
        }

        if (refreshRoutine != null)
        {
            StopCoroutine(refreshRoutine);
            refreshRoutine = null;
        }
    }

    private sealed class AnimatedTelemetryItem
    {
        public readonly StatLeaderLineView Line;
        public readonly PerformanceStatCardView Card;
        public readonly CanvasGroup CardGroup;
        public readonly RectTransform CardRect;

        public AnimatedTelemetryItem(
            StatLeaderLineView line,
            PerformanceStatCardView card,
            CanvasGroup cardGroup,
            RectTransform cardRect)
        {
            Line = line;
            Card = card;
            CardGroup = cardGroup;
            CardRect = cardRect;
        }
    }
}
