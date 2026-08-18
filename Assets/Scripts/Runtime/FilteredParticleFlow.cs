using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(ParticleSystem))]
public class FilteredParticleFlow : MonoBehaviour
{
    [System.NonSerialized] private float simulationSpeedMultiplier = 1f;

    public float SimulationSpeedMultiplier
    {
        get => simulationSpeedMultiplier;
        set => simulationSpeedMultiplier = Mathf.Max(0f, value);
    }

    [Header("References")]
    public Transform centerPoint;

    [Header("Slope Surface Markers")]
    [Tooltip("Upper-left edge of the lower red slope/chute.")]
    public Transform slopeStartLeftPoint;

    [Tooltip("Upper-right edge of the lower red slope/chute.")]
    public Transform slopeStartRightPoint;

    [Tooltip("Lower-left edge of the lower red slope/chute.")]
    public Transform slopeEndLeftPoint;

    [Tooltip("Lower-right edge of the lower red slope/chute.")]
    public Transform slopeEndRightPoint;

    [Header("Outlet")]
    public Transform rightOutletPoint;

    [Header("Mesh Height")]
    public float meshY = 1f;
    public float hoverHeight = 0.04f;

    [Header("Initial Fall Onto Mesh")]
    public float initialFallDuration = 0.20f;
    public float initialFallHeight = 0.8f;

    [Tooltip("Small random sideways spread while rocks are falling from the inlet.")]
    public float fallHorizontalSpread = 0.08f;

    [Header("Vibration On Mesh")]
    public float vibrationHeight = 0.025f;
    public float vibrationSpeed = 35f;

    [Header("Small Rock Spiral On Mesh")]
    [Tooltip("Enable for anti-clockwise spiral. Disable if direction looks wrong from camera.")]
    public bool antiClockwise = true;

    [Tooltip("How fast small rocks rotate on mesh.")]
    public float spiralAngularSpeed = 0.40f;

    [Tooltip("Starting radius around center.")]
    public float startRadius = 0.10f;

    [Tooltip("Small rocks should not go too far outward before falling through mesh.")]
    public float maxSmallRockRadius = 0.45f;

    [Tooltip("How slowly small rocks move outward before falling through mesh.")]
    public float outwardDriftSpeed = 0.08f;

    [Tooltip("Random radius variation.")]
    public float radiusJitter = 0.04f;

    [Header("Filter / Pass Through Mesh")]
    [Tooltip("Normalized particle age when small rocks start passing through mesh.")]
    [Range(0.05f, 0.9f)]
    public float filterDropStartAge = 0.38f;

    [Tooltip("Vertical falling speed after rocks pass through the mesh.")]
    public float filterDropSpeed = 1.0f;

    [Tooltip("Small height above slope surface where the rock is considered landed.")]
    public float slopeLandingOffset = 0.035f;

    [Tooltip("If enabled, particles outside the slope width are gently corrected toward the closest slope area while falling.")]
    public bool allowGentleAirCorrection = false;

    [Tooltip("Very small horizontal correction while falling. Keep low if enabled.")]
    public float airCorrectionSpeed = 0.08f;

    [Header("Slope Sliding")]
    [Tooltip("Speed while sliding down the lower slope/chute.")]
    public float slopeSlideSpeed = 0.80f;

    [Tooltip("How tightly rocks stay on their personal slope lane.")]
    [Range(0.1f, 1f)]
    public float slopeLineStickiness = 0.85f;

    [Tooltip("Static side variation per particle. This is not animated, so it will not create zig-zag.")]
    public float slopeSideJitter = 0.01f;

    [Tooltip("Height vibration while on slope. Keep subtle.")]
    public float slopeVibrationHeight = 0.006f;

    [Tooltip("Speed of height vibration while on slope.")]
    public float slopeVibrationSpeed = 14f;

    [Tooltip("After reaching this much slope progress, particle switches to outlet travel.")]
    [Range(0.85f, 1f)]
    public float slopeEndProgress = 0.98f;

