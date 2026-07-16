# API Contract and Compatibility Guide

## 1. Purpose

This file prevents frontend-backend mismatch.

Use when:

```text
- UI already exists and backend must support it
- Backend API changes may break UI
- Request/response shape is unclear
- A new endpoint is added
- A field is renamed or removed
- Search/filter/pagination must match UI
```

## 2. Core Rule

The API contract must match both:

```text
1. What the UI needs
2. What the backend/business rules allow
```

Do not create a new API style if the project already has one.

## 3. API Contract Template

```text
Feature:
Screen/UI:
Endpoint:
Method:
Auth required:
Allowed roles:

Request:
- Params:
- Query:
- Body:

Success response:
Status code:
Body:

Error responses:
- 400:
- 401:
- 403:
- 404:
- 409:
- 500:

Frontend fields used:
Backend fields returned:
Sensitive fields excluded:
Pagination:
Sorting:
Filtering:
```

## 4. Compatibility Checklist

| ID | Check | Expected | Status |
|---|---|---|---|
| API-01 | Endpoint exists | UI can call it | Not Checked |
| API-02 | HTTP method matches UI | GET/POST/PATCH/DELETE correct | Not Checked |
| API-03 | Request params match | IDs/query/body correct | Not Checked |
| API-04 | Response fields match UI | UI fields exist | Not Checked |
| API-05 | Status codes are consistent | Error handling works | Not Checked |
| API-06 | Auth/role enforced | Backend protects route | Not Checked |
| API-07 | Sensitive fields excluded | No password/token/secret | Not Checked |
| API-08 | Pagination/search/filter match UI | Results behave correctly | Not Checked |
| API-09 | Empty/error states supported | UI can display them | Not Checked |

## 5. Breaking Change Rules

A change is breaking if it:

```text
- Renames a field used by UI
- Removes a field used by UI
- Changes endpoint path/method
- Changes success response structure
- Changes error response structure
- Changes enum/status values
- Changes auth requirement
```

Before a breaking change:

```text
1. Identify all frontend/API clients using it.
2. Update all affected callers.
3. Update tests/manual test cases.
4. Document the change.
5. Confirm no old flow is needed.
```

## 6. UI-to-Backend Mapping Table

| UI Element | UI Field/Action | Expected API | Current API | Status | Notes |
|---|---|---|---|---|---|
|  |  |  |  | Not Checked |  |

## 7. Error Response Standard

Use existing project format. If unknown, document current behavior.

Example format:

```json
{
  "success": false,
  "message": "User not found",
  "errorCode": "user_not_found"
}
```

## 8. API Test Cases

For each endpoint, test:

```text
- Success case
- Missing input
- Invalid input
- Not authenticated
- Wrong role
- Not found
- Duplicate/conflict
- Empty result
```
