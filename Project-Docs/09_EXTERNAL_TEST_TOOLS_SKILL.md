# External Test Tools Skill and Evidence Matrix

## 1. Purpose

This skill defines how an AI coding agent should plan, run, organize, and report tests using both:

```text
1. Project-native tests
2. External testing tools
```

This skill is project-agnostic.  
It can be applied to backend, frontend, full-stack, mobile, desktop, AI/RAG, dashboard, school, and local demo projects.

The purpose is not to force every project to use every tool.

The purpose is to help the team choose suitable external tools, test the right areas, and export a clear test evidence table:

```text
- Test type
- What is tested
- Tool used
- Why this tool
- Command or method
- Expected result
- Actual result
- Pass/Fail
- Evidence location
```

Use this skill when the user says:

```text
- Test the project with external tools.
- Make a full test matrix.
- Export which test used which tool.
- Test API/UI/security/performance/accessibility.
- Prepare evidence for defense/demo.
- Compare manual test, unit test, and tool-based test.
- I want a professional test report table.
```

---

## 2. Core Rule

Do not randomly add tools.

The AI Agent must choose testing tools based on:

```text
1. User goal
2. Actual project stack
3. Available local environment
4. Risk level of the feature
5. Defense/demo requirement
6. Time available
```

The AI Agent must not assume the project uses a specific stack.

Examples:

```text
- Use Newman only if Postman collections exist or API testing with Postman is suitable.
- Use Playwright/Cypress/Selenium only if UI/E2E browser testing is needed.
- Use OWASP ZAP only against local/dev apps that the team owns or has permission to test.
- Use k6/JMeter only if performance/load testing is relevant.
- Use SonarQube/SonarCloud only if static analysis setup is available or requested.
- Use Lighthouse only if web UI quality/performance/accessibility is relevant.
```

---

## 3. Relationship With Existing Files

This skill complements, not replaces:

```text
- WORKFLOW_GUIDE_PROFESSIONAL.md
- COMPREHENSIVE_TEST_GUIDE.md
- BUSINESS_RULES.md
- 01_SECURITY_DEFENSE_CHECKLIST.md
- 02_DEMO_READINESS_QUALITY_GATE.md
- 06_API_CONTRACT_COMPATIBILITY.md
- 08_AI_RAG_EVALUATION_SAFETY.md
```

Use this skill after the project’s business rules and test guide are understood.

Recommended flow:

```text
1. Read business rules.
2. Read current test guide.
3. Discover project stack and commands.
4. Select relevant external tools.
5. Build a test tool matrix.
6. Run or describe tests.
7. Export evidence table.
8. Summarize gaps and recommendations.
```

---

## 4. Testing Tool Categories

The AI Agent should classify tests into these categories.

| Category | Goal | Example Tools |
|---|---|---|
| Unit Test | Test small functions/classes/services | Project-native test framework |
| Integration Test | Test components working together | Project-native integration test framework |
| API Test | Test HTTP endpoints, request/response, auth | Postman, Newman, Insomnia |
| UI/E2E Test | Test user flow in browser/app | Playwright, Cypress, Selenium |
| Accessibility Test | Check accessibility issues | Lighthouse, axe DevTools |
| Performance / Load Test | Check response time and behavior under load | k6, JMeter |
| Static Code Analysis | Find bugs, smells, maintainability issues | SonarQube, language analyzers |
| Dependency / SCA Test | Check vulnerable dependencies | OWASP Dependency-Check, npm audit, pip-audit |
| Secret Scan | Find exposed secrets/tokens/passwords | Gitleaks, TruffleHog |
| Security Dynamic Scan | Scan running local app for common web issues | OWASP ZAP |
| Database Test | Check migration/schema/query behavior | Project ORM migration tools, DB scripts |
| AI/RAG Evaluation | Test retrieval, grounding, citation, hallucination | Manual eval set, RAGAS, DeepEval, custom scripts |
| Smoke Test | Fast check that main flows work | Manual checklist, Playwright, Postman |
| Regression Test | Ensure old features still work | Existing tests + selected E2E/API cases |

