# Skill 3 — Role-Based System Criteria Comparison

## 1. Purpose

Use this skill when the project already has a system, UI, backend, database, or documentation, and the task is to compare the current implementation against external criteria by role and system logic.

This skill is different from a general audit skill.

The focus is not only:

```text
Does the system meet the criteria?
```

The focus is:

```text
Does each role meet its own criteria?
Does Admin meet Admin requirements?
Does Moderator meet Moderator requirements?
Does User meet User requirements?
Does Super Admin meet Super Admin requirements?
Does the system logic correctly support those roles?
Does the UI match the backend permission and data flow?
```

Use this skill when the user says things like:

```text
- Compare Admin with the criteria.
- Compare Moderator with the requirements.
- Check whether User functions are enough.
- Check role permission logic.
- Compare system logic with the rubric.
- Check if Admin, Mod, User are implemented correctly.
- Compare current system with teacher/client requirements.
- Check whether UI and backend match for each role.
- Find what is missing for each role.
```

---

## 2. Core Rule

Never compare the system in a vague way.

Bad output:

```text
The Admin part is mostly okay, but some features are missing.
```

Good output:

```text
Admin Criterion A3: Admin can lock/unlock users.
Evidence: UI has Lock button, backend has PATCH /admin/users/:id/status.
Status: Partially Met.
Gap: Backend does not prevent Admin from locking Super Admin.
Fix: Add service-layer role guard before updating user status.
Priority: High.
```

Every judgment must include:

```text
- Criterion
- Role/module
- Evidence from current system
- Status
- Gap
- Recommended fix
- Priority
```

---

## 3. Main Comparison Targets

When this skill is used, separate the comparison into these sections if applicable:

```text
1. Admin comparison
2. Moderator comparison
3. User comparison
4. Super Admin comparison
5. Guest/Public comparison
6. System logic comparison
7. UI-to-backend consistency
8. Database/model consistency
9. Permission matrix
10. Priority fix plan
```

Do not force sections that do not exist in the project.

If the project only has Admin and User, only compare Admin, User, and system logic.

If the project has Admin, Moderator, User, and Super Admin, compare all of them separately.

---

## 4. Required Input Handling

The user may provide one or more of these inputs:

```text
- Assignment rubric
- Teacher requirements
- Client requirements
- UI screenshots
- Figma screen
- Frontend code
- Backend code
- API documentation
- Database schema
- README
- User stories
- Use case diagram
- Role description
- Sample report/template
- GitHub project code
```

Treat the requirement/rubric as the main source of truth.

If the user provides UI and backend, compare UI behavior with backend implementation.

If the user provides only UI, infer required backend behavior carefully and mark assumptions.

If the user provides only backend, compare implemented routes/services/models with the role criteria.

If evidence is missing, mark it as:

```text
Unclear / Need More Evidence
```

Do not pretend something exists if it is not shown.

---

## 5. Status Definitions

Use only these status labels:

```text
Met
Partially Met
Not Met
Unclear / Need More Evidence
```

### Met

The current system clearly satisfies the criterion.

Example:

```text
Criterion: Admin can view user list.
Evidence: GET /admin/users exists, requires ADMIN role, and UI renders user table.
Status: Met.
```

### Partially Met

Some parts exist, but the feature is incomplete, unsafe, or inconsistent.

Example:

```text
Criterion: Moderator can review reports.
Evidence: Report queue UI exists, but backend does not check MODERATOR role.
Status: Partially Met.
```

### Not Met

The system does not implement the criterion or implements the wrong behavior.

Example:

```text
Criterion: Only Super Admin can assign Admin role.
Evidence: Role update endpoint allows ADMIN and SUPER_ADMIN.
Status: Not Met.
```

### Unclear / Need More Evidence

There is not enough information to judge.

Example:

```text
Criterion: Admin actions are logged.
Evidence: No audit log file/model was provided.
Status: Unclear / Need More Evidence.
```

---

## 6. Priority Definitions

Use only these priority labels:

```text
Critical
High
Medium
Low
```

### Critical

Use Critical for:

```text
- Security/permission hole
- Wrong role can access protected function
- Data loss risk
- Main feature cannot work
- Sensitive data exposed
```

### High

Use High for:

```text
- Required rubric feature missing
- UI cannot connect to backend
- Important business logic wrong
- Important validation missing
- Role hierarchy broken
```

### Medium

Use Medium for:

```text
- Partial edge case missing
- Response format inconsistent
- Missing audit log
- Missing test case
- Pagination/search/filter incomplete
```

### Low

Use Low for:

