# _CURRENT_SESSION — Codex AI Box setup

**Started:** 2026-06-29T03:53:29Z
**Agent:** Codex
**Goal:** Configure the installed Codex CLI to use API AI Box according to `https://api.ai-box.vn/docs`, without exposing or storing API keys in the repository.
**Status:** PAUSED

---

## 0. Context loaded
- [x] User-provided `AGENTS.md` instructions
- [x] `previous_session/rule.md`
- [x] `previous_session/handoff_backend_2026-06-20.md` (newest backend handoff in this workspace)
- [x] Existing root `_CURRENT_SESSION.md` checked; it belongs to an older layout task, so this topic-specific log is used
- [ ] `previous_session/skill.md` (not present in this workspace)

## 1. Verified state at start
- Codex CLI: `0.142.3`
- Global config: `C:\Users\T540p\.codex\config.toml`
- Existing default model: `gpt-5.5`; no AI Box provider/profile configured
- AI Box docs require a local CodeProxy endpoint at `http://127.0.0.1:8787/v1` and profiles for `deepseek-v4-pro` / `deepseek-v4-flash`

## 2. Plan
1. Verify current Codex custom-provider syntax against the current official Codex manual.
2. Add the AI Box provider and profiles without overwriting existing settings.
3. Verify Codex parses both profiles and document the API-key startup step securely.

## 3. Progress log (append-only, newest last)

### 2026-06-29T03:53:29Z — Context and documentation verified
- AI Box Codex CLI configuration section inspected in the JavaScript-rendered docs.
- Official Codex manual refreshed and checked for `model_providers`, `wire_api`, and profile behavior.
- No source-code changes made.

### 2026-06-29T03:55:54Z — AI Box profiles added
- Backed up the global Codex config to `C:\Users\T540p\.codex\config.toml.aibox-backup-20260629T035554Z`.
- Appended the `deepseek` provider and the `deepseek-pro` / `deepseek-flash` profiles exactly as required by the AI Box config-file instructions.
- Existing global Codex settings and default `gpt-5.5` selection were preserved.
- Attempt to inspect `@codeproxy/cli` was denied by the execution safety review because it would download and execute third-party npm code; no package code was run.

### 2026-06-29T03:57:38Z — AI Box profile syntax found outdated
- Verification with Codex 0.142.3 failed: `[profiles.deepseek-pro]` and `[profiles.deepseek-flash]` are legacy syntax removed in Codex 0.134.0.
- Official current syntax confirmed: keep the provider in global `config.toml`, move each profile's top-level overrides into `$CODEX_HOME/<profile>.config.toml`.
- Next: migrate the two profile tables into separate files, then rerun parse verification.

### 2026-06-29T03:59:33Z — Current Codex profile layout verified
- Migrated the legacy tables into `deepseek-pro.config.toml` and `deepseek-flash.config.toml`.
- Preserved the shared `[model_providers.deepseek]` block in global `config.toml`.
- `codex --profile deepseek-pro mcp list` → exit 0.
- `codex --profile deepseek-flash mcp list` → exit 0.
- A preliminary verification using `--strict-config` was rejected because that flag is not supported by `codex mcp`; reran with the supported command successfully.

### 2026-06-29T04:00:18Z — Paused before third-party proxy execution
- Explained that Codex can use DeepSeek through AI Box as its model backend, while direct agent-to-agent collaboration requires a separate orchestrator.
- Profile setup is complete. Live inference remains paused because executing npm package `@codeproxy/cli` requires explicit user approval and a locally entered AI Box API key.

### 2026-06-29T04:03:23Z — Third-party proxy execution authorized
- User explicitly approved running npm package `@codeproxy/cli`.
- Next: inspect its supported authentication inputs, then start the local proxy without logging the API key.

### 2026-06-29T04:23:03Z — Direction changed to OpenCode default model
- User changed the active target from launching the Codex proxy to using DeepSeek V4 Pro as OpenCode's default model.
- Backed up global OpenCode config to `C:\Users\T540p\.config\opencode\opencode.json.aibox-backup-20260629T042206Z`.
- Added custom provider `aibox` using `@ai-sdk/openai-compatible` and `https://api.ai-box.vn/v1`.
- Set both `model` and `small_model` to `aibox/deepseek-v4-pro`, so primary agents, inherited subagents, and lightweight tasks use the requested model unless an agent/project/CLI override is present.
- JSON parse verification passed; `opencode models aibox` returned `aibox/deepseek-v4-pro` with exit 0.
- No `AIBOX_API_KEY` is currently set at user or process scope; live API inference remains untested.

