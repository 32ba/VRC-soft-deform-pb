using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using VRC.Dynamics;
using VRC.SDK3.Dynamics.PhysBone.Components;

namespace SoftDeformPB.Editor
{
    internal static class SoftDeformCollisionRig
    {
        internal sealed class Support
        {
            public Vector3 Center;
            public Vector3 Direction;
            public float Radius;
            public float RequestedRadius;
            public VRCPhysBoneCollider Existing;
            public bool SharedWithLeft;
        }

        public static void Create(GameObject avatar, SoftDeformRigGeometry.Chain left, SoftDeformRigGeometry.Chain right, SoftDeformPBSetup setup)
        {
            // Plan all supports before allocating colliders. The Inspector uses these same functions.
            var torso = PlanTorsos(avatar, left, right, setup);
            var leftOpposing = PlanOpposing(left, right, setup);
            var rightOpposing = PlanOpposing(right, left, setup);
            var leftTorso = AddTorsoSupport(left, "Left", torso[0]);
            if (torso[1].SharedWithLeft) Register(right.PhysBone, leftTorso);
            else AddTorsoSupport(right, "Right", torso[1]);
            AddOpposingSurface(left, right, "Left", leftOpposing);
            AddOpposingSurface(right, left, "Right", rightOpposing);
        }

        internal static Support[] PlanTorsos(GameObject avatar, SoftDeformRigGeometry.Chain left,
            SoftDeformRigGeometry.Chain right, SoftDeformPBSetup setup)
        {
            var a = PlanTorso(avatar, left, right, setup);
            var b = PlanTorso(avatar, right, left, setup);
            // Preserve the previous sequential generator's reuse of its first generated plane.
            bool leftFrameActive = true;
            for (var t = left.Frame; t != null && t != avatar.transform; t = t.parent)
                leftFrameActive &= t.gameObject.activeSelf;
            if (setup.reuseTorsoColliders && leftFrameActive && a.Existing == null && b.Existing == null &&
                Vector3.Dot(right.Origin - right.Direction * right.Reach * 0.5f - a.Center, a.Direction) <= 0 &&
                right.Samples.All(s => Vector3.Dot(s.Position - a.Center, a.Direction) - s.Radius >= right.Reach * (0.015f + setup.torsoSupportClearance)))
                b = new Support { Center = a.Center, Direction = a.Direction, SharedWithLeft = true };
            return new[] { a, b };
        }

        internal static Support PlanTorso(GameObject avatar, SoftDeformRigGeometry.Chain chain,
            SoftDeformRigGeometry.Chain other, SoftDeformPBSetup setup)
        {
            float margin = chain.Reach * (0.015f + setup.torsoSupportClearance);
            bool Suitable(VRCPhysBoneCollider candidate)
            {
                if (candidate == null || !candidate.enabled || !candidate.gameObject.activeSelf || candidate.insideBounds) return false;
                if (!candidate.transform.IsChildOf(avatar.transform)) return false;
                for (var t = candidate.transform; t != avatar.transform; t = t.parent)
                    if (!t.gameObject.activeSelf) return false;
                var root = candidate.rootTransform != null ? candidate.rootTransform : candidate.transform;
                if (!root.IsChildOf(avatar.transform)) return false;
                if (root.IsChildOf(chain.Root) || root.IsChildOf(other.Root)) return false;
                // A torso support is behind the attachment, near it, and clear of
                // every sampled rest segment including the authored collision radius.
                if (SignedDistance(candidate, chain.Origin - chain.Direction * chain.Reach * 0.5f) > 0) return false;
                return chain.Samples.All(s => SignedDistance(candidate, s.Position) - s.Radius >= margin);
            }
            var existing = setup.reuseTorsoColliders
                ? (chain.PhysBone.colliders ?? new List<VRCPhysBoneColliderBase>()).OfType<VRCPhysBoneCollider>()
                    .Concat(avatar.GetComponentsInChildren<VRCPhysBoneCollider>(true)).Distinct().FirstOrDefault(Suitable)
                : null;
            if (existing != null)
            {
                var root = existing.rootTransform != null ? existing.rootTransform : existing.transform;
                return new Support { Existing = existing, Center = root.TransformPoint(existing.position),
                    Direction = root.rotation * existing.rotation * Vector3.up, Radius = existing.radius * SoftDeformRigGeometry.MaximumScale(root) };
            }

            float offset = chain.Samples.Min(s => Vector3.Dot(s.Position - chain.Origin, chain.Direction) - s.Radius) - margin;
            return new Support { Center = chain.Origin + chain.Direction * offset, Direction = chain.Direction };
        }