---

## 5. Tool Selection Matrix

Before running tests, create this table.

| Area | Risk | Suggested Tool | Required? | Reason | Setup Needed |
|---|---|---|---:|---|---|
| API endpoints | High | Postman/Newman | Yes/No | Validate request/response/auth | Collection/env |
| UI demo flow | High | Playwright/Cypress/Selenium | Yes/No | Validate browser flow | Test script |
| Security baseline | Medium/High | OWASP ZAP | Yes/No | Passive local scan | Running app |
| Secrets | High | Gitleaks | Yes/No | Prevent secret exposure | CLI |
| Dependencies | Medium | OWASP Dependency-Check / package audit | Yes/No | Find vulnerable deps | CLI/tool |
| Code quality | Medium | SonarQube / built-in analyzers | Yes/No | Maintainability/static issues | Scanner/server |
| Performance | Medium | k6/JMeter | Yes/No | Basic load check | Script |
| Accessibility | Medium | Lighthouse/axe | Yes/No | Web UI quality | Browser/CLI |
| AI/RAG quality | High if AI feature | RAG eval set/custom script | Yes/No | Check grounding/retrieval | Dataset |

---

## 6. Minimum Recommended External Tool Set for Local Defense

For a local academic defense project, do not overdo it.

Recommended minimum:

```text
1. API tool: Postman or Newman
2. Browser/UI tool: Playwright or manual browser checklist
3. Security baseline: Gitleaks for secrets
4. Web quality: Lighthouse if project has web UI
5. Optional security scan: OWASP ZAP baseline scan against local app
6. Optional performance: k6 small smoke-load test
```

If the project has AI/RAG features, add:

```text
7. AI/RAG evaluation table with fixed questions and expected behavior
```

---

## 7. Full Test Evidence Matrix

The final output must include a table like this.

| ID | Test Type | Feature/Area | Scenario | Tool | Command/Method | Expected Result | Actual Result | Status | Evidence |
|---|---|---|---|---|---|---|---|---|---|
| T-API-001 | API | Login | Valid login | Postman/Newman | `newman run ...` | 200 + token/session |  | Not Run |  |
| T-UI-001 | UI/E2E | Upload document | Upload valid file | Playwright | `npx playwright test` | Upload succeeds |  | Not Run |  |
| T-SEC-001 | Secret Scan | Repo | Scan committed files | Gitleaks | `gitleaks detect` | No secrets |  | Not Run |  |
| T-ZAP-001 | Security Scan | Local web app | Passive scan | OWASP ZAP | `zap-baseline.py` | No high-risk issues |  | Not Run |  |
| T-PERF-001 | Performance | API | 10 users for 30s | k6 | `k6 run ...` | p95 under threshold |  | Not Run |  |
| T-A11Y-001 | Accessibility | Home page | Lighthouse audit | Lighthouse | `lhci autorun` | Meets target score |  | Not Run |  |
| T-RAG-001 | AI/RAG | Document QA | Ask known question | Custom eval/manual | Eval table | Grounded answer |  | Not Run |  |

Status values:

```text
PASS
FAIL
PARTIAL
BLOCKED
NOT RUN
NOT APPLICABLE
```

---

## 8. Test Result Summary Table

After testing, produce this summary.

| Test Category | Tool | Total | Pass | Fail | Partial | Blocked | Not Run | Notes |
|---|---|---:|---:|---:|---:|---:|---:|---|
| Unit | Project-native |  |  |  |  |  |  |  |
| API | Postman/Newman |  |  |  |  |  |  |  |
| UI/E2E | Playwright/Cypress/Selenium |  |  |  |  |  |  |  |
| Security | Gitleaks/ZAP |  |  |  |  |  |  |  |
| Performance | k6/JMeter |  |  |  |  |  |  |  |
| Accessibility | Lighthouse/axe |  |  |  |  |  |  |  |
| AI/RAG | Custom/RAGAS/DeepEval/manual |  |  |  |  |  |  |  |