### 2026-06-29T04:24:21Z — API key kept out of OpenCode config
- Removed the environment-key reference from `opencode.json`; OpenCode's credential store via `/connect` will be used instead.
- Final JSON parse passed and `opencode models aibox` again returned `aibox/deepseek-v4-pro` with exit 0.

### 2026-06-29T04:45:14Z — Codex-to-AI-Box route packaged and smoke-tested
- User returned the active target to Codex and requested the AI Box route from the vendor docs.
- Verified existing current-format Codex provider/profile configuration for `deepseek-v4-pro` and local Responses endpoint `127.0.0.1:8787/v1`.
- Installed pinned `@codeproxy/cli@0.2.9` under `C:\Users\T540p\.codex\aibox-proxy` (outside the repository).
- Added a Node launcher that translates Codex Responses API calls to AI Box OpenAI Chat Completions at `https://api.ai-box.vn/v1`.
- Added `C:\Users\T540p\.codex\use-aibox-codex.ps1`: securely prompts for the key, keeps it out of config/command-line arguments, starts the proxy hidden, opens Codex with `--profile deepseek-pro`, and stops/cleans the proxy on exit.
- JavaScript syntax check passed; PowerShell parser check passed; Codex profile parse passed.
- Proxy lifecycle smoke test with a non-secret dummy value opened port 8787 successfully, then stopped the exact test PID and verified the port was clean.
- Live upstream inference remains untested because no AI Box API key is available to the session.

### 2026-06-29T04:51:10Z — PRE-FLIGHT: rollback Codex AI Box route
- User explicitly requested rollback and intends to continue with OpenCode later.
- Scope locked: remove only Codex AI Box provider/profile/launcher/package artifacts; preserve all unrelated Codex settings and preserve the existing OpenCode AI Box configuration.
- Verified port 8787 is not listening before deletion.
- Exact targets verified under `C:\Users\T540p\.codex`: `deepseek-pro.config.toml`, `deepseek-flash.config.toml`, `use-aibox-codex.ps1`, `aibox-proxy`, and the two AI Box backup files created by this session.

### 2026-06-29T04:52:09Z — Codex AI Box rollback complete
- Removed `[model_providers.deepseek]` from the global Codex config without replacing or reverting unrelated settings.
- Removed the two DeepSeek profile files, the secure launcher, the pinned CodeProxy installation, and both Codex AI Box backup artifacts created by this session.
- Verification: DeepSeek provider absent; profile/launcher/package paths absent; port 8787 not listening.
- `codex mcp list` → exit 0, confirming the remaining global Codex config parses successfully.
- Codex default remains `gpt-5.5`.
- OpenCode configuration was intentionally preserved; its default remains `aibox/deepseek-v4-pro` for the user's later OpenCode work.

### 2026-06-29T08:32:28Z — OpenCode builder/reviewer agents configured
- User resumed with OpenCode as the target: DeepSeek V4 Pro as the primary builder and Codex/GPT 5.5 as the reviewer.
- Backed up global OpenCode config to `C:\Users\T540p\.config\opencode\opencode.json.agents-backup-20260629T083111Z`.
- Configured built-in `build` as a primary agent using `aibox/deepseek-v4-pro`, with edit/bash access and permission to invoke only `reviewer` through the Task tool.
- Configured `reviewer` as a read-only subagent using `shineshop/codex/gpt5.5`; edits, nested tasks, external directories, and web access are denied, while safe git inspection and .NET build/test commands are allowed.
- Builder prompt requires invoking the reviewer after meaningful code changes and addressing critical findings before final response.
- JSON parsing passed; `opencode models aibox` and `opencode models shineshop` exposed both selected models; `opencode agent list` recognized `build (primary)` and `reviewer (subagent)` with the intended permissions.
- `opencode auth list` reports zero stored credentials. AI Box inference is pending a new key entered locally via `/connect`; the key previously posted in chat was not used.

### 2026-06-29T09:08:19Z — OpenCode startup failure fixed
- Symptom: 4/5 desktop startup requests failed (`config.providers`, `provider.list`, `app.agents`, `config.get`).
- Root cause from OpenCode logs: `opencode.json` had a missing comma between the AI Box `baseURL` and `apiKey` properties (`CommaExpected` at line 111).
- Fixed only the missing comma; provider, key value, models, and agent definitions were otherwise preserved.
- Verification: PowerShell JSON parse passed; `opencode agent list` returned `build (primary)` and `reviewer (subagent)`; `opencode models aibox` returned `aibox/deepseek-v4-pro`; `opencode debug startup` exited 0.

