using System;
using UnityEngine;

public class OrbitCameraController : MonoBehaviour
{
    public event Action ZoomAvailabilityChanged;

    private const float ZoomLimitEpsilon = 0.01f;

    public bool CanZoomIn => !introPlaying && desiredDistance > minDistance + ZoomLimitEpsilon;
    public bool CanZoomOut => !introPlaying && desiredDistance < maxDistance - ZoomLimitEpsilon;
    [Header("Target")]
    public Transform target;

    [Tooltip("Optional offset from target center. Useful if the hopper center is too low.")]
    public Vector3 targetOffset = Vector3.zero;

    [Header("Start Position")]
    [Tooltip("If true, the camera starts from its current Scene position instead of using yaw/pitch/distance values.")]
    public bool useCurrentCameraPositionAsStart = false;

    [Header("Intro Animation")]
    [Tooltip("Distance used at the beginning of the intro animation.")]
    public float startDistance = 14f;

    [Tooltip("Pitch used at the beginning of the intro animation.")]
    public float startPitch = 20f;

    [Tooltip("Yaw used at the beginning of the intro animation.")]
    public float startYaw = -130f;

    [Tooltip("Higher values make the intro transition finish faster.")]
    [Min(0.01f)]
    public float animationSpeed = 1.5f;

    [Header("Distance / Zoom")]
    public float currentDistance = 12f;
    public float minDistance = 3f;
    public float maxDistance = 14f;

    [Header("Global Speed Limit")]
    [Tooltip("The absolute maximum speed limit for all inputs. The Inspector will clamp values to ensure they do not exceed this.")]
    public float maxAllowedSpeed = 15f;

    [Tooltip("Used only for W and S keyboard zoom.")]
    public float keyboardZoomSpeed = 10f;

    [Tooltip("Used for + and - UI buttons.")]
    public float buttonZoomSpeed = 10f;

    [Tooltip("Used for mouse wheel / trackpad scroll zoom.")]
    public float mouseWheelZoomSpeed = 0.5f;

    [Header("Orbit Rotation")]
    public float yaw = -90f;

    [Tooltip("Vertical angle. 0 = horizontal view, 55 = view from height.")]
    public float pitch = 8f;

    [Tooltip("Mouse drag orbit speed for desktop/WebGL.")]
    public float mouseOrbitSpeed = 4f;

    [Tooltip("Touch drag orbit speed for mobile/WebGL.")]
    public float touchOrbitSpeed = 0.15f;

    [Header("Angle Limits")]
    public float minPitch = -4f;
    public float maxPitch = 40f;

    [Header("Input")]
    public bool enableMouseDrag = true;
    public bool enableTouchDrag = true;
    public bool enableKeyboardWS = true;
    public bool enableMouseWheelZoom = true;

    [Header("Mobile Pinch Zoom")]
    public bool enablePinchZoom = true;

    [Tooltip("1 feels like direct manipulation. Lower values make the gesture gentler.")]
    [Range(0.25f, 2f)]
    public float pinchZoomSensitivity = 1f;

    [Tooltip("Ignores tiny two-finger changes to prevent visible camera jitter.")]
    [Min(0f)]
    public float pinchDeadZonePixels = 1.5f;

    [Header("Mouse Settings")]
    public int mouseButton = 0;
    public bool ignoreMouseWhenPointerOverUI = true;

    [Header("Smoothing")]
    public bool smoothMotion = true;
    public float smoothSpeed = 12f;

    [Header("Framing")]
    [Tooltip("Free space kept at the left and right when the camera pulls back to show a point, " +
             "as a share of the screen. The telemetry rails sit in this band.")]
    [Range(0f, 0.45f)] public float framingMarginX = 0.22f;

    [Tooltip("Free space kept at the top and bottom when the camera pulls back to show a point.")]
    [Range(0f, 0.45f)] public float framingMarginY = 0.14f;

    [Header("Debug")]
    public bool showDebug = false;

    private float desiredDistance;
    private float desiredYaw;
    private float desiredPitch;

    private float introStartDistance;
    private float introStartYaw;
    private float introStartPitch;

    private float introTargetDistance;
    private float introTargetYaw;
    private float introTargetPitch;

    private float introProgress;
    private bool introPlaying;

    private bool zoomingIn;
    private bool zoomingOut;

    private Vector2 lastTouchPosition;
    private bool hasLastTouchPosition;
    private float previousPinchDistance;
    private bool pinchActive;
    private bool lastCanZoomIn;
    private bool lastCanZoomOut;
    private bool zoomAvailabilityInitialised;

