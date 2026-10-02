using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace SoftDeformPB.Editor
{
    // A final animation job writes only explicitly selected blendshapes after the FX/motion inputs.
    // Updating job data keeps the graph, motion time and initialized PhysBones intact.
    internal sealed class SoftDeformPreviewShapeLayer : IDisposable
    {
        private struct ShapeJob : IAnimationJob
        {
            public PropertyStreamHandle handle;
            public bool enabled;
            public float value;
            public void ProcessRootMotion(AnimationStream stream) { }
            public void ProcessAnimation(AnimationStream stream)
            {
                if (enabled && handle.IsValid(stream)) handle.SetFloat(stream, value);
            }
        }

        private readonly PlayableGraph graph;
        private readonly Animator animator;
        private readonly AnimationPlayableOutput output;
        private readonly Playable input;
        private readonly List<AnimationScriptPlayable> nodes = new List<AnimationScriptPlayable>();
        private readonly List<int> bindings = new List<int>();
        private string[] keys = Array.Empty<string>();
        private bool disposed;
        internal int BoundShapeCount => nodes.Count;

        internal SoftDeformPreviewShapeLayer(PlayableGraph graph, Animator animator, AnimationPlayableOutput output)
        {
            this.graph = graph;
            this.animator = animator;
            this.output = output;
            input = output.GetSourcePlayable();
        }

        internal void Refresh(SoftDeformPreviewOverrides settings)
        {
            if (disposed || !graph.IsValid() || animator == null) return;
            var nextKeys = settings.shapes.Select(s => s.rendererPath + "\n" + s.name).ToArray();
            if (!keys.SequenceEqual(nextKeys))
            {
                ClearNodes();
                keys = nextKeys;
                Playable tail = input;
                for (int index = 0; index < settings.shapes.Count; index++)
                {
                    var shape = settings.shapes[index];
                    Transform target = string.IsNullOrEmpty(shape.rendererPath) ? animator.transform : animator.transform.Find(shape.rendererPath);
                    var renderer = target != null ? target.GetComponent<SkinnedMeshRenderer>() : null;
                    if (renderer?.sharedMesh == null || renderer.sharedMesh.GetBlendShapeIndex(shape.name) < 0) continue;
                    var node = AnimationScriptPlayable.Create(graph, new ShapeJob
                    {
                        handle = animator.BindStreamProperty(target, typeof(SkinnedMeshRenderer), "blendShape." + shape.name)
                    }, 1);
                    graph.Connect(tail, 0, node, 0);
                    node.SetInputWeight(0, 1);
                    nodes.Add(node);
                    bindings.Add(index);
                    tail = node;
                }
                output.SetSourcePlayable(tail);
            }
            for (int i = 0; i < nodes.Count; i++)
            {
                var job = nodes[i].GetJobData<ShapeJob>();
                var shape = settings.shapes[bindings[i]];
                job.enabled = shape.enabled && !float.IsNaN(shape.value) && !float.IsInfinity(shape.value);
                job.value = shape.value;
                nodes[i].SetJobData(job);
            }
        }

        private void ClearNodes()
        {
            if (graph.IsValid())
            {
                output.SetSourcePlayable(input);
                foreach (var node in nodes) if (node.IsValid()) graph.DestroyPlayable(node);
            }
            nodes.Clear();
            bindings.Clear();
        }

        public void Dispose()
        {
            if (disposed) return;
            ClearNodes();
            disposed = true;
        }
    }
}
