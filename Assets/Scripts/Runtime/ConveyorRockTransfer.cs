using UnityEngine;

/// <summary>
/// Carries rocks from a separator outlet along a conveyor belt.
///
/// The falling rocks use the section-clipping material, which hides anything
/// beyond the cut plane - and the belts run straight across it. So each rock
/// is handed over as it drops onto the belt: removed from its falling system
/// and re-emitted, with the same size, colour and spin, into this system,
/// whose material does not clip. On the belt it behaves like a rock on a real
/// belt: a small bounce off the rubber, kinetic friction carrying it up to
/// belt speed, the skirts keeping it on the belt, and removal at the kill
/// point. The belt's scrolling surface is kept at the same speed.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(ParticleSystem))]
[DefaultExecutionOrder(100)] // After the flow scripts have moved this frame's falling rocks.
public sealed class ConveyorRockTransfer : MonoBehaviour
{
    private const float Gravity = 9.81f;

    private static readonly int SpeedId = Shader.PropertyToID("_Speed");
    private static readonly int DirectionId = Shader.PropertyToID("_Direction");

    [Header("Hand-off")]
    [Tooltip("Rock systems whose falling rocks land on this belt.")]
    [SerializeField] private ParticleSystem[] feedSystems = System.Array.Empty<ParticleSystem>();

    [Tooltip("Where falling rocks meet the belt. Its height is the belt surface, and the belt runs from here towards the kill point.")]
    [SerializeField] private Transform landingPoint;

    [Tooltip("Where rocks leave the belt and are removed.")]
    [SerializeField] private Transform killPoint;

    [Tooltip("Half the usable belt width in metres. Rocks landing outside it are not caught; riding rocks are kept inside it, as the skirt boards would.")]
    [SerializeField, Min(0.01f)] private float beltHalfWidth = 0.28f;

    [Tooltip("Height of the belt surface above the landing and kill points, measured from the belt by scene setup, so rocks rest on the belt rather than in it.")]
    [SerializeField] private float surfaceOffset;

    [Tooltip("How far behind the landing point, along the belt, a falling rock is still caught (metres).")]
    [SerializeField, Min(0f)] private float catchBehind = 0.5f;

    [Tooltip("Share of landing rocks carried along the belt. The rest merge into the load where they land, so a dense screen flow reads as a belt load rather than a pile.")]
    [SerializeField, Range(0f, 1f)] private float rideShare = 1f;

    [Header("Belt speed")]
    [Tooltip("Belt speed in metres per second, used while no Belt Speed sensor is linked or it reads zero.")]
    [SerializeField, Min(0f)] private float beltSpeed = 0.8f;

    [Tooltip("Optional Belt Speed sensor (m/s). Rocks and the belt surface follow its live reading.")]
    [SerializeField] private PerformanceStatSource beltSpeedSensor;

    [Header("Landing")]
    [Tooltip("Friction between rock and belt. A landed rock is carried up to belt speed at friction x g.")]
    [SerializeField, Range(0.05f, 1.5f)] private float friction = 0.6f;

    [Tooltip("Share of the impact speed returned as a bounce. Rubber belting absorbs most of it.")]
    [SerializeField, Range(0f, 0.6f)] private float restitution = 0.15f;

    [Tooltip("Seconds for a landed rock's tumbling to die away once it rests on the belt.")]
    [SerializeField, Min(0.01f)] private float spinSettleSeconds = 0.25f;

    [Header("Belt surface")]
    [Tooltip("Belt renderer whose scrolling surface is kept in step with the rocks. Optional.")]
    [SerializeField] private Renderer beltRenderer;

    [Tooltip("Metres of belt per unit of surface texture, measured from the belt mesh by scene setup.")]
    [SerializeField, Min(0.0001f)] private float beltMetresPerUv = 1f;

    [Tooltip("Texture scroll direction that moves the surface towards the kill point, measured by scene setup.")]
    [SerializeField] private Vector2 beltUvDirection = new(0f, 1f);

    private ParticleSystem conveyor;
    private ParticleSystem.Particle[] riding;
    private ParticleSystem.Particle[] falling;
    private MaterialPropertyBlock beltBlock;
    private float restHeight;
    private float appliedBeltSpeed = -1f;