    private void Start()
    {
        if (target == null)
            return;

        if (useCurrentCameraPositionAsStart)
        {
            CalculateOrbitValuesFromCurrentCameraPosition();
            introPlaying = false;
            PublishZoomAvailability(true);
            return;
        }

        introTargetDistance = Mathf.Clamp(currentDistance, minDistance, maxDistance);
        introTargetYaw = yaw;
        introTargetPitch = Mathf.Clamp(pitch, minPitch, maxPitch);

        introStartDistance = Mathf.Clamp(startDistance, minDistance, maxDistance);
        introStartYaw = startYaw;
        introStartPitch = Mathf.Clamp(startPitch, minPitch, maxPitch);

        desiredDistance = introStartDistance;
        desiredYaw = introStartYaw;
        desiredPitch = introStartPitch;

        introProgress = 0f;
        introPlaying = true;

        ApplyCameraPosition(true);
        PublishZoomAvailability(true);
    }

    private void Update()
    {
        if (target == null)
            return;

        if (introPlaying)
        {
            UpdateIntroAnimation();
            ApplyCameraPosition(true);
        }
        else
        {
            HandleKeyboardInput();
            HandleButtonZoomInput();
            HandleMouseWheelInput();
            HandleMouseDragInput();
            HandlePinchZoomInput();
            HandleTouchDragInput();

            ClampValues();
            ApplyCameraPosition(false);
        }

        if (showDebug)
        {
            Debug.Log(
                "Orbit Camera | Distance: " + desiredDistance.ToString("F2") +
                " | Yaw: " + desiredYaw.ToString("F1") +
                " | Pitch: " + desiredPitch.ToString("F1") +
                " | Intro Playing: " + introPlaying
            );
        }

        if (!CanZoomIn)
            zoomingIn = false;
        if (!CanZoomOut)
            zoomingOut = false;
        PublishZoomAvailability(false);
    }

    private void UpdateIntroAnimation()
    {
        introProgress += Time.deltaTime * animationSpeed;
        float t = Mathf.Clamp01(introProgress);

        // Cubic ease-out: moves quickly at first, then settles gently.
        float easedT = 1f - Mathf.Pow(1f - t, 3f);

        desiredDistance = Mathf.Lerp(introStartDistance, introTargetDistance, easedT);
        desiredPitch = Mathf.Lerp(introStartPitch, introTargetPitch, easedT);
        desiredYaw = Mathf.LerpAngle(introStartYaw, introTargetYaw, easedT);

        if (t >= 1f)
        {
            desiredDistance = introTargetDistance;
            desiredPitch = introTargetPitch;
            desiredYaw = introTargetYaw;

            currentDistance = desiredDistance;
            pitch = desiredPitch;
            yaw = desiredYaw;

            introPlaying = false;
        }
    }

    // Automatically clamps the inspector values whenever a change is made in the editor
    private void OnValidate()
    {
        maxAllowedSpeed = Mathf.Max(0f, maxAllowedSpeed);

        keyboardZoomSpeed = Mathf.Clamp(keyboardZoomSpeed, 0f, maxAllowedSpeed);
        buttonZoomSpeed = Mathf.Clamp(buttonZoomSpeed, 0f, maxAllowedSpeed);
        mouseWheelZoomSpeed = Mathf.Clamp(mouseWheelZoomSpeed, 0f, maxAllowedSpeed);
        mouseOrbitSpeed = Mathf.Clamp(mouseOrbitSpeed, 0f, maxAllowedSpeed);
        touchOrbitSpeed = Mathf.Clamp(touchOrbitSpeed, 0f, maxAllowedSpeed);
        pinchZoomSensitivity = Mathf.Clamp(pinchZoomSensitivity, 0.25f, 2f);
        pinchDeadZonePixels = Mathf.Max(0f, pinchDeadZonePixels);

        minDistance = Mathf.Max(0.01f, minDistance);
        maxDistance = Mathf.Max(minDistance, maxDistance);

        currentDistance = Mathf.Clamp(currentDistance, minDistance, maxDistance);
        startDistance = Mathf.Clamp(startDistance, minDistance, maxDistance);

        pitch = Mathf.Clamp(pitch, minPitch, maxPitch);
        startPitch = Mathf.Clamp(startPitch, minPitch, maxPitch);

        animationSpeed = Mathf.Max(0.01f, animationSpeed);
    }

    private void CalculateOrbitValuesFromCurrentCameraPosition()
    {
        Vector3 center = target.position + targetOffset;
        Vector3 offset = transform.position - center;

        float distance = offset.magnitude;

        if (distance < 0.001f)
            distance = currentDistance;

        desiredDistance = Mathf.Clamp(distance, minDistance, maxDistance);

        Vector3 directionFromTargetToCamera = offset.normalized;

        desiredPitch = Mathf.Asin(directionFromTargetToCamera.y) * Mathf.Rad2Deg;
        desiredYaw = Mathf.Atan2(directionFromTargetToCamera.x, directionFromTargetToCamera.z) * Mathf.Rad2Deg;

        desiredPitch = Mathf.Clamp(desiredPitch, minPitch, maxPitch);

        currentDistance = desiredDistance;
        yaw = desiredYaw;
        pitch = desiredPitch;

        ApplyCameraPosition(true);
    }