    [Header("Horizontal Travel To Right Outlet")]
    [Tooltip("Speed after reaching the end of the slope.")]
    public float horizontalTravelSpeed = 0.55f;

    [Tooltip("Distance to outlet path target where particle starts exiting/falling.")]
    public float outletFallDistance = 0.08f;

    [Header("X Axis Stabilisation")]
    [Tooltip("Keeps each particle mostly in its own X lane after it leaves the slope, avoiding the funnel effect.")]
    public bool preserveXFromSlopeToOutlet = true;

    [Tooltip("0 = keep original slope-end X perfectly. 1 = move fully toward outlet X. Use low values.")]
    [Range(0f, 1f)]
    public float outletXFollowStrength = 0.06f;

    [Tooltip("Small static X variation per particle while travelling to outlet.")]
    public float outletXRandomDrift = 0.015f;

    [Tooltip("Keeps X mostly stable after the particle exits the outlet.")]
    public bool preserveXAfterOutletExit = true;

    [Tooltip("0 = remove almost all X velocity after outlet. 1 = keep original X velocity.")]
    [Range(0f, 1f)]
    public float outletExitXVelocityMultiplier = 0.05f;

    [Tooltip("How strongly the falling particle is pulled back to its locked X lane after outlet.")]
    [Range(0f, 1f)]
    public float outletFallXLockStrength = 0.85f;

    [Header("Outlet Final Approach Controls")]
    [Tooltip("If true, particle snaps exactly to outlet before falling. Keep false for natural movement.")]
    public bool snapToOutletBeforeFall = false;

    [Tooltip("Distance from outlet where final approach slowdown starts.")]
    public float outletFinalApproachDistance = 0.30f;

    [Tooltip("Extra slow-down applied when particle is very close to the outlet. Lower = slower final approach.")]
    [Range(0.05f, 1f)]
    public float outletFinalApproachMultiplier = 0.30f;

    [Header("Tunnel / Outlet Vibration")]
    public float tunnelVibrationHeight = 0.008f;
    public float tunnelVibrationSpeed = 25f;

    [Header("Falling From Right Outlet")]
    [Tooltip("Initial downward component after the rock exits the outlet.")]
    public float outletFallSpeed = 0.65f;

    [Tooltip("How long particles survive after exiting the outlet.")]
    public float outletFallLifetime = 1.0f;

    [Header("Outlet Exit Trajectory")]
    [Tooltip("Forward push as particles leave the outlet. Higher = particles shoot outward more before falling.")]
    public float outletExitForwardSpeed = 0.65f;

    [Tooltip("Random sideways spread after outlet exit.")]
    public float outletExitSideRandomness = 0.12f;

    [Tooltip("Small upward kick when particles leave outlet. Use low value.")]
    public float outletExitUpSpeed = 0.12f;

    [Tooltip("Random variation in upward kick.")]
    public float outletExitUpRandomness = 0.08f;

    [Tooltip("Gravity applied after outlet exit. Higher = particles curve downward faster.")]
    public float outletExitGravity = 3.2f;

    [Tooltip("Keeps horizontal motion while falling instead of dropping straight down.")]
    public bool keepOutletTrajectoryWhileFalling = true;

    [Header("Randomness")]
    public float speedJitter = 0.25f;
    public float angleJitter = 0.5f;

    [Header("Rotation Vibration")]
    public bool enableRotationVibration = true;
    public float rotationVibrationSpeed = 25f;
    public float rotationVibrationAmount = 8f;

    [Header("Performance")]
    [Tooltip("How often inactive per-particle state is removed. This does not change emission, density, motion, or rendering; it only avoids scanning the state table every frame.")]
    [SerializeField, Min(1)] private int stateCleanupIntervalFrames = 30;

    private ParticleSystem ps;
    private ParticleSystem.Particle[] particles;

    private enum FlowPhase
    {
        Mesh,
        VerticalDropToSlope,
        SlideOnSlope,
        MoveToOutlet,
        FallFromOutlet
    }