### 2026-06-29T09:12:31Z — Recurrent JSON mutation normalized and TUI verified
- User reproduced the same four-request startup failure when running `opencode` from `C:\Users\T540p`.
- Latest log showed `aibox.options` had been modified again after the previous successful verification: missing comma after `baseURL` plus a trailing comma on the `apiKey` property.
- Normalized only the AI Box options object to strict JSON while preserving the existing key value.
- Verification from the exact HOME working directory: strict JSON parse passed, `opencode debug config` passed, `opencode agent list` returned both configured agents.
- Launched the real OpenCode TUI from HOME in a PTY; startup completed and displayed `Build · DeepSeek V4 Pro · API AI BOX`. Test TUI was then exited; no startup error remained.

### 2026-06-29T09:19:34Z — OpenCode upgraded to 1.17.11
- User reported the TUI updater could not update OpenCode 1.15.10.
- Root cause: OpenCode was installed via npm, but neither `npm` nor `node` was present in the user's PATH, so both the built-in updater and the package postinstall initially failed.
- Closed only the verified OpenCode executable process that was locking the package directory.
- Used the Node/npm runtime bundled with Codex, temporarily prepended it to PATH for postinstall, and upgraded the existing npm prefix to `opencode-ai@1.17.11`.
- A transient SQLite `ALTER TABLE session ADD metadata` error occurred when parallel verification processes raced the first-run migration; a sequential rerun passed.
- Final verification: version `1.17.11`, resolved config pass, `build (primary)` and `reviewer (subagent)` present, AI Box model present, real TUI launched from HOME showing `Build · DeepSeek V4 Pro · API AI BOX` and version `1.17.11` with no update prompt.

## 4. Files changed this session
| Path | Change |
|---|---|
| `previous_session/_CURRENT_SESSION_CODEX_AIBOX.md` | Created topic-specific live session log |
| `C:\Users\T540p\.codex\config.toml` | Added AI Box provider and two profiles |
| `C:\Users\T540p\.codex\config.toml.aibox-backup-20260629T035554Z` | Backup created before config edit |
| `C:\Users\T540p\.codex\deepseek-pro.config.toml` | Added current-format DeepSeek V4 Pro profile |
| `C:\Users\T540p\.codex\deepseek-flash.config.toml` | Added current-format DeepSeek V4 Flash profile |
| `C:\Users\T540p\.codex\config.toml.aibox-legacy-20260629T035836Z` | Backup of the legacy AI Box profile layout |
| `C:\Users\T540p\.config\opencode\opencode.json` | Added AI Box provider; set main and small defaults to DeepSeek V4 Pro |
| `C:\Users\T540p\.config\opencode\opencode.json.aibox-backup-20260629T042206Z` | Backup before OpenCode config edit |
| `C:\Users\T540p\.codex\aibox-proxy\` | Pinned local CodeProxy 0.2.9 installation and Node launcher |
| `C:\Users\T540p\.codex\use-aibox-codex.ps1` | One-command secure AI Box proxy + Codex profile launcher |
| `C:\Users\T540p\.config\opencode\opencode.json` | Added DeepSeek builder and read-only Codex reviewer agents |
| `C:\Users\T540p\.config\opencode\opencode.json.agents-backup-20260629T083111Z` | Backup before agent configuration |

## 5. Commands run (side effects only)
- Refreshed the official Codex manual cache in the OS temp directory.
- Backed up and appended AI Box blocks to the global Codex config.
- Migrated legacy profile tables to two current-format profile files.
- Backed up and updated global OpenCode configuration.
- Installed pinned CodeProxy package outside the repo and smoke-tested the local route lifecycle.

## 6. Decisions locked
- Preserve all existing global Codex settings and append only the AI Box provider/profile blocks.
- Never place the AI Box API key in the repository or session log.

## 7. Open questions / risks
- AI Box API key is not yet available to this session; the proxy cannot be started until the user supplies it locally.
- The AI Box proxy must be running on port 8787 whenever an AI Box profile is used.
- AI Box docs currently show pre-0.134.0 profile syntax; local config must use the newer official Codex layout.

## 8. Next step (if paused/crashed now)
Open OpenCode, run `/connect`, choose `Other`, use provider id `aibox`, and enter a newly rotated AI Box key. Then smoke-test the `build` agent and manually invoke `@reviewer` once.

## 9. Quick Facts (snapshot)
```
Codex CLI:     0.142.3
AI Box config: rolled back from Codex
Codex default: gpt-5.5
Proxy:         removed; port 8787 clean
OpenCode:      1.15.10; aibox/deepseek-v4-pro is main + small default
OpenCode auth: AIBOX_API_KEY not set; inference not tested
CodeProxy:     removed from Codex
```
