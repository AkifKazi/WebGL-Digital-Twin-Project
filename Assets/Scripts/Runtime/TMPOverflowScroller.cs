using TMPro;
using UnityEngine;
using UnityEngine.UI;

public enum TextOverflowMotion
{
    PingPong,
    Continuous
}

[DisallowMultipleComponent]
[RequireComponent(typeof(TMP_Text))]
public sealed class TMPOverflowScroller : MonoBehaviour
{
    [SerializeField] private TextOverflowMotion motion = TextOverflowMotion.PingPong;
    [SerializeField, Min(1f)] private float speed = 22f;
    [SerializeField, Min(0f)] private float edgePause = 1.2f;
    [SerializeField, Min(0f)] private float continuousGap = 32f;
    [SerializeField, Min(0f)] private float edgeFadeWidth = 16f;
    [SerializeField, Min(0f)] private float edgeFadeTransition = 12f;

    private TMP_Text label;
    private RectTransform labelRect;
    private RectTransform viewport;
    private RectMask2D viewportMask;
    private float overflow;
    private float direction = -1f;
    private float pauseRemaining;
    private float originX;
    private string previousText;
    private bool originCaptured;
    private float leftFadeStrength;
    private float rightFadeStrength;
    private bool preRenderSubscribed;

    private void Awake()
    {
        label = GetComponent<TMP_Text>();
        labelRect = transform as RectTransform;
        viewport = transform.parent as RectTransform;
        EnsureMask();
        SubscribeToPreRender();
        ResetMotion();
    }

    private void OnEnable()
    {
        SubscribeToPreRender();
        ResetMotion();
    }

    private void OnDisable()
    {
        UnsubscribeFromPreRender();
    }

    private void LateUpdate()
    {
        if (label == null || labelRect == null || viewport == null)
            return;

        if (!string.Equals(previousText, label.text, System.StringComparison.Ordinal))
            ResetMotion();

        RecalculateOverflow();
        if (overflow <= 0.5f)
        {
            SetX(originX);
            SetFadeStrengths(0f, 0f);
            return;
        }

        if (pauseRemaining > 0f)
        {
            pauseRemaining -= Time.unscaledDeltaTime;
            return;
        }

        float x = labelRect.anchoredPosition.x;
        if (motion == TextOverflowMotion.PingPong)
        {
            x += direction * speed * Time.unscaledDeltaTime;
            float minimumX = originX - overflow;
            if (x <= minimumX)
            {
                x = minimumX;
                direction = 1f;
                pauseRemaining = edgePause;
            }
            else if (x >= originX)
            {
                x = originX;
                direction = -1f;
                pauseRemaining = edgePause;
            }
        }
        else
        {
            x -= speed * Time.unscaledDeltaTime;
            if (x <= originX - label.preferredWidth - continuousGap)
                x = originX + viewport.rect.width;
        }

        SetX(x);
        UpdateDirectionalFade(x);
    }

    public void SetMotion(TextOverflowMotion value)
    {
        if (motion == value)
            return;
        motion = value;
        ResetMotion();
    }

    public void CaptureRestingPosition()
    {
        if (labelRect == null)
            labelRect = transform as RectTransform;
        originX = labelRect != null ? labelRect.anchoredPosition.x : 0f;
        originCaptured = true;
        ResetMotion();
    }

    public void ResetMotion()
    {
        if (label == null)
            label = GetComponent<TMP_Text>();
        if (labelRect == null)
            labelRect = transform as RectTransform;
        // Metric slots can be cloned and then moved under a dedicated runtime
        // viewport. Always refresh the parent instead of retaining the cloned
        // component's old cached header.
        viewport = transform.parent as RectTransform;
        EnsureMask();

        previousText = label != null ? label.text : string.Empty;
        if (!originCaptured && labelRect != null)
        {
            originX = labelRect.anchoredPosition.x;
            originCaptured = true;
        }
        direction = -1f;
        pauseRemaining = edgePause;
        SetX(originX);
        RecalculateOverflow();
        UpdateDirectionalFade(originX);
    }

