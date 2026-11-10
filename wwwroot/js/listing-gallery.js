/** Adds optional photo selection while leaving every image visible when JavaScript is unavailable. */
(function initializeListingGalleries() {
    "use strict";

    /** Connects accessible controls to one listing's ordered photo figures. */
    function connectGallery(gallery) {
        const slides = Array.from(gallery.querySelectorAll("[data-gallery-slide]"));
        const controls = gallery.querySelector("[data-gallery-controls]");

        if (slides.length < 2 || !controls) {
            return;
        }

        const selectors = Array.from(controls.querySelectorAll("[data-gallery-index]"));
        const status = controls.querySelector("[data-gallery-status]");
        let selectedIndex = 0;

        /** Shows one photo and synchronizes its toggle state and screen-reader announcement. */
        function showPhoto(index) {
            selectedIndex = (index + slides.length) % slides.length;

            for (let position = 0; position < slides.length; position++) {
                slides[position].hidden = position !== selectedIndex;
                selectors[position].setAttribute("aria-pressed", String(position === selectedIndex));
            }

            status.textContent = `Photo ${selectedIndex + 1} of ${slides.length}`;
        }

        /** Selects the photo identified by the activated server-rendered button. */
        function selectPhoto(event) {
            const index = Number(event.currentTarget.getAttribute("data-gallery-index"));

            if (Number.isInteger(index) && index >= 0 && index < slides.length) {
                showPhoto(index);
            }
        }

        /** Moves backward through the gallery, wrapping at the first photo. */
        function previousPhoto() {
            showPhoto(selectedIndex - 1);
        }

        /** Moves forward through the gallery, wrapping at the last photo. */
        function nextPhoto() {
            showPhoto(selectedIndex + 1);
        }

        /** Supports arrow, Home, and End navigation inside the gallery controls without intercepting page navigation. */
        function navigatePhotos(event) {
            if (event.altKey || event.ctrlKey || event.metaKey) {
                return;
            }

            switch (event.key) {
                case "ArrowLeft":
                    previousPhoto();
                    break;
                case "ArrowRight":
                    nextPhoto();
                    break;
                case "Home":
                    showPhoto(0);
                    break;
                case "End":
                    showPhoto(slides.length - 1);
                    break;
                default:
                    return;
            }

            event.preventDefault();
            selectors[selectedIndex].focus();
        }

        for (const selector of selectors) {
            selector.addEventListener("click", selectPhoto);
        }

        controls.querySelector("[data-gallery-previous]").addEventListener("click", previousPhoto);
        controls.querySelector("[data-gallery-next]").addEventListener("click", nextPhoto);
        controls.addEventListener("keydown", navigatePhotos);
        showPhoto(0);
        gallery.setAttribute("data-gallery-ready", "true");
        controls.hidden = false;
    }

    for (const gallery of document.querySelectorAll("[data-listing-gallery]")) {
        connectGallery(gallery);
    }
})();
