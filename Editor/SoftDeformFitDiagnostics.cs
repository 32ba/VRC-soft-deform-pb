using System;
using System.Collections.Generic;
using System.Linq;
using nadena.dev.modular_avatar.core;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Dynamics.PhysBone.Components;

namespace SoftDeformPB.Editor
{
    internal static class SoftDeformFitDiagnostics
    {
        internal sealed class Notice
        {
            public MessageType Severity;
            public string Text;
            public UnityEngine.Object Context;
        }

        internal sealed class Skin
        {
            public SkinnedMeshRenderer Renderer;
            public int LeftVertices, RightVertices;
            public int MappedBones;
            public bool Measured;
            public Bounds Bounds;
        }

        internal sealed class Report
        {
            public readonly List<Notice> Notices = new List<Notice>();
            public readonly List<Skin> Skins = new List<Skin>();
            public SoftDeformRigGeometry.Chain[] Chains;
            public SoftDeformCollisionRig.Support[] Torso, Opposing;
            public float SuggestedTravel, SuggestedSpread;
            public bool Valid => Chains != null && Torso != null && Opposing != null && !Notices.Any(n => n.Severity == MessageType.Error);
            public void Add(MessageType severity, string text, UnityEngine.Object context = null) =>
                Notices.Add(new Notice { Severity = severity, Text = text, Context = context });
        }

