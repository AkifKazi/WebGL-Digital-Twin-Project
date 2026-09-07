using System;
using System.Collections;
using UnityEngine;

public enum MachineViewMode
{
    FullModel = 0,
    CrossSection = 1,
    XRay = 2
}

/// <summary>
/// Single owner of the three inspection views.
///
/// Full Model and Cross Section keep delegating to
/// <see cref="HybridHopperClipController"/> exactly as before. X-Ray reuses the
/// cross-section geometry, then hands presentation to
/// <see cref="MachineXRayPresenter"/> and the environment to
/// <see cref="XRayEnvironmentController"/>.
/// </summary>
[DisallowMultipleComponent]
public sealed class MachineViewModeController : MonoBehaviour
{
    [Header("Views")]
    [Tooltip("Existing cross-section controller. Full Model and Cross Section delegate to it unchanged.")]
    [SerializeField] private HybridHopperClipController clipController;
    [Tooltip("Presenter that swaps the machine to the hologram material.")]
    [SerializeField] private MachineXRayPresenter xrayPresenter;
    [Tooltip("Controller that darkens the scene while X-Ray is active.")]
    [SerializeField] private XRayEnvironmentController environmentController;

    [Header("Start state")]
    [Tooltip("View selected when the scene starts.")]
    [SerializeField] private MachineViewMode startMode = MachineViewMode.FullModel;

    /// <summary>Raised whenever the active view changes.</summary>
    public event Action<MachineViewMode> ModeChanged;

    /// <summary>Raised while a view transition runs, and again when it settles.</summary>
    public event Action<bool> TransitionStateChanged;

    public MachineViewMode CurrentMode { get; private set; } = MachineViewMode.FullModel;

    public bool IsTransitioning =>
        transitionRoutine != null ||
        (clipController != null && clipController.IsTransitioning);

    private Coroutine transitionRoutine;

    private void Start()
    {
        CurrentMode = clipController != null && clipController.IsCrossSection
            ? MachineViewMode.CrossSection
            : MachineViewMode.FullModel;

        if (startMode != CurrentMode)
            SetMode(startMode);
        else
            ModeChanged?.Invoke(CurrentMode);
    }

    // Unity events on the segmented buttons bind to these.
    public void SelectFullModel() => SetMode(MachineViewMode.FullModel);

    public void SelectCrossSection() => SetMode(MachineViewMode.CrossSection);

    public void SelectXRay() => SetMode(MachineViewMode.XRay);

    public void SetMode(MachineViewMode mode)
    {
        if (mode == CurrentMode || transitionRoutine != null)
            return;

        if (clipController != null && clipController.IsTransitioning)
            return;

        MachineViewMode previous = CurrentMode;
        CurrentMode = mode;

        ModeChanged?.Invoke(CurrentMode);
        TransitionStateChanged?.Invoke(true);

        transitionRoutine = StartCoroutine(TransitionRoutine(previous, mode));
    }

    private IEnumerator TransitionRoutine(MachineViewMode previous, MachineViewMode next)
    {
        // Leaving X-Ray: fade the hologram out and bring the environment back
        // before the geometry changes, so the two never cross over.
        if (previous == MachineViewMode.XRay)
        {
            if (xrayPresenter != null)
            {
                xrayPresenter.SetPresenting(false);
                yield return new WaitWhile(() => xrayPresenter.IsTransitioning);
            }

            if (environmentController != null)
                environmentController.SetDarkEnvironment(false);
        }

        switch (next)
        {
            case MachineViewMode.FullModel:
                if (clipController != null)
                    clipController.ShowFull();
                break;

            case MachineViewMode.CrossSection:
                if (clipController != null)
                    clipController.ShowCrossSection();
                break;

            case MachineViewMode.XRay:
                // X-Ray inspects the cutaway geometry, so make sure the clip
                // controller is already there before the hologram appears.
                if (clipController != null && !clipController.IsCrossSection)
                {
                    clipController.ShowCrossSection();
                    yield return new WaitWhile(() => clipController.IsTransitioning);
                }

                if (environmentController != null)
                    environmentController.SetDarkEnvironment(true);

                if (xrayPresenter != null)
                {
                    xrayPresenter.SetPresenting(true);
                    yield return new WaitWhile(() => xrayPresenter.IsTransitioning);
                }

                break;
        }

        if (clipController != null)
            yield return new WaitWhile(() => clipController.IsTransitioning);

        transitionRoutine = null;
        TransitionStateChanged?.Invoke(false);
    }
}
