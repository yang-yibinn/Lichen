# Thallus lifecycle regression checks

The complete `build.ps1` compiles the actual `LichenThallusGroup.cs` with the minimal public-API substitutes in this directory into a separate executable. It references Core and .NET Framework only; neither Rhino nor Grasshopper is loaded. Test sources and the test executable are excluded from all product packages.

The six cases cover member deletion and replacement under the same GUID, repeated undo/redo, nested cache invalidation, undo completion when object events are suppressed, idempotent event attachment and cleanup across removal/transfer/close/unload/reopen, unchanged serialized membership/metadata, and explicit removal remaining removed.

The cache model is based on offline inspection of SDK 8.0.23304.9001: `GH_Group.Objects()` caches resolved instances, `ExpireCaches()` clears that cache and expires layout, and the document's automatic group expiration filters by the native group component GUID. Thallus has a distinct GUID. The previous production source fails the deletion/restoration regression; the patched source passes.

These are contract tests of Lichen's event wiring, not host integration tests. Archive storage is a substitute, so save/reopen in Grasshopper, native undo ordering, group selection/movement, rendering, and endpoint ownership still require the [owner-run checks](../../docs/validation-0.8.3.md). The test harness intentionally does not emulate or certify those behaviors.
