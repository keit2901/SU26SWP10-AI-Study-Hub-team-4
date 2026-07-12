# AI Share Moderation Test Data

This folder contains three manual test scenarios for the folder share moderation flow:

1. `01_auto_approve`
2. `02_human_review`
3. `03_hard_reject`

Recommended demo flow:

1. Create a folder in the app using the suggested name from each `folder-metadata.json`.
2. Copy the suggested description into the folder description field.
3. Upload or recreate the sample document names/content in that folder.
4. Trigger `Share`.

Expected outcomes:

- `01_auto_approve` -> AI auto-approves
- `02_human_review` -> AI sends to human review
- `03_hard_reject` -> AI rejects on hard-rule signal, then student can use `Request human review`

Notes:

- The current moderator implementation in code is deterministic and metadata-driven.
- File names and folder descriptions matter because the AI moderator checks them directly.
