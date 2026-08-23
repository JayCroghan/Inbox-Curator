(() => {
  const scrollStateKey = "inbox-curator:triage-scroll";
  const maxScrollStateAgeMs = 2 * 60 * 1000;

  const returnLocationFor = (form) => {
    const currentLocation = new URL(window.location.href);
    const returnUrl = new FormData(form).get("ReturnUrl");
    if (typeof returnUrl !== "string" || returnUrl.length === 0) {
      return currentLocation;
    }

    const requestedLocation = new URL(returnUrl, currentLocation.origin);
    return requestedLocation.origin === currentLocation.origin
      ? requestedLocation
      : currentLocation;
  };

  const captureScrollPosition = (form) => {
    try {
      const target = returnLocationFor(form);
      window.sessionStorage.setItem(scrollStateKey, JSON.stringify({
        pathname: target.pathname,
        search: target.search,
        scrollY: window.scrollY,
        timestamp: Date.now()
      }));
    } catch {
      // Scroll restoration is progressive enhancement; submission must continue.
    }
  };

  const consumeScrollPosition = () => {
    try {
      const serializedState = window.sessionStorage.getItem(scrollStateKey);
      if (serializedState === null) return;

      window.sessionStorage.removeItem(scrollStateKey);
      const state = JSON.parse(serializedState);
      const isValid = state !== null
        && typeof state.pathname === "string"
        && typeof state.search === "string"
        && typeof state.scrollY === "number"
        && Number.isFinite(state.scrollY)
        && typeof state.timestamp === "number";
      if (!isValid) return;

      const age = Date.now() - state.timestamp;
      if (age < 0 || age > maxScrollStateAgeMs) return;
      if (state.pathname !== window.location.pathname || state.search !== window.location.search) return;

      window.requestAnimationFrame(() => window.scrollTo(0, Math.max(0, state.scrollY)));
    } catch {
      try {
        window.sessionStorage.removeItem(scrollStateKey);
      } catch {
        // sessionStorage may be unavailable; the page remains fully usable.
      }
    }
  };

  window.addEventListener("pageshow", consumeScrollPosition, { once: true });

  document.querySelectorAll("form[data-preserve-triage-scroll]").forEach((form) => {
    form.addEventListener("submit", () => captureScrollPosition(form));
  });

  const workspace = document.querySelector("[data-scan-running]");
  if (workspace?.dataset.scanRunning === "true") {
    window.setTimeout(() => window.location.reload(), 5000);
  }

  document.querySelectorAll("[data-auto-submit]").forEach((control) => {
    control.addEventListener("change", () => control.form?.submit());
  });

  document.querySelectorAll(".decision-controls").forEach((details) => {
    details.addEventListener("toggle", () => {
      if (!details.open) return;
      document.querySelectorAll(".decision-controls[open]").forEach((other) => {
        if (other !== details) other.removeAttribute("open");
      });
    });
  });
})();
