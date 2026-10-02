using UnityEngine;

namespace SoftDeformPB
{
    [System.Flags]
    public enum SoftDeformMotionForceOverrides
    {
        None = 0,
        Pull = 1 << 0,
        Spring = 1 << 1,
        Stiffness = 1 << 2,
        Immobile = 1 << 3,
        Gravity = 1 << 4,
        GravityFalloff = 1 << 5
    }

    [DisallowMultipleComponent]
    [AddComponentMenu("Soft Deform PB/Soft Deform PB Setup")]
    public sealed class SoftDeformPBSetup : MonoBehaviour
    {
        [Header("Breast bones")]
        public Transform leftBreast;
        public Transform rightBreast;

        [Header("Rest pose support")]
        [Tooltip("Reduce gravity-induced sag at rest on the generated main and root PhysBones. One removes gravity at their authored rest orientation; movement, Pull, Spring and limits remain. Zero preserves the previous response.")]
        [Range(0.0f, 1.0f)] public float restPoseSupport = 0.0f;

        [Header("Shape response")]
        [Range(0.05f, 0.6f)] public float squashDepth = 0.32f;
        [Range(0.0f, 0.4f)] public float stretchDepth = 0.12f;
        [Range(0.0f, 1.0f)] public float volumeRetention = 0.9f;
        [Range(0.0f, 1.0f)] public float horizontalShare = 0.55f;
        [Range(3, 9)] public int responseSamples = 5;

        [Header("Existing PhysBone squash/stretch")]
        [Range(0.0f, 1.0f)] public float maxSquish = 0.35f;
        [Range(0.0f, 1.0f)] public float maxStretch = 0.15f;
        [Range(0.0f, 1.0f)] public float stretchMotion = 0.65f;

        [Header("Parallel motion / angle retention")]
        [Tooltip("Retain the authored angle during vertical physical motion. Zero follows the PhysBone; one cancels this part of its rotation on the visual bones.")]
        [Range(0.0f, 1.0f)] public float verticalAngleRetention = 0.0f;
        [Tooltip("Retain the authored angle during horizontal physical motion. The body's and clothing's visual bones receive the same correction.")]
        [Range(0.0f, 1.0f)] public float horizontalAngleRetention = 0.0f;
        [Tooltip("Additional vertical retention at full Squish, as a share of the angle still allowed by Vertical Angle Retention.")]
        [Range(0.0f, 1.0f)] public float squishVerticalAngleRetention = 0.0f;
        [Tooltip("Additional horizontal retention at full Squish, as a share of the angle still allowed by Horizontal Angle Retention.")]
        [Range(0.0f, 1.0f)] public float squishHorizontalAngleRetention = 0.0f;

        [Header("Directional compression")]
        [Tooltip("Maximum visual narrowing while a hand or finger touches the outer side and the physical chain moves inward. Zero preserves the previous shape response.")]
        [Range(0.0f, 0.5f)] public float lateralCompressionDepth = 0.0f;
        [Tooltip("Share of lateral compression volume compensation directed upward; the remainder adds forward projection.")]
        [Range(0.0f, 1.0f)] public float lateralVerticalShare = 0.5f;
        [Tooltip("Inward tip travel for full lateral response, as a fraction of the first physical segment length. The initial 10% is a dead zone.")]
        [Range(0.15f, 0.8f)] public float lateralResponseDistance = 0.35f;
        [Tooltip("Maximum correction toward the authored facing direction when gathering. Limited by the sensor angle and reduced by existing horizontal angle retention, including Squish retention.")]
        [Range(0.0f, 15.0f)] public float gatheringAngleCorrection = 0.0f;
        [Tooltip("Minimum compression ratio relative to rest. Reserves a shared budget for PhysBone shortening and posture compression, and limits visual narrowing. Zero preserves the previous limits. This does not measure mesh penetration.")]
        [Range(0.0f, 1.0f)] public float minimumCompressionRatio = 0.0f;

        [Header("Gravity shape compensation")]
        [Range(0.0f, 0.35f)] public float gravitySupineSpread = 0.20f;
        [Tooltip("Supine shortening on the isolated visual bones. PhysBone keeps its own length response.")]
        [Range(0.0f, 0.3f)] public float gravitySupineCompression = 0.10f;
        [Range(0.0f, 0.03f)] public float gravitySupineRootSpread = 0.009f;
        [Tooltip("Visual horizontal elongation when lying on either side, with reciprocal vertical narrowing. Zero retains the previous shape; native PhysBone still supplies movement.")]
        [Range(0.0f, 0.3f)] public float gravitySideElongation = 0.0f;
        [Tooltip("Visual vertical elongation when upside down, with reciprocal horizontal narrowing. Blends through the authored up-direction sensor; zero retains the previous shape.")]
        [Range(0.0f, 0.3f)] public float gravityInvertedElongation = 0.0f;

