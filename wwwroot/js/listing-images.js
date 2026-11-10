/** Reveals the existing photo fallback when secure image delivery fails, including cached failures. */
(function initializeListingImages() {
    "use strict";

    /** Hides unavailable image bytes while retaining the listing link and its descriptive title. */
    function showImageFallback(event) {
        revealFallback(event.currentTarget);
    }

    /** Reveals an optional accessible fallback without changing image delivery or listing navigation. */
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
