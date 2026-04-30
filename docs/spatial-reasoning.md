# Spatial Reasoning And Asset Placement

This note tracks what the ARPG prop pass taught us about agent placement in 3D space.

## Why This Matters

Agents can place objects quickly only when they have more than raw transform controls. A transform can be mathematically valid while still being visually wrong: a prop can have its bounds above the ground and still be upside down.

The ARPG POC exposed this directly. Several generated prop models looked upright by renderer bounds after a `pitch: -90` transform, but the human-visible semantic orientation was inverted. Flipping to `pitch: 90` fixed the semantic direction, then the bridge had to read `ModelRenderer.Bounds` and lift each object so its world bounds min Z sat on the ground plane.

## What Current Tools Can And Cannot Infer

Current bridge tools can help with:

- reading object transforms with `gameobject.get`;
- mutating transforms with `gameobject.set_transform`;
- assigning models and materials;
- inspecting a model asset before placement with `asset.inspect_model`, including bounds, material slots, common orientation candidates, and candidate ground offsets;
- capturing a rendered camera view with `visual.capture_camera`, including a saved PNG and luminance stats;
- reading `ModelRenderer.Bounds` and `LocalBounds` through `component.get_properties` with `includeAll: true`;
- raycasting against the scene after colliders exist.

These are still not enough to infer "which side is up" with confidence.

Bounds prove occupancy, not semantic orientation. A chest, banner, skull pile, or obelisk can have a reasonable bounding box while still being inverted. Asset metadata may exist in source formats such as FBX/GLB/OBJ, but the bridge does not currently extract or normalize source up-axis, node transforms, pivot/origin, forward axis, or importer conversion data.

## Proposed Bridge Direction

Add asset-aware spatial tools instead of expecting agents to solve every placement from `set_transform`.

Candidate tools:

- `asset.inspect_model`: implemented v0. It loads a model and returns model/render/physics bounds, material slots, source/compiled paths, common orientation candidates, footprints, and candidate ground offsets. Future versions should add pivot/origin details and any available source-axis metadata.
- `asset.set_orientation_override`: store a project-local orientation profile for an asset path, including base rotation, ground offset, forward axis, semantic tags, and confidence/source.
- `asset.get_orientation_override`: read that profile before placing the asset.
- `gameobject.place_asset`: high-level placement that takes asset path, position, yaw, material, parent, and `alignToGround`, then applies the known orientation override and returns renderer-bounds verification.
- `visual.capture_camera`: implemented v0. It captures a live CameraComponent to PNG and reports luminance stats, which gives agents a rendered feedback channel for visibility/readability checks.
- `asset.preview_capture`: capture isolated asset previews for human or vision-model confirmation when metadata confidence is low.

The override file should live in the project, for example:

```json
{
  "models/agent_bridge/arpg_props/cursed_obelisk.vmdl": {
    "baseRotation": { "pitch": 90, "yaw": 0, "roll": 0 },
    "groundOffset": 114.314,
    "forwardAxis": "+Y",
    "confidence": "human_verified",
    "notes": "Corrected during ARPG prop layout pass on 2026-04-30."
  }
}
```

## Placement Workflow

Recommended agent workflow once these tools exist:

1. Search/select a model asset.
2. Inspect model bounds and orientation candidates with `asset.inspect_model`.
3. If an override exists, use it.
4. If no override exists, place an orientation-candidate fixture with several rotations.
5. Capture a camera view with `visual.capture_camera` and ask for human or vision confirmation when candidates are ambiguous.
6. Save the confirmed override.
7. Use `gameobject.place_asset` for future placements.
8. Verify final placement by reading transform and renderer bounds back.

## Near-Term Work

- Add project-local orientation overrides.
- Teach the ARPG POC prop kit to use overrides for all generated props.
- Add `gameobject.place_asset` so agents can place known assets with the override and ground alignment in one verified operation.
- Add an isolated asset preview capture path for comparing candidate rotations without cluttering the active scene.
- Add live smoke cases for `asset.inspect_model`, `visual.capture_camera`, and one override-backed placement that survives save/load.
- Record ambiguous assets rather than pretending the bridge can infer semantic orientation from geometry alone.
