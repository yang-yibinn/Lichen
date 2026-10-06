# Lichen 0.8.3

This release adds Spotlight and the latest non-curve improvements to the public 0.8.2 codebase. Curve Descriptor, Descriptor to Curve, Apply Curve Edit, their geometry contracts, and the curve-snapshot export option are excluded. Main exports remain schema 0.8.

## Changes

- **Spotlight:** toggle temporary third-party and optional script highlights from the radial menu or empty-canvas context menu. A fixed upper-right legend offers layer and focus settings, collapse, and close. Inspection is limited to a deterministic set of 500 eligible top-level objects, with partial coverage disclosed. State is session-only.
- **Radial menu:** Select chain, Create Thallus, and Spotlight form a left arc. Edge placement moves the wheel and companions together, guards against accidental opening-release activation, and preserves another extension's custom radial interaction.
- **Thallus fixes:** refresh cached member instances after delete/undo/redo; exclude the hidden endpoint from group bounds; move the T socket and label outside compact member content; suppress the hidden endpoint's profiler duration.
- **Graph Mapper:** capture authored graph type, domains, grips, constraints, and bounded fixed samples, with explicit partial/unavailable reporting. A disconnected Cull Pattern is no longer joined to an unrelated mapper chain in the affected workflow description.
- **Galapagos:** distinguish captured genome targets from unresolved or out-of-scope references.
- **Export dialog:** restore Purpose, Requested task, and Constraints from local settings per root/document; add distinct Copy Markdown, Save Markdown, and Save JSON icons.
- **Build:** pin official Rhino/Grasshopper 8.0.23304.9001 reference packages and verify their hashes. Runtime packages contain only the three Lichen assemblies and license.

## Installation and compatibility

Download `Lichen-0.8.3.zip` and its checksum from this GitHub release, then follow [manual installation](installation.md). A `.yak` archive and checksum are also supplied as downloads. This GitHub publication does not update Rhino Package Manager's hosted listing.

Windows and Grasshopper 1 are required. The build targets the Rhino 8.0 SDK, but compilation alone does not certify behavior across Rhino 8 service releases or runtime modes. Earlier public documentation targeted Rhino 8.30+. The early/later Rhino host matrix remains incomplete.

## Validation status

Automated evidence is recorded in [validation-0.8.3.md](validation-0.8.3.md). Host-free tests and standalone WinForms checks do not certify native menu dismissal, pointer/focus behavior, Thallus undo/rendering, or display scaling. Those remain owner-run checks; no Rhino or Grasshopper host was launched during release preparation.

Capture and export remain local and read-only. Spotlight does not edit the definition, change selection, or request a solution. Explicit Select chain and Thallus authoring retain their existing selection/undo behavior.