    private struct ParticleFlowState
    {
        public FlowPhase phase;
        public float slopeProgress;
        public float sidePosition;
        public float lockedOutletX;
        public float random1;
        public float random2;
        public float random3;
        public float random4;
        public float random5;
    }

    private Dictionary<uint, ParticleFlowState> states;
    private HashSet<uint> activeSeeds;
    private List<uint> deadSeeds;
    private int cleanupCountdown;
    private static int nextCleanupOffset;

    private void Awake()
    {
        ps = GetComponent<ParticleSystem>();
        int capacity = ps.main.maxParticles;
        particles = new ParticleSystem.Particle[capacity];
        states = new Dictionary<uint, ParticleFlowState>(capacity);
        activeSeeds = new HashSet<uint>(capacity);
        deadSeeds = new List<uint>(capacity);
        stateCleanupIntervalFrames = Mathf.Max(1, stateCleanupIntervalFrames);
        cleanupCountdown = nextCleanupOffset % stateCleanupIntervalFrames;
        nextCleanupOffset = (nextCleanupOffset + 1) % stateCleanupIntervalFrames;
    }

    private void LateUpdate()
    {
        if (!ReferencesReady())
            return;

        float deltaTime = Time.deltaTime * simulationSpeedMultiplier;

        if (deltaTime <= 0f)
            return;

        int count = ps.GetParticles(particles);
        bool collectActiveSeeds = cleanupCountdown <= 0;
        if (collectActiveSeeds)
            activeSeeds.Clear();

        Vector3 center = centerPoint.position;
        Vector3 outlet = rightOutletPoint.position;

        float surfaceY = meshY + hoverHeight;
        float spiralDirection = antiClockwise ? 1f : -1f;

        SlopeData slope = BuildSlopeData();

        for (int i = 0; i < count; i++)
        {
            ParticleSystem.Particle p = particles[i];

            uint seed = p.randomSeed;
            if (collectActiveSeeds)
                activeSeeds.Add(seed);

            float age01 = 1f - (p.remainingLifetime / p.startLifetime);

            Vector3 pos = p.position;

            ParticleFlowState state;
            if (!states.TryGetValue(seed, out state))
            {
                state.phase = FlowPhase.Mesh;
                state.slopeProgress = 0f;
                state.sidePosition = 0f;
                state.lockedOutletX = 0f;
                state.random1 = Hash01(seed);
                state.random2 = Hash01(seed + 23);
                state.random3 = Hash01(seed + 71);
                state.random4 = Hash01(seed + 101);
                state.random5 = Hash01(seed + 202);
                states.Add(seed, state);
            }

            float r1 = state.random1;
            float r2 = state.random2;
            float r3 = state.random3;
            float r4 = state.random4;
            float r5 = state.random5;

            float speedMultiplier = Mathf.Max(0.25f, 1f + (r1 - 0.5f) * speedJitter);
            float angleOffset = r2 * Mathf.PI * 2f;
            float radiusOffset = (r3 - 0.5f) * radiusJitter;

            // ------------------------------------------------------------
            // PHASE 1: Initial fall from feeder onto upper mesh.
            // ------------------------------------------------------------
            if (age01 < initialFallDuration && state.phase == FlowPhase.Mesh)
            {
                float t = Mathf.Clamp01(age01 / initialFallDuration);
                pos.y = Mathf.Lerp(surfaceY + initialFallHeight, surfaceY, t);

                Vector3 fallJitter = new Vector3(r4 - 0.5f, 0f, r5 - 0.5f);
                pos += fallJitter * fallHorizontalSpread * deltaTime;

                p.position = pos;
                p.velocity = Vector3.down;

                ApplyRotationVibration(ref p, r2);

                states[seed] = state;
                particles[i] = p;
                continue;
            }

            // ------------------------------------------------------------
            // PHASE 2: Spiral/vibrate on upper mesh.
            // ------------------------------------------------------------
            if (age01 < filterDropStartAge && state.phase == FlowPhase.Mesh)
            {
                float flowAge = Mathf.InverseLerp(initialFallDuration, filterDropStartAge, age01);

                float radiusProgress = Mathf.Clamp01(flowAge * outwardDriftSpeed);
                float radius = Mathf.Lerp(startRadius, maxSmallRockRadius, radiusProgress);
                radius += radiusOffset;

                float angle =
                    angleOffset +
                    spiralDirection * flowAge * Mathf.PI * 2f * spiralAngularSpeed * speedMultiplier;

                Vector3 spiralPos = center;
                spiralPos.x += Mathf.Cos(angle) * radius;
                spiralPos.z += Mathf.Sin(angle) * radius;

                float vibration = Mathf.Sin(Time.time * vibrationSpeed + r2 * 100f) * vibrationHeight;
                spiralPos.y = surfaceY + vibration;

                p.position = Vector3.Lerp(pos, spiralPos, 0.35f);
                p.velocity = Vector3.zero;

                ApplyRotationVibration(ref p, r2);

                states[seed] = state;
                particles[i] = p;
                continue;
            }

            // Once filter age is crossed, enter vertical drop only once.
            if (state.phase == FlowPhase.Mesh)
            {
                state.phase = FlowPhase.VerticalDropToSlope;
            }

            // ------------------------------------------------------------
            // PHASE 3: Fall almost vertically onto the lower slope.
            // ------------------------------------------------------------
            if (state.phase == FlowPhase.VerticalDropToSlope)
            {
                SlopeSample sample = GetSlopeSampleFromWorldPosition(pos, slope);

                if (allowGentleAirCorrection)
                {
                    Vector3 flatCurrent = new Vector3(pos.x, 0f, pos.z);
                    Vector3 flatTarget = new Vector3(sample.point.x, 0f, sample.point.z);

                    Vector3 correctedFlat = Vector3.MoveTowards(
                        flatCurrent,
                        flatTarget,
                        airCorrectionSpeed * deltaTime
                    );

                    pos.x = correctedFlat.x;
                    pos.z = correctedFlat.z;
                }

                pos.y -= filterDropSpeed * speedMultiplier * deltaTime;

                float landingY = sample.point.y + slopeLandingOffset;

                if (pos.y <= landingY)
                {
                    SlopeSample landedSample = GetSlopeSampleFromWorldPosition(pos, slope);

                    state.phase = FlowPhase.SlideOnSlope;
                    state.slopeProgress = Mathf.Clamp01(landedSample.progress);
                    state.sidePosition = Mathf.Clamp(landedSample.side, -1f, 1f);

                    Vector3 landedPoint = GetPointOnSlope(
                        state.slopeProgress,
                        state.sidePosition,
                        slope
                    );

                    landedPoint.y += slopeLandingOffset;
                    pos = landedPoint;
                }

                p.position = pos;
                p.velocity = Vector3.down * filterDropSpeed;

                ApplyRotationVibration(ref p, r2);

                states[seed] = state;
                particles[i] = p;
                continue;
            }

            // ------------------------------------------------------------
            // PHASE 4: Slide down lower slope/chute.
            // ------------------------------------------------------------
            if (state.phase == FlowPhase.SlideOnSlope)
            {
                float slopeLength = Mathf.Max(0.001f, slope.length);
                float progressStep = (slopeSlideSpeed * speedMultiplier * deltaTime) / slopeLength;

                state.slopeProgress = Mathf.Clamp01(state.slopeProgress + progressStep);

                float staticSideOffset = (r2 - 0.5f) * slopeSideJitter;
                float finalSide = Mathf.Clamp(state.sidePosition + staticSideOffset, -1f, 1f);

                Vector3 targetOnSlope = GetPointOnSlope(
                    state.slopeProgress,
                    finalSide,
                    slope
                );

                float slopeVibration =
                    Mathf.Sin(Time.time * slopeVibrationSpeed + r3 * 100f) *
                    slopeVibrationHeight;

                targetOnSlope.y += slopeLandingOffset + slopeVibration;

                pos = Vector3.Lerp(pos, targetOnSlope, slopeLineStickiness);

                p.position = pos;
                p.velocity = Vector3.zero;

                if (state.slopeProgress >= slopeEndProgress)
                {
                    state.phase = FlowPhase.MoveToOutlet;

                    // Important: lock the particle's current X lane.
                    // This prevents the later outlet movement from funneling everything into one X position.
                    state.lockedOutletX = pos.x;
                }

                ApplyRotationVibration(ref p, r2);

                states[seed] = state;
                particles[i] = p;
                continue;
            }

            // ------------------------------------------------------------
            // PHASE 5: Move from slope end to filtered right outlet.
            // New behavior: X remains mostly stable. Movement is mainly Y/Z.
            // ------------------------------------------------------------
            if (state.phase == FlowPhase.MoveToOutlet)
            {
                Vector3 slopeExitPoint = GetPointOnSlope(1f, state.sidePosition, slope);

                Vector3 outletTarget = outlet;
                outletTarget.y = slopeExitPoint.y + slopeLandingOffset;

                if (preserveXFromSlopeToOutlet)
                {
                    float staticXDrift = (r4 - 0.5f) * 2f * outletXRandomDrift;

                    // Low follow strength means particles mostly keep their own X lane.
                    outletTarget.x = Mathf.Lerp(
                        state.lockedOutletX,
                        outlet.x,
                        outletXFollowStrength
                    ) + staticXDrift;
                }

                float distanceBeforeMove = Vector3.Distance(
                    new Vector3(pos.x, 0f, pos.z),
                    new Vector3(outletTarget.x, 0f, outletTarget.z)
                );

                float approachMultiplier = 1f;

                if (distanceBeforeMove <= outletFinalApproachDistance)
                {
                    float t = Mathf.InverseLerp(
                        outletFallDistance,
                        outletFinalApproachDistance,
                        distanceBeforeMove
                    );

                    approachMultiplier = Mathf.Lerp(
                        outletFinalApproachMultiplier,
                        1f,
                        t
                    );
                }

                float finalTravelSpeed =
                    horizontalTravelSpeed *
                    speedMultiplier *
                    approachMultiplier;

                Vector3 horizontalPos = pos;
                horizontalPos.y = outletTarget.y;

                horizontalPos = Vector3.MoveTowards(
                    horizontalPos,
                    outletTarget,
                    finalTravelSpeed * deltaTime
                );

                float tunnelVibration =
                    Mathf.Sin(Time.time * tunnelVibrationSpeed + r3 * 100f) *
                    tunnelVibrationHeight;

                horizontalPos.y = outletTarget.y + tunnelVibration;

                p.position = horizontalPos;
                p.velocity = Vector3.zero;

                float outletDeltaX = horizontalPos.x - outletTarget.x;
                float outletDeltaZ = horizontalPos.z - outletTarget.z;
                float outletDistanceSquared =
                    outletDeltaX * outletDeltaX + outletDeltaZ * outletDeltaZ;

                if (outletDistanceSquared <= outletFallDistance * outletFallDistance)
                {
                    state.phase = FlowPhase.FallFromOutlet;

                    Vector3 exitStartPosition = horizontalPos;

                    if (snapToOutletBeforeFall)
                    {
                        exitStartPosition = outletTarget;
                    }

                    Vector3 outletForwardDir = GetOutletForwardDirection(center, outlet);
                    Vector3 outletSideDir = Vector3.Cross(Vector3.up, outletForwardDir).normalized;

                    if (outletSideDir.sqrMagnitude < 0.0001f)
                        outletSideDir = Vector3.right;

                    float sideRandom = (r2 - 0.5f) * 2f;
                    float upRandom = r3;

                    Vector3 exitVelocity =
                        outletForwardDir * outletExitForwardSpeed +
                        outletSideDir * sideRandom * outletExitSideRandomness +
                        Vector3.up * (outletExitUpSpeed + upRandom * outletExitUpRandomness) +
                        Vector3.down * outletFallSpeed;

                    if (preserveXAfterOutletExit)
                    {
                        // Keep X movement after outlet extremely low.
                        exitVelocity.x *= outletExitXVelocityMultiplier;
                    }

                    p.position = exitStartPosition;
                    p.velocity = exitVelocity;
                    p.remainingLifetime = Mathf.Min(p.remainingLifetime, outletFallLifetime);
                }

                ApplyRotationVibration(ref p, r2);

                states[seed] = state;
                particles[i] = p;
                continue;
            }

            // ------------------------------------------------------------
            // PHASE 6: Exit from outlet with trajectory.
            // New behavior: X is damped back to the particle's locked lane.
            // ------------------------------------------------------------
            if (state.phase == FlowPhase.FallFromOutlet)
            {
                Vector3 exitVelocity = p.velocity;

                if (keepOutletTrajectoryWhileFalling)
                {
                    exitVelocity += Vector3.down * outletExitGravity * deltaTime;

                    pos += exitVelocity * deltaTime;

                    if (preserveXAfterOutletExit)
                    {
                        pos.x = Mathf.Lerp(
                            pos.x,
                            state.lockedOutletX,
                            outletFallXLockStrength
                        );

                        exitVelocity.x *= outletExitXVelocityMultiplier;
                    }

                    p.position = pos;
                    p.velocity = exitVelocity;
                }
                else
                {
                    pos += Vector3.down * outletFallSpeed * deltaTime;

                    if (preserveXAfterOutletExit)
                    {
                        pos.x = Mathf.Lerp(
                            pos.x,
                            state.lockedOutletX,
                            outletFallXLockStrength
                        );
                    }

                    p.position = pos;
                    p.velocity = Vector3.down * outletFallSpeed;
                }

                p.remainingLifetime = Mathf.Min(p.remainingLifetime, outletFallLifetime);

                ApplyRotationVibration(ref p, r2);

                states[seed] = state;
                particles[i] = p;
                continue;
            }

            states[seed] = state;
            particles[i] = p;
        }

        ps.SetParticles(particles, count);
        if (collectActiveSeeds)
        {
            CleanupDeadParticles();
            cleanupCountdown = stateCleanupIntervalFrames - 1;
        }
        else
        {
            cleanupCountdown--;
        }
    }