```text
- Naming issue
- Minor UX issue
- Small documentation issue
- Code style inconsistency
```

---

## 7. Role-Based Comparison Checklist

## 7.1 Admin Comparison

Use this section for all Admin-related criteria.

Check whether Admin can:

```text
- Access Admin dashboard
- View summary statistics
- View user list
- Search/filter users
- View user detail
- Lock/unlock users
- Change user status
- Manage reports or escalations
- Monitor content
- Restore or remove content if required
- View activity history/session history if required
- View audit logs if required
- Manage system-level data if required
- Use only Admin-permitted routes
```

Check whether Admin is blocked from actions that should belong to Super Admin:

```text
- Granting Super Admin
- Removing Super Admin
- Editing Super Admin account
- Deleting critical system data without protection
- Changing roles beyond their authority
- Accessing private data without requirement
```

Admin comparison output example:

```text
ID: A1
Criterion: Admin can lock/unlock user accounts.
Evidence: UI has Lock button and backend has PATCH /admin/users/:id/status.
Status: Partially Met.
Gap: Backend does not prevent Admin from locking Super Admin.
Recommended Fix: Add service-layer check: if target.role === SUPER_ADMIN, reject unless actor is SUPER_ADMIN.
Priority: High.
```

---

## 7.2 Moderator Comparison

Use this section for Moderator-related criteria.

Check whether Moderator can:

```text
- Access moderation dashboard
- View moderation queue
- View reports assigned to them
- Review flagged content
- Approve/reject content
- Hide/remove inappropriate content if required
- Escalate issue to Admin
- Add moderation notes
- View report status
- Use only moderation-related routes
```

Check whether Moderator is blocked from Admin-only actions:

```text
- Cannot manage all users unless explicitly required
- Cannot change user roles
- Cannot access Admin dashboard if not allowed
- Cannot view audit logs unless required
- Cannot access system settings
- Cannot hard-delete critical data unless required
- Cannot grant/revoke roles
```

Moderator comparison output example:

```text
ID: M2
Criterion: Moderator can review reported content but cannot change user roles.
Evidence: Moderator UI has report queue, but backend allows MODERATOR on /admin/users/:id/role.
Status: Not Met.
Gap: Moderator can access role-management endpoint.
Recommended Fix: Restrict role update endpoint to SUPER_ADMIN or ADMIN only depending on rubric.
Priority: Critical.
```

---

## 7.3 User Comparison

Use this section for normal User-related criteria.

Check whether User can:

```text
- Register
- Login
- Logout
- View own profile
- Update allowed profile fields
- Upload/create own content if required
- View own content/data
- Edit own content if required
- Delete own content if required
- Report content
- Search/browse allowed resources
- Use only user-level routes
```

Check whether User is blocked from restricted actions:

```text
- Cannot access Admin dashboard
- Cannot access Moderator queue
- Cannot edit other users
- Cannot view private data of other users
- Cannot call Admin backend endpoints
- Cannot modify role/status fields
- Cannot delete other users' resources
```

User comparison output example:

```text
ID: U4
Criterion: User can only update their own profile.
Evidence: PATCH /users/:id accepts any id and only checks authentication.
Status: Not Met.
Gap: Missing ownership check.
Recommended Fix: Compare req.user.id with params.id unless actor has Admin role.
Priority: Critical.
```

---

## 7.4 Super Admin Comparison

Use this section only if the system has Super Admin.

Check whether Super Admin can:

```text
- Manage Admin accounts
- Grant/revoke Admin role
- Grant/revoke Moderator role
- Access all Admin functions
- Manage critical settings
- View all audit logs
- Override Admin decisions if required
- Protect system-level configuration
```

Check whether dangerous Super Admin actions are protected:

```text
- Cannot accidentally remove themselves
- Cannot delete the last Super Admin
- Critical actions are logged
- Role changes are logged
- Permission changes require strict checks
```

Super Admin comparison output example:

```text
ID: SA1
Criterion: Only Super Admin can grant Admin role.
Evidence: roleUpdate service allows ADMIN and SUPER_ADMIN.
Status: Not Met.
Gap: Regular Admin can grant Admin role.
Recommended Fix: Restrict Admin role assignment to SUPER_ADMIN only.
Priority: Critical.
```

---

## 7.5 Guest/Public Comparison

Use this section only if the system has public access.

Check whether Guest can:

```text
- View public pages
- Register/login
- Search public content if allowed
- Access public documentation if allowed
```

Check whether Guest is blocked from:

```text
- Accessing private user data
- Uploading content if login is required
- Reporting content if login is required
- Calling protected API routes
- Accessing Admin/Moderator endpoints
```

