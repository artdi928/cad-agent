# cad-agent rules

- REUSE > ADAPT > BUILD; inspect licenses and preserve notices before copying.
- The AutoCAD plugin is a deterministic executor, never an AI client.
- `CadAgent.Protocol` and `CadAgent.Bridge` must not reference Autodesk assemblies.
- B1 is read-only and exposes only the four methods listed in README.
- Never add arbitrary command, LISP, C#, shell, save, or mutation execution to B1.
- Never access AutoCAD APIs from a pipe worker; enqueue into the bounded request pump and access AutoCAD only from its `Application.Idle` callback.
- Do not track Autodesk assemblies or machine-specific paths.
- Preserve user changes; keep diffs bounded.
- Report AutoCAD runtime PASS only with actual interactive evidence.
