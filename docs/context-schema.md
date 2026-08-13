# Lichen context schema

Lichen Exact exports use a deterministic JSON document designed for coding agents, diffing, and future read-only integrations.

## Version 0.8

Version 0.8 adds no field. It gives the existing `scope.rootThallusIds` array ordered meaning: for a T-root export, its entries preserve the flattened, validated Grasshopper runtime order of distinct opaque Thallus identities. That same order controls outer-region Markdown presentation. Readers written for 0.7 must treat 0.8 as a new schema version because sorting `rootThallusIds` would now discard author-visible meaning.

Version 0.7 added author-defined `thalli`, `scope.rootThallusIds`, `scope.selectedThallusIds`, and the `thallus_root` scope mode. Version 0.6 added the top-level `exportSignature` and bounded `runtimeTreeShape` parameter field.

The top-level fields are emitted in this order:

1. `schemaVersion`
2. `name`
3. `rhinoVersion`
4. `grasshopperVersion`
5. `scope`
6. `userContext`
7. `nodes`
8. `edges`
9. `boundaryInputs`
10. `boundaryOutputs`
11. `groups`
12. `thalli`
13. `dependencies`
14. `analysis`
15. `extractionNotes`
16. `exportSignature`

All property names use lower camel case. Identifiers use lowercase GUID strings when they originate in Grasshopper.

## Scope

`scope.selectedObjectIds` records originally selected exportable Grasshopper objects. `scope.selectedThallusIds` records any Thalli selected as top-menu export sources. `scope.includedObjectIds` records the final resolved scope after expanding selected Thalli to their exact effective members and applying the selected expansion rule. `nodeLimitReached` explicitly reports a truncated traversal.

For `scope.mode: "export_root"`, the marker component is not present in `nodes`, `edges`, or boundaries. The optional root fields record:

- `rootLabel`: the marker nickname used as its human-readable label
- `rootSourceObjectIds`: the distinct objects directly connected to X

An Export Root scope contains the deterministic transitive upstream closure of X and only internal wires between included objects. It intentionally has no outgoing side-branch boundaries and does not serialize the terminal wire into the excluded marker. Selection-based modes retain their existing incoming and outgoing boundary behavior.

For `scope.mode: "thallus_root"`, `rootThallusIds` records the connected outermost Thalli in validated T-token order. Grasshopper tree paths are flattened in their existing stable path/item order; every identity must appear exactly once. The included graph is still the exact set union of explicit effective membership, not geometric containment, upstream-wire inference, or inclusion of Merge/Jitter/Relay routing objects. Incoming and outgoing connections crossing that membership are retained as boundaries. A Thallus scope that exceeds the object limit is rejected rather than partially serialized.

## Thalli

Each top-level `thalli` record contains:

- `instanceId` and author-visible `name`
- optional author-provided `description` and ordered key/value `properties`
- optional `parentThallusId` for an explicitly nested child
- `directMemberIds` for members assigned directly to that Thallus
- `effectiveMemberIds` for its deterministic direct-plus-descendant union
- `missingMemberIds` for stale references that could not be captured

Only outermost Thalli can be connected to `Lichen.T`. Their T port is presented directly on the Thallus boundary; the subordinate Grasshopper source object required to carry the wire is an invisible implementation detail. Nested Thalli contribute through their parent and do not expose an independent output, but a nested Thallus selected directly for a top-menu export can serve as that selection scope. Peer Thalli may overlap; shared nodes are serialized once, disclosed as shared membership in Markdown, and are not forced into an artificial parent relationship. User descriptions and properties remain explicitly labeled as user-provided. Only the allow-listed keys `purpose`, `role`, `stage`, and `discipline` contribute bounded context to cautious purpose inference.

The endpoint emits an opaque non-string runtime identity. `Lichen.T` accepts direct endpoints or identities carried through the exact native variable-input Merge, Jitter's `List`-to-`Values` path, and native Relay. Validation requires the token sequence and complete live port-aware route to agree. Generic values or strings, Jitter `Indices`, unsupported intermediaries, duplicate or missing identities, deleted/stale/mismatched ownership, nested owners, and cycles are rejected. These runtime routing objects are not serialized as Thallus members.

## Nodes and parameters

Each node includes stable instance and type identifiers, component metadata, assembly metadata, concrete `runtimeTypeName`, state, canvas bounds, group membership, parameters, runtime messages, and optional script or persistent-value data.

Stateful or controlling nodes may also include:

- `executionMetadata`: ordered key/value facts read through safe public APIs, such as timer intervals, data-dam delays, recorder limits, cluster storage mode, or solver settings.
- `controlLinks`: relationships that are not ordinary Grasshopper wires. Examples include timer targets and Galapagos genome/fitness links. Each record has a `role` and `targetNodeId`.
- `clusterGraph`: a bounded nested graph for an unprotected cluster, or an explicit status explaining why inspection was unavailable.