        // Read-only: no settings, hierarchy, meshes, or source colliders are changed.
        internal static Report Analyze(SoftDeformPBSetup setup, bool measureSkin = true)
        {
            var report = new Report();
            if (setup == null) return report;
            var descriptor = setup.GetComponentInParent<VRCAvatarDescriptor>(true);
            if (descriptor == null)
            {
                report.Add(MessageType.Error, "Place Setup inside an avatar with a VRC Avatar Descriptor.", setup);
                return report;
            }
            var avatar = descriptor.gameObject;
            try
            {
                var setups = avatar.GetComponentsInChildren<SoftDeformPBSetup>(true);
                var claimed = new HashSet<Transform>();
                foreach (var item in setups) SoftDeformPBGeneratePass.ValidateSetup(avatar.transform, item, claimed);
                SoftDeformPBGeneratePass.ValidateParameterPrefixes(avatar.transform, setups);
                var clothing = SoftDeformClothingLimits.Resolve(avatar, setups, avatar.GetComponentsInChildren<SoftDeformPBClothingSupport>(true));
                if (clothing.TryGetValue(setup, out var limits))
                    report.Add(MessageType.Info, $"Clothing limits: generated attachment travel at most {limits.AttachmentBudget:G3} parent-local units per side; rotation budget {limits.Angle:G3} degrees.");
            }
            catch (InvalidOperationException e) { report.Add(MessageType.Error, e.Message, setup); }

            try
            {
                // Validate this setup separately so an error on another Setup does not hide its geometry.
                SoftDeformPBGeneratePass.ValidateSetup(avatar.transform, setup, new HashSet<Transform>());
                report.Chains = new[] { setup.leftBreast, setup.rightBreast }.Select(t =>
                    SoftDeformRigGeometry.Capture(t, SoftDeformPBGeneratePass.FindExistingPhysBone(avatar.transform, setup, t))).ToArray();
                float reach = report.Chains.Min(c => c.Frame.InverseTransformVector(c.Direction * c.Reach).magnitude);
                report.SuggestedTravel = Mathf.Clamp(reach * 0.08f, 0, 0.05f);
                report.SuggestedSpread = Mathf.Clamp(reach * 0.06f, 0, 0.03f);
                report.Torso = SoftDeformCollisionRig.PlanTorsos(avatar, report.Chains[0], report.Chains[1], setup);
                report.Opposing = new[] { SoftDeformCollisionRig.PlanOpposing(report.Chains[0], report.Chains[1], setup),
                    SoftDeformCollisionRig.PlanOpposing(report.Chains[1], report.Chains[0], setup) };
            }
            catch (InvalidOperationException e)
            {
                if (!report.Notices.Any(n => n.Text == e.Message)) report.Add(MessageType.Error, e.Message, setup);
            }
            if (report.Chains == null) return report;

            if (report.Chains[0].Frame != report.Chains[1].Frame)
                report.Add(MessageType.Warning, "The selected bones have different parent frames. Per-side sensors will be used; Clothing Support requires a shared parent.", setup);
            float reachRatio = report.Chains.Max(c => c.Reach) / report.Chains.Min(c => c.Reach);
            if (reachRatio > 1.5f) report.Add(MessageType.Warning, "Left/right forward reach differs by more than 50%. Check the selected roots, endpoints and scale.", setup);
            foreach (var chain in report.Chains)
            {
                var pb = chain.PhysBone;
                var direction = SoftDeformRigGeometry.InferLocalDirection(chain.Root, pb);
                var axes = SoftDeformShapeMath.InferAxes(chain.Root, direction);
                float alignment = Mathf.Abs(Vector3.Dot(direction.normalized, SoftDeformShapeMath.AxisVector(axes.Longitudinal)));
                if (alignment < Mathf.Cos(20 * Mathf.Deg2Rad))
                    report.Add(MessageType.Warning, $"{chain.Root.name}: the physical direction is over 20 degrees from its inferred scale axis. Inspect the axis arrows and child/endpoint direction.", pb);
                int branches = chain.Root.Cast<Transform>().Count(t => !SoftDeformRigGeometry.Ignored(pb, t) && t.localPosition.sqrMagnitude > 1e-10f);
                if (branches > 1)
                    report.Add(MessageType.Warning, $"{chain.Root.name}: {branches} direct physical branches. The first usable child's direction chooses the axes; all branches contribute to support clearance.", pb);
                var scale = chain.Root.lossyScale;
                float smallest = Mathf.Min(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
                if (scale.x < 0 || scale.y < 0 || scale.z < 0 || SoftDeformRigGeometry.MaximumScale(chain.Root) / smallest > 1.01f)
                    report.Add(MessageType.Warning, $"{chain.Root.name}: mirrored or nonuniform scale. The support fit uses an enclosing collision radius; inspect the final shape.", chain.Root);
                if (!pb.enabled) report.Add(MessageType.Warning, $"{chain.Root.name}: the selected PhysBone is disabled and remains disabled at build time.", pb);
                foreach (var collider in (pb.colliders ?? new List<VRC.Dynamics.VRCPhysBoneColliderBase>()).OfType<VRCPhysBoneCollider>().Where(c => c != null && c.enabled && !c.insideBounds).Distinct())
                {
                    if (chain.Samples.Min(s => SoftDeformCollisionRig.SignedDistance(collider, s.Position) - s.Radius) < -0.00001f)
                        report.Add(MessageType.Warning, $"{chain.Root.name}: registered collider '{collider.name}' overlaps the sampled rest chain. It will be retained; inspect its shape and the PhysBone radius.", collider);
                }
            }
            if (report.Opposing != null && report.Opposing.Any(s => s.Radius < s.RequestedRadius - 0.00001f))
                report.Add(MessageType.Info, "An opposing sphere is smaller than the requested coverage to preserve rest clearance. The solid outline shows the size that will be generated.");
            InspectSkins(avatar, report, measureSkin);
            return report;
        }

        private static void InspectSkins(GameObject avatar, Report report, bool measureSkin)
        {
            var mapping = new Dictionary<Transform, Transform>();
            foreach (var merge in avatar.GetComponentsInChildren<ModularAvatarMergeArmature>(true))
            {
                var target = merge.mergeTarget?.Get(merge);
                if (target == null || !target.transform.IsChildOf(avatar.transform))
                {
                    report.Add(MessageType.Warning, $"Merge Armature '{merge.name}' has no valid target inside this avatar.", merge);
                    continue;
                }
                mapping[merge.transform] = target.transform;
                foreach (var pair in merge.GetBonesMapping() ?? new List<(Transform, Transform)>())
                    mapping[pair.Item2] = pair.Item1; // MA returns (base, merge), including prefix/suffix matching.
            }
            int Side(Transform bone, HashSet<Transform> visited)
            {
                if (bone == null || !visited.Add(bone)) return -1;
                for (int side = 0; side < 2; side++) if (bone.IsChildOf(report.Chains[side].Root)) return side;
                // Unmatched clothing-only descendants follow their nearest mapped ancestor.
                for (var t = bone; t != null && t != avatar.transform; t = t.parent)
                    if (mapping.TryGetValue(t, out var destination)) return Side(destination, visited);
                return -1;
            }
            foreach (var renderer in avatar.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (renderer.sharedMesh == null) continue;
                var skin = new Skin { Renderer = renderer }; report.Skins.Add(skin);
                var bones = renderer.bones;
                int[] sides = bones.Select(t => Side(t, new HashSet<Transform>())).ToArray();
                skin.MappedBones = bones.Where((t, i) => t != null && sides[i] >= 0 &&
                    !report.Chains.Any(c => t.IsChildOf(c.Root))).Count();
                if (!sides.Any(s => s >= 0)) continue;
                var mesh = new Mesh();
                try
                {
                    var weights = renderer.sharedMesh.boneWeights;
                    Vector3[] vertices = null;
                    if (measureSkin) { renderer.BakeMesh(mesh); vertices = mesh.vertices; }
                    float Weight(int bone, float weight, int side) => bone >= 0 && bone < sides.Length && sides[bone] == side ? weight : 0;
                    for (int v = 0; v < weights.Length; v++)
                    {
                        var w = weights[v]; bool affected = false;
                        for (int side = 0; side < 2; side++)
                        {
                            float weight = Weight(w.boneIndex0, w.weight0, side) + Weight(w.boneIndex1, w.weight1, side) +
                                Weight(w.boneIndex2, w.weight2, side) + Weight(w.boneIndex3, w.weight3, side);
                            if (weight < 0.001f) continue;
                            if (side == 0) skin.LeftVertices++; else skin.RightVertices++;
                            affected = true;
                        }
                        if (!affected || vertices == null || v >= vertices.Length) continue;
                        var point = report.Chains[0].Frame.InverseTransformPoint(renderer.transform.TransformPoint(vertices[v]));
                        if (!skin.Measured) { skin.Bounds = new Bounds(point, Vector3.zero); skin.Measured = true; }
                        else skin.Bounds.Encapsulate(point);
                    }
                }
                catch (UnityException e) { report.Add(MessageType.Warning, $"Could not measure '{renderer.name}': {e.Message}", renderer); }
                finally { UnityEngine.Object.DestroyImmediate(mesh); }
            }
            if (!report.Skins.Any(s => s.LeftVertices > 0) || !report.Skins.Any(s => s.RightVertices > 0))
                report.Add(MessageType.Warning, "No weighted renderer was found for at least one selected side. Check mesh bone references and Merge Armature targets; fitting does not create weights.");
        }
    }
}
