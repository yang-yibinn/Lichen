# Spotlight menu lifecycle checks

The complete build compiles the production legend panel, menu-choice handler, and deferred menu-lifetime helper against WinForms and Core. Only Grasshopper's UI-scale property is substituted; no Rhino or Grasshopper assembly is loaded. The executable and test sources are excluded from product packages.

Eight checks cover the previous disposal-during-Closed policy, click delivery before deferred cleanup, script-layer target resolution, outside-click/Escape close notifications, continued Windows message dispatch, borrowed-menu preservation, twenty Settings rebuild/toggle cycles, stale-choice rejection, and teardown with pending or failed dispatch. The Settings test uses the native dropdown-opening pipeline but cancels before display; no application window or popup is shown.

The tests explicitly simulate close notifications and event order. They establish menu ownership, lifetime, callback delivery, and state/legend behavior, not reproduction of the owner's Grasshopper freeze. Actual pointer capture, focus return, outside-click dismissal, script runtime identities, and host repaint remain the [owner-run checks](../../docs/validation-0.8.3.md).