    private void RecalculateOverflow()
    {
        if (label == null || viewport == null)
            return;
        label.ForceMeshUpdate();
        overflow = Mathf.Max(0f, label.preferredWidth - viewport.rect.width);
    }

    private void EnsureMask()
    {
        if (viewport == null)
            return;

        viewportMask = viewport.GetComponent<RectMask2D>();
        if (viewportMask == null)
            viewportMask = viewport.gameObject.AddComponent<RectMask2D>();

        // RectMask2D applies horizontal softness symmetrically. The title
        // needs independent left/right fades, so clipping remains hard here
        // and ApplyDirectionalFade handles the Spotify-style edge treatment.
        viewportMask.softness = Vector2Int.zero;
    }

    private void SetX(float x)
    {
        if (labelRect == null)
            return;
        Vector2 position = labelRect.anchoredPosition;
        position.x = x;
        labelRect.anchoredPosition = position;
        label?.SetVerticesDirty();
    }

    private void UpdateDirectionalFade(float x)
    {
        if (overflow <= 0.5f || edgeFadeWidth <= 0f)
        {
            SetFadeStrengths(0f, 0f);
            return;
        }

        if (motion == TextOverflowMotion.Continuous)
        {
            SetFadeStrengths(1f, 1f);
            return;
        }

        float hiddenOnLeft = Mathf.Clamp(originX - x, 0f, overflow);
        float hiddenOnRight = Mathf.Max(0f, overflow - hiddenOnLeft);
        float transition = Mathf.Max(0.01f, edgeFadeTransition);
        SetFadeStrengths(
            Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(hiddenOnLeft / transition)),
            Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(hiddenOnRight / transition)));
    }

    private void SetFadeStrengths(float left, float right)
    {
        if (Mathf.Approximately(leftFadeStrength, left) &&
            Mathf.Approximately(rightFadeStrength, right))
            return;

        leftFadeStrength = left;
        rightFadeStrength = right;
        label?.SetVerticesDirty();
    }

    private void SubscribeToPreRender()
    {
        if (label == null)
            label = GetComponent<TMP_Text>();
        if (label == null || preRenderSubscribed)
            return;

        label.OnPreRenderText += ApplyDirectionalFade;
        preRenderSubscribed = true;
    }

    private void UnsubscribeFromPreRender()
    {
        if (label != null && preRenderSubscribed)
            label.OnPreRenderText -= ApplyDirectionalFade;
        preRenderSubscribed = false;
    }

    private void ApplyDirectionalFade(TMP_TextInfo textInfo)
    {
        if (labelRect == null || viewport == null || edgeFadeWidth <= 0f ||
            (leftFadeStrength <= 0f && rightFadeStrength <= 0f))
            return;

        Rect viewportRect = viewport.rect;
        byte baseAlpha = (byte)Mathf.RoundToInt(255f * label.color.a);

        for (int i = 0; i < textInfo.characterCount; i++)
        {
            TMP_CharacterInfo character = textInfo.characterInfo[i];
            if (!character.isVisible)
                continue;

            int materialIndex = character.materialReferenceIndex;
            int vertexIndex = character.vertexIndex;
            Vector3[] vertices = textInfo.meshInfo[materialIndex].vertices;
            Color32[] colors = textInfo.meshInfo[materialIndex].colors32;

            for (int vertex = 0; vertex < 4; vertex++)
            {
                int index = vertexIndex + vertex;
                Vector3 world = labelRect.TransformPoint(vertices[index]);
                float viewportX = viewport.InverseTransformPoint(world).x;
                float leftFactor = Mathf.Clamp01(
                    (viewportX - viewportRect.xMin) / edgeFadeWidth);
                float rightFactor = Mathf.Clamp01(
                    (viewportRect.xMax - viewportX) / edgeFadeWidth);
                float alpha = Mathf.Lerp(1f, leftFactor, leftFadeStrength) *
                              Mathf.Lerp(1f, rightFactor, rightFadeStrength);
                colors[index].a = (byte)Mathf.RoundToInt(baseAlpha * alpha);
            }
        }
    }
}