        private static VRCPhysBoneCollider AddTorsoSupport(SoftDeformRigGeometry.Chain chain, string side, Support plan)
        {
            if (plan.Existing != null) { Register(chain.PhysBone, plan.Existing); return plan.Existing; }
            var support = NewCollider(chain.Frame, "SoftDeformPB Torso Support " + side);
            support.transform.position = plan.Center;
            support.transform.rotation = Quaternion.FromToRotation(Vector3.up, plan.Direction);
            support.shapeType = VRCPhysBoneColliderBase.ShapeType.Plane;
            support.bonesAsSpheres = false;
            Register(chain.PhysBone, support);
            return support;
        }

        internal static Support PlanOpposing(SoftDeformRigGeometry.Chain owner,
            SoftDeformRigGeometry.Chain receiver, SoftDeformPBSetup setup)
        {
            Vector3 center = owner.Origin + owner.Direction * owner.Reach * setup.opposingColliderCenter;
            float margin = Mathf.Min(owner.Reach, receiver.Reach) * 0.015f;
            float clearance = receiver.Samples.Min(s => Vector3.Distance(s.Position, center) - s.Radius) - margin;
            float radius = Mathf.Min(owner.Reach * setup.opposingColliderCoverage, clearance);
            if (radius <= margin)
                throw new InvalidOperationException($"[Soft Deform PB] Cannot fit the opposing collider on '{owner.Root.name}' without intersecting the other chain at rest. Check the selected bones, existing PhysBone radii, and Opposing Collider Center.");
            return new Support { Center = center, Direction = owner.Direction, Radius = radius, RequestedRadius = owner.Reach * setup.opposingColliderCoverage };
        }

        private static void AddOpposingSurface(SoftDeformRigGeometry.Chain owner,
            SoftDeformRigGeometry.Chain receiver, string side, Support plan)
        {
            // Follow attachment motion, not the opposite outer PhysBone's solved
            // tip. This avoids a circular dependency between the two primary chains.
            var surface = NewCollider(owner.Root.parent, "SoftDeformPB Opposing Surface " + side);
            surface.transform.position = plan.Center;
            surface.shapeType = VRCPhysBoneColliderBase.ShapeType.Sphere;
            surface.radius = plan.Radius / SoftDeformRigGeometry.MaximumScale(surface.transform);
            surface.bonesAsSpheres = true;
            Register(receiver.PhysBone, surface);
        }

        private static VRCPhysBoneCollider NewCollider(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var collider = go.AddComponent<VRCPhysBoneCollider>();
            collider.globalCollision = VRCPhysBoneBase.AdvancedBool.False;
            collider.insideBounds = false;
            return collider;
        }

        private static void Register(VRCPhysBone physBone, VRCPhysBoneCollider collider)
        {
            var list = physBone.colliders != null ? new List<VRCPhysBoneColliderBase>(physBone.colliders) : new List<VRCPhysBoneColliderBase>();
            if (!list.Contains(collider)) list.Add(collider);
            physBone.colliders = list;
        }

        internal static float SignedDistance(VRCPhysBoneCollider collider, Vector3 point)
        {
            Transform root = collider.rootTransform != null ? collider.rootTransform : collider.transform;
            Vector3 center = root.TransformPoint(collider.position);
            Vector3 axis = root.rotation * collider.rotation * Vector3.up;
            if (collider.shapeType == VRCPhysBoneColliderBase.ShapeType.Plane)
                return Vector3.Dot(point - center, axis);
            float scale = SoftDeformRigGeometry.MaximumScale(root);
            float radius = collider.radius * scale;
            if (collider.shapeType == VRCPhysBoneColliderBase.ShapeType.Capsule)
            {
                // Use an enclosing capsule for the reuse decision, so a difference
                // in SDK capsule-height interpretation cannot introduce rest contact.
                float halfLength = collider.height * scale * 0.5f;
                center += axis * Mathf.Clamp(Vector3.Dot(point - center, axis), -halfLength, halfLength);
            }
            return Vector3.Distance(point, center) - radius;
        }
    }
}
