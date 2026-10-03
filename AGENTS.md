# AGENTS.md

- Do not use Git commands to undo or discard changes. In particular, do not use `git checkout`, `git restore`, `git reset`, `git revert`, or any other Git command for rollback or deletion. Edit files directly and preserve unrelated changes.
- If a bug-fix attempt does not fix the bug, first remove only the code introduced by that failed attempt. Then try a different fix; do not layer it on top of the failed attempt. Preserve unrelated changes.
- For feature work, bug fixes, and software errors, write and run a minimal automated test that reproduces the real behavior and runs the code under test without mocks. Remove tests added solely to verify the feature or fix after verification. Preserve pre-existing tests. If the user explicitly asks to add a test, add it and keep it.
- Write commit subjects in Conventional Commit format: `<type>(<scope>): <imperative summary>`. Omit the scope when it adds no useful context. Use `feat` for new functionality, `fix` for bug fixes, and `chore` for maintenance or documentation; use other standard types as appropriate.
- Do not create commits unless the user explicitly asks you to commit in the current conversation.
- Before running `dotnet build` or `dotnet test`, disable Avalonia build telemetry for that command/session by setting `AVALONIA_TELEMETRY_OPTOUT=1` (in PowerShell: `$env:AVALONIA_TELEMETRY_OPTOUT = '1'`). Do not change the user's persistent environment.
