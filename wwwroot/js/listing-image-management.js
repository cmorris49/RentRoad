/** Adds local upload previews and duplicate-submit feedback without changing server image validation. */
(function initializeImageManagement() {
    "use strict";

    /** Connects one multipart form; no file is sent until its normal authenticated submission. */
    function connectUpload(form) {
        const input = form.querySelector("[data-image-file]");
        const preview = form.querySelector("[data-upload-preview]");
        const image = form.querySelector("[data-upload-preview-image]");
        const caption = form.querySelector("[data-upload-preview-caption]");
        const status = form.querySelector("[data-upload-preview-status]");
        const submit = form.querySelector("[data-upload-submit]");
        let previewUrl = null;
        let submitting = false;

        /** Releases only the temporary object URL created by this form. */
        function releasePreview() {
            if (previewUrl !== null) {
                URL.revokeObjectURL(previewUrl);
                previewUrl = null;
            }
        }

        /** Displays the selected local file without uploading it or claiming that it passed validation. */
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
            status.textContent = "Local preview only. Choose Upload image to save it.";
        }

        /** Keeps an undecodable preview from hiding the authoritative upload feedback. */
        function previewUnavailable() {
            preview.hidden = true;
            releasePreview();
            status.textContent = "A preview is unavailable for this file. The server will validate any submitted image.";
        }

        /** Waits for client validation handlers before disabling the button for an actual submission. */
        function beginUpload(event) {
            if (submitting) {
                event.preventDefault();
                return;
            }

            queueMicrotask(() => confirmSubmission(event));
        }

        /** Disables submission only after all client validation listeners have accepted the event. */
        function confirmSubmission(event) {
            if (event.defaultPrevented || !form.checkValidity()) {
                return;
            }

            submitting = true;
            submit.disabled = true;
            submit.textContent = "Uploading…";
            form.setAttribute("aria-busy", "true");
            status.textContent = "Uploading your image. Please wait.";
        }

        /** Restores the form if browser history returns a previously submitted document. */
        function restoreUpload() {
            submitting = false;
            submit.disabled = false;
            submit.textContent = "Upload image";
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
