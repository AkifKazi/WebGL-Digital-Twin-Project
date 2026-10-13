using System.Linq;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class ZoomControlAvailabilityView : MonoBehaviour
{
    [SerializeField] private OrbitCameraController controller;
    [SerializeField] private Button zoomOutButton;
    [SerializeField] private Button zoomInButton;
    [SerializeField] private Graphic zoomOutIcon;
    [SerializeField] private Graphic zoomInIcon;

    private void Awake()
    {
        ResolveReferences();
    }

    private void OnEnable()
    {
        ResolveReferences();
        if (controller != null)
        {
            controller.ZoomAvailabilityChanged -= Refresh;
            controller.ZoomAvailabilityChanged += Refresh;
        }
        Refresh();
    }

    private void OnDisable()
    {
        if (controller != null)
            controller.ZoomAvailabilityChanged -= Refresh;
    }

    private void ResolveReferences()
    {
        if (controller == null)
            controller = FindAnyObjectByType<OrbitCameraController>(FindObjectsInactive.Include);

        Button[] buttons = GetComponentsInChildren<Button>(true);
        zoomOutButton ??= buttons.FirstOrDefault(button => button.name.Contains("Zoom Out"));
        zoomInButton ??= buttons.FirstOrDefault(button => button.name.Contains("Zoom In"));
        zoomOutIcon ??= FindIcon(zoomOutButton);
        zoomInIcon ??= FindIcon(zoomInButton);
    }

    private void Refresh()
    {
        bool canZoomOut = controller != null && controller.CanZoomOut;
        bool canZoomIn = controller != null && controller.CanZoomIn;
        ApplyAvailability(zoomOutButton, zoomOutIcon, canZoomOut);
        ApplyAvailability(zoomInButton, zoomInIcon, canZoomIn);
    }

    private void ApplyAvailability(Button button, Graphic icon, bool available)
    {
        if (button != null)
            button.interactable = available;
        if (icon != null)
            icon.color = UIControlColors.For(false, available);
    }

    private static Graphic FindIcon(Button button)
    {
        if (button == null)
            return null;

        return button.GetComponentsInChildren<Graphic>(true)
            .FirstOrDefault(graphic => graphic.transform != button.transform);
    }
}
