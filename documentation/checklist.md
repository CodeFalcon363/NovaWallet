# Engineering QA Checklist (from CheckList.xlsx)

Source: `documentation/CheckList.xlsx`, `Sheet1` (single sheet, columns
`CHECKLIST ITEM` / `YES/NO`). Order preserved as in the spreadsheet. This is
FirstBank's internal general-purpose engineering checklist; not all items are
applicable to a backend-only ledger service (e.g. Next.js frontend items) — see
notes in the BRD's Scope Boundaries section for how each item maps (or doesn't)
to this project.

- [X] Input validations on all the VMs and DTOs (do not allow unwanted characters
  in inputs, also provide for character length)
- [X] Negative amount test
- [X] Roles validation, for APIs... someone from another role should not be able
  to call an endpoint for another role
- [X] Internal Scan should return 100% for frontend
- [ ] Internal scan should return 100% for backend
- [X] Frontend (Next.js) should be updated to the latest version
- [X] Avoid returning delicate information to the frontend if they are not using
  it, e.g. Phone number, NIN, BVN, Email
- [X] Rate limiting is added
- [X] Create a Postman collection for backend
- [X] Ensure that the new clientId validation is integrated (this will help to
  stop Man-in-the-Middle attacks)
- [X] Ensure that all configurable items are in the config (so that when the
  application is moved to subsidiaries it will be easy to customize)
- [ ] Ensure that response headers only return what is useful (X-XSS-Protection,
  Strict-Transport-Security, X-Frame-Options, X-Content-Type-Options,
  Content-Security-Policy)
- [X] Ensure to validate uploaded document types and sizes
- [ ] Server disclosure on headers
- [ ] Ensure that OWASP Top 10 are followed accordingly
- [X] Login user must be validated, using 2-factor authentication
- [X] Unit Testing
- [X] Read Me file creation

**Legend:** `[x]` = marked `YES` in the spreadsheet. `[ ]` = left blank
(no value) in the spreadsheet — treated as not-yet-confirmed/open, not as `NO`.