        [Header("Root motion driver")]
        [Tooltip("Upper bound on physics-driven root translation in the chest parent's local units. Zero removes the root drivers, including their rotation; the posture offset remains independent.")]
        [Range(0.0f, 0.05f)] public float rootMotionMaxOffset = 0.010f;
        [Range(1.0f, 30.0f)] public float rootMotionMaxAngle = 15.0f;
        [Range(0.0f, 1.0f)] public float rootMotionRotationShare = 0.15f;
        [Range(0.0f, 1.0f)] public float rootMotionPull = 0.1444f;
        [Range(0.0f, 1.0f)] public float rootMotionSpring = 0.5184f;
        [Range(0.0f, 1.0f)] public float rootMotionImmobile = 0.0064f;
        [Range(0.0f, 1.0f)] public float rootMotionGravity = 0.5184f;
        [Range(0.0f, 1.0f)] public float rootMotionGravityFalloff = 1.0f;

        [Header("Directional root motion")]
        [Tooltip("Share of root translation along the inferred horizontal axis. One preserves the existing response; zero locks this direction. Does not change the posture offset.")]
        [Range(0.0f, 1.0f)] public float rootHorizontalTranslation = 1.0f;
        [Range(0.0f, 1.0f)] public float rootVerticalTranslation = 1.0f;
        [Range(0.0f, 1.0f)] public float rootLongitudinalTranslation = 1.0f;
        [Tooltip("Share of root rotation producing horizontal motion, in addition to Root Motion Rotation Share. Forces and timing remain shared across axes.")]
        [Range(0.0f, 1.0f)] public float rootHorizontalRotation = 1.0f;
        [Range(0.0f, 1.0f)] public float rootVerticalRotation = 1.0f;
        [Range(0.0f, 1.0f)] public float rootTwistRotation = 1.0f;

        [Header("Existing PhysBone motion")]
        [Tooltip("Keep each selected PhysBone's integration method, curves and angular limits. Selected force overrides replace only their base values. Squish/Stretch and visual settings still apply. Explicit Clothing Support replaces angular limits using Motion Max Pitch/Yaw. Root and secondary motion remain separate additions.")]
        public bool preserveExistingMotion = false;
        [Tooltip("When preserving existing motion, replace only these base force values on the build copy. Each side keeps its authored curves, integration method, Immobile Type and angular limits. Spring is Momentum for Advanced PhysBones; Stiffness only affects Advanced integration.")]
        public SoftDeformMotionForceOverrides motionForceOverrides = SoftDeformMotionForceOverrides.None;
        [Range(0.0f, 1.0f)] public float motionPull = 0.1024f;
        [Range(0.0f, 1.0f)] public float motionSpring = 0.7744f;
        [Range(0.0f, 1.0f)] public float motionStiffness = 0.0f;
        [Range(0.0f, 1.0f)] public float motionImmobile = 0.0025f;
        [Range(0.0f, 1.0f)] public float motionGravity = 0.0784f;
        [Range(0.0f, 1.0f)] public float motionGravityFalloff = 1.0f;
        [Range(0.0f, 90.0f)] public float motionMaxPitch = 45.0f;
        [Range(0.0f, 90.0f)] public float motionMaxYaw = 40.0f;

        [Header("Secondary rebound")]
        [Tooltip("Share of the delayed local rotation applied to the visual bones. This does not add another squash/stretch response.")]
        [Range(0.0f, 0.4f)] public float secondaryMotionStrength = 0.15f;
        [Range(0.01f, 1.0f)] public float secondaryMotionPull = 0.04f;
        [Range(0.0f, 1.0f)] public float secondaryMotionSpring = 0.64f;
        [Range(1.0f, 45.0f)] public float secondaryMotionMaxAngle = 25.0f;

        [Header("Collision support")]
        [Tooltip("Opposing collider radius as a fraction of the chain's forward reach. The generated radius is capped to avoid contact at the authored rest pose.")]
        [Range(0.2f, 1.0f)] public float opposingColliderCoverage = 0.65f;
        [Tooltip("Search for a suitable existing torso support. Disabling this generates a support but keeps all colliders already registered on the original PhysBone.")]
        public bool reuseTorsoColliders = true;
        [Tooltip("Extra rest clearance behind the chain, as a fraction of forward reach. Also used when checking whether an existing torso collider is suitable.")]
        [Range(0.0f, 0.5f)] public float torsoSupportClearance = 0.0f;
        [Tooltip("Position of the opposing support sphere along the chain's forward reach. Its radius remains capped by clearance from the other chain.")]
        [Range(0.05f, 0.95f)] public float opposingColliderCenter = 0.5f;

        [Header("Generated parameter prefix")]
        public string parameterPrefix = "SoftDeformPB";

        public void UseGravityTrialDefaults()
        {
            preserveExistingMotion = false;
            motionForceOverrides = SoftDeformMotionForceOverrides.None;
            rootMotionMaxOffset = 0.010f;
            rootMotionMaxAngle = 15.0f;
            rootMotionRotationShare = 0.15f;
            rootMotionPull = 0.1444f;
            rootMotionSpring = 0.5184f;
            rootMotionImmobile = 0.0064f;
            rootMotionGravity = 0.5184f;
            rootMotionGravityFalloff = 1.0f;
            motionPull = 0.1024f;
            motionSpring = 0.7744f;
            motionImmobile = 0.0025f;
            motionGravity = 0.0784f;
            motionGravityFalloff = 1.0f;
            motionMaxPitch = 45.0f;
            motionMaxYaw = 40.0f;
        }
    }
}
