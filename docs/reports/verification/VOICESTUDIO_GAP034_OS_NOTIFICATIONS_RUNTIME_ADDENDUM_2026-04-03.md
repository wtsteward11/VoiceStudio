# GAP-034 OS notifications — runtime display addendum (2026-04-03)

**Parent closure:** [VOICESTUDIO_GAP034_OS_NOTIFICATIONS_LANE_CLOSURE_2026-04-03.md](./VOICESTUDIO_GAP034_OS_NOTIFICATIONS_LANE_CLOSURE_2026-04-03.md)

## Purpose

Automated tests prove **dispatch**, **deduplication**, and **failure isolation**. This addendum records **operator-visible** Windows notification behavior so expectations stay honest across deployment shapes.

## Observed behavior (class notes)

| Shape | Success path | Failure path | Notes |
|-------|--------------|--------------|-------|
| **Unpackaged dev** (F5 / `dotnet run`) | Toast/banner may appear for batch complete, training complete, export complete | Export with backend stopped: expect **one** failure toast aligned with `ICompletionOsNotificationService` title/body | App identity and notification policy depend on `app.manifest` / OS focus rules. |
| **Packaged (MSIX)** | Same producers; Windows may route to **Notification Center** when focus is elsewhere | Same | Quiet hours / Focus Assist can suppress banners; notification may still land in center. |

## What to spot-check once (manual)

1. **Batch:** Run a short batch job to completion → **one** OS notification + existing in-app toast. Trigger a failed job → **one** failure OS notification for that `JobId`.
2. **Training:** Complete and fail paths → **one** notification each per terminal `JobId`.
3. **Export:** **File → Export Audio** to a valid path → **one** success notification. Stop backend and export → **one** failure notification per attempt (per-invocation `Guid`).

## Privacy reminder

Bodies use **job names**, output **file names**, and **truncated** error text — not full paths or secrets (see `CompletionOsNotificationMessages.Shorten`).
