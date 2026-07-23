window.idleSessionMonitor = (() => {
    const events = [
        "mousemove",
        "mousedown",
        "keydown",
        "scroll",
        "touchstart",
        "click",
        "focus"
    ];

    let timeoutId = null;
    let timeoutMs = 15 * 60 * 1000;
    let dotNetRef = null;
    let started = false;

    const resetTimer = () => {
        if (!started) {
            return;
        }

        if (timeoutId) {
            clearTimeout(timeoutId);
        }

        timeoutId = window.setTimeout(async () => {
            const callbackRef = dotNetRef;
            stop();

            if (!callbackRef) {
                return;
            }

            try {
                await callbackRef.invokeMethodAsync("HandleIdleTimeout");
            } catch {
            }
        }, timeoutMs);
    };

    const handleActivity = () => resetTimer();

    const handleVisibilityChange = () => {
        if (document.visibilityState === "visible") {
            resetTimer();
        }
    };

    const attach = () => {
        events.forEach(eventName => window.addEventListener(eventName, handleActivity, true));
        document.addEventListener("visibilitychange", handleVisibilityChange, true);
    };

    const detach = () => {
        events.forEach(eventName => window.removeEventListener(eventName, handleActivity, true));
        document.removeEventListener("visibilitychange", handleVisibilityChange, true);
    };

    const stop = () => {
        if (timeoutId) {
            clearTimeout(timeoutId);
            timeoutId = null;
        }

        if (started) {
            detach();
            started = false;
        }
    };

    return {
        start(ref, idleTimeoutMs) {
            dotNetRef = ref;
            timeoutMs = typeof idleTimeoutMs === "number" && idleTimeoutMs > 0
                ? idleTimeoutMs
                : timeoutMs;

            if (!started) {
                attach();
                started = true;
            }

            resetTimer();
        },
        stop,
        dispose() {
            stop();
            dotNetRef = null;
        }
    };
})();