---

## 9. Evidence Folder Structure

If the project needs exported test evidence, use this structure.

```text
test-evidence/
├── README.md
├── summary/
│   └── test-summary.md
├── api/
│   ├── postman-collection.json
│   ├── postman-environment.json
│   └── newman-report.html
├── ui-e2e/
│   ├── playwright-report/
│   └── screenshots/
├── security/
│   ├── gitleaks-report.json
│   └── zap-report.html
├── performance/
│   └── k6-summary.json
├── accessibility/
│   └── lighthouse-report.html
├── rag-eval/
│   └── rag-evaluation-results.md
└── logs/
    └── app-test-log.txt
```

Do not commit sensitive reports if they contain tokens, private URLs, or secrets.

---

## 10. API Testing With Postman / Newman

Use when:

```text
- The project has REST/HTTP APIs.
- The team wants repeatable API tests.
- The project already has Postman collections.
- UI-backend contract must be verified.
```

Checklist:

```text
- Collection exists or is created.
- Environment variables use placeholders.
- Auth token/session handling is safe.
- Success and failure cases included.
- Newman report exported if needed.
```

Example command:

```bash
newman run <collection.json> -e <environment.json> --reporters cli,html --reporter-html-export test-evidence/api/newman-report.html
```

API test cases should include:

```text
- Success
- Missing input
- Invalid input
- Unauthenticated
- Wrong role
- Not found
- Duplicate/conflict
```

---

## 11. UI/E2E Testing With Browser Tools

Use when:

```text
- The project has important UI flows.
- Manual demo flow must be repeatable.
- The team wants screenshots/videos as evidence.
```

Possible tools:

```text
- Playwright
- Cypress
- Selenium
```

Recommended for local defense:

```text
Playwright if starting fresh, because it supports modern browser automation and multi-language usage.
Use the tool already present in the project if one exists.
```

Example test scenarios:

```text
- User login
- Admin login
- Upload document
- Search/filter table
- Submit form
- Role access denied
- Main demo flow
```

Example command:

```bash
npx playwright test --reporter=html
```

Evidence:

```text
- HTML report
- Screenshots
- Trace/video if enabled
```

---

## 12. Security Testing With External Tools

## 12.1. Secret Scanning

Use:

```text
- Gitleaks
- TruffleHog
- GitHub secret scanning if available
```

Example command:

```bash
gitleaks detect --source . --report-format json --report-path test-evidence/security/gitleaks-report.json
```

Expected:

```text
No real passwords, API keys, tokens, private keys, or service role secrets committed.
```

## 12.2. OWASP ZAP Baseline Scan

Use only against:

```text
- Local app
- Dev/test environment
- Systems the team owns or has permission to test
```

Do not use active scan against third-party or production targets without permission.

Example baseline command:

```bash
docker run --rm -t owasp/zap2docker-stable zap-baseline.py -t <LOCAL_APP_URL> -r zap-report.html
```

Expected for local defense:

```text
- No High risk issue
- Medium/Low issues documented
- False positives explained
```

---

## 13. Performance Testing With k6 or JMeter

Use when:

```text
- API response time matters.
- The feature may be slow.
- The committee may ask about performance.
- The team wants simple load evidence.
```

For local defense, keep it small:

```text
- 5 to 20 virtual users
- 30 to 60 seconds
- Avoid stressing local machine too much
```

Example k6 command:

```bash
k6 run test-evidence/performance/basic-load-test.js
```

Recommended metrics:

```text
- http_req_duration average
- http_req_duration p95
- http_req_failed
- checks pass rate
```

Do not claim production scalability from local laptop load tests.

Say:

```text
This is a local smoke-load test, not a production benchmark.
```

---

## 14. Accessibility and Web Quality Testing

Use when the project has web UI.

Possible tools:

```text
- Lighthouse
- Lighthouse CI
- axe DevTools
```

Check:

```text
- Accessibility
- Performance
- Best practices
- SEO if relevant
```

