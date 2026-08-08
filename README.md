# Lichen

Lichen is a local-first, read-only Grasshopper plugin that turns a selected part of a definition—or a persistent Lichen Export Root—into deterministic Markdown and JSON for documentation, troubleshooting, and handoff to a coding agent.

Lichen is designed to fit into the Grasshopper workflow you already know. You continue to create and revise the definition in Grasshopper, choose what context to share, and decide whether and how to apply any response. Lichen does not give an AI control of the canvas or send anything automatically.

Developed by Yibin Yang at Adrian Smith + Gordon Gill Architecture (AS+GG).

## Lichen in Grasshopper

![Animated Lichen Export Root workflow in Grasshopper](docs/images/lichen-export-root.gif)

<p align="center">
  <img src="docs/images/lichen-export-dialog.png" alt="Lichen Grasshopper context export dialog" width="600">
</p>

## Requirements

- Rhino 8.30 or later for Windows
- Grasshopper 1

## Installation

1. In Rhino, run `PackageManager`.
2. Search for **LichenGH** and select **Install**. The installed Grasshopper plugin and component are named **Lichen**.
3. Restart Rhino if prompted, then open Grasshopper.

## Use

1. Select the Grasshopper components you want help understanding, troubleshooting, documenting, or developing further.
2. Choose **Lichen → Copy Context…** and click **Copy Markdown**.
3. Paste the Markdown into the coding agent or chat of your choice together with your question or request.

For a persistent scope, place the **Lichen** component at the end of a workflow, connect the result to X, and double-click the component. Selecting an Export Root highlights exactly the upstream chain included in its export without changing the definition. Choose **Select chain** from the component's right-click menu, or from the Lichen companion button attached to Grasshopper's middle-click radial menu, to replace the selection with the highlighted chain and its selected Lichen marker or markers.

Lichen supports selected-only, immediate-upstream, all-upstream, entire-document, and persistent Export Root scopes. Brief, Technical, and Exact detail levels range from a concise workflow handoff to a complete JSON-backed graph representation. Technical output disambiguates colliding cluster and port labels and reports bounded, already-computed data-tree paths when they carry useful topology. Every export includes a deterministic Lichen provenance seal, and the dialog reports exact Markdown and JSON character and UTF-8 byte sizes. The Exact JSON contract is documented in [`docs/context-schema.md`](docs/context-schema.md).

## Privacy and behavior

Lichen runs locally. It does not serialize full geometry, access the network, call an AI model, send telemetry, modify definition content, alter wires or component states, or force a Grasshopper solution. Capture, highlighting, and export never change selection. Selection changes only after the user explicitly invokes **Select chain**; that command changes selection alone so the user can move or group the chain with Grasshopper's normal tools. Password-protected clusters remain opaque, and safely accessible C# and Python source is read without executing or compiling it.

## Build

Requirements:

- Windows with Rhino 8.30 or later installed in the standard location
- .NET Framework 4.8 runtime/build tools

From PowerShell in the repository root:

```powershell
.\build.ps1
```

The build compiles the plugin, runs the host-free automated test suite, validates package contents and versions, and creates manual-install and Rhino Package Manager artifacts under the ignored `artifacts` directory. It does not launch Rhino or Grasshopper.

The public source tree contains:

```text
src/                 Plugin, core graph model, and Grasshopper adapters
tests/Lichen.Tests/  Deterministic host-free test runner
packaging/yak/       Rhino Package Manager metadata
docs/                Installation and Exact JSON schema documentation
```

## License

Lichen is available under the [MIT License](LICENSE).