    // --------------------------------------------------------------------
    // SLOPE CALCULATION
    // --------------------------------------------------------------------

    private struct SlopeData
    {
        public Vector3 startLeft;
        public Vector3 startRight;
        public Vector3 endLeft;
        public Vector3 endRight;

        public Vector3 startMid;
        public Vector3 endMid;

        public Vector3 flatStartMid;
        public Vector3 flatEndMid;

        public Vector3 lengthDir;
        public Vector3 widthDir;

        public float length;
        public float startHalfWidth;
        public float endHalfWidth;
    }

    private struct SlopeSample
    {
        public Vector3 point;
        public float progress;
        public float side;
    }

    private SlopeData BuildSlopeData()
    {
        SlopeData slope = new SlopeData();

        slope.startLeft = slopeStartLeftPoint.position;
        slope.startRight = slopeStartRightPoint.position;
        slope.endLeft = slopeEndLeftPoint.position;
        slope.endRight = slopeEndRightPoint.position;

        slope.startMid = (slope.startLeft + slope.startRight) * 0.5f;
        slope.endMid = (slope.endLeft + slope.endRight) * 0.5f;

        slope.flatStartMid = FlattenY(slope.startMid);
        slope.flatEndMid = FlattenY(slope.endMid);

        Vector3 flatLength = slope.flatEndMid - slope.flatStartMid;
        slope.length = flatLength.magnitude;
        slope.lengthDir = slope.length > 0.0001f ? flatLength.normalized : Vector3.forward;

        Vector3 startWidth = FlattenY(slope.startRight) - FlattenY(slope.startLeft);
        Vector3 endWidth = FlattenY(slope.endRight) - FlattenY(slope.endLeft);
        Vector3 averageWidth = startWidth + endWidth;

        if (averageWidth.sqrMagnitude > 0.0001f)
            slope.widthDir = averageWidth.normalized;
        else
            slope.widthDir = Vector3.Cross(Vector3.up, slope.lengthDir).normalized;

        slope.startHalfWidth = Mathf.Max(0.001f, startWidth.magnitude * 0.5f);
        slope.endHalfWidth = Mathf.Max(0.001f, endWidth.magnitude * 0.5f);

        return slope;
    }