For academic/local project, report:

```text
- Page tested
- Score
- Major issues
- Screenshot/report path
- Fix recommendation
```

Example Lighthouse CI command:

```bash
lhci autorun
```

---

## 15. Static Code Analysis and Dependency Testing

Use when:

```text
- Code quality matters.
- The project has many files.
- The team wants maintainability evidence.
```

Possible tools:

```text
- SonarQube / SonarCloud
- Language-native analyzers
- ESLint / TypeScript checks
- dotnet analyzers
- Checkstyle / SpotBugs / PMD
```

Dependency/SCA tools:

```text
- OWASP Dependency-Check
- npm audit
- pip-audit
- dotnet list package --vulnerable
- Maven/Gradle dependency audit plugins
```

Report:

```text
- Tool
- Command
- Number of issues
- Severity
- Which issues must be fixed before defense
- Which issues are acceptable for local demo
```

---

## 16. AI/RAG Evaluation With Tools or Structured Manual Tests

Use when the system includes AI/RAG.

Possible approaches:

```text
- Manual evaluation table
- Custom script
- RAGAS
- DeepEval
- Golden question set
```

Minimum required for defense:

```text
- 5 to 10 fixed questions
- Expected behavior
- Retrieved source/chunk if available
- Actual answer
- Result: PASS / FAIL / PARTIAL
```

Test at least:

```text
- Answer found in document
- Answer requiring multiple sections
- Question not found in document
- Ambiguous question
- Failure case when AI/embedding service is unavailable
```

Do not claim the AI is always correct.

Say:

```text
The evaluation checks whether the system is grounded on provided documents for selected test cases.
```

---

## 17. Test Planning Workflow for AI Agent

When asked to test with external tools, the AI Agent must follow this workflow.

```text
1. Discover project stack and available commands.
2. Read existing test guide and business rules.
3. Identify critical features and risk areas.
4. Select suitable external tools.
5. Explain why each tool is selected.
6. Create the test evidence matrix.
7. Run tests only if the environment supports it.
8. Export or describe report locations.
9. Summarize pass/fail/blocked/not-run.
10. Recommend what to fix first.
```

Do not run destructive or aggressive tests.

Do not use active security scanning without permission.

Do not send private/local data to external cloud tools unless user approves.

---

## 18. External Test Report Template

Use this template as final output.

```markdown
# External Test Tools Report

## 1. Scope

Project:
Branch:
Commit:
Environment:
Date:
Tester:
App URL:
Test data/account:
Tools used:

## 2. Tool Selection

| Test Area | Tool | Reason | Required? | Status |
|---|---|---|---:|---|

## 3. Test Evidence Matrix

| ID | Test Type | Feature/Area | Scenario | Tool | Command/Method | Expected | Actual | Status | Evidence |
|---|---|---|---|---|---|---|---|---|---|

## 4. Summary

| Category | Tool | Total | Pass | Fail | Partial | Blocked | Not Run | Notes |
|---|---|---:|---:|---:|---:|---:|---:|---|

## 5. Issues Found

| ID | Severity | Area | Issue | Evidence | Recommended Fix |
|---|---|---|---|---|---|

## 6. Defense Readiness

Status:
Reason:
Must fix before defense:
Can improve later:
```

---

## 19. Common Mistakes to Avoid

Do not:

```text
- Use every tool just to look professional.
- Run scans against systems you do not own.
- Run heavy load tests on local machine and call it production performance.
- Commit reports containing tokens or passwords.
- Claim external tool tests passed without evidence.
- Ignore project-native tests.
- Replace business-rule tests with only generic tool scans.
- Ignore manual demo flow.
```

---

## 20. Final Rule

The final test report must answer:

```text
1. What was tested?
2. Which tool tested it?
3. Why was that tool chosen?
4. How was the test run?
5. What was expected?
6. What actually happened?
7. Did it pass?
8. Where is the evidence?
9. What should be fixed first?
```

If the report does not answer these questions, it is incomplete.
