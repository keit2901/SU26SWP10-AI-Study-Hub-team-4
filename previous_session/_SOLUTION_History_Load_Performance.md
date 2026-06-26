# Solution Research: Chat History Load Performance (24s + 1-2s Server Freeze)

## Root Cause Analysis

The 24s load with 1-2s server freeze is caused by **four stacked bottlenecks** that trigger sequentially when a workspace is opened:

### Bottleneck 1: Full-List JSON Deserialization from ProtectedSessionStorage

```
AiChatSessionState.History (List<AiChatHistoryExchange>)
  └─ AiChatHistoryExchange
       ├─ Question (string)
       ├─ ScopeLabel (string)
       ├─ AskedAt (DateTimeOffset)
       └─ Response (AiChatAnswerResponse)
            ├─ Answer (string) — can be 500-5000+ chars each
            ├─ RefusalReason (string?)
            ├─ DurationMs (long?)
            └─ Sources (IReadOnlyList<AiChatSourceDto>)
                 └─ AiChatSourceDto
                      ├─ Label, FileName, Excerpt (string) — Excerpt can be 500 chars
                      ├─ DocumentId, ChunkIndex, PageNumber, Score
```

- `ProtectedSessionStorage` applies **data protection (encrypt/decrypt)** on top of `sessionStorage` — more CPU than raw JSON
- 50 exchanges × ~3KB per exchange = ~150KB encrypted blob → deserialize on every workspace open
- Single-threaded on Blazor Server's UI thread → blocks all other circuit work → **visible freeze**

### Bottleneck 2: Full Exchange List Render (`@foreach` loop)

```razor
@foreach (var (exchange, idx) in ChatSession.History.Select((e, i) => (e, i)))
{
    @RenderExchange(exchange, idx)
}
```

- Every historical exchange is rendered into the component tree, regardless of viewport visibility
- `RenderExchange` builds a complex RenderFragment for each exchange:
  - User bubble + AI bubble
  - AI toolbar (copy button, copied state check via `_copiedIndexes.Contains(index)`)
  - Citation badges + popup tooltip for every source
  - `FormatAnswerMarkup` called per exchange (see Bottleneck 3)

### Bottleneck 3: `FormatAnswerMarkup` — 6 Regex Passes Per Answer

```csharp
// Called for each exchange on every render
foreach (exchange in History)
    FormatAnswerMarkup(exchange.Response.Answer)
    // Regex #1: \*\*(.+?)\*\*  → bold
    // Regex #2: \*(.+?)\*      → italic
    // Regex #3: `(.+?)`        → code
    // Regex #4: ^(\d+)\.\s+(.+)$ → numbered list
    // Regex #5: ^[-•]\s+(.+)$  → bullet list
    // Regex #6: \r\n|\n        → <br />
```

- 6 regex passes per answer × N exchanges = 6N regex calls
- Each regex compiles at runtime (no `RegexOptions.Compiled`)
- With 50 exchanges, that's **300 regex evaluations** on first render
- These are on the server's UI thread → contributes to the freeze

### Bottleneck 4: SignalR Diff Serialization

After rendering, Blazor Server computes the HTML diff and serializes it over SignalR. The full rendered tree (50+ exchanges with citations) produces a large diff payload, extending the time before the user sees a responsive page.

---

## Solution Options (Ordered by Impact)

### Solution A: Lazy-Load History (Paginated)

**Problem**: Loading ALL history entries at once.

**Fix**: Restore only the last N exchanges (e.g., 20). Add "Load earlier messages" button or infinite scroll.

**Implementation sketch**:
```csharp
private const int MaxVisibleExchanges = 20;
private List<AiChatHistoryExchange> _visibleHistory = new();

private async Task LoadHistoryAsync()
{
    var stored = await SessionStorage.GetAsync<List<AiChatHistoryExchange>>(HistoryStorageKey);
    if (stored.Success && stored.Value?.Count > 0)
    {
        ChatSession.Replace(stored.Value);
        _visibleHistory = stored.Value.TakeLast(MaxVisibleExchanges).ToList();
        _historyOffset = Math.Max(0, stored.Value.Count - MaxVisibleExchanges);
        StateHasChanged();
    }
}

private async Task LoadEarlierAsync()
{
    var earlier = ChatSession.History
        .Skip(_historyOffset - MaxVisibleExchanges)
        .Take(MaxVisibleExchanges)
        .ToList();
    _visibleHistory.InsertRange(0, earlier);
    _historyOffset -= earlier.Count;
    StateHasChanged();
}
```

