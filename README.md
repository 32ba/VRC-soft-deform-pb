# Soft Deform PB

Soft Deform PB adds soft deformation and posture response to existing breast
PhysBones on VRChat PC avatars. Version: `0.0.1`.

## Requirements

- Unity 2022.3
- VRChat SDK Avatars 3.10.4 or later
- NDMF 1.14.8 or later
- Modular Avatar 1.18.7 or later
- An avatar with left and right breast PhysBones

The supported dependency ranges are listed in [package.json](package.json).

## Installation

Install the required dependencies in your avatar project first. In Unity's
Package Manager, choose **+ > Add package from git URL** and enter:

```text
https://github.com/32ba/VRC-soft-deform-pb.git
```

A Git client must be installed for this method. See
[Unity's Git package installation guide](https://docs.unity3d.com/2022.3/Documentation/Manual/upm-ui-giturl.html).

## Setup

1. Keep the avatar's existing breast PhysBones.
2. Add **Soft Deform PB Setup** to the avatar root.
3. Assign the left and right bones controlled by those PhysBones.
4. Use **Fit and diagnose** to check the bones and clothing.
5. Adjust the Inspector's Easy pads and use **動かして確認** to preview motion.
6. Build the avatar through NDMF or VRChat Build & Test.

Use Modular Avatar Merge Armature for clothing as usual. Clothing must have
weights mapped to the selected breast bones. Add **Soft Deform PB Clothing
Support** to an outfit when it needs tighter movement limits.

- [Tuning workflow](Docs~/motion-tuning.md)
- [Motion preview and comparison](Docs~/motion-quality-check.md)

## Scope

The package targets PC avatars. It uses the existing PhysBone motion for
squash/stretch response, with adjustable root movement, posture deformation,
angle retention, and delayed rebound. Body and mapped clothing share the
deformation.

Clothing weights and fitting remain the responsibility of the avatar setup.
The package does not simulate fabric or generate general world/floor contact.
Check the final result in VRChat, including remote viewing, grabbing, and
Avatar Scaling; the Unity preview covers only the Editor workflow.

## License

MIT. See [LICENSE](LICENSE).
