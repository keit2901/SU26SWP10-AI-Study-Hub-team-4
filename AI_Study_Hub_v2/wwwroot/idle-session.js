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
    let lastActivityAt = Date.now();

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

    const notifyTimeout = async () => {
        const callbackRef = dotNetRef;
        stop();

        if (!callbackRef) {
            return;
        }

        try {
            await callbackRef.invokeMethodAsync("HandleIdleTimeout");
        } catch {
        }
    };

    const scheduleTimeout = (delayMs) => {
        if (!started) {
            return;
        }

        if (timeoutId) {
            clearTimeout(timeoutId);
        }

        timeoutId = window.setTimeout(checkIdle, Math.max(0, delayMs));
    };

    const checkIdle = async () => {
        if (!started) {
            return;
        }

        const elapsedMs = Date.now() - lastActivityAt;
        if (elapsedMs >= timeoutMs) {
            await notifyTimeout();
            return;
        }

        scheduleTimeout(timeoutMs - elapsedMs);
    };

    const resetTimer = () => {
        lastActivityAt = Date.now();
        scheduleTimeout(timeoutMs);
    };

    const handleActivity = () => resetTimer();

    const handleVisibilityChange = () => {
        if (document.visibilityState === "visible") {
            void checkIdle();
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

    return {
        start(ref, idleTimeoutMs) {
            dotNetRef = ref;
            timeoutMs = typeof idleTimeoutMs === "number" && idleTimeoutMs > 0
                ? idleTimeoutMs
                : timeoutMs;
            lastActivityAt = Date.now();

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