### Cluster graphs

`clusterGraph.inspectionStatus` is one of:

- `inspected`
- `protected`
- `unavailable`
- `depth_limit_reached`
- `node_limit_reached`
- `cycle_detected`

An inspected cluster graph contains its internal nodes, edges, groups, dependencies, analysis, and extraction notes. Only clusters included by the requested outer scope are inspected. Nested cluster nodes may carry their own `clusterGraph` up to the configured depth and shared internal-node limits. `nodeLimitReached` records truncation. Lichen uses Grasshopper's public cluster-document accessor, supplies no password, does not export linked file paths, and does not flatten internal identifiers or wires into the outer graph.

Every cluster graph may also contain:

- `userProvidedPurpose`: optional text entered by the user for that outer cluster. It is always labeled as user-provided and is not treated as observed behavior.
- `blackBoxSummary`: a deterministic summary used when internals are unavailable. It is limited to visible component metadata, exposed ports, surrounding outer-graph connections, and already-computed output counts, and explicitly states that it does not describe the hidden implementation.

Parameter records distinguish:

- Persistent settings in `persistentDataSummary`
- Already-computed volatile counts in `runtimeDataSummary`
- Bounded already-computed branch paths and item counts in `runtimeTreeShape`. The default single `{0}` path is omitted. At most eight paths are sampled; consecutive paths with equal counts are summarized as a range, irregular samples remain individually visible, and additional paths are disclosed as an omitted count.
- Access, optional, expression, flatten, graft, simplify, and reverse behavior
- Source and recipient counts

Full geometry and volatile data items are never serialized.

Technical Markdown presents only noteworthy runtime topology: multi-branch structures, explicit tree operations, parameter modifiers, and differing same-name input/output shapes. Routine single-path facts remain in Exact Markdown and JSON. Numeric-only Panel contents remain serialized as Panel text but are presented as recipient-labeled workflow values rather than Author Signals; descriptive Panel prose remains an author signal.

## Export signature and Markdown seal

`exportSignature` identifies the artifact as a Lichen export and contains:

- `product`: `Lichen`
- `exporterVersion`: the Lichen plugin version that produced the export
- `fingerprintAlgorithm`: `sha256`
- `contextFingerprint`: the lowercase 64-character SHA-256 digest of the deterministic UTF-8 JSON with `exportSignature` omitted

Markdown presents the same provenance as a short `LCHN-` seal using the first 12 uppercase fingerprint characters. The full fingerprint in Exact JSON is authoritative. This is a reproducible content fingerprint, not a digital author signature: it identifies equal captured content and detects accidental changes, but it does not establish who created an artifact.

To verify a seal, parse the JSON, retain the recorded fingerprint, omit `exportSignature`, serialize the remaining schema using Lichen's deterministic field ordering and formatting, and hash those UTF-8 bytes with SHA-256.

## Edges and boundaries

Every internal or boundary-crossing wire has a source node/port and target node/port. `boundaryStatus` is one of:

- `internal`
- `incoming`
- `outgoing`

Boundary records additionally store readable internal and external node/parameter names. External boundary nodes are identified without being added to the included-node inventory.

## Analysis and execution semantics

`analysis.executionSemantics` separates visible dataflow from non-linear or stateful execution. It contains:

- `hasNonLinearBehavior`
- `ordinaryWireGraphHasCycle`
- `regions`: paired iterative/control regions with start and end IDs, nesting level, configured iteration limit when available, carried values, included node IDs, and evidence.
- `components`: timers, triggers, data dams, recorders, gates, solvers, clusters, and other conservatively recognized controllers, with behavior and evidence.
- `notes`: snapshot limitations and cautions about execution order.

An ordinary wire graph can be acyclic while a controller repeatedly evaluates a region or the whole definition. Consequently, `analysis.detectedOperations` is a dataflow-operation summary and must not be interpreted as literal execution order when `hasNonLinearBehavior` is true.

Script behavior descriptions and possible roles are deterministic Markdown presentation aids derived from the exact source and component context. The serialized script source remains the authoritative machine-readable record.

## Determinism

- Nodes, edges, groups, Thalli, dependencies, messages, and identifiers are stably ordered.
- T-root IDs preserve the stable flattened order of the validated already-computed token stream; they are not re-sorted by ID.
- No timestamps or export-session identifiers are included.
- The provenance seal likewise contains no user, machine, file-path, timestamp, or session data.
- Re-exporting an unchanged graph with unchanged options produces identical JSON.
- Exact Markdown embeds the same JSON text returned by **Save JSON**.

Changing field names or meanings requires a `schemaVersion` change.
