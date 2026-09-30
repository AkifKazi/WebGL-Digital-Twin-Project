using System;
using TMPro;
using UnityEngine;

/// <summary>
/// Keeps one label showing the language table's copy for its key, and
/// re-applies it whenever the table or the language changes. Views add it to
/// labels they build in code with <see cref="Bind"/>; it can also be added to
/// a label in the editor, where an empty fallback takes the label's authored
/// text.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(TMP_Text))]
public sealed class UITextBinding : MonoBehaviour
{
    [Tooltip("Key of the row in the language table (Assets/StreamingAssets/Localization/ui_text.csv).")]
    [SerializeField] private string key;

    [Tooltip("Shown when the table has no copy for the key. Empty uses the label's authored text.")]
    [SerializeField] private string fallback;

    private TMP_Text label;

    /// <summary>Raised after the copy is applied, for views that size themselves to it.</summary>
    public event Action<UITextBinding> Applied;

    public string Key => key;
    public TMP_Text Label => label != null ? label : label = GetComponent<TMP_Text>();

    /// <summary>Binds <paramref name="text"/> to <paramref name="bindingKey"/> and shows its copy now.</summary>
    public static UITextBinding Bind(TMP_Text text, string bindingKey, string fallbackText)
    {
        if (text == null)
            return null;

        if (!text.TryGetComponent(out UITextBinding binding))
            binding = text.gameObject.AddComponent<UITextBinding>();
        binding.key = bindingKey;
        binding.fallback = fallbackText;
        binding.Apply();
        return binding;
    }

    private void Awake()
    {
        if (string.IsNullOrEmpty(fallback) && Label != null)
            fallback = Label.text;
    }

    private void OnEnable()
    {
        UIText.Changed -= Apply;
        UIText.Changed += Apply;
        Apply();
    }

    private void OnDisable()
    {
        UIText.Changed -= Apply;
    }

    /// <summary>
    /// Lets a label whose box is fixed take longer copy: the type shrinks to
    /// fit, down to <paramref name="minimumShare"/> of its authored size, and
    /// past that the copy is cut short with an ellipsis instead of running
    /// into its neighbours. Safe to call more than once.
    /// </summary>
    public static void AllowShrinkToFit(TMP_Text text, float minimumShare = 0.7f)
    {
        if (text == null)
            return;

        float authored = text.enableAutoSizing ? text.fontSizeMax : text.fontSize;
        if (authored <= 0f)
            return;
        text.enableAutoSizing = true;
        text.fontSizeMax = authored;
        text.fontSizeMin = Mathf.Max(1f, authored * Mathf.Clamp01(minimumShare));
        text.overflowMode = TextOverflowModes.Ellipsis;
    }

    public void Apply()
    {
        if (Label == null)
            return;

        string text = UIText.Get(key, fallback);
        if (!string.Equals(Label.text, text, StringComparison.Ordinal))
            Label.text = text;
        Applied?.Invoke(this);
    }
}
