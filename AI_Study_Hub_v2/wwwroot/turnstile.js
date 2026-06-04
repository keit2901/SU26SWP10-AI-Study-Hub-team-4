window.aiStudyHubTurnstile = (() => {
  const scriptUrl = 'https://challenges.cloudflare.com/turnstile/v0/api.js?render=explicit';
  let scriptPromise;
  const callbacks = new Map();

  function loadScript() {
    if (window.turnstile) {
      return Promise.resolve();
    }

    if (scriptPromise) {
      return scriptPromise;
    }

    scriptPromise = new Promise((resolve, reject) => {
      const existing = document.querySelector('script[data-aistudyhub-turnstile="true"]');
      if (existing) {
        existing.addEventListener('load', () => resolve(), { once: true });
        existing.addEventListener('error', () => reject(new Error('Turnstile script failed to load.')), { once: true });
        return;
      }

      const script = document.createElement('script');
      script.src = scriptUrl;
      script.async = true;
      script.defer = true;
      script.dataset.aistudyhubTurnstile = 'true';
      script.onload = () => resolve();
      script.onerror = () => reject(new Error('Turnstile script failed to load.'));
      document.head.appendChild(script);
    });

    return scriptPromise;
  }

  async function render(elementId, dotNetRef, options) {
    await loadScript();

    const element = document.getElementById(elementId);
    if (!element) {
      throw new Error(`Turnstile container not found: ${elementId}`);
    }

    if (!window.turnstile) {
      throw new Error('Turnstile API is not available.');
    }

    if (callbacks.has(elementId)) {
      reset(elementId);
      return callbacks.get(elementId);
    }

    const widgetId = window.turnstile.render(element, {
      sitekey: options.siteKey,
      theme: options.theme || 'light',
      size: options.size || 'flexible',
      action: options.action || undefined,
      callback: token => dotNetRef.invokeMethodAsync('OnTurnstileSuccess', token),
      'error-callback': errorCode => dotNetRef.invokeMethodAsync('OnTurnstileError', String(errorCode || 'unknown-error')),
      'expired-callback': () => dotNetRef.invokeMethodAsync('OnTurnstileExpired'),
      'timeout-callback': () => dotNetRef.invokeMethodAsync('OnTurnstileExpired')
    });

    callbacks.set(elementId, widgetId);
    return widgetId;
  }

  function reset(elementId) {
    const widgetId = callbacks.get(elementId);
    if (widgetId && window.turnstile) {
      window.turnstile.reset(widgetId);
    }
  }

  function remove(elementId) {
    const widgetId = callbacks.get(elementId);
    if (widgetId && window.turnstile) {
      window.turnstile.remove(widgetId);
    }
    callbacks.delete(elementId);
  }

  return { render, reset, remove };
})();
