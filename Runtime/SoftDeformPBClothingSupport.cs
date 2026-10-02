using UnityEngine;

namespace SoftDeformPB
{
    [DisallowMultipleComponent]
    [AddComponentMenu("Soft Deform PB/Soft Deform PB Clothing Support")]
    public sealed class SoftDeformPBClothingSupport : MonoBehaviour
    {
        [Tooltip("Leave empty when the avatar has exactly one Soft Deform PB Setup. Limits apply to both body and clothing and are fixed at build time, including initially inactive outfits.")]
        public SoftDeformPBSetup targetSetup;

        [Tooltip("Maximum generated attachment offset, combining root motion and supine spread, in the shared chest parent's local units. Zero locks this offset.")]
        [Range(0, 0.1f)] public float maximumRootTravel = 0.01f;

        [Tooltip("Budget in degrees shared by primary swing, root rotation, secondary rebound and gathering correction. Primary limits are centered on the authored pose in the build copy. This is not a bound on every mesh vertex's orientation.")]
        [Range(0, 180)] public float maximumSwingAngle = 30;

        [Tooltip("Maximum additional distance between the two generated attachment offsets, in the shared chest parent's local units. Does not limit mesh width, bone stretching, or authored animation. Zero locks the generated attachment offsets.")]
        [Range(0, 0.2f)] public float maximumSeparationIncrease = 0.015f;
    }
}
