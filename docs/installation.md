# Installing and updating Lichen

Lichen 0.8.3 is available from [GitHub Releases](https://github.com/yang-yibinn/Lichen/releases/tag/v0.8.3) as a versioned ZIP containing one `Lichen` folder. This publication does not update the hosted Rhino Package Manager listing; its available version may be older. The plugin contains three runtime files that must remain together—`Lichen.gha`, `Lichen.Core.dll`, and `Lichen.Adapters.dll`—plus the MIT notice in `LICENSE.txt`.

## Package Manager installation

1. Start Rhino 8.30 or later and run `PackageManager`.
2. Search for **LichenGH** and select **Install**. The installed Grasshopper plugin and component are named **Lichen**.
3. Restart Rhino if prompted, then open Grasshopper.
4. Confirm **Lichen -> Copy Context...** appears in the Grasshopper menu.

Package Manager handles the installation path and future updates. Rhino 8 for Windows; built against SDK 8.0.23304.9001. Earlier/later service-release and runtime-mode host validation remains incomplete; the previous public target was 8.30+. Grasshopper 1 is required.

## Clean installation

1. Close Rhino before changing Grasshopper plugin files.
2. Extract `Lichen-<version>.zip`.
3. In File Explorer, right-click the ZIP or extracted files, choose **Properties**, and select **Unblock** if Windows displays that option.
4. Copy the extracted `Lichen` folder into `%AppData%\Grasshopper\Libraries`.
5. Start Rhino and Grasshopper yourself.
6. Confirm **Lichen → Copy Context…** appears in the Grasshopper menu.

The resulting installation path should contain:

```text
%AppData%\Grasshopper\Libraries\Lichen\Lichen.gha
%AppData%\Grasshopper\Libraries\Lichen\Lichen.Core.dll
%AppData%\Grasshopper\Libraries\Lichen\Lichen.Adapters.dll
%AppData%\Grasshopper\Libraries\Lichen\LICENSE.txt
```

## Updating an existing installation

For a Package Manager installation, run `PackageManager`, select **LichenGH**, and install the available update. Restart Rhino if prompted.

For a manual installation:

1. Close every Rhino process. Loaded plugin files may otherwise remain locked.
2. Rename the installed `Lichen` folder to `Lichen-backup-<old-version>` so the previous release is recoverable.
3. Extract the new versioned ZIP and copy its complete `Lichen` folder into `%AppData%\Grasshopper\Libraries`.
4. Do not mix DLLs from different Lichen releases.
5. Start Rhino and Grasshopper and verify the menu, dialog, export, and Export Root behavior before removing the backup.
6. After the new release is verified, the backup folder may be removed. Do not leave the backup inside the Grasshopper Libraries directory because Grasshopper may attempt to load its `.gha`; move it elsewhere first if it must be retained.

## Verifying the installed version

Before starting Rhino, right-click the installed `Lichen.gha`, choose **Properties → Details**, and confirm that its file version matches the release. All three binaries in Lichen 0.8.3 report assembly version `0.8.3.0`.

The adjacent `.sha256` file verifies the release ZIP before extraction. Its recorded hash should match the result of:

```powershell
Get-FileHash .\Lichen-0.8.3.zip -Algorithm SHA256
```

## Rollback

1. Close every Rhino process.
2. Move the current `Lichen` folder out of `%AppData%\Grasshopper\Libraries`.
3. Restore the backed-up release as `%AppData%\Grasshopper\Libraries\Lichen`.
4. Start Rhino and Grasshopper yourself and confirm the menu and a basic export.

Lichen stores no accounts, API keys, or telemetry. Export metadata drafts and cluster purpose notes are stored locally in `%AppData%\Grasshopper\Lichen.xml` and remain compatible across plugin rollback as settings strings. Older versions ignore metadata drafts.

## Uninstalling

For a Package Manager installation, uninstall **LichenGH** from Rhino's Package Manager and restart Rhino if prompted. For a manual installation, close Rhino and remove `%AppData%\Grasshopper\Libraries\Lichen`. Either route removes the plugin without changing Grasshopper definitions. To also remove locally saved metadata drafts and cluster purpose notes, delete `%AppData%\Grasshopper\Lichen.xml`; keep that file if the notes should return after reinstalling Lichen.
