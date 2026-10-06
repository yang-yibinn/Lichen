# Lichen 0.8.3 validation

## Automated evidence — 2026-10-05

- Complete Windows PowerShell build against pinned Rhino/Grasshopper SDK 8.0.23304.9001: passed with compiler warnings treated as errors.
- 138 Core tests: passed, zero failures. These include Spotlight classification, deterministic coverage/session rules, radial geometry, Graph Mapper state, metadata drafts, Galapagos references, Thallus bounds, and ordinary export/provenance regressions.
- Six production Thallus lifecycle checks against host doubles: passed.
- Eight production Spotlight panel/menu checks in standalone WinForms: passed. These simulate event sequences; they do not reproduce native Grasshopper pointer/focus behavior.
- Public-release contract: schema 0.8 and exporter version 0.8.3, without the curve inclusion option, payload, or curve-tool Core types.
- Compiled metadata exclusion: no Curve Descriptor, Descriptor to Curve, Apply Curve Edit, or associated implementation metadata in any shipped assembly.
- Runtime inventory: exactly `Lichen.gha`, `Lichen.Core.dll`, `Lichen.Adapters.dll`, and `LICENSE.txt`; all assemblies report 0.8.3.0.
- ZIP and Yak archives and SHA-256 files generated. Yak archive targets `rh8_0-win`; the pre-existing `Lichen` versus `LichenGH` content-name warning remains nonfatal.
- Frozen synthetic export comparisons retain their original explicit exporter version so the release-version increment does not rewrite baseline hashes.

Private research artifacts and their benchmark results are not included in the public source or packages. No Rhino or Grasshopper host was launched, and no installation was performed during release preparation.

## Host checks still requiring direct observation

Record exact Rhino/Grasshopper versions, runtime mode, display scale, and the tested case. Do not mark a case passed from compilation or a test double.

1. Confirm the new package loads, the Spotlight legend/radial/context labels display correctly, and the three excluded curve components are absent from Lichen's component list.
2. In Spotlight, toggle known Python/C# scripts, reopen Settings to check state, and repeat at least ten outside-click dismissal cycles, Escape, document switches, and normal canvas actions without freezes. Verify outlines and unchanged solution/selection/dirty state.
3. Check radial gestures at edges/corners and different display scales, including stationary opening release, narrow windows, resize, and other installed radial extensions.
4. Delete/undo/redo ordinary Thallus members, including sole and nested members. Check the boundary, group movement, Exact membership, save/reopen, T socket clearance, and profiler behavior independently.
5. Check Graph Mapper samples against authored state, export-draft restoration, Galapagos reference reporting, and all copy/save buttons. Repeat normal exports to check determinism.
6. Test both early and later Rhino 8 service releases and applicable runtime modes before treating the 8.0 compile target as a compatibility certification.
