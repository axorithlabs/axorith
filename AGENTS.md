# Change Management Rules

- Do not use Git commands to undo or discard changes. In particular, do not use `git checkout`, `git restore`, `git reset`, `git revert`, or any other Git command for rollback or deletion. Edit files directly and preserve unrelated changes.
- If a bug-fix attempt does not fix the bug, first remove only the code introduced by that failed attempt. Then try a different fix; do not layer it on top of the failed attempt. Preserve unrelated changes.
- Verify every bug or software-error fix with an automated test that reproduces the real behavior and runs the code under test without mocks.

## Commit Messages

- Write commit subjects in Conventional Commit format: `<type>(<scope>): <imperative summary>`. Omit the scope when it adds no useful context. Use `feat` for new functionality, `fix` for bug fixes, and `chore` for maintenance or documentation; use other standard types as appropriate.