---

## 8. System Logic Comparison

Use this section when checking the overall logic of the system.

System logic is not one screen or one role. It is how the whole system behaves.

Check these areas:

---

### 8.1 Authentication Logic

Check:

```text
- Register works correctly
- Login works correctly
- Logout works correctly
- Password is hashed
- Token/session is generated correctly
- Token/session expiration exists if required
- Locked users cannot login
- Deleted users cannot login
- Invalid login does not expose sensitive info
```

Example:

```text
Criterion: Locked users cannot login.
Evidence: login service checks email/password but does not check user.status.
Status: Not Met.
Gap: Locked users can still login.
Fix: Add status check before generating token.
Priority: Critical.
```

---

### 8.2 Authorization Logic

Check:

```text
- Protected routes require authentication
- Role middleware exists
- Role middleware is applied to correct routes
- Admin-only routes reject User
- Moderator-only routes reject User
- Super Admin-only actions are protected
- Backend protection exists even if UI hides buttons
```

Important rule:

```text
UI hiding is not security.
Backend must enforce permission.
```

---

### 8.3 Data Ownership Logic

Check:

```text
- User can only view own private data
- User can only update own resources
- User can only delete own resources
- Moderator can only access moderation-related data
- Admin can access data according to requirement
- Cross-user access is blocked
```

---

### 8.4 State Transition Logic

Check status changes such as:

```text
- ACTIVE -> LOCKED
- LOCKED -> ACTIVE
- PENDING -> APPROVED
- PENDING -> REJECTED
- OPEN -> RESOLVED
- VISIBLE -> HIDDEN
- ACTIVE -> DELETED
```

For each transition, check:

```text
- Who can perform it?
- Is the transition allowed?
- Is invalid transition blocked?
- Is it recorded in audit log if required?
```

Example:

```text
Criterion: Report can move from OPEN to RESOLVED only by Moderator/Admin.
Evidence: PATCH /reports/:id/status allows any authenticated user.
Status: Not Met.
Gap: Normal User can resolve reports.
Fix: Add MODERATOR/ADMIN role guard.
Priority: Critical.
```

---

### 8.5 Validation Logic

Check:

```text
- Required fields
- Empty/null values
- Invalid enum values
- Invalid ID format
- Duplicate data
- Not found data
- Min/max length
- File type/size if uploads exist
- Date range if filters exist
```

---

### 8.6 Error Handling Logic

Check:

```text
- 400 for invalid input
- 401 for unauthenticated
- 403 for unauthorized
- 404 for not found
- 409 for duplicate/conflict
- 500 for unexpected server error
- Error response format is consistent
```

Recommended response format:

```json
{
  "success": false,
  "message": "User not found"
}
```

---

### 8.7 Audit Log Logic

Check whether important actions are logged:

```text
- Admin locks/unlocks user
- Admin changes user role
- Moderator approves/rejects content
- Admin deletes/restores content
- Super Admin changes permission
- Sensitive data is modified
```

Audit log should include:

```text
- actor_id
- action
- target_type
- target_id
- old_value if useful
- new_value if useful
- created_at
```

---

### 8.8 API Response Consistency

Check:

```text
- Success response format is consistent
- Error response format is consistent
- Field names match frontend usage
- Status code matches result
- Pagination format is consistent
- Sensitive fields are not returned
```

Example:

```text
Criterion: User list API should not return password_hash.
Evidence: GET /admin/users returns full User object including password_hash.
Status: Not Met.
Gap: Sensitive field exposure.
Fix: Exclude password_hash in select/projection.
Priority: Critical.
```

---

## 9. UI-to-Backend Consistency Comparison

Use this section when UI already exists.

Check:

```text
- Every UI button has a backend action if needed
- Every form field maps to backend request body
- Every table column exists in API response
- Search input maps to backend query
- Filter dropdown maps to backend query
- Pagination UI maps to backend pagination
- Modal actions have backend endpoints
- UI success state matches backend success response
- UI error state handles backend errors
- Role-based UI controls match backend permissions
```

Important security rule:

```text
If the UI hides an action but the backend still allows the request, the system does not meet the criterion.
```

UI-to-backend example:

```text
UI Element: Lock User button
Expected Backend: PATCH /admin/users/:id/status with ADMIN/SUPER_ADMIN only
Current Backend: PATCH exists but only requires authentication
Status: Partially Met
Fix: Add role guard
Priority: Critical
```

---

## 10. Database/Model Consistency Comparison

Check whether database supports the role criteria.

Check:

