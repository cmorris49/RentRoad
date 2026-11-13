/**
 * Reveals the existing photo fallback when secure image delivery fails, including cached failures.
 *
 * @returns {void} Does not return a value.
 */
(function initializeListingImages() {
    "use strict";

    /**
     * Hides unavailable image bytes while retaining the listing link and its descriptive title.
     *
     * @param {Event} event - Native event supplied to this handler.
     * @returns {void} Does not return a value.
     */
    function showImageFallback(event) {
        revealFallback(event.currentTarget);
    }

    /**
     * Reveals an optional accessible fallback without changing image delivery or listing navigation.
     *
     * @param {HTMLImageElement} image - Rendered image used by this component.
     * @returns {void} Does not return a value.
     */
    function revealFallback(image) {
        image.hidden = true;
        const fallback = image.parentElement?.querySelector("[data-image-fallback]");

        if (fallback) {
            fallback.removeAttribute("aria-hidden");
        }
    }

    const images = document.querySelectorAll("[data-listing-image]");

    for (const image of images) {
        image.addEventListener("error", showImageFallback, { once: true });

        if (image.complete && image.naturalWidth === 0) {
            revealFallback(image);
        }
    }
})();