    private void HandleKeyboardInput()
    {
        if (!enableKeyboardWS)
            return;

        if (Input.GetKey(KeyCode.W))
        {
            desiredDistance -= keyboardZoomSpeed * Time.deltaTime;
        }

        if (Input.GetKey(KeyCode.S))
        {
            desiredDistance += keyboardZoomSpeed * Time.deltaTime;
        }
    }

    private void HandleButtonZoomInput()
    {
        if (zoomingIn)
        {
            desiredDistance -= buttonZoomSpeed * Time.deltaTime;
        }

        if (zoomingOut)
        {
            desiredDistance += buttonZoomSpeed * Time.deltaTime;
        }
    }

    private void HandleMouseWheelInput()
    {
        if (!enableMouseWheelZoom)
            return;

        float scroll = Input.mouseScrollDelta.y;

        if (Mathf.Abs(scroll) > 0.001f)
        {
            desiredDistance -= scroll * mouseWheelZoomSpeed;
        }
    }

    private void HandleMouseDragInput()
    {
        if (!enableMouseDrag)
            return;

#if UNITY_EDITOR || UNITY_STANDALONE || UNITY_WEBGL
        if (Input.touchCount > 0)
            return;

        if (Input.GetMouseButton(mouseButton))
        {
            if (ignoreMouseWhenPointerOverUI && UnityEngine.EventSystems.EventSystem.current != null)
            {
                if (UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject())
                    return;
            }

            float mouseX = Input.GetAxis("Mouse X");
            float mouseY = Input.GetAxis("Mouse Y");

            desiredYaw += mouseX * mouseOrbitSpeed;
            desiredPitch -= mouseY * mouseOrbitSpeed;
        }
#endif
    }

    private void HandleTouchDragInput()
    {
        if (!enableTouchDrag)
            return;

        if (Input.touchCount != 1)
        {
            hasLastTouchPosition = false;
            return;
        }

        Touch touch = Input.GetTouch(0);

        if (UnityEngine.EventSystems.EventSystem.current != null)
        {
            if (UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject(touch.fingerId))
            {
                hasLastTouchPosition = false;
                return;
            }
        }

        if (touch.phase == TouchPhase.Began)
        {
            lastTouchPosition = touch.position;
            hasLastTouchPosition = true;
            return;
        }

        if (touch.phase == TouchPhase.Moved && hasLastTouchPosition)
        {
            Vector2 delta = touch.position - lastTouchPosition;

            desiredYaw += delta.x * touchOrbitSpeed;
            desiredPitch -= delta.y * touchOrbitSpeed;

            lastTouchPosition = touch.position;
        }

        if (touch.phase == TouchPhase.Ended || touch.phase == TouchPhase.Canceled)
        {
            hasLastTouchPosition = false;
        }
    }

    private void HandlePinchZoomInput()
    {
        if (!enablePinchZoom || Input.touchCount != 2)
        {
            pinchActive = false;
            previousPinchDistance = 0f;
            return;
        }

        Touch first = Input.GetTouch(0);
        Touch second = Input.GetTouch(1);

        if (IsTouchOverUI(first) || IsTouchOverUI(second))
        {
            pinchActive = false;
            previousPinchDistance = 0f;
            return;
        }

        float currentPinchDistance = Vector2.Distance(first.position, second.position);

        if (currentPinchDistance <= 0.01f)
            return;

        if (!pinchActive ||
            first.phase == TouchPhase.Began ||
            second.phase == TouchPhase.Began)
        {
            previousPinchDistance = currentPinchDistance;
            pinchActive = true;
            hasLastTouchPosition = false;
            return;
        }

        float pixelDelta = currentPinchDistance - previousPinchDistance;

        if (Mathf.Abs(pixelDelta) >= pinchDeadZonePixels)
        {
            // A ratio makes the gesture consistent across screen resolutions.
            float distanceRatio = previousPinchDistance / currentPinchDistance;
            desiredDistance *= Mathf.Pow(distanceRatio, pinchZoomSensitivity);
            ClampValues();
        }

        previousPinchDistance = currentPinchDistance;
        hasLastTouchPosition = false;
    }

    private static bool IsTouchOverUI(Touch touch)
    {
        UnityEngine.EventSystems.EventSystem eventSystem =
            UnityEngine.EventSystems.EventSystem.current;

        return eventSystem != null &&
               eventSystem.IsPointerOverGameObject(touch.fingerId);
    }

    private void ClampValues()
    {
        desiredDistance = Mathf.Clamp(desiredDistance, minDistance, maxDistance);
        desiredPitch = Mathf.Clamp(desiredPitch, minPitch, maxPitch);
    }

