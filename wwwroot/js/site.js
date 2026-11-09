/** Initializes the saved or system theme before styles load, then connects the navigation toggle. */
(function initializeTheme() {
    "use strict";

    const THEME_KEY = "rentroad-theme";
    const systemTheme = window.matchMedia("(prefers-color-scheme: dark)");
    let selectedTheme = readTheme();

    /** Reads a valid saved preference, tolerating unavailable browser storage. */
    function readTheme() {
        try {
            const value = window.localStorage.getItem(THEME_KEY);
            return value === "light" || value === "dark" ? value : null;
        } catch {
            return null;
        }
    }

    /** Applies the preference and synchronizes the accessible dark-mode toggle. */
    function applyTheme() {
        const theme = selectedTheme ?? (systemTheme.matches ? "dark" : "light");
        document.documentElement.setAttribute("data-bs-theme", theme);
        const toggle = document.querySelector("[data-theme-toggle]");

        if (toggle) {
            toggle.setAttribute("aria-pressed", String(theme === "dark"));
            toggle.hidden = false;
        }
    }

    /** Saves an explicit choice, keeping it active for this page if storage is blocked. */
    function toggleTheme() {
        selectedTheme = document.documentElement.getAttribute("data-bs-theme") === "dark" ? "light" : "dark";
        applyTheme();

        try {
            window.localStorage.setItem(THEME_KEY, selectedTheme);
        } catch {
            return;
        }
    }

    /** Connects the theme control after the navigation is available. */
    function connectToggle() {
        const toggle = document.querySelector("[data-theme-toggle]");

        if (toggle) {
            toggle.addEventListener("click", toggleTheme);
        }

        applyTheme();
    }

    /** Follows operating-system changes until the user makes an explicit selection. */
    function followSystemTheme() {
        if (selectedTheme === null) {
            applyTheme();
        }
    }

    /** Synchronizes changes or removal of the saved preference in another tab. */
    function syncStoredTheme(event) {
        if (event.key === THEME_KEY || event.key === null) {
            selectedTheme = readTheme();
            applyTheme();
        }
    }

    applyTheme();
    systemTheme.addEventListener("change", followSystemTheme);
    window.addEventListener("storage", syncStoredTheme);
    document.addEventListener("DOMContentLoaded", connectToggle, { once: true });
})();