**Impact**: Reduces render tree by 60-80% (20 vs 50+ exchanges). Eliminates freeze.
**Cost**: ~1 hour to implement.
**Drawback**: User must click "Load earlier" to see older messages.

---

### Solution B: Virtualize Exchange List (`<Virtualize>`)

**Problem**: All exchanges rendered into the component tree regardless of visibility.

**Fix**: Use Blazor's built-in `Virtualize` component to render only items in the viewport.

**Implementation sketch**:
```razor
<Virtualize Items="@ChatSession.History.Reverse()" Context="exchange"
            ItemSize="180" OverscanCount="3">
    <div class="workspace-exchange">
        @RenderExchange(exchange, exchange.Index)
    </div>
</Virtualize>
```

**Impact**: Only ~10 items rendered instead of 50+. DOM tree shrinks 80%.
**Cost**: ~30 minutes.
**Drawback**: Exchange rendering must use a fixed `ItemSize` heuristic (or `Virtualize` with `ItemSizeProvider` for variable height). Scroll position management needed.

---

### Solution C: Cache `FormatAnswerMarkup` Output

**Problem**: 6 regex passes per answer on every render.

**Fix**: Cache formatted output per exchange and only recompute when the exchange changes.

**Implementation sketch**:
```csharp
private readonly Dictionary<int, string> _formattedAnswerCache = new();

private string GetFormattedAnswer(AiChatHistoryExchange exchange, int index)
{
    if (!_formattedAnswerCache.TryGetValue(index, out var formatted))
    {
        formatted = FormatAnswerMarkup(exchange.Response.Answer);
        _formattedAnswerCache[index] = formatted;
    }
    return formatted;
}
```

Clear cache when history changes (new exchange added, history cleared).

Additionally: use `RegexOptions.Compiled` on the static regex patterns for ~5-10x per-call speedup.

**Impact**: Eliminates 6N regex calls on every render. First render still pays cost, but subsequent renders (navigation away-and-back) are instant.
**Cost**: ~15 minutes.
**Drawback**: Cache keyed by index — needs cache invalidation when new items are added.

---

### Solution D: Store Exchanges Individually Instead of One Blob

**Problem**: Single `sessionStorage` key with giant encrypted JSON blob — slow deserialize.

**Fix**: Store each exchange as an individual `sessionStorage` key. Load only the most recent N on workspace open.

**Implementation sketch**:
```csharp
private const int MaxStoredExchanges = 100;
private const int MaxLoadedExchanges = 20;

private async Task SaveHistoryAsync()
{
    // Store each exchange individually, keyed by index
    var count = ChatSession.History.Count;
    var startIndex = Math.Max(0, count - MaxStoredExchanges);
    for (var i = startIndex; i < count; i++)
    {
        var key = $"{HistoryStorageKey}_msg_{i}";
        await SessionStorage.SetAsync(key, ChatSession.History[i]);
    }
    await SessionStorage.SetAsync($"{HistoryStorageKey}_meta", new { Count = count, StartIndex = startIndex });
}

private async Task LoadHistoryAsync()
{
    var meta = await SessionStorage.GetAsync<JsonElement>($"{HistoryStorageKey}_meta");
    if (!meta.Success) return;
    var totalCount = meta.Value.GetProperty("Count").GetInt32();
    var startIndex = meta.Value.GetProperty("StartIndex").GetInt32();
    var loadFrom = Math.Max(startIndex, totalCount - MaxLoadedExchanges);
    
    var exchanges = new List<AiChatHistoryExchange>();
    for (var i = loadFrom; i < totalCount; i++)
    {
        var stored = await SessionStorage.GetAsync<AiChatHistoryExchange>($"{HistoryStorageKey}_msg_{i}");
        if (stored.Success) exchanges.Add(stored.Value!);
    }
    ChatSession.Replace(exchanges);
}
```