```text
- Required tables/models exist
- Required fields exist
- Role field exists
- Status field exists
- Ownership foreign keys exist
- Report/content relationship exists
- Audit log table exists if required
- Soft delete field exists if required
- Timestamps exist if required
- Unique constraints exist
- Enum constraints exist
```

Example:

```text
Criterion: Admin can lock/unlock users.
Evidence: users table has no status field.
Status: Not Met.
Gap: Database cannot store locked state.
Fix: Add status field with enum ACTIVE/LOCKED.
Priority: High.
```

---

## 11. Permission Matrix Requirement

When roles exist, create a permission matrix.

Example:

| Action | Guest | User | Moderator | Admin | Super Admin | Current System | Status |
|---|---:|---:|---:|---:|---:|---|---|
| View public content | Yes | Yes | Yes | Yes | Yes | Matches | Met |
| Upload document | No | Yes | Yes | Yes | Yes | User only | Partially Met |
| Report content | No | Yes | Yes | Yes | Yes | Matches | Met |
| Review reports | No | No | Yes | Yes | Yes | User can access API | Not Met |
| Lock user | No | No | No | Yes | Yes | Auth only | Not Met |
| Change user role | No | No | No | No/Partial | Yes | Admin allowed | Not Met |
| View audit logs | No | No | No | Yes | Yes | Missing | Not Met |

Rules:

```text
- Use the external criteria to decide expected permission.
- Use the current system evidence to decide current permission.
- If UI and backend disagree, backend determines real security.
```

---

## 12. Required Output Format

When using this skill, answer in this format:

```text
# Role-Based System Criteria Comparison

## 1. Scope
Current system/module:
External criteria:
Roles checked:
Files/screens reviewed:
Important assumptions:

## 2. Overall Summary
Total criteria checked:
Met:
Partially Met:
Not Met:
Unclear:
Highest risk:
Submission/demo readiness:

## 3. Admin Comparison
| ID | Criterion | Evidence | Status | Gap | Recommended Fix | Priority |

## 4. Moderator Comparison
| ID | Criterion | Evidence | Status | Gap | Recommended Fix | Priority |

## 5. User Comparison
| ID | Criterion | Evidence | Status | Gap | Recommended Fix | Priority |

## 6. Super Admin Comparison
| ID | Criterion | Evidence | Status | Gap | Recommended Fix | Priority |

## 7. System Logic Comparison
| ID | Criterion | Evidence | Status | Gap | Recommended Fix | Priority |

## 8. UI-to-Backend Consistency
| UI Element | Expected Backend | Current Backend | Status | Recommended Fix | Priority |

## 9. Permission Matrix
| Action | Guest | User | Moderator | Admin | Super Admin | Current System | Status |

## 10. Priority Fix Plan
### Critical
### High
### Medium
### Low

## 11. Test Cases Needed
### Admin test cases
### Moderator test cases
### User test cases
### Permission/security test cases
### System logic test cases

## 12. Final Verdict
- What is already good?
- What must be fixed before submission/demo?
- What can be improved later?
- Is the system acceptable based on the criteria?
```

Do not skip the final verdict.

---

## 13. Fix Recommendation Rules

When recommending fixes:

```text
- Do not rewrite the whole system.
- Do not suggest unrelated features.
- Do not change architecture unless required.
- Suggest the smallest safe fix.
- Tie every fix to a specific gap.
- Mention the likely file/layer to fix if known.
```

Good fix:

```text
Add requireRole("ADMIN", "SUPER_ADMIN") middleware to PATCH /admin/users/:id/status.
```

Bad fix:

```text
Rebuild the whole authentication system.
```

---

## 14. Test Case Requirement

For each role, provide test cases.

### Admin Test Cases

```text
- Admin can access admin dashboard.
- Admin can view user list.
- Admin can lock normal user.
- Admin cannot lock Super Admin.
- Admin cannot perform Super Admin-only role assignment.
```

### Moderator Test Cases

```text
- Moderator can access moderation queue.
- Moderator can approve/reject reported content.
- Moderator cannot change user roles.
- Moderator cannot access Admin user management.
```

### User Test Cases

```text
- User can access own profile.
- User cannot access Admin dashboard.
- User cannot update another user.
- User cannot call moderation endpoints.
```

### System Logic Test Cases

```text
- Locked user cannot login.
- Deleted user cannot login.
- Invalid token returns 401.
- Wrong role returns 403.
- Not found resource returns 404.
- Duplicate data returns 409.
```

---

## 15. Prompt Template for AI Coding Agent

Use this prompt when asking an AI agent to perform role-based system comparison.

