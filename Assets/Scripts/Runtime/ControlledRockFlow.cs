using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(ParticleSystem))]
public class ControlledRockFlow : MonoBehaviour
{
    [System.NonSerialized] private float simulationSpeedMultiplier = 1f;

    public float SimulationSpeedMultiplier
    {
        get => simulationSpeedMultiplier;
        set => simulationSpeedMultiplier = Mathf.Max(0f, value);
    }

    [Header("References")]
    public Transform centerPoint;
    public Transform outletPoint;

    [Header("Mesh Height")]
    public float meshY = 1f;
    public float hoverHeight = 0.04f;

    [Header("Falling Onto Mesh")]
    public float initialFallDuration = 0.25f;
    public float initialFallHeight = 0.8f;

    [Tooltip("Small random sideways spread while rocks are falling from the inlet.")]
    public float fallHorizontalSpread = 0.08f;

    [Header("Spiral Direction")]
    [Tooltip("Enable this for anti-clockwise spiral. Disable for clockwise.")]
    public bool antiClockwise = true;

    [Header("Spiral Motion")]
    [Tooltip("How fast particles rotate around the center. Keep low for slow movement.")]
    public float spiralAngularSpeed = 0.45f;

    [Tooltip("Starting radius around center.")]
    public float startRadius = 0.12f;

    [Tooltip("Maximum radius particles can slowly reach.")]
    public float outerRadius = 1.15f;

    [Tooltip("How slowly particles move from center toward outer ring.")]
    public float outwardDriftSpeed = 0.12f;

    [Tooltip("Small random variation in particle radius.")]
    public float radiusJitter = 0.06f;

    [Header("Outlet Attraction")]
    [Tooltip("Particles only start getting pulled when they are within this distance from the outlet.")]
    public float outletAttractionDistance = 0.45f;

    [Tooltip("Speed at which particles move toward the outlet once attracted.")]
    public float outletPullSpeed = 0.45f;

    [Tooltip("Distance from outlet where particles start falling/exiting.")]
    public float outletFallDistance = 0.08f;

    [Header("Outlet Final Approach Controls")]
    [Tooltip("Distance from outlet where final approach slowdown starts.")]
    public float outletFinalApproachDistance = 0.30f;

    [Tooltip("Extra slow-down when particle is very close to outlet. Lower = slower final approach.")]
    [Range(0.05f, 1f)]
    public float outletFinalApproachMultiplier = 0.30f;

    [Tooltip("If true, particle snaps exactly to outlet before falling. Keep false for natural exit.")]
    public bool snapToOutletBeforeFall = false;

    [Header("Outlet Exit / Falling Trajectory")]
    [Tooltip("Initial downward speed after reaching outlet.")]
    public float outletFallSpeed = 0.65f;

    [Tooltip("Forward push as rocks leave the outlet.")]
    public float outletExitForwardSpeed = 0.85f;

    [Tooltip("Small upward kick as rocks leave the outlet.")]
    public float outletExitUpSpeed = 0.08f;

    [Tooltip("Random variation in upward kick.")]
    public float outletExitUpRandomness = 0.06f;

    [Tooltip("Random sideways spread after outlet exit.")]
    public float outletExitSideRandomness = 0.08f;

    [Tooltip("Gravity after outlet exit. Higher = faster downward curve.")]
    public float outletExitGravity = 3.2f;

    [Tooltip("How long particles survive after beginning outlet fall.")]
    public float outletFallLifetime = 1.1f;

    [Header("Rolling While Falling")]
    [Tooltip("Enable rolling rotation after the rock exits the outlet.")]
    public bool enableRollingWhileFalling = true;

    [Tooltip("Axis around which the rock rolls while falling. Try X, Y, or Z depending on your rock mesh orientation.")]
    public Vector3 fallRollAxis = new Vector3(1f, 0f, 0f);

    [Tooltip("Initial roll speed in degrees per second. Fast at the start.")]
    public float fallRollInitialSpeed = 1600f;

    [Tooltip("How quickly roll speed decays. Higher = slows down faster.")]
    public float fallRollDecayRate = 1.8f;

    [Tooltip("Minimum roll speed while falling. Use 0 if you want it to almost stop.")]
    public float fallRollMinimumSpeed = 120f;

    [Tooltip("Random variation in roll speed per particle.")]
    [Range(0f, 1f)]
    public float fallRollSpeedRandomness = 0.25f;

    [Tooltip("Randomly reverse roll direction per particle.")]
    public bool randomizeRollDirection = true;

    [Header("Randomness")]
    public float speedJitter = 0.25f;
    public float heightJitter = 0.015f;
    public float angleJitter = 0.5f;

    [Header("Height Vibration")]
    public float heightJitterSpeed = 18f;