    /// <summary>Current rock speed along the belt, metres per second.</summary>
    public float RockSpeed { get; private set; }

    /// <summary>Rocks that have reached the belt since the scene started.</summary>
    public int LandedTotal { get; private set; }

    /// <summary>Rocks carried along the belt since the scene started.</summary>
    public int CarriedTotal { get; private set; }

    private void Awake()
    {
        conveyor = GetComponent<ParticleSystem>();
        riding = new ParticleSystem.Particle[conveyor.main.maxParticles];

        int feedCapacity = 1;
        foreach (ParticleSystem feed in feedSystems)
        {
            if (feed != null)
                feedCapacity = Mathf.Max(feedCapacity, feed.main.maxParticles);
        }

        falling = new ParticleSystem.Particle[feedCapacity];

        // A rock rests on the belt, not in it: its mean half-extent at size 1.
        Mesh mesh = GetComponent<ParticleSystemRenderer>().mesh;
        Vector3 extents = mesh != null ? mesh.bounds.extents : Vector3.one * 0.5f;
        restHeight = (extents.x + extents.y + extents.z) / 3f;
    }

    private void LateUpdate()
    {
        if (landingPoint == null || killPoint == null)
            return;

        RockSpeed = beltSpeedSensor != null && beltSpeedSensor.CurrentValue > 0f
            ? beltSpeedSensor.CurrentValue
            : beltSpeed;

        SyncBeltSurface();

        float deltaTime = Time.deltaTime * conveyor.main.simulationSpeed;

        if (deltaTime <= 0f)
            return;

        BeltFrame belt = new(landingPoint.position + Vector3.up * surfaceOffset, killPoint.position + Vector3.up * surfaceOffset);

        CatchFallingRocks(belt);
        MoveRidingRocks(belt, deltaTime);
    }

    private void CatchFallingRocks(in BeltFrame belt)
    {
        int capacity = conveyor.main.maxParticles;

        foreach (ParticleSystem feed in feedSystems)
        {
            if (feed == null)
                continue;

            int count = feed.GetParticles(falling);
            bool caught = false;

            for (int i = 0; i < count; i++)
            {
                ParticleSystem.Particle rock = falling[i];
                Vector3 position = rock.position;
                float along = belt.AlongOf(position);

                if (along < -catchBehind || along >= belt.Length ||
                    Mathf.Abs(belt.SideOf(position)) > beltHalfWidth)
                {
                    continue;
                }

                Vector3 size = rock.GetCurrentSize3D(feed);
                float surface = belt.SurfaceHeight(along) + RestOffset(size);

                if (position.y > surface)
                    continue;

                LandedTotal++;

                if (Hash01(rock.randomSeed) < rideShare && conveyor.particleCount < capacity)
                {
                    conveyor.Emit(new ParticleSystem.EmitParams
                    {
                        position = new Vector3(position.x, surface, position.z),
                        velocity = rock.velocity,
                        startSize3D = size,
                        rotation3D = rock.rotation3D,
                        angularVelocity3D = rock.angularVelocity3D,
                        startColor = rock.GetCurrentColor(feed),
                        startLifetime = 600f,
                        randomSeed = rock.randomSeed,
                        applyShapeToPosition = false
                    }, 1);

                    CarriedTotal++;
                }

                // Every rock that reaches the belt leaves its clipped falling system.
                rock.remainingLifetime = -1f;
                falling[i] = rock;
                caught = true;
            }

            if (caught)
                feed.SetParticles(falling, count);
        }
    }