    private void ApplyCameraPosition(bool instant)
    {
        Vector3 center = target.position + targetOffset;

        Quaternion rotation = Quaternion.Euler(desiredPitch, desiredYaw, 0f);
        Vector3 desiredPosition = center - rotation * Vector3.forward * desiredDistance;

        if (instant || !smoothMotion)
        {
            transform.position = desiredPosition;
            transform.rotation = rotation;
        }
        else
        {
            float interpolation = 1f - Mathf.Exp(-smoothSpeed * Time.deltaTime);

            transform.position = Vector3.Lerp(
                transform.position,
                desiredPosition,
                interpolation
            );

            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                rotation,
                interpolation
            );
        }

        transform.LookAt(center);

        currentDistance = desiredDistance;
        yaw = desiredYaw;
        pitch = desiredPitch;
    }

    // UI button functions for EventTrigger Pointer Down / Pointer Up

    public void StartZoomIn()
    {
        if (CanZoomIn)
            zoomingIn = true;
    }

    public void StopZoomIn()
    {
        zoomingIn = false;
    }

    public void StartZoomOut()
    {
        if (CanZoomOut)
            zoomingOut = true;
    }

    public void StopZoomOut()
    {
        zoomingOut = false;
    }

    // Optional direct functions for normal OnClick buttons

    public void StepZoomIn()
    {
        if (!CanZoomIn)
            return;

        desiredDistance -= buttonZoomSpeed * 0.25f;
        ClampValues();
        PublishZoomAvailability(false);
    }

    public void StepZoomOut()
    {
        if (!CanZoomOut)
            return;

        desiredDistance += buttonZoomSpeed * 0.25f;
        ClampValues();
        PublishZoomAvailability(false);
    }

    /// <summary>
    /// Pulls the camera back just far enough for <paramref name="worldPoint"/> to
    /// sit inside the view, clear of the telemetry rails. Never moves closer and
    /// never turns the camera, so the view the user set is kept; the existing
    /// smoothing makes the move gentle. False when nothing had to move.
    /// </summary>
    public bool EnsureWorldPointVisible(Vector3 worldPoint)
    {
        if (target == null || introPlaying)
            return false;

        Camera view = GetComponent<Camera>();
        if (view == null || IsPointFramed(worldPoint, desiredDistance, view))
            return false;

        // The nearest distance that frames the point: halve the gap a few times
        // rather than stepping out in fixed jumps, so the pull-back is minimal.
        float near = desiredDistance;
        float far = maxDistance;
        if (!IsPointFramed(worldPoint, far, view))
        {
            desiredDistance = far;
            ClampValues();
            PublishZoomAvailability(false);
            return true;
        }

        for (int i = 0; i < 12 && far - near > 0.05f; i++)
        {
            float middle = (near + far) * 0.5f;
            if (IsPointFramed(worldPoint, middle, view))
                far = middle;
            else
                near = middle;
        }

        desiredDistance = far;
        ClampValues();
        PublishZoomAvailability(false);
        return true;
    }

    /// <summary>True when the point sits inside the margins at the given orbit distance.</summary>
    private bool IsPointFramed(Vector3 worldPoint, float distance, Camera view)
    {
        Vector3 center = target.position + targetOffset;
        Quaternion rotation = Quaternion.Euler(desiredPitch, desiredYaw, 0f);
        Vector3 position = center - rotation * Vector3.forward * distance;
        Vector3 forward = (center - position).normalized;

        Vector3 offset = worldPoint - position;
        float depth = Vector3.Dot(offset, forward);
        if (depth <= view.nearClipPlane)
            return false;

        Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
        if (right.sqrMagnitude < 0.0001f)
            right = rotation * Vector3.right;
        Vector3 up = Vector3.Cross(forward, right);

        float halfHeight = depth * Mathf.Tan(view.fieldOfView * 0.5f * Mathf.Deg2Rad);
        float halfWidth = halfHeight * view.aspect;
        float x = Vector3.Dot(offset, right) / Mathf.Max(halfWidth, 0.0001f);
        float y = Vector3.Dot(offset, up) / Mathf.Max(halfHeight, 0.0001f);

        return Mathf.Abs(x) <= 1f - framingMarginX && Mathf.Abs(y) <= 1f - framingMarginY;
    }

    private void PublishZoomAvailability(bool force)
    {
        bool canZoomIn = CanZoomIn;
        bool canZoomOut = CanZoomOut;
        if (!force && zoomAvailabilityInitialised &&
            canZoomIn == lastCanZoomIn && canZoomOut == lastCanZoomOut)
        {
            return;
        }

        zoomAvailabilityInitialised = true;
        lastCanZoomIn = canZoomIn;
        lastCanZoomOut = canZoomOut;
        ZoomAvailabilityChanged?.Invoke();
    }
}
