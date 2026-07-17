# Security Defense Checklist

## 1. Purpose

This file provides a lightweight security checklist for a local academic/demo project.

It is not a full enterprise security program.  
It is designed to help the team prove that the project has basic security, permission, and data-protection logic before defense.

Use this file before:

```text
- Demo day
- Committee review
- Code review
- Role/permission testing
- Final submission
```

## 2. Scope

Main goals:

```text
1. Prevent obvious role/permission mistakes.
2. Prevent users from accessing data they should not access.
3. Prevent inactive/disabled users from using protected features.
4. Avoid leaking secrets, tokens, passwords, or API keys.
5. Validate risky inputs such as file upload, IDs, and status values.
6. Be able to explain security decisions clearly.
```

Out of scope unless required:

```text
- Full penetration testing
- Enterprise compliance
- Cloud production hardening
- 24/7 monitoring
- Payment security
```

## 3. Auth Checklist

| ID | Check | Expected | Status | Evidence |
|---|---|---|---|---|
| SEC-AUTH-01 | Register validates required fields | Invalid input rejected | Not Checked | |
| SEC-AUTH-02 | Login rejects wrong credentials | Clear error, no crash | Not Checked | |
| SEC-AUTH-03 | Password is not stored by app in plain text | Auth provider/hash used | Not Checked | |
| SEC-AUTH-04 | Inactive/disabled user cannot login | 403 or equivalent error | Not Checked | |
| SEC-AUTH-05 | Protected APIs require token/session | 401 when unauthenticated | Not Checked | |
| SEC-AUTH-06 | Current user endpoint returns safe fields only | No token/password/secret | Not Checked | |
| SEC-AUTH-07 | Logout behavior is defined | Session/token removed or user redirected | Not Checked | |

## 4. Role and Permission Checklist

Use the project’s actual roles. Do not invent roles.

| ID | Check | Expected | Status | Evidence |
|---|---|---|---|---|
| SEC-ROLE-01 | Normal user cannot access Admin pages | UI and backend block access | Not Checked | |
| SEC-ROLE-02 | Normal user cannot call Admin API directly | Backend returns 401/403 | Not Checked | |
| SEC-ROLE-03 | Moderator cannot perform Admin-only actions | Backend enforces role separation | Not Checked | |
| SEC-ROLE-04 | Admin cannot perform Super Admin-only actions if applicable | Backend enforces hierarchy | Not Checked | |
| SEC-ROLE-05 | UI hiding is not the only protection | Backend has permission checks | Not Checked | |
| SEC-ROLE-06 | Role change is restricted | Only allowed role can change roles | Not Checked | |
| SEC-ROLE-07 | Sensitive actions are logged if audit log exists | Lock/delete/approve/reject recorded | Not Checked | |

## 5. Data Ownership Checklist

| ID | Check | Expected | Status | Evidence |
|---|---|---|---|---|
| SEC-DATA-01 | User can only view own private data | Cross-user access blocked | Not Checked | |
| SEC-DATA-02 | User can only edit/delete own resources | Ownership check exists | Not Checked | |
| SEC-DATA-03 | Admin/Moderator access follows business rules | No excessive access | Not Checked | |
| SEC-DATA-04 | Deleted/hidden/private data does not appear publicly | Queries filter status/visibility | Not Checked | |
| SEC-DATA-05 | API does not return sensitive fields | Password/token/secret excluded | Not Checked | |

## 6. Upload Security Checklist

Use if the project supports file upload.

| ID | Check | Expected | Status | Evidence |
|---|---|---|---|---|
| SEC-UPLOAD-01 | Empty file rejected | 400 or clear error | Not Checked | |
| SEC-UPLOAD-02 | File size limit exists | Oversized file rejected | Not Checked | |
| SEC-UPLOAD-03 | File extension whitelist exists | Unsupported type rejected | Not Checked | |
| SEC-UPLOAD-04 | MIME type checked if implemented | Fake extension rejected | Not Checked | |
| SEC-UPLOAD-05 | Upload failure handled cleanly | No stuck state | Not Checked | |
| SEC-UPLOAD-06 | User must be active to upload | Inactive user rejected | Not Checked | |

## 7. Secret Handling Checklist

| ID | Check | Expected | Status | Evidence |
|---|---|---|---|---|
| SEC-SECRET-01 | No real API keys committed | Use env/user-secrets/local secret store | Not Checked | |
| SEC-SECRET-02 | No real passwords in public docs | Use placeholders | Not Checked | |
| SEC-SECRET-03 | Secret files are gitignored | `.env`, local secrets not committed | Not Checked | |
| SEC-SECRET-04 | Logs do not print tokens/passwords | Sensitive values masked | Not Checked | |
| SEC-SECRET-05 | Demo accounts are safe test accounts | No personal/private accounts | Not Checked | |

## 8. Required Manual Security Tests

| ID | Test | Steps | Expected |
|---|---|---|---|
| T-SEC-01 | Normal user cannot access Admin route | Login as normal user, open Admin URL | Access denied or redirected |
| T-SEC-02 | Normal user cannot call Admin API | Call Admin API with normal user token | 401/403 |
| T-SEC-03 | Inactive user cannot login | Disable account, attempt login | Login rejected |
| T-SEC-04 | Invalid upload type rejected | Upload unsupported file type | Rejected |
| T-SEC-05 | Oversized upload rejected | Upload file above limit | Rejected |
| T-SEC-06 | Cross-user access blocked | User A tries User B resource | 403/404 |
| T-SEC-07 | Sensitive fields not returned | Inspect API response | No password/token/secret |

## 9. Evidence Format

```text
Security Check:
Result: PASS / FAIL / PARTIAL / NOT CHECKED
Evidence:
- Screenshot:
- API response:
- Test command:
- Relevant file:
- Notes:
```

## 10. Final Security Decision

```text
READY:
- No critical role/auth/data leak issue.
- Basic auth and role tests pass.
- No real secrets in committed files.

READY WITH NOTES:
- Minor issues exist but do not affect demo safety.
- Limitation is documented clearly.

NOT READY:
- Normal user can access Admin/Moderator function.
- Inactive user can still use protected features.
- Secrets are exposed.
- Upload accepts dangerous files without restriction.
```
