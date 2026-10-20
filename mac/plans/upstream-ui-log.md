# Upstream UI changes (R9)

Written by the U-tasks of an upstream sync (`mac/plans/RUNBOOK.md` section 5, `mac/UPSTREAM-SYNC.md` section 5). `mac/tools/upstream-sync-check.sh` reads it: step `ui-parity` is `WARN` for every changed Windows UI file of the checked range that has no row here for the upstream commit that changed it.

One row per changed UI file and upstream commit: the file as the check prints it, the short hash of the upstream commit that last changed it in the merged range (`git log -1 --format=%h <FROM>..<TO> -- <file>`), the U-task that carries the work, and the parity-ledger rows it added or changed (each with status `todo` until the U-task is done, see `mac/plans/parity-ledger.md`).

| File | Upstream commit | U-task | Ledger rows |
|---|---|---|---|
