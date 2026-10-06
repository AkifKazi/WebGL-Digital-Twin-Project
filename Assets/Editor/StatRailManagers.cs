using System.Linq;
using UnityEngine;

/// <summary>
/// Finds one layout's <see cref="StatRailManager"/> in the open scene, for
/// setup tools, validators and tests. Both layouts use the same component;
/// the portrait one is the one that always runs rows.
/// </summary>
public static class StatRailManagers
{
    /// <summary>The wide layout's rails: a desktop browser, a phone held sideways.</summary>
    public static StatRailManager Wide(FindObjectsInactive inactive = FindObjectsInactive.Exclude) =>
        Find(false, inactive);

    /// <summary>The portrait layout's rails: a phone held upright.</summary>
    public static StatRailManager Portrait(FindObjectsInactive inactive = FindObjectsInactive.Exclude) =>
        Find(true, inactive);

    private static StatRailManager Find(bool alwaysRows, FindObjectsInactive inactive) =>
        Object.FindObjectsByType<StatRailManager>(inactive)
            .FirstOrDefault(manager => manager.AlwaysRows == alwaysRows);
}
