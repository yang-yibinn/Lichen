# Lichen 0.8.1

**Lichen creates a shared language between Grasshopper definitions and coding agents.**

Lichen is designed to fit into the Grasshopper workflow you already know. You continue to create and revise the definition in Grasshopper. When you want outside help, Lichen communicates only the part of the workflow you selected to the AI of your choice. It does not give the AI control of the canvas or take over the scripting process. You decide what to share, which suggestions to use, and how to apply them, so the Grasshopper definition remains under your control.

## To use Lichen:

1. Select the Grasshopper components you want help understanding, troubleshooting, or developing further.
2. Open Lichen and click **Copy Markdown**.
3. Paste the Markdown into Claude, Codex, or any AI coding agent or chat together with your question or request.

The agent receives structured context about the selected components, connections, settings, dependencies, scripts, warnings, and surrounding workflow. Lichen does not send anything automatically.

Developed by Yibin Yang at Adrian Smith + Gordon Gill Architecture (AS+GG), Lichen reflects experience with computational design practice and the need for clearer exchange between visual definitions and coding workflows.

Like its biological namesake, a lichen exists through cooperation between different systems. The plugin plays a similar role: it forms a quiet connective layer between Grasshopper's visual graph and tools that work best with structured text. It is infrastructure for handoff, not an AI assistant embedded in the canvas.

## Design principles

- **The Grasshopper workflow stays yours:** Lichen communicates the selected context without giving an AI control of the canvas or scripting process. You decide whether and how to apply the response.
- **Tool-agnostic context, not an AI chat:** Lichen does not connect to an AI provider and does not confine the handoff to one assistant or conversation.
- **Inspectable before sharing:** exports are ordinary Markdown and JSON, so you remain in control of what leaves the definition.
- **Local-first and read-only export:** no account, API key, network connection, telemetry, or forced Grasshopper solution. Explicit Thallus creation/editing is an undoable local document action.
- **Deterministic output:** the same captured graph state and options produce stable artifacts suitable for comparison and version control.
- **Semantic rather than purely structural:** Lichen reports graph structure together with important settings, dependencies, scripts, clusters, boundaries, and cautious execution/workflow context.
- **Persistent visual scope:** the Lichen Export Root marks a named upstream workflow. Selecting it highlights exactly the contributing chain without changing the definition. The explicit **Select chain** action selects that chain and its Lichen marker for normal Grasshopper move or group operations.
- **Author-defined workflow scope:** a Thallus records exact group-like membership, nesting, description, and properties. One or more outermost Thalli can connect to `Lichen.T` without manually selecting their members.

## Export scopes

- Selected objects only
- Immediate upstream
- All upstream
- Entire document
- Persistent, nickname-labeled Lichen Export Roots

- Exact, persistent Thallus workflow scopes

## Detail levels

- **Brief** communicates purpose, workflow, boundaries, warnings, and dependencies.
- **Technical** adds component inventory, important settings, noteworthy runtime counts, scripts, workflow regions, and execution notes.
- **Exact** includes every captured object and connection plus the complete JSON graph.

## Clusters and custom scripts

Lichen can inspect unprotected clusters as bounded nested graphs. Password-protected clusters remain opaque: Lichen never requests or attempts a password, and reports only their visible interface and surrounding connections. Accessible C# and Python source can be included deliberately; Lichen reports unsupported access safely instead of executing or compiling scripts.

## Privacy and behavior

Lichen runs locally. It does not serialize full geometry, access the network, call an AI model, alter wires, change component states during export, or force a solution. Capture, highlighting, and export remain read-only. Selection changes only after the user explicitly invokes **Select chain**; **Create Thallus** and Thallus editing mutate only their requested Lichen document objects with Grasshopper undo records. A 500-object limit keeps each scope bounded; upstream roots disclose truncation, while oversized exact Thallus scopes abort rather than export partially.

## Version 0.8.1 highlights

- **Select chain** from the Lichen component menu or its Grasshopper middle-click radial companion
- Schema 0.6 provenance seals for deterministic Markdown and Exact JSON verification
- Bounded data-tree shape reporting focused on meaningful origins, operations, and topology changes
- Broader workflow-purpose synthesis using graph stages, scripts, clusters, and iterative regions
- Clearer Export Root results, numeric controls, colliding labels, and exact character and UTF-8 byte sizes

## Package Manager installation

Install **LichenGH** from Rhino's Package Manager and restart Rhino when prompted. The Grasshopper plugin and component are named **Lichen**.

## Compatibility

- Rhino 8.30 or later for Windows
- Grasshopper 1

Source, documentation, and issue tracking: <https://github.com/yang-yibinn/Lichen>

License: MIT
