/** Reveals the existing photo fallback when secure image delivery fails, including cached failures. */
(function initializeListingImages() {
    "use strict";

    /** Hides unavailable image bytes while retaining the listing link and its descriptive title. */
    function showImageFallback(event) {
        event.currentTarget.hidden = true;
    }

    const images = document.querySelectorAll("[data-listing-image]");

    for (const image of images) {
        image.addEventListener("error", showImageFallback, { once: true });

        if (image.complete && image.naturalWidth === 0) {
            image.hidden = true;
        }
    }
})();
