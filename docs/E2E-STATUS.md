# Fusion add-in E2E runbook

Install the add-in on the machine running Fusion 360:

- **macOS:** from the repository root, run `./scripts/install-addon.sh`. This
  uses the reference Python add-in because the C#/.NET Framework add-in is not
  supported by Fusion for macOS.
- **Windows:** from PowerShell, run
  `powershell -ExecutionPolicy Bypass -File scripts\install-addon.ps1` to build
  and install the C# add-in.

Open Fusion 360 and run **Shift+S → Add-Ins → Fusion360MCP → Run** if it did not
start automatically. Confirm the TEXT COMMANDS window shows
`[MCP] Server listening on localhost:9876`, then call `ping` from the MCP
client and expect `{"ok": true, "status": "pong"}`.

For a stateful E2E smoke, use a named, saved Fusion document and save it before
the run. Unsaved designs may be discarded when Fusion restarts, making the
resulting scene state unavailable for follow-up inspection.
