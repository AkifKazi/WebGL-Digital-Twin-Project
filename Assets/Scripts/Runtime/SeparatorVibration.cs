using UnityEngine;

public class SeparatorVibration : MonoBehaviour
{
    [Header("Vibration Settings")]
    public float amplitudeX = 0.015f;
    public float amplitudeY = 0.006f;
    public float frequency = 35f;

    [Header("Optional Rotation")]
    public float rotationAmplitude = 0.4f;

    private Vector3 startPosition;
    private Quaternion startRotation;

    void Start()
    {
        startPosition = transform.localPosition;
        startRotation = transform.localRotation;
    }

    void Update()
    {
        float t = Time.time * frequency;

        float x = Mathf.Sin(t) * amplitudeX;
        float y = Mathf.Sin(t * 1.3f) * amplitudeY;

        transform.localPosition = startPosition + new Vector3(x, y, 0f);

        float zRot = Mathf.Sin(t * 0.8f) * rotationAmplitude;
        transform.localRotation = startRotation * Quaternion.Euler(0f, 0f, zRot);
    }
}