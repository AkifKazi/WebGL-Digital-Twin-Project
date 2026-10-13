using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class PortraitTelemetryPager : MonoBehaviour
{
    [Header("Pagination")]
    [Tooltip("The rails of the layout whose pages these buttons turn.")]
    [SerializeField] private StatRailManager railManager;

    [Header("Telemetry visibility")]
    [Tooltip("This layout's telemetry visibility. Pagination is available only while the inspection view shows telemetry.")]
    [SerializeField] private TelemetryVisibility visibility;
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

    private bool listenersAdded;

    public StatRailManager RailManager => railManager;
    public TelemetryVisibility Visibility => visibility;
    public CanvasGroup PaginationCanvasGroup => paginationCanvasGroup;
    public Button PreviousButton => previousButton;
    public Button NextButton => nextButton;
    public Graphic PreviousIcon => previousIcon;
    public Graphic NextIcon => nextIcon;

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

        if (visibility != null)
        {
            visibility.InspectionShowsTelemetryChanged -= HandleInspectionChanged;
            visibility.InspectionShowsTelemetryChanged += HandleInspectionChanged;
        }
    }

    private void Unsubscribe()
    {
        if (railManager != null)
            railManager.PaginationChanged -= HandlePaginationChanged;
        if (visibility != null)
            visibility.InspectionShowsTelemetryChanged -= HandleInspectionChanged;
    }

    private void ShowPreviousPage()
    {
        if (railManager != null)
            railManager.ShowPreviousPage();
    }

    private void ShowNextPage()
    {
        if (railManager != null)
            railManager.ShowNextPage();
    }

    private void HandlePaginationChanged(int currentPageIndex, int pageCount)
    {
        Refresh();
    }

    private void HandleInspectionChanged(bool showsTelemetry)
    {
        Refresh();
    }

    private void Refresh()
    {
        bool telemetryVisible = visibility != null && visibility.InspectionShowsTelemetry;
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

    private int PageCount => railManager != null ? railManager.PageCount : 1;
    private bool CanShowPreviousPage => railManager != null && railManager.CanShowPreviousPage;
    private bool CanShowNextPage => railManager != null && railManager.CanShowNextPage;

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
            icon.color = UIControlColors.For(false, available);

        if (group == null)
            return;

        bool individuallyVisible = paginationCanvasGroup != null || telemetryVisible;
        group.alpha = individuallyVisible ? 1f : 0f;
        group.blocksRaycasts = telemetryVisible;
        group.interactable = telemetryVisible;
    }

}
