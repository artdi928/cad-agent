# cad-agent rules

- REUSE > ADAPT > BUILD; inspect licenses and preserve notices before copying.
- The AutoCAD plugin is a deterministic executor, never an AI client.
- `CadAgent.Protocol` and `CadAgent.Bridge` must not reference Autodesk assemblies.
- AutoCAD mutations are allowlisted, preconditioned, transactionally atomic.
- Authoritative postcondition verification occurs before transaction commit.
- No mutation bypasses change-plan validation/dry-run.
- Real project plans and source data remain untracked.
- The AutoCAD plugin remains deterministic and AI-independent.
- MText and MLeader are inspect-only in mutation v1.
- Never access AutoCAD APIs from a pipe worker; enqueue into the bounded request pump and access AutoCAD only from its `Application.Idle` callback.
- Do not track Autodesk assemblies or machine-specific paths.
- Preserve user changes; keep diffs bounded.
- Report AutoCAD runtime PASS only with actual interactive evidence.
