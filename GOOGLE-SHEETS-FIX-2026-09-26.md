# Google Sheets credential fix - 2026-09-26

The previous service-account key returned `invalid_grant: account not found` for `crm-sheet-access@profitnxcrm.iam.gserviceaccount.com`.
That key is intentionally not included in this ZIP.

## Local Windows
Place a fresh downloaded Google service-account JSON key as:
- `google-service-account.json` in the project root, OR
- `App_Data/google-service-account.json`

The application checks both locations and the published output folder.

## Render / server
Set the complete fresh JSON in the secret environment variable `GOOGLE_SERVICE_ACCOUNT_JSON`.

## Google Sheet permission
Share the target spreadsheet with the fresh service account's `client_email` as Editor.
