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