    private void MoveRidingRocks(in BeltFrame belt, float deltaTime)
    {
        int count = conveyor.GetParticles(riding);

        if (count == 0)
            return;

        Vector3 beltVelocity = belt.Along * RockSpeed;
        float speedChange = friction * Gravity * deltaTime;
        float spinDecay = Mathf.Exp(-deltaTime / spinSettleSeconds);

        for (int i = 0; i < count; i++)
        {
            ParticleSystem.Particle rock = riding[i];
            Vector3 position = rock.position;
            Vector3 velocity = rock.velocity;
            float along = belt.AlongOf(position);

            if (along >= belt.Length)
            {
                rock.remainingLifetime = -1f;
                riding[i] = rock;
                continue;
            }

            float surface = belt.SurfaceHeight(along) + RestOffset(rock.GetCurrentSize3D(conveyor));

            if (position.y > surface + 0.001f)
            {
                // Airborne after a bounce.
                velocity.y -= Gravity * deltaTime;
            }
            else
            {
                position.y = surface;

                // A hard landing hops a little; otherwise the rock follows the belt.
                velocity.y = velocity.y < -0.2f ? -velocity.y * restitution : beltVelocity.y;

                // Kinetic friction brings the rock's ground speed to the belt's.
                Vector3 ground = new(velocity.x, 0f, velocity.z);
                ground = Vector3.MoveTowards(ground, new Vector3(beltVelocity.x, 0f, beltVelocity.z), speedChange);
                velocity.x = ground.x;
                velocity.z = ground.z;

                rock.angularVelocity3D *= spinDecay;
            }

            // The skirt boards keep the load on the belt.
            float side = belt.SideOf(position);

            if (Mathf.Abs(side) > beltHalfWidth)
            {
                position -= belt.Side * (side - Mathf.Sign(side) * beltHalfWidth);

                float outward = Vector3.Dot(velocity, belt.Side);

                if (outward * side > 0f)
                    velocity -= belt.Side * outward;
            }

            rock.position = position;
            rock.velocity = velocity;
            riding[i] = rock;
        }

        conveyor.SetParticles(riding, count);
    }

    private void SyncBeltSurface()
    {
        if (beltRenderer == null || Mathf.Approximately(RockSpeed, appliedBeltSpeed))
            return;

        appliedBeltSpeed = RockSpeed;
        beltBlock ??= new MaterialPropertyBlock();

        beltRenderer.GetPropertyBlock(beltBlock);
        beltBlock.SetFloat(SpeedId, RockSpeed / beltMetresPerUv);
        beltBlock.SetVector(DirectionId, new Vector4(beltUvDirection.x, beltUvDirection.y, 0f, 0f));
        beltRenderer.SetPropertyBlock(beltBlock);
    }

    private float RestOffset(Vector3 size) => restHeight * (size.x + size.y + size.z) / 3f;

    // Stable per rock, so the same rocks ride every time.
    private static float Hash01(uint seed)
    {
        seed ^= seed >> 16;
        seed *= 0x7feb352dU;
        seed ^= seed >> 15;
        seed *= 0x846ca68bU;
        seed ^= seed >> 16;
        return (seed & 0x00FFFFFFU) / 16777216f;
    }

    /// <summary>The belt as seen from above: its run direction, width axis and surface height.</summary>
    private readonly struct BeltFrame
    {
        public readonly Vector3 Origin;
        public readonly Vector3 Along;
        public readonly Vector3 Side;
        public readonly float Length;
        private readonly float startHeight;
        private readonly float rise;

        public BeltFrame(Vector3 start, Vector3 end)
        {
            Vector3 run = end - start;
            Vector3 flat = new(run.x, 0f, run.z);

            Origin = start;
            Length = Mathf.Max(flat.magnitude, 0.01f);
            Along = flat / Length;
            Side = Vector3.Cross(Vector3.up, Along);
            startHeight = start.y;
            rise = run.y / Length;
        }

        public float AlongOf(Vector3 point) => Vector3.Dot(point - Origin, Along);

        public float SideOf(Vector3 point) => Vector3.Dot(point - Origin, Side);

        public float SurfaceHeight(float along) => startHeight + rise * Mathf.Clamp(along, 0f, Length);
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (landingPoint == null || killPoint == null)
            return;

        BeltFrame belt = new(landingPoint.position + Vector3.up * surfaceOffset, killPoint.position + Vector3.up * surfaceOffset);
        Vector3 start = landingPoint.position - belt.Along * catchBehind;
        Vector3 end = killPoint.position;
        Vector3 width = belt.Side * beltHalfWidth;

        Gizmos.color = new Color(0.08f, 0.90f, 1f, 1f);
        Gizmos.DrawLine(start - width, end - width);
        Gizmos.DrawLine(start + width, end + width);
        Gizmos.DrawLine(start - width, start + width);
        Gizmos.color = new Color(1f, 0.22f, 0.20f, 1f);
        Gizmos.DrawLine(end - width, end + width);
    }
#endif
}