**Impact**: Loads only 20 individual small items instead of 1 giant 150KB blob. No massive JSON deserialization.
**Cost**: ~2 hours (rewrite storage pattern).
**Drawback**: More complex storage code. Multiple `sessionStorage` keys per workspace.

---

### Solution E: Lazy-Render Exchange Content with Show More

**Problem**: Full answer Markdown rendering + citation DOM for every exchange.

**Fix**: Show answer as truncated plain text by default. "Show full answer" button to expand.

**Implementation sketch**:
```razor
@{
    var showFull = _expandedExchanges.Contains(index);
}
<div class="ai-response-body">
    @if (showFull)
    {
        @((MarkupString)GetFormattedAnswer(exchange, index))
    }
    else if (exchange.Response.Answer.Length > 300)
    {
        <div>@exchange.Response.Answer[..300]...</div>
        <button @onclick="() => ExpandExchange(index)">Show full answer</button>
    }
    else
    {
        @((MarkupString)GetFormattedAnswer(exchange, index))
    }
</div>
```

**Impact**: ~80% reduction in rendered content per exchange. Regex only runs on expanded exchanges.
**Cost**: ~1 hour.
**Drawback**: Extra user click to see full answer. Adds UI complexity.

---

### Solution F: Defer `StateHasChanged` After History Restore (Quick Win)

**Problem**: `StateHasChanged()` is called immediately, forcing synchronous render of all exchanges before the user sees anything.

**Fix**: Delay `StateHasChanged()` to the next animation frame, letting the UI thread breathe first.

**Implementation sketch**:
```csharp
private async Task LoadHistoryAsync()
{
    // ... load from storage ...
    await Task.Delay(1); // Yield to UI thread
    StateHasChanged();
}
```

**Impact**: Minor — doesn't reduce total work, but avoids the 1-2s complete freeze by letting the SignalR keepalive through.
**Cost**: 5 minutes.
**Drawback**: Doesn't actually fix the root cause; just masks the freeze slightly.

---

### Solution G: Periodically Prune Old History

**Problem**: History grows unboundedly — every new QA pair adds ~3KB.

**Fix**: Prune history when it exceeds MAX_EXCHANGES (e.g., 100). Keep oldest ones as truncated "summary" entries.

**Implementation sketch**:
```csharp
private const int MaxExchanges = 100;

private async Task SaveHistoryAsync()
{
    if (ChatSession.History.Count > MaxExchanges)
    {
        // Keep only the last MaxExchanges
        var pruned = ChatSession.History
            .TakeLast(MaxExchanges)
            .ToList();
        ChatSession.Replace(pruned);
    }
    var json = JsonSerializer.Serialize(ChatSession.History.ToList());
    await SessionStorage.SetAsync(HistoryStorageKey, json);
}
```

**Impact**: Caps storage/deserialization cost at 100 exchanges.
**Cost**: 10 minutes.
**Drawback**: Old history is lost (hard cutoff, not graceful degradation).

---

## Recommended Implementation Order

| Priority | Solution | Time | Impact | Complexity |
|----------|----------|------|--------|------------|
| 1 | **G: Prune old history** (cap at 50-100) | 10 min | High | Trivial |
| 2 | **C: Cache FormatAnswerMarkup** | 15 min | High | Low |
| 3 | **A: Lazy-load (last 20 exchanges)** | 1 hr | Very High | Medium |
| 4 | **B: Virtualize exchange list** | 30 min | High | Medium |
| 5 | **E: Lazy-render exchange content** | 1 hr | Medium | Medium |
| 6 | **D: Individual exchange keys** | 2 hr | High | High |
| 7 | **F: Defer StateHasChanged** | 5 min | Low | Trivial |

**Recommended first pass** (30 min total, ~80% improvement):
1. Cap history at 50 exchanges (Solution G)
2. Cache formatted answers (Solution C)
3. Virtualize the exchange list (Solution B)

The 24s → expected ~2-3s after these three changes.
