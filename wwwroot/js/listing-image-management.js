/**
 * Adds local upload previews and duplicate-submit feedback without changing server image validation.
 *
 * @returns {void} Does not return a value.
 */
(function initializeImageManagement() {
    "use strict";
    const text = window.RentRoadText;

    /**
     * Connects one multipart form; no file is sent until its normal authenticated submission.
     *
     * @param {HTMLFormElement} form - Rendered form used by this component.
     * @returns {void} Does not return a value.
     */
    function connectUpload(form) {
        const input = form.querySelector("[data-image-file]");
        const preview = form.querySelector("[data-upload-preview]");
        const image = form.querySelector("[data-upload-preview-image]");
        const caption = form.querySelector("[data-upload-preview-caption]");
        const status = form.querySelector("[data-upload-preview-status]");
        const submit = form.querySelector("[data-upload-submit]");
        let previewUrl = null;
        let submitting = false;

        /**
         * Releases only the temporary object URL created by this form.
         *
         * @returns {void} Does not return a value.
         */
        function releasePreview() {
            if (previewUrl !== null) {
                URL.revokeObjectURL(previewUrl);
                previewUrl = null;
            }
        }

        /**
         * Displays the selected local file without uploading it or claiming that it passed validation.
         *
         * @returns {void} Does not return a value.
         */
        function previewSelection() {
            releasePreview();
            preview.hidden = true;
            image.removeAttribute("src");
            status.textContent = "";
            const file = input.files?.[0];

            if (!file || typeof URL.createObjectURL !== "function") {
                return;
            }

            previewUrl = URL.createObjectURL(file);
            image.src = previewUrl;
            caption.textContent = file.name;
            preview.hidden = false;
            status.textContent = text("ListingImages.LocalPreviewOnlyChooseUploadImageToSaveIt");
        }

        /**
         * Keeps an undecodable preview from hiding the authoritative upload feedback.
         *
         * @returns {void} Does not return a value.
         */
        function previewUnavailable() {
            preview.hidden = true;
            releasePreview();
            status.textContent = text("ListingImages.PreviewUnavailable");
        }

        /**
         * Waits for client validation handlers before disabling the button for an actual submission.
         *
         * @param {SubmitEvent} event - Native event supplied to this handler.
         * @returns {void} Does not return a value.
         */
        function beginUpload(event) {
            if (submitting) {
                event.preventDefault();

                return;
            }

            queueMicrotask(() => confirmSubmission(event));
        }

        /**
         * Disables submission only after all client validation listeners have accepted the event.
         *
         * @param {SubmitEvent} event - Native event supplied to this handler.
         * @returns {void} Does not return a value.
         */
        function confirmSubmission(event) {
            if (event.defaultPrevented || !form.checkValidity()) {
                return;
            }

            submitting = true;
            submit.disabled = true;
            submit.textContent = text("ListingImages.Uploading");
            form.setAttribute("aria-busy", "true");
            status.textContent = text("ListingImages.UploadingYourImagePleaseWait");
        }

        /**
         * Restores the form if browser history returns a previously submitted document.
         *
         * @returns {void} Does not return a value.
         */
        function restoreUpload() {
            submitting = false;
            submit.disabled = false;
            submit.textContent = text("ListingImages.UploadImage");
            form.removeAttribute("aria-busy");
            previewSelection();
        }

        input.addEventListener("change", previewSelection);
        image.addEventListener("error", previewUnavailable);
        form.addEventListener("submit", beginUpload);
        window.addEventListener("pagehide", releasePreview);
        window.addEventListener("pageshow", restoreUpload);
    }

    for (const form of document.querySelectorAll("[data-image-upload]")) {
        connectUpload(form);
    }
})();