    [Header("Rotation Vibration Before Outlet")]
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
        SpiralOnMesh,
        MoveToOutlet,
        FallFromOutlet
    }

    private struct ParticleState
    {
        public FlowPhase phase;
        public float fallElapsed;
        public float rollMultiplier;
        public float rollDirection;
        public float random1;
        public float random2;
        public float random3;
        public float random4;
        public float random5;
    }

    private Dictionary<uint, ParticleState> states;
    private HashSet<uint> activeSeeds;
    private List<uint> deadSeeds;
    private int cleanupCountdown;
    private static int nextCleanupOffset;

    private void Awake()
    {
        ps = GetComponent<ParticleSystem>();
        int capacity = ps.main.maxParticles;
        particles = new ParticleSystem.Particle[capacity];
        states = new Dictionary<uint, ParticleState>(capacity);
        activeSeeds = new HashSet<uint>(capacity);
        deadSeeds = new List<uint>(capacity);
        stateCleanupIntervalFrames = Mathf.Max(1, stateCleanupIntervalFrames);
        cleanupCountdown = nextCleanupOffset % stateCleanupIntervalFrames;
        nextCleanupOffset = (nextCleanupOffset + 1) % stateCleanupIntervalFrames;
    }

    private void LateUpdate()
    {
        if (centerPoint == null || outletPoint == null)
            return;

        float deltaTime = Time.deltaTime * simulationSpeedMultiplier;

        if (deltaTime <= 0f)
            return;

        int count = ps.GetParticles(particles);
        bool collectActiveSeeds = cleanupCountdown <= 0;
        if (collectActiveSeeds)
            activeSeeds.Clear();

        Vector3 center = centerPoint.position;
        Vector3 outlet = outletPoint.position;

        float surfaceY = meshY + hoverHeight;
        float spiralDirection = antiClockwise ? 1f : -1f;

        for (int i = 0; i < count; i++)
        {
            ParticleSystem.Particle p = particles[i];

            uint seed = p.randomSeed;
            if (collectActiveSeeds)
                activeSeeds.Add(seed);

            float age01 = 1f - (p.remainingLifetime / p.startLifetime);

            Vector3 pos = p.position;

            ParticleState state;
            if (!states.TryGetValue(seed, out state))
            {
                state.phase = FlowPhase.SpiralOnMesh;
                state.fallElapsed = 0f;

                state.rollMultiplier = Mathf.Lerp(
                    1f - fallRollSpeedRandomness,
                    1f + fallRollSpeedRandomness,
                    Hash01(seed + 303)
                );

                state.rollDirection = 1f;

                if (randomizeRollDirection && Hash01(seed + 404) < 0.5f)
                    state.rollDirection = -1f;

                state.random1 = Hash01(seed);
                state.random2 = Hash01(seed + 19);
                state.random3 = Hash01(seed + 47);
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
            float particleAngleOffset = r2 * Mathf.PI * 2f;
            float particleRadiusOffset = (r3 - 0.5f) * radiusJitter;

            // ------------------------------------------------------------
            // PHASE 1: Initial fall from feeder onto mesh.
            // ------------------------------------------------------------
            if (age01 < initialFallDuration && state.phase == FlowPhase.SpiralOnMesh)
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
            // PHASE 2: Falling from outlet.
            // Rock keeps forward motion, curves downward, and rolls.
            // ------------------------------------------------------------
            if (state.phase == FlowPhase.FallFromOutlet)
            {
                state.fallElapsed += deltaTime;

                Vector3 velocity = p.velocity;

                velocity += Vector3.down * outletExitGravity * deltaTime;
                pos += velocity * deltaTime;

                p.position = pos;
                p.velocity = velocity;

                p.remainingLifetime = Mathf.Min(p.remainingLifetime, outletFallLifetime);

                ApplyFallingRoll(ref p, ref state);

                states[seed] = state;
                particles[i] = p;
                continue;
            }

            // ------------------------------------------------------------
            // PHASE 3: Calculate spiral position on mesh.
            // ------------------------------------------------------------
            float flowAge = Mathf.InverseLerp(initialFallDuration, 1f, age01);

            float radiusProgress = Mathf.Clamp01(flowAge * outwardDriftSpeed);
            float radius = Mathf.Lerp(startRadius, outerRadius, radiusProgress);
            radius += particleRadiusOffset;

            float angle =
                particleAngleOffset +
                spiralDirection * flowAge * Mathf.PI * 2f * spiralAngularSpeed * speedMultiplier;

            Vector3 spiralPos = center;
            spiralPos.x += Mathf.Cos(angle) * radius;
            spiralPos.z += Mathf.Sin(angle) * radius;

            float heightVibration =
                Mathf.Sin(Time.time * heightJitterSpeed + r2 * 100f) * heightJitter;

            spiralPos.y = surfaceY + heightVibration;

            float distanceDeltaX = pos.x - outlet.x;
            float distanceDeltaZ = pos.z - outlet.z;
            float distanceToOutletSquared =
                distanceDeltaX * distanceDeltaX + distanceDeltaZ * distanceDeltaZ;

            // ------------------------------------------------------------
            // PHASE 4: Spiral normally until near outlet.
            // ------------------------------------------------------------
            if (distanceToOutletSquared > outletAttractionDistance * outletAttractionDistance &&
                state.phase == FlowPhase.SpiralOnMesh)
            {
                p.position = Vector3.Lerp(pos, spiralPos, 0.35f);
                p.velocity = Vector3.zero;

                ApplyRotationVibration(ref p, r2);

                states[seed] = state;
                particles[i] = p;
                continue;
            }

            state.phase = FlowPhase.MoveToOutlet;

            // ------------------------------------------------------------
            // PHASE 5: Move smoothly toward outlet with final slowdown.
            // ------------------------------------------------------------
            if (state.phase == FlowPhase.MoveToOutlet)
            {
                Vector3 outletTarget = outlet;
                outletTarget.y = surfaceY;

                float distanceBeforeMove = Vector3.Distance(
                    new Vector3(pos.x, surfaceY, pos.z),
                    new Vector3(outlet.x, surfaceY, outlet.z)
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

                float finalOutletSpeed =
                    outletPullSpeed *
                    speedMultiplier *
                    approachMultiplier;

                Vector3 pulledPos = Vector3.MoveTowards(
                    pos,
                    outletTarget,
                    finalOutletSpeed * deltaTime
                );

                float tunnelVibration =
                    Mathf.Sin(Time.time * heightJitterSpeed + r2 * 100f) * heightJitter;

                pulledPos.y = surfaceY + tunnelVibration;

                p.position = pulledPos;
                p.velocity = Vector3.zero;

                ApplyRotationVibration(ref p, r2);

                float finalDeltaX = pulledPos.x - outlet.x;
                float finalDeltaZ = pulledPos.z - outlet.z;
                float finalOutletDistanceSquared =
                    finalDeltaX * finalDeltaX + finalDeltaZ * finalDeltaZ;

                // --------------------------------------------------------
                // PHASE 6: Enter outlet-falling state.
                // --------------------------------------------------------
                if (finalOutletDistanceSquared <= outletFallDistance * outletFallDistance)
                {
                    state.phase = FlowPhase.FallFromOutlet;
                    state.fallElapsed = 0f;

                    Vector3 exitStartPosition = pulledPos;

                    if (snapToOutletBeforeFall)
                    {
                        exitStartPosition = outlet;
                        exitStartPosition.y = surfaceY;
                    }

                    Vector3 outletForwardDir = GetOutletForwardDirection(center, outlet);
                    Vector3 outletSideDir = Vector3.Cross(Vector3.up, outletForwardDir).normalized;

                    if (outletSideDir.sqrMagnitude < 0.0001f)
                        outletSideDir = Vector3.right;

                    float sideRandom = (Hash01(seed + 505) - 0.5f) * 2f;
                    float upRandom = Hash01(seed + 606);

                    Vector3 exitVelocity =
                        outletForwardDir * outletExitForwardSpeed +
                        outletSideDir * sideRandom * outletExitSideRandomness +
                        Vector3.up * (outletExitUpSpeed + upRandom * outletExitUpRandomness) +
                        Vector3.down * outletFallSpeed;

                    p.position = exitStartPosition;
                    p.velocity = exitVelocity;
                    p.remainingLifetime = Mathf.Min(p.remainingLifetime, outletFallLifetime);

                    ApplyFallingRoll(ref p, ref state);
                }

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

    private void ApplyFallingRoll(ref ParticleSystem.Particle p, ref ParticleState state)
    {
        if (!enableRollingWhileFalling)
            return;

        Vector3 axis = fallRollAxis;

        if (axis.sqrMagnitude < 0.0001f)
            axis = Vector3.right;

        axis.Normalize();

        float decayedSpeed =
            fallRollInitialSpeed *
            state.rollMultiplier *
            Mathf.Exp(-fallRollDecayRate * state.fallElapsed);

        decayedSpeed = Mathf.Max(fallRollMinimumSpeed, decayedSpeed);

        // ParticleSystem.Particle.rotation3D uses DEGREES.
        float rotationDegrees =
            decayedSpeed *
            state.rollDirection *
            Time.deltaTime * simulationSpeedMultiplier;

        p.rotation3D += axis * rotationDegrees;
    }

    private void ApplyRotationVibration(ref ParticleSystem.Particle p, float randomOffset)
    {
        if (!enableRotationVibration)
            return;

        float rotationShake =
            Mathf.Sin(Time.time * rotationVibrationSpeed + randomOffset * 100f) *
            rotationVibrationAmount;

        // ParticleSystem.Particle.rotation3D uses DEGREES.
        p.rotation3D += new Vector3(
            rotationShake,
            rotationShake * 0.6f,
            rotationShake * 0.4f
        ) * Time.deltaTime * simulationSpeedMultiplier;
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
