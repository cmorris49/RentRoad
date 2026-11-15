/**
 * Prevents repeated owner deletion submissions without changing the confirmed POST contract.
 *
 * @returns {void} Does not return a value.
 */
(function connectOwnerDeletion() {
    "use strict";

    for (const form of document.querySelectorAll("[data-listing-delete]")) {
        form.addEventListener("submit", event => {
            if (form.dataset.submitting === "true") {
                event.preventDefault();
                return;
            }

            if (!form.checkValidity()) return;

            form.dataset.submitting = "true";
            form.querySelector('button[type="submit"]').disabled = true;
        });
    }
})();