```text
You are a senior full-stack/backend auditor. The project already has an existing system. Your task is to compare the current implementation against the external criteria by role and system logic.

Do not rewrite the whole system.
Do not immediately code.
First perform a role-based criteria comparison.

Compare the system using these sections:
1. Admin comparison
2. Moderator comparison
3. User comparison
4. Super Admin comparison if the role exists
5. System logic comparison
6. UI-to-backend consistency
7. Database/model consistency
8. Permission matrix
9. Priority fix plan
10. Test cases
11. Final verdict

For every criterion, include:
- Criterion
- Role/module
- Evidence from current system
- Status: Met / Partially Met / Not Met / Unclear
- Gap
- Recommended fix
- Priority: Critical / High / Medium / Low

Important rules:
- Treat the rubric/requirement as the source of truth.
- If UI hides a button but backend still allows the action, the criterion is not met.
- If evidence is missing, mark it as Unclear / Need More Evidence.
- Do not guess as confirmed.
- Do not mix Admin and Moderator responsibilities.
- Keep fixes focused and tied to specific gaps.
- Do not suggest large refactors unless necessary.

External criteria:
[PASTE RUBRIC / REQUIREMENTS HERE]

Current system evidence:
[PASTE UI / BACKEND / DATABASE / API / SCREENSHOTS / FILES HERE]
```

---

## 16. Short Prompt Version

Use this when you want a shorter command.

```text
Compare the current system with the external criteria by role. Separate Admin, Moderator, User, Super Admin, system logic, UI-backend consistency, database consistency, and permission matrix. For each criterion, show evidence, status, gap, fix, and priority. Do not rewrite the system. If evidence is missing, mark it as unclear.
```

---

## 17. Vietnamese Prompt Version

Use this version if the user wants to give instructions in Vietnamese.

```text
Bạn là senior full-stack/backend auditor. Project đã có hệ thống sẵn. Nhiệm vụ của bạn là so sánh hệ thống hiện tại với các tiêu chí tham khảo bên ngoài theo từng role và logic hệ thống.

Không code lại toàn bộ hệ thống.
Không sửa code ngay từ đầu.
Trước tiên hãy audit và so sánh theo role.

Hãy tách rõ:
1. Admin
2. Moderator
3. User
4. Super Admin nếu có
5. Logic hệ thống
6. Độ khớp UI với backend
7. Database/model
8. Permission matrix
9. Kế hoạch sửa theo độ ưu tiên
10. Test cases
11. Kết luận cuối

Với mỗi tiêu chí, hãy ghi:
- Tiêu chí
- Role/module liên quan
- Bằng chứng trong hệ thống hiện tại
- Trạng thái: Met / Partially Met / Not Met / Unclear
- Khoảng thiếu/gap
- Cách sửa đề xuất
- Mức ưu tiên: Critical / High / Medium / Low

Luật quan trọng:
- Rubric/yêu cầu là nguồn chính để so sánh.
- Không gộp Admin và Moderator nếu yêu cầu tách riêng.
- UI ẩn nút không có nghĩa là an toàn; backend vẫn phải chặn quyền.
- Nếu thiếu bằng chứng thì ghi Unclear, không được bịa.
- Chỉ đề xuất sửa đúng phần thiếu, không refactor lớn nếu không cần.

Tiêu chí tham khảo:
[PASTE RUBRIC / REQUIREMENT Ở ĐÂY]

Hệ thống hiện tại:
[PASTE UI / BACKEND / DATABASE / API / SCREENSHOT / FILE Ở ĐÂY]
```

---

## 18. Common Mistakes to Avoid

The assistant must avoid:

```text
- Saying the system is good without evidence
- Mixing Admin and Moderator responsibilities
- Treating UI hiding as real backend security
- Ignoring ownership checks
- Ignoring Super Admin hierarchy
- Ignoring locked/deleted user login behavior
- Ignoring audit logs for sensitive actions
- Recommending full rewrite too early
- Adding features not required by the criteria
- Marking unclear items as met
- Forgetting test cases
- Forgetting priority levels
```

---

## 19. Final Rule

The final output must help the user answer these questions clearly:

```text
1. Admin đã đủ tiêu chí chưa?
2. Moderator đã đủ tiêu chí chưa?
3. User đã đủ tiêu chí chưa?
4. Super Admin nếu có đã đúng quyền chưa?
5. Logic hệ thống có lỗi quyền, dữ liệu, validation, state flow không?
6. UI và backend có khớp nhau không?
7. Database có đủ support các chức năng không?
8. Cái gì cần sửa trước để demo/nộp bài an toàn?
```

If the answer does not clearly answer these questions, the audit is incomplete.
