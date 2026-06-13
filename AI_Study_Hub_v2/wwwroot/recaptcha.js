window.aiStudyHubRecaptcha = (() => {
  const scriptUrl = 'https://www.google.com/recaptcha/api.js?render=explicit';
  let scriptPromise;
  const callbacks = new Map();

  function loadScript() {
    if (window.grecaptcha && window.grecaptcha.render) {
      return Promise.resolve();
    }

    if (scriptPromise) {
      return scriptPromise;
    }

    scriptPromise = new Promise((resolve, reject) => {
      let script = document.querySelector('script[data-aistudyhub-recaptcha="true"]');
      if (!script) {
        script = document.createElement('script');
        script.src = scriptUrl;
        script.async = true;
        script.defer = true;
        script.dataset.aistudyhubRecaptcha = 'true';
        document.head.appendChild(script);
      }

      const startTime = Date.now();
      const interval = setInterval(() => {
        if (window.grecaptcha && window.grecaptcha.render) {
          clearInterval(interval);
          resolve();
        } else if (Date.now() - startTime > 10000) {
          clearInterval(interval);
          scriptPromise = null;
          reject(new Error('reCAPTCHA failed to load within 10 seconds.'));
        }
      }, 100);

      script.addEventListener('error', () => {
        clearInterval(interval);
        scriptPromise = null;
        reject(new Error('reCAPTCHA script failed to load.'));
      }, { once: true });
    });

    return scriptPromise;
  }

  async function render(elementId, dotNetRef, options) {
    await loadScript();

    const element = document.getElementById(elementId);
    if (!element) {
      throw new Error(`reCAPTCHA container not found: ${elementId}`);
    }

    if (!window.grecaptcha || !window.grecaptcha.render) {
      throw new Error('reCAPTCHA API is not available.');
    }

    if (callbacks.has(elementId)) {
      reset(elementId);
      return callbacks.get(elementId);
    }

    const widgetId = window.grecaptcha.render(element, {
      sitekey: options.siteKey,
      theme: options.theme || 'light',
      size: options.size || 'normal',
      callback: token => dotNetRef.invokeMethodAsync('OnRecaptchaSuccess', token),
      'expired-callback': () => dotNetRef.invokeMethodAsync('OnRecaptchaExpired'),
      'error-callback': () => dotNetRef.invokeMethodAsync('OnRecaptchaError', 'unknown-error')
    });

    callbacks.set(elementId, widgetId);
    return widgetId;
  }

  function reset(elementId) {
    const widgetId = callbacks.get(elementId);
    if (widgetId !== undefined && window.grecaptcha) {
      window.grecaptcha.reset(widgetId);
    }
  }

  function remove(elementId) {
    callbacks.delete(elementId);
  }

  return { render, reset, remove };
})();
