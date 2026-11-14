/**
 * Keeps the single Bootstrap mobile menu usable with keyboard, touch, and viewport changes.
 */
(function connectNavigation() {
    "use strict";

    const menu = document.getElementById("main-navigation");
    const toggle = document.querySelector('[aria-controls="main-navigation"]');
    const desktop = window.matchMedia("(min-width: 1200px)");

    if (!menu || !toggle || !window.bootstrap?.Collapse) return;

    /**
     * Closes the mobile panel without changing desktop navigation or opening another dropdown.
     *
     * @param {boolean} restoreFocus - Whether to return focus to the hamburger button.
     */
    function closeMenu(restoreFocus) {
        if (!menu.classList.contains("show")) return;

        window.bootstrap.Collapse.getOrCreateInstance(menu, { toggle: false }).hide();

        if (restoreFocus) {
            toggle.focus();
        }
    }

    menu.addEventListener("keydown", event => {
        if (event.key === "Escape" && !desktop.matches) {
            event.preventDefault();
            closeMenu(true);
        }
    });
    menu.addEventListener("click", event => {
        if (!desktop.matches && event.target.closest("a[href]")) {
            closeMenu(false);
        }
    });
    desktop.addEventListener("change", () => closeMenu(false));
})();
