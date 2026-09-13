# B0 reuse review

Upstream snapshots reviewed on 2026-09-11:

- `AI4CharityPL/toolbank-autocad` at `6f8adf987b674c0ba0e9b85780aaddefc2ab7a10`, MIT.
- `beiming183-cloud/AutoCAD-MCP` at `11f7c47e5038796a20451b38b23032e625b5aa26`, MIT.

| Capability | toolbank-autocad | AutoCAD-MCP | Build ourselves | Decision |
|---|---|---|---|---|
| AutoCAD 2025 / .NET 8 | Direct target | Native worker targets 2025/2026 | No benefit | REUSE project settings |
| Plugin bootstrap | `IExtensionApplication`; broad registration | Small entry point | Rename commands and scope | ADAPT |
| Named Pipe transport | Multi-session server | `CurrentUserOnly`, one client | Small B1 endpoint | ADAPT both |
| Framing | 4-byte LE length + UTF-8 JSON, 16 MiB | Same, 8 MiB | Reduce bound to 1 MiB | ADAPT toolbank |
| DTO/protocol | Large tool envelope and handshake | Native RPC DTOs | Four-method B1 contract | BUILD |
| Main-thread dispatch | Captured `SynchronizationContext`; unsafe inline fallback if null | `ExecuteInApplicationContext` failed in observed runtime | Bounded queue + `Application.Idle` | BUILD minimal pump |
| `DocumentLock` | Used for writes, not reads | Used for mutations | B1 has no mutations | SKIP |
| Transactions | Read/write helpers | Native transactions | Read-only open/close transactions | ADAPT |
| Active document | `MdiActiveDocument` + structured error | Registry/fencing | Single active-document read | ADAPT |
| Reconnect | Concurrent sessions | Listener loop | One request per connection | BUILD minimal |
| Shutdown | Cancellation + drain | Cancel + bounded wait | Bounded listener stop | ADAPT |
| Error model | Large enum | String codes | Small stable string codes | BUILD |
| Tests | Broad category tests; framing concepts | Python/native integration | Protocol and fake-pipe tests | BUILD |
| Installer | Generated per-user bundle | Signed install workflow | Static bundle + small copy script | ADAPT |
| License | MIT | MIT | N/A | notices preserved |

Not imported: ToolBank, MCP category servers, 692 tools, AI providers, UI,
vision/OCR, COM/LISP fallbacks, validators, standards, mutation and arbitrary
execution surfaces.

Security note: `PipeOptions.CurrentUserOnly` is taken from the secondary
reference. B1 does not add a token because the pipe is local/current-user and
the allowlist is read-only. Add authentication only if the boundary becomes
cross-user, remote, privileged, or mutating.

R1 runtime evidence showed that calling `Application.DocumentManager` from the
pipe worker before `ExecuteInApplicationContext` still crossed the AutoCAD-owned
thread boundary. The remediation therefore retains both upstream transports but
uses an AutoCAD `Application.Idle` event as the only consumer of a bounded,
AutoCAD-independent request queue.

# Universal 2D geometry primitives v1 reuse review

Reviewed for stage `stage-cad-agent-universal-2d-geometry-primitives-v1`:

| Area | Upstream / Existing State | Decision | Rationale |
|---|---|---|---|
| Plan schema | Existing `ChangePlan` v1 with property mutations | ADAPT | Retain `version: 1`, `drawing`, `operations`. Extend `PlanOperation` with nullable geometry fields. 100% backward compatible. |
| Primitive DTOs | Upstream uses ad-hoc command parameters or untyped dictionaries | BUILD / ADAPT | Typed `PointDto`, `ScaleDto` with strict `double.IsFinite` verification and deterministic WCS units. |
| Transaction model | Existing `PlanExecutor` single-transaction with preflight dry-run & postcondition verification | ADAPT | ModelSpace entity creations execute inside the single transaction. Verifies postconditions before commit; on abort, fresh open-close transaction confirms created handles do not exist. |
| Entity creation | Upstream creates entities in command handlers | ADAPT | Dedicated deterministic `EntityCreator` in `CadAgent.Plugin` using standard `Autodesk.AutoCAD.DatabaseServices` primitives (`Line`, `Polyline`, `Circle`, `Arc`, `DBText`, `MText`, `BlockReference`). Zero COM/LISP/command string execution. |
| Readback representation | `cad_list_entities` returns `EntityDto` | ADAPT | Extend `EntityDto` and `EntityInspector` to return defining geometry for `Line`, `Polyline`, `Circle`, `Arc`, `DBText`, `MText`, `BlockReference`. |
| MCP mapping | 7 existing tools | KEEP | Retain existing tools. No tool-per-primitive explosion. `cad_validate_change_plan` and `cad_apply_change_plan` carry the typed primitives. |
| Modifications | `move_entity`, `rotate_entity`, `delete_entity`, `scale_entity` | DEFERRED TO v1.1 | Kept out of scope for v1 per Ponytail Lite baseline and prompt to ensure atomic creation rollback verification remains simple and robust. |
