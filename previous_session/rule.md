# rule.md — Session Progress Tracking Rule

**Mục đích:** Đảm bảo nếu session bị crash, hết context, mất kết nối, đổi máy, hoặc bất cứ biến cố nào, **session sau vẫn resume được trong < 5 phút** mà không mất tiến độ.

**Áp dụng cho:** Mọi agent (OpenCode / Claude / Kiro / khác) làm việc trên repo `D:\FPT\summer2026\SWP391`.

**Vị trí log:** `D:\FPT\summer2026\SWP391\previous_session\`

---

## 1. Nguyên tắc cốt lõi

1. **Luôn có một file "live" duy nhất** cho session đang chạy: `_CURRENT_SESSION.md`. Đây là nơi ghi tiến độ liên tục.
2. **Update là việc đầu tiên** sau mỗi hành động đáng kể, **không** dồn cuối session.
3. **Append-only trong session.** Không xoá history cũ; sai thì gạch ngang + ghi lại.
4. **End-of-session phải đóng file:** rename `_CURRENT_SESSION.md` thành `NN_Session_YYYY-MM-DD_<topic>_Handoff.md` (NN = số thứ tự kế tiếp).
5. **Mỗi update phải timestamp ISO 8601** (`2026-05-26T01:52Z`), tránh “vừa nãy” / “lúc trước”.
6. **Không tin trí nhớ.** Trước khi viết “đã làm X”, verify bằng tool (read file, run command). Nếu chưa verify → đánh dấu `[UNVERIFIED]`.

---

## 2. Khi nào phải update `_CURRENT_SESSION.md`

Update **ngay** sau mỗi sự kiện sau (không đợi tích luỹ):

| Trigger | Bắt buộc ghi |
|---|---|
| Đọc xong file context khởi đầu (Resume Pack, plan, handoff) | Section "Context loaded" |
| Sửa / tạo / xoá file source code | Path + 1 dòng mô tả thay đổi |
| Chạy migration, build, test, docker cmd | Command + exit status (pass/fail/output tóm tắt) |
| Thêm/đổi package, secret, config | Tên + version + lý do |
| Lock một quyết định với user | Decision + rationale + ai confirm |
| Gặp error / bug / blocker | Symptom + root cause (nếu biết) + fix attempt |
| Hoàn thành 1 task trong todo list | Đánh dấu DONE + ref tới evidence |
| User đổi hướng / đổi yêu cầu | Old plan → new plan, ngắn gọn |
| Trước khi chạy lệnh có rủi ro (drop, delete, force-push, prod) | Pre-flight log + xác nhận user |

**Không cần ghi:** mỗi tool call vụn vặt (read 1 file để xem), mỗi lần grep, mỗi message trao đổi nhỏ.

---

## 3. Cấu trúc bắt buộc của `_CURRENT_SESSION.md`

```markdown
# _CURRENT_SESSION — <topic ngắn>

**Started:** 2026-05-26T01:52Z
**Agent:** OpenCode (kr/claude-opus-4.7)
**Goal:** <1-2 dòng mục tiêu session>
**Status:** IN_PROGRESS | PAUSED | CLOSING

---

## 0. Context loaded
- [x] 02_Resume_Pack.md (read 2026-05-26T01:53Z)
- [x] 11_Session_..._D1D2_Handoff.md
- [ ] 07_Phase2_Document_RAG_Plan.md (skip — không cần cho task này)

## 1. Verified state at start
<output ngắn gọn của verify script Section 5.2 file 11, hoặc lý do skip>

## 2. Plan
1. <bước 1>
2. <bước 2>
...

## 3. Progress log (append-only, newest cuối)

### 2026-05-26T02:05Z — Step 1 done
- Edited `AI_Study_Hub_v2/Services/AuthService.cs:142` (added null check)
- `dotnet build` → 0 err, 0 warn
- Evidence: <command output / file path / commit hash>

### 2026-05-26T02:18Z — Step 2 blocked
- Symptom: `dotnet ef database update` → `relation "documents" does not exist`
- Hypothesis: migration chưa apply trên container mới
- Next try: re-run on supabase-db container

### 2026-05-26T02:30Z — Step 2 fixed
- ...

## 4. Files changed this session
| Path | Change |
|---|---|
| `AI_Study_Hub_v2/Services/AuthService.cs` | +null check L142 |
| `AI_Study_Hub_v2.Tests/AuthServiceTests.cs` | +2 tests |

## 5. Commands run (chỉ những lệnh có side-effect)
- `dotnet ef migrations add AddXyz` → OK
- `git commit -m "..."` → `abc1234`

## 6. Decisions locked
- D-2026-05-26-01: <quyết định> — confirmed by Kiệt at 02:14Z

## 7. Open questions / risks
- Q: <câu hỏi chưa trả lời>
- R: <rủi ro chưa mitigate>

## 8. Next step (nếu pause/crash now)
**Câu lệnh chính xác để resume:** `<command>` hoặc `<đọc file X rồi tiếp step Y>`

