# Lichen context schema

Lichen Exact exports use a deterministic JSON document designed for coding agents, diffing, and future read-only integrations.

## Version 0.5

Version 0.5 adds optional Export Root metadata to `scope`: `rootLabel` and `rootSourceObjectIds`. Version 0.4 added the optional `clusterGraph` on cluster nodes. Readers written for earlier versions must treat 0.5 as a new schema version.

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
12. `dependencies`
13. `analysis`
14. `extractionNotes`

All property names use lower camel case. Identifiers use lowercase GUID strings when they originate in Grasshopper.

## Scope

`scope.selectedObjectIds` records the original selection. `scope.includedObjectIds` records the final resolved scope after applying the selected expansion rule. `nodeLimitReached` explicitly reports a truncated traversal.

For `scope.mode: "export_root"`, the marker component is not present in `nodes`, `edges`, or boundaries. The optional root fields record:

- `rootLabel`: the marker nickname used as its human-readable label
- `rootSourceObjectIds`: the distinct objects directly connected to X

An Export Root scope contains the deterministic transitive upstream closure of X and only internal wires between included objects. It intentionally has no outgoing side-branch boundaries and does not serialize the terminal wire into the excluded marker. Selection-based modes retain their existing incoming and outgoing boundary behavior.

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
- Access, optional, expression, flatten, graft, simplify, and reverse behavior
- Source and recipient counts

Full geometry and volatile data items are never serialized.

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

- Nodes, edges, groups, dependencies, messages, and identifiers are stably ordered.
- No timestamps or export-session identifiers are included.
- Re-exporting an unchanged graph with unchanged options produces identical JSON.
- Exact Markdown embeds the same JSON text returned by **Save JSON**.

Changing field names or meanings requires a `schemaVersion` change.
