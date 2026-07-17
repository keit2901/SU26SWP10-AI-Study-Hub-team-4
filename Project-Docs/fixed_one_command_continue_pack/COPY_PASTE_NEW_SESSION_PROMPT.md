# Copy-Paste Prompt for New Session

Use this as the first message in a new AI Agent session:

```text
tiếp tục SWP.

Bắt buộc làm đúng quy trình resume:
1. Đọc startup-keywords.md.
2. Đọc skill/project guide chính.
3. Đọc _CURRENT_SESSION.md nếu có.
4. Nếu không có thì đọc handoff/session mới nhất.
5. Verify repo/branch/git status bằng lệnh read-only.
6. Xác định task đang dở.
7. Chọn đúng main skill, không dùng skill tùm lum.
8. In Continue Summary.
9. Nếu bước tiếp theo an toàn thì làm tiếp luôn.
10. Không khám phá lại project từ đầu.
11. Không chỉ survey rồi dừng.
12. Không pull/merge/push/migration/reset/delete nếu chưa xin phép.

Nếu task đang dở không rõ, hãy liệt kê 2-4 hướng tiếp tục thay vì hỏi lại toàn bộ context.
```
```

For Admin Backend specifically:

```text
tiếp tục SWP phần Admin Backend.

Đọc startup/session/handoff trước, xác định Admin Backend đang dở, kiểm tra Admin UI và backend hiện có, lập checklist already exists / missing / mismatch / risk, rồi implement mục Critical/High đầu tiên nếu không cần migration hoặc breaking change. Không chỉ survey rồi dừng.
```
