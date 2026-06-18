window.aiStudyHubRecaptcha = (() => {
  const scriptUrl = 'https://www.google.com/recaptcha/api.js?render=explicit';
  const instances = {};

  function loadScript() {
    return new Promise((resolve, reject) => {
      if (typeof grecaptcha !== 'undefined') {
        grecaptcha.ready(resolve);
        return;
      }
      const existing = document.querySelector('script[data-aistudyhub-recaptcha="true"]');
      if (existing) {
        existing.addEventListener('load', () => grecaptcha.ready(resolve), { once: true });
        existing.addEventListener('error', () => reject(new Error('reCAPTCHA script failed to load.')), { once: true });
        return;
      }
      const script = document.createElement('script');
      script.src = scriptUrl;
      script.async = true;
      script.defer = true;
      script.dataset.aistudyhubRecaptcha = 'true';
      script.onload = () => grecaptcha.ready(resolve);
      script.onerror = () => reject(new Error('reCAPTCHA script failed to load.'));
      document.head.appendChild(script);
    });
  }

  return {
    render: async (elementId, dotNetRef) => {
      await loadScript();
      const element = document.getElementById(elementId);
      if (!element) throw new Error('Container not found: ' + elementId);
      const widgetId = grecaptcha.render(element, {
        sitekey: '6LcglxotAAAAAJMIi0jZaLDtbPWuk9HUDeVTwH2x',
        callback: (token) => {
          if (dotNetRef) dotNetRef.invokeMethodAsync('OnRecaptchaSuccess', token);
        },
        'expired-callback': () => {
          if (dotNetRef) dotNetRef.invokeMethodAsync('OnRecaptchaExpired');
        },
        'error-callback': () => {
          if (dotNetRef) dotNetRef.invokeMethodAsync('OnRecaptchaError', 'unknown-error');
        }
      });
      instances[elementId] = widgetId;
      return widgetId;
    },
    reset: (elementId) => {
      const widgetId = instances[elementId];
      if (widgetId !== undefined && typeof grecaptcha !== 'undefined') {
        grecaptcha.reset(widgetId);
      }
    },
    remove: (elementId) => {
      const widgetId = instances[elementId];
      if (widgetId !== undefined && typeof grecaptcha !== 'undefined') {
        grecaptcha.reset(widgetId);
      }
      delete instances[elementId];
    },
    getResponse: (elementId) => {
      const widgetId = instances[elementId];
      if (widgetId !== undefined && typeof grecaptcha !== 'undefined') {
        return grecaptcha.getResponse(widgetId);
      }
      return '';
    }
  };
})();