## 9. Quick Facts (snapshot)
```
Containers:    11/11 running
DB:            postgres @ localhost:5432
Backend:       <RUNNING|STOPPED> @ localhost:5240
Migrations:    <list>
Tests:         <N/M passed>
Git:           <branch> @ <short-sha>, <clean|N uncommitted>
```
```

---

## 4. Tần suất flush ra disk

- **Sau mỗi mục Section 3 (progress log entry)** → write file ngay lập tức.
- Không buffer nhiều entry rồi mới ghi. Nếu agent crash giữa entry → coi entry đó như chưa làm.
- Mỗi 30 phút wallclock, dù không có entry mới, vẫn touch file + ghi heartbeat `### <timestamp> — heartbeat (no progress)` để session sau biết khoảng thời gian dead.

---

## 5. Quy tắc đóng session

Khi user nói “đóng session” / “kết thúc” / “tạm dừng dài hạn”:

1. Set `Status: CLOSING` ở header.
2. Verify lại state cuối: chạy verify script (kiểu Section 5.2 của file 11) → paste output vào Section 1.
3. Đảm bảo Section 8 “Next step” cụ thể đến mức **paste-and-run**.
4. Rename file: `_CURRENT_SESSION.md` → `NN_Session_YYYY-MM-DD_<topic>_Handoff.md`.
   - NN = (max số thứ tự hiện có) + 1. Lúc viết rule này: next NN = **12**.
   - `<topic>` snake_case, ngắn (≤ 4 từ), ví dụ `Sprint1_D2_Smoke`.
5. Update `02_Resume_Pack.md` nếu state tổng (schema / decisions / backlog) đổi. Đừng để Resume Pack drift.
6. Commit handoff file: `git add previous_session/NN_*.md 02_Resume_Pack.md && git commit -m "docs(session): close NN <topic>"`.
   - **Không** auto-push trừ khi user yêu cầu.

---

## 6. Quy tắc mở session mới

Đầu mỗi session, **trước khi gõ lệnh code đầu tiên**:

1. Liệt kê `previous_session\` → tìm file số lớn nhất.
2. Đọc theo thứ tự: `02_Resume_Pack.md` → handoff file mới nhất → plan tương ứng.
3. Tạo `_CURRENT_SESSION.md` mới (template Section 3) trong < 60 giây.
4. Chạy verify script của handoff trước → paste output vào Section 1.
5. Nếu verify FAIL: **dừng**, báo user trước khi sửa state. Không tự ý migrate lùi / drop data.

---

## 7. Anti-patterns (cấm)

- ❌ Viết tiến độ vào chat reply rồi không ghi file (mất khi context reset).
- ❌ Sửa file handoff cũ đã đóng (history must be immutable). Sai thì viết file đính chính mới.
- ❌ “Done” mà không có evidence (command output / file diff / test result).
- ❌ Dồn 10 thay đổi rồi mới update 1 lần.
- ❌ Dùng từ mơ hồ: “gần xong”, “ổn rồi”, “hình như pass”. Phải có số / status cụ thể.
- ❌ Tạo file handoff không theo thứ tự `NN_` hoặc đặt ngoài `previous_session\`.
- ❌ Xoá `_CURRENT_SESSION.md` mà chưa rename thành handoff.

---

## 8. Crash recovery checklist (cho session N+1)

Nếu mở `_CURRENT_SESSION.md` và thấy `Status: IN_PROGRESS` (tức session trước crash):

1. Đọc Section 8 “Next step” — đó là điểm tiếp.
2. Re-verify state hiện tại có khớp Section 9 “Quick Facts” cuối cùng không.
3. Nếu lệch: ghi delta vào progress log với marker `### <ts> — POST-CRASH RECONCILE`.
4. Tiếp tục từ next step. **Không** rename file thành handoff cho tới khi session kết thúc bình thường.
5. Khi đóng, Section 3 progress log sẽ có cả phần pre-crash + post-crash → giữ nguyên, đó là đúng lịch sử.

---

## 9. Ví dụ tối thiểu của 1 entry tốt

```markdown
### 2026-05-26T03:11Z — Sprint1 D2 smoke step 4 PASS
- Action: POST `/api/documents/upload` với file `test.pdf` (2.3MB)
- Result: 200 OK, doc_id=`uuid-abc`, status=`uploading`
- Storage check: `docker exec supabase-storage ls /var/lib/storage/documents/<uuid>` → file present
- DB check: `SELECT status FROM documents WHERE id='uuid-abc'` → `uploading`
- Next: trigger ingest worker (step 5)
```

Ngắn, có evidence, có next pointer. Đủ để session sau đọc 1 entry là biết mình đang ở đâu.

---

**END.** Rule này tự nó là live document — nếu pattern thay đổi, sửa file này + ghi entry vào `_CURRENT_SESSION.md` giải thích lý do.
