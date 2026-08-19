(() => {
  const workspace = document.querySelector("[data-scan-running]");
  if (workspace?.dataset.scanRunning === "true") {
    window.setTimeout(() => window.location.reload(), 5000);
  }

  document.querySelectorAll("[data-auto-submit]").forEach((control) => {
    control.addEventListener("change", () => control.form?.submit());
  });
})();