    private SlopeSample GetSlopeSampleFromWorldPosition(Vector3 worldPosition, SlopeData slope)
    {
        Vector3 flatPosition = FlattenY(worldPosition);

        float progress = 0f;

        if (slope.length > 0.0001f)
        {
            progress = Vector3.Dot(
                flatPosition - slope.flatStartMid,
                slope.lengthDir
            ) / slope.length;
        }

        progress = Mathf.Clamp01(progress);

        Vector3 centerAtProgress = Vector3.Lerp(
            slope.startMid,
            slope.endMid,
            progress
        );

        Vector3 flatCenterAtProgress = FlattenY(centerAtProgress);

        float halfWidthAtProgress = Mathf.Lerp(
            slope.startHalfWidth,
            slope.endHalfWidth,
            progress
        );

        float side = Vector3.Dot(
            flatPosition - flatCenterAtProgress,
            slope.widthDir
        ) / Mathf.Max(0.001f, halfWidthAtProgress);

        side = Mathf.Clamp(side, -1f, 1f);

        Vector3 point = GetPointOnSlope(progress, side, slope);

        SlopeSample sample = new SlopeSample();
        sample.point = point;
        sample.progress = progress;
        sample.side = side;

        return sample;
    }

    private Vector3 GetPointOnSlope(float progress, float side, SlopeData slope)
    {
        progress = Mathf.Clamp01(progress);
        side = Mathf.Clamp(side, -1f, 1f);

        Vector3 leftEdge = Vector3.Lerp(
            slope.startLeft,
            slope.endLeft,
            progress
        );

        Vector3 rightEdge = Vector3.Lerp(
            slope.startRight,
            slope.endRight,
            progress
        );

        float side01 = Mathf.InverseLerp(-1f, 1f, side);

        return Vector3.Lerp(leftEdge, rightEdge, side01);
    }

