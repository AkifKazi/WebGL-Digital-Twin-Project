using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class PortraitTelemetryPager : MonoBehaviour
{
    [Header("Pagination")]
    [SerializeField] private PortraitStatRailManager railManager;
    [SerializeField] private WideStatRailManager wideRailManager;

    [Header("Telemetry visibility")]
    [Tooltip("Pagination is available only while telemetry is visible in Cross Section view.")]
    [SerializeField] private HybridHopperClipController hopperController;
    [Tooltip("Optional shared group used when pagination has its own standalone panel.")]
    [SerializeField] private CanvasGroup paginationCanvasGroup;

    [Header("Previous page")]
    [SerializeField] private Button previousButton;
    [SerializeField] private CanvasGroup previousCanvasGroup;
    [SerializeField] private Graphic previousIcon;

    [Header("Next page")]
    [SerializeField] private Button nextButton;
    [SerializeField] private CanvasGroup nextCanvasGroup;
    [SerializeField] private Graphic nextIcon;

    [Header("Availability colours")]
    [SerializeField] private Color enabledIconColor = Color.white;
    [SerializeField] private Color disabledIconColor = new(0.48f, 0.52f, 0.55f, 1f);

    private bool listenersAdded;

    public PortraitStatRailManager RailManager => railManager;
    public WideStatRailManager WideRailManager => wideRailManager;
    public HybridHopperClipController HopperController => hopperController;
    public CanvasGroup PaginationCanvasGroup => paginationCanvasGroup;
    public Button PreviousButton => previousButton;
    public Button NextButton => nextButton;
    public Graphic PreviousIcon => previousIcon;
    public Graphic NextIcon => nextIcon;
    public bool HasExactlyOneRailManager => (railManager != null) != (wideRailManager != null);

    private void Awake()
    {
        AddButtonListeners();
    }

    private void OnEnable()
    {
        AddButtonListeners();
        Subscribe();
        Refresh();
    }

    private void OnDisable()
    {
        Unsubscribe();
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
        if (railManager != null)
        {
            railManager.PaginationChanged -= HandlePaginationChanged;
            railManager.PaginationChanged += HandlePaginationChanged;
        }

        if (wideRailManager != null)
        {
            wideRailManager.PaginationChanged -= HandlePaginationChanged;
            wideRailManager.PaginationChanged += HandlePaginationChanged;
        }

        if (hopperController != null)
        {
            hopperController.ViewStateChanged -= HandleViewStateChanged;
            hopperController.ViewStateChanged += HandleViewStateChanged;
        }
    }

    private void Unsubscribe()
    {
        if (railManager != null)
            railManager.PaginationChanged -= HandlePaginationChanged;
        if (wideRailManager != null)
            wideRailManager.PaginationChanged -= HandlePaginationChanged;
        if (hopperController != null)
            hopperController.ViewStateChanged -= HandleViewStateChanged;
    }

    private void ShowPreviousPage()
    {
        if (railManager != null)
            railManager.ShowPreviousPage();
        else
            wideRailManager?.ShowPreviousPage();
    }

    private void ShowNextPage()
    {
        if (railManager != null)
            railManager.ShowNextPage();
        else
            wideRailManager?.ShowNextPage();
    }

    private void HandlePaginationChanged(int currentPageIndex, int pageCount)
    {
        Refresh();
    }

    private void HandleViewStateChanged(bool isCrossSection)
    {
        Refresh();
    }

    private void Refresh()
    {
        bool telemetryVisible = hopperController != null && hopperController.IsCrossSection;
        bool paginationVisible = telemetryVisible && PageCount > 1;
        bool canGoBack = paginationVisible && CanShowPreviousPage;
        bool canGoForward = paginationVisible && CanShowNextPage;

        if (paginationCanvasGroup != null)
        {
            paginationCanvasGroup.alpha = paginationVisible ? 1f : 0f;
            paginationCanvasGroup.interactable = paginationVisible;
            paginationCanvasGroup.blocksRaycasts = paginationVisible;
        }

        ApplyAvailability(
            previousButton,
            previousCanvasGroup,
            previousIcon,
            paginationVisible,
            canGoBack);
        ApplyAvailability(
            nextButton,
            nextCanvasGroup,
            nextIcon,
            paginationVisible,
            canGoForward);
    }

    private int PageCount => railManager != null
        ? railManager.PageCount
        : wideRailManager != null ? wideRailManager.PageCount : 1;

    private bool CanShowPreviousPage => railManager != null
        ? railManager.CanShowPreviousPage
        : wideRailManager != null && wideRailManager.CanShowPreviousPage;

    private bool CanShowNextPage => railManager != null
        ? railManager.CanShowNextPage
        : wideRailManager != null && wideRailManager.CanShowNextPage;

    private void ApplyAvailability(
        Button button,
        CanvasGroup group,
        Graphic icon,
        bool telemetryVisible,
        bool available)
    {
        if (button != null)
            button.interactable = telemetryVisible && available;

        if (icon != null)
            icon.color = available ? enabledIconColor : disabledIconColor;

        if (group == null)
            return;

        bool individuallyVisible = paginationCanvasGroup != null || telemetryVisible;
        group.alpha = individuallyVisible ? 1f : 0f;
        group.blocksRaycasts = telemetryVisible;
        group.interactable = telemetryVisible;
    }

}
