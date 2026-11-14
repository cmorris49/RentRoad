/**
 * Applies the saved or system theme before styles load and connects appearance preferences.
 */
(function initializeTheme() {
    "use strict";

    const THEME_KEY = "rentroad-theme";
    const systemTheme = window.matchMedia("(prefers-color-scheme: dark)");
    let selectedTheme = readTheme();

    /**
     * Reads a saved preference while tolerating unavailable browser storage.
     *
     * @returns {"light"|"dark"|null} The explicit preference, or null for the system theme.
     */
    function readTheme() {
        try {
            const value = window.localStorage.getItem(THEME_KEY);

            return value === "light" || value === "dark" ? value : null;
        } catch {
            return null;
        }
    }

    /**
     * Applies the current preference and synchronizes any settings controls.
     */
    function applyTheme() {
        const theme = selectedTheme ?? (systemTheme.matches ? "dark" : "light");

        document.documentElement.setAttribute("data-bs-theme", theme);

        for (const choice of document.querySelectorAll("[data-theme-choice]")) {
            choice.checked = choice.value === (selectedTheme ?? "system");
        }
    }

    /**
     * Applies an explicit choice immediately and saves it when storage is available.
     *
     * @param {Event} event - Change event from a theme radio input.
     */
    function selectTheme(event) {
        const choice = event.currentTarget;

        if (!choice.checked || !["system", "light", "dark"].includes(choice.value)) return;

        selectedTheme = choice.value === "system" ? null : choice.value;
        applyTheme();

        try {
            if (selectedTheme === null) {
                window.localStorage.removeItem(THEME_KEY);
            } else {
                window.localStorage.setItem(THEME_KEY, selectedTheme);
            }
        } catch {
            selectedTheme = choice.value === "system" ? null : choice.value;
        }

        const status = document.querySelector("[data-theme-status]");

        if (status) {
            status.textContent = choice.dataset.themeConfirmation;
        }
    }

    /**
     * Connects appearance controls after the document is ready without requiring a navbar button.
     */
    function connectPreferences() {
        for (const choice of document.querySelectorAll("[data-theme-choice]")) {
            choice.addEventListener("change", selectTheme);
        }

        applyTheme();
    }

    /**
     * Follows operating-system changes until an explicit light or dark preference is selected.
     */
    function followSystemTheme() {
        if (selectedTheme === null) {
            applyTheme();
        }
    }

    /**
     * Synchronizes preference changes made in another tab.
     *
     * @param {StorageEvent} event - Browser storage notification.
     */
    function syncStoredTheme(event) {
        if (event.key === THEME_KEY || event.key === null) {
            selectedTheme = readTheme();
            applyTheme();
        }
    }

    applyTheme();
    systemTheme.addEventListener("change", followSystemTheme);
    window.addEventListener("storage", syncStoredTheme);
    document.addEventListener("DOMContentLoaded", connectPreferences, { once: true });
})();
