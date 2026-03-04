# Beta Program (Item 39)

**Purpose:** Define the v1.1.0 beta pipeline: recruitment, feedback intake, crash reporting opt-in, and release cadence.

## Recruitment

- **Target:** 10 beta testers for the content-creator wedge.
- **Criteria:** Content creators (podcasters, YouTubers, course creators) who record their own voice and need consistent, broadcast-quality output. Willing to run pre-release builds and provide structured feedback.
- **Recruitment channels:** Direct outreach, community post, or sign-up form; document in project notes.

## Feedback intake

- **In-app:** Feedback button in the Help panel (or equivalent) that opens a short form; submission is stored locally (e.g. JSON under user data) and can be optionally emailed to the maintainer.
- **Local storage:** Feedback entries saved to a local file (path TBD, e.g. `data/feedback.json` or under AppData) so users can review what they sent; no automatic network submission without consent.
- **Optional email:** If the user opts in, allow sending a summary (no PII) or attaching the feedback file to a designated address.

## Crash report opt-in

- **Extend consent:** Use existing `AnalyticsService.cs` consent (or equivalent) to include "Send crash reports" as an opt-in. When enabled, crash/minidump or error bundles may be included in support bundle export or sent to a designated endpoint.
- **Document:** In-app and in docs, state that crash reporting is off by default and only sent when the user has opted in.

## Release cadence

- **Weekly builds:** Use existing Gate C publish (or equivalent) to produce weekly beta builds (e.g. versioned as `1.1.0-beta.N`).
- **Distribution:** Installer or portable artifact from CI; link or instructions in beta program communication.
- **Changelog:** Maintain a short beta changelog so testers know what changed between builds.

## Success criteria for beta

- 10 beta testers have used the app for at least 2 weeks (tracked via self-report or usage instrumentation).
- Feedback and crash reports (where opted in) are reviewed and triaged; blockers fixed before v1.1.0 release.

## Revision history

| Date       | Change |
|------------|--------|
| 2026-02-28 | Initial beta program doc (Item 39). |
