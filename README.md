# cad-agent

Minimal read-only boundary between AutoCAD 2025 and a local .NET 8 bridge.
It is not an MCP server yet and has no AI/provider dependency.

```text
CadAgent.Bridge (.NET 8 process)
  -> current-user named pipe, length-prefixed versioned JSON
  -> bounded queue (32 pending requests)
  -> AutoCAD Application.Idle pump (up to 4 requests per callback)
  -> active drawing, short-lived DocumentLock, read-only transactions
```

The public method allowlist is:

- `system.ping`
- `cad.get_drawing_info`
- `cad.list_layers`
- `cad.list_blocks`
- `cad.list_entities` (generic inspection: DBText, MText, MLeader, BlockReference)
- `cad.validate_change_plan` (read-only dry run for change plans)
- `cad.apply_change_plan` (atomic allowlisted mutation with postcondition-before-commit)

AutoCAD `ObjectId` values never cross the boundary; stable hexadecimal handles are
returned.

### Safe mutation V1 invariants:
- **Allowlisted mutations only**: `set_dbtext` (`DBText.TextString`) and `set_block_attribute` (`AttributeReference.TextString`).
- **Inspect-only in V1**: `MText` and `MLeader` (formatting and content-type risk deferred to V2).
- **Exact preconditions**: Required string comparison; missing attribute tag fails closed.
- **Atomic flow**: Full preflight read -> DocumentLock -> Single transaction -> Apply -> Verify postcondition inside transaction -> Commit/Abort.
- **Zero auto-save**: The plugin never executes Save, SaveAs, or command-line save. After apply, the DWG is marked modified (`*`) and the operator decides when to save.


## Requirements and build

- Windows x64, .NET SDK 8.
- Pass `/p:AutoCADDir="D:\path\AutoCAD 2025"` or set `AUTOCAD_2025_DIR`;
  no machine-specific path is tracked.
- Autodesk assemblies remain local and are not copied to build output.

```powershell
dotnet restore CadAgent.sln
dotnet build CadAgent.sln -c Release /p:AutoCADDir="C:\Program Files\Autodesk\AutoCAD 2025"
dotnet test CadAgent.sln -c Release --no-build
```

## Installation and runtime verification

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\install-plugin.ps1
```

Restart AutoCAD 2025, open a drawing, and run:

```text
ECA_PING
ECA_STATUS
```

From a separate PowerShell terminal:

```powershell
dotnet run --project .\src\CadAgent.Bridge -- system.ping
dotnet run --project .\src\CadAgent.Bridge -- cad.get_drawing_info
dotnet run --project .\src\CadAgent.Bridge -- cad.list_layers
dotnet run --project .\src\CadAgent.Bridge -- cad.list_blocks
```

Each command must return JSON with `ok:true` and the same `requestId` as the
request. Disconnect and repeat `system.ping` to verify reconnect. Keep
AutoCAD `SECURELOAD` enabled; production deployment should Authenticode-sign
the plugin according to the organization's certificate policy.

`cad.list_blocks` intentionally does not recurse into nested block definitions
or traverse Xrefs. Dynamic references use their dynamic block table record name;
anonymous non-dynamic references retain their actual definition name.

## Reuse and limitations

See [docs/reuse-review.md](docs/reuse-review.md) and
[THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md). The plugin build can be
verified wherever the AutoCAD SDK assemblies exist. Runtime plugin load and
drawing reads require an interactive AutoCAD session and must not be called
verified until the operator procedure above has actually run.
