using UnityEngine;

/// <summary>
/// Marks a renderer created purely to draw a hover overlay on top of an
/// existing mesh. Systems that walk the equipment hierarchy skip these, so a
/// proxy is never mistaken for real machine geometry.
/// </summary>
[DisallowMultipleComponent]
public sealed class MachineOverlayProxy : MonoBehaviour
{
}
