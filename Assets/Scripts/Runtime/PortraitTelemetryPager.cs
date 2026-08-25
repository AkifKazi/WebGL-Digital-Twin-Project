using System.Collections;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class PortraitTelemetryPager : MonoBehaviour
{
    [Header("Pagination")]
    [SerializeField] private PortraitStatRailManager railManager;

    [Header("Previous page")]
    [SerializeField] private Button previousButton;
    [SerializeField] private CanvasGroup previousCanvasGroup;

    [Header("Next page")]
    [SerializeField] private Button nextButton;
    [SerializeField] private CanvasGroup nextCanvasGroup;

    [Header("Transition")]
    [SerializeField, Min(0.01f)] private float availabilityFadeDuration = 0.3f;

    private Coroutine previousFade;
    private Coroutine nextFade;
    private bool listenersAdded;

    public PortraitStatRailManager RailManager => railManager;
    public Button PreviousButton => previousButton;
    public Button NextButton => nextButton;
    public float AvailabilityFadeDuration => availabilityFadeDuration;

    private void Awake()
    {
        AddButtonListeners();
    }

    private void OnEnable()
    {
        AddButtonListeners();
        Subscribe();
        Refresh(true);
    }

    private void OnDisable()
    {
        Unsubscribe();
        StopFades();
    }

    private void OnDestroy()
    {
        Unsubscribe();
        RemoveButtonListeners();
    }

    private void AddButtonListeners()
    {
        if (listenersAdded)
            return;

        previousButton?.onClick.AddListener(ShowPreviousPage);
        nextButton?.onClick.AddListener(ShowNextPage);
        listenersAdded = true;
    }

    private void RemoveButtonListeners()
    {
        if (!listenersAdded)
            return;

        previousButton?.onClick.RemoveListener(ShowPreviousPage);
        nextButton?.onClick.RemoveListener(ShowNextPage);
        listenersAdded = false;
    }

    private void Subscribe()
    {
        if (railManager == null)
            return;

        railManager.PaginationChanged -= HandlePaginationChanged;
        railManager.PaginationChanged += HandlePaginationChanged;
    }

    private void Unsubscribe()
    {
        if (railManager != null)
            railManager.PaginationChanged -= HandlePaginationChanged;
    }

    private void ShowPreviousPage()
    {
        railManager?.ShowPreviousPage();
    }

    private void ShowNextPage()
    {
        railManager?.ShowNextPage();
    }

    private void HandlePaginationChanged(int currentPageIndex, int pageCount)
    {
        Refresh(false);
    }

    private void Refresh(bool instant)
    {
        bool canGoBack = railManager != null && railManager.CanShowPreviousPage;
        bool canGoForward = railManager != null && railManager.CanShowNextPage;

        ApplyAvailability(previousButton, previousCanvasGroup, canGoBack, instant, true);
        ApplyAvailability(nextButton, nextCanvasGroup, canGoForward, instant, false);
    }

    private void ApplyAvailability(
        Button button,
        CanvasGroup group,
        bool available,
        bool instant,
        bool previous)
    {
        if (button != null)
            button.interactable = available;

        if (group == null)
            return;

        group.blocksRaycasts = available;
        group.interactable = available;

        Coroutine active = previous ? previousFade : nextFade;
        if (active != null)
            StopCoroutine(active);

        if (instant || !isActiveAndEnabled)
        {
            group.alpha = available ? 1f : 0f;
            if (previous)
                previousFade = null;
            else
                nextFade = null;
            return;
        }

        Coroutine fade = StartCoroutine(FadeTo(group, available ? 1f : 0f, previous));
        if (previous)
            previousFade = fade;
        else
            nextFade = fade;
    }

    private IEnumerator FadeTo(CanvasGroup group, float target, bool previous)
    {
        float start = group.alpha;
        float elapsed = 0f;

        while (elapsed < availabilityFadeDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            group.alpha = Mathf.Lerp(start, target, Mathf.Clamp01(elapsed / availabilityFadeDuration));
            yield return null;
        }

        group.alpha = target;
        if (previous)
            previousFade = null;
        else
            nextFade = null;
    }

    private void StopFades()
    {
        if (previousFade != null)
            StopCoroutine(previousFade);
        if (nextFade != null)
            StopCoroutine(nextFade);
        previousFade = null;
        nextFade = null;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        availabilityFadeDuration = Mathf.Max(0.01f, availabilityFadeDuration);
    }
#endif
}