    // --------------------------------------------------------------------
    // UTILITY
    // --------------------------------------------------------------------

    private bool ReferencesReady()
    {
        return centerPoint != null &&
               slopeStartLeftPoint != null &&
               slopeStartRightPoint != null &&
               slopeEndLeftPoint != null &&
               slopeEndRightPoint != null &&
               rightOutletPoint != null;
    }

    private Vector3 FlattenY(Vector3 v)
    {
        return new Vector3(v.x, 0f, v.z);
    }

    private Vector3 GetOutletForwardDirection(Vector3 center, Vector3 outlet)
    {
        Vector3 dir = new Vector3(
            outlet.x - center.x,
            0f,
            outlet.z - center.z
        );

        if (dir.sqrMagnitude < 0.0001f)
            return Vector3.right;

        return dir.normalized;
    }

    private void CleanupDeadParticles()
    {
        deadSeeds.Clear();

        foreach (uint seed in states.Keys)
        {
            if (!activeSeeds.Contains(seed))
                deadSeeds.Add(seed);
        }

        for (int i = 0; i < deadSeeds.Count; i++)
        {
            states.Remove(deadSeeds[i]);
        }
    }

    private void ApplyRotationVibration(ref ParticleSystem.Particle p, float randomOffset)
    {
        if (!enableRotationVibration)
            return;

        float rotationShake =
            Mathf.Sin(Time.time * rotationVibrationSpeed + randomOffset * 100f) *
            rotationVibrationAmount;

        p.rotation3D += new Vector3(
            rotationShake,
            rotationShake * 0.6f,
            rotationShake * 0.4f
        ) * Time.deltaTime * simulationSpeedMultiplier;
    }

    private float Hash01(uint seed)
    {
        seed ^= 2747636419u;
        seed *= 2654435769u;
        seed ^= seed >> 16;
        seed *= 2654435769u;
        seed ^= seed >> 16;
        seed *= 2654435769u;

        return (seed & 0x00FFFFFF) / 16777216f;
    }
}
