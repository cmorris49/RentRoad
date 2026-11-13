/**
 * Enhances native horizontal public listing rows without handling events from their image galleries.
 *
 * @returns {void} Does not return a value.
 */
(function initializeListingScrollers() {
    "use strict";

    /**
     * Connects bounded scrolling controls and keyboard navigation to one native list.
     *
     * @param {HTMLElement} root - Rendered root used by this component.
     * @returns {void} Does not return a value.
     */
    function connect(root) {
        const track = root.querySelector("[data-scroller-track]");

        if (!track) return;

        const controls = root.querySelector("[data-scroller-controls]");
        const previous = root.querySelector("[data-scroller-previous]");
        const next = root.querySelector("[data-scroller-next]");

        /**
         * Updates boundary controls after native scrolling, viewport changes, or card resizing.
         *
         * @returns {void} Does not return a value.
         */
        function update() {
            const maximum = Math.max(0, track.scrollWidth - track.clientWidth);

            previous.disabled = track.scrollLeft <= 1;
            next.disabled = track.scrollLeft >= maximum - 1;
            controls.hidden = maximum <= 1;
        }

        /**
         * Scrolls one card at a time, respecting the operating system's motion preference.
         *
         * @param {number} direction - Direction of the requested movement.
         * @param {number|undefined} endpoint - Explicit scroll destination, when provided.
         * @returns {void} Does not return a value.
         */
        function move(direction, endpoint) {
            const card = track.firstElementChild;
            const distance = card
                ? card.getBoundingClientRect().width + parseFloat(getComputedStyle(track).columnGap || 0)
                : track.clientWidth;
            const maximum = Math.max(0, track.scrollWidth - track.clientWidth);
            const target = endpoint === undefined ? track.scrollLeft + direction * distance : endpoint;

            track.scrollTo({
                left: Math.max(0, Math.min(maximum, target)),
                behavior: window.matchMedia("(prefers-reduced-motion: reduce)").matches ? "instant" : "smooth"
            });
        }

        previous.addEventListener("click", () => move(-1));
        next.addEventListener("click", () => move(1));
        track.addEventListener("scroll", update, { passive: true });
        track.addEventListener(
            "keydown",
            event => {
                if (event.target !== track || event.altKey || event.ctrlKey || event.metaKey || event.shiftKey) {
                    return;
                }

                if (!["ArrowLeft", "ArrowRight", "Home", "End"].includes(event.key)) return;

                event.preventDefault();
                move(
                    event.key === "ArrowLeft" ? -1 : 1,
                    event.key === "Home" ? 0 : event.key === "End" ? track.scrollWidth : undefined);
            });
        window.addEventListener("resize", update);

        if (typeof ResizeObserver !== "undefined") {
            new ResizeObserver(update).observe(track);
        }

        update();
    }

    document.querySelectorAll("[data-listing-scroller]").forEach(connect);
})();
