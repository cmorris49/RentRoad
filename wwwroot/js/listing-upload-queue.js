/**
 * Provides a bounded sequential selection queue; each selected entry can be submitted only once.
 *
 * @returns {void} Does not return a value.
 */
(function initializeListingUploadQueue() {
    "use strict";
    const text = window.RentRoadText;

    /**
     * Connects local selection and drop previews without uploading or asserting server validation.
     *
     * @param {HTMLFormElement} form - Rendered form used by this component.
     * @param {object} options - Options used by this component.
     * @returns {object} The local upload queue controller and its preview accessors.
     */
    function connect(form, options) {
        const input = form.querySelector('[name="File"]');
        const drop = form.querySelector("[data-upload-drop]");
        const list = form.querySelector("[data-upload-queue]");
        const summary = form.querySelector("[data-upload-summary]");
        const items = [];
        let running = false;
        let serial = 0;

        /**
         * Releases only this selection's local preview URL.
         *
         * @param {object} item - Queued selection whose local preview should be released.
         * @returns {void} Does not return a value.
         */
        function release(item) {
            if (item.url) {
                URL.revokeObjectURL(item.url);
            }

            item.url = null;
        }

        /**
         * Lists each file's independent validation, transfer progress and final result.
         *
         * @returns {void} Does not return a value.
         */
        function render() {
            list.replaceChildren();

            for (const item of items) {
                const row = document.createElement("li");

                row.className = "editor-queued-photo";

                if (item.url) {
                    const image = document.createElement("img");

                    image.src = item.url;
                    image.alt = text("UploadQueue.LocalPreview", item.file.name);
                    row.append(image);
                }

                const body = document.createElement("div");
                const name = document.createElement("strong");

                name.textContent = item.file.name;
                const details = document.createElement("p");

                details.className = "form-text";
                details.textContent = text(
                    "UploadQueue.FileStatus",
                    (item.file.size / 1024).toLocaleString(document.documentElement?.lang, { maximumFractionDigits: 1 }),
                    text("UploadQueue." + (item.status === "Check saved photos" ? "CheckSavedPhotos" : item.status)));
                const message = document.createElement("p");

                message.className = item.error ? "field-validation-error" : "form-text";
                message.textContent = item.message || "";
                body.append(name, details, message);

                if (item.status === "Uploading") {
                    const progress = document.createElement("progress");

                    progress.max = 100;

                    if (item.progress !== null) {
                        progress.value = item.progress;
                    }

                    progress.setAttribute("aria-label", text("UploadQueue.Progress", item.file.name));
                    body.append(progress);
                }

                const remove = document.createElement("button");

                remove.type = "button";
                remove.className = "btn btn-sm btn-outline-primary";
                remove.textContent = item.attempted ? text("UploadQueue.Dismiss") : text("Common.Remove");
                remove.setAttribute(
                    "aria-label",
                    text(item.attempted ? "UploadQueue.DismissFile" : "UploadQueue.RemoveFile", item.file.name));
                remove.disabled = running;
                remove.addEventListener(
                    "click",
                    () => {
                        release(item);
                        items.splice(items.indexOf(item), 1);
                        render();
                        options.changed?.();
                    });
                row.append(body, remove);
                list.append(row);
            }

            input.disabled = running;
            summary.textContent = items.length
                ? text(
                    "UploadQueue.Summary",
                    items.filter(item => item.status === "Uploaded").length,
                    items.filter(item => item.status === "Ready").length,
                    items.filter(item => item.error).length)
                : "";
        }

        /**
         * Performs advisory checks against configured storage limits, retaining invalid files for explicit
         *   feedback.
         *
         * @param {FileList|File[]|undefined} files - Selected or dropped files to add to the local queue.
         * @returns {void} Does not return a value.
         */
        function add(files) {
            if (running) return;

            let duplicates = 0;

            for (const file of Array.from(files || [])) {
                if (items.some(item => item.file.name === file.name
                    && item.file.size === file.size
                    && item.file.lastModified === file.lastModified)) {
                    duplicates++;
                    continue;
                }

                const supported = ["image/jpeg", "image/png", "image/webp"].includes(file.type);
                const valid = supported && file.size > 0 && file.size <= options.maxSizeBytes;
                const item = {
                    id: ++serial,
                    file,
                    status: valid ? "Ready" : "Invalid",
                    attempted: false,
                    error: !valid,
                    progress: null,
                    message: valid
                        ? text("UploadQueue.LocalPreviewServerValidationPending")
                        : text("UploadQueue.InvalidFile", options.maxSizeBytes / 1048576),
                    url: null
                };

                if (valid) {
                    item.url = URL.createObjectURL(file);
                }

                items.push(item);
            }

            input.value = "";
            render();

            if (duplicates) {
                summary.textContent = text("UploadQueue.Duplicates", summary.textContent, duplicates);
            }

            options.changed?.();
        }

        /**
         * Uploads a captured batch once, continuing after definite rejections and stopping on uncertain
         *   outcomes.
         *
         * @param {function(File, Function): Promise<object>} send - Uploads one file and reports transfer
         *   progress.
         * @returns {Promise<{uploaded: number, rejected: number, uncertain: boolean}>} Confirmed upload
         *   totals and whether an uncertain outcome stopped the batch.
         */
        async function run(send) {
            if (running) {
                return {
                    uploaded: 0,
                    rejected: 0,
                    uncertain: false
                };
            }

            const batch = items.filter(item => item.status === "Ready" && !item.attempted);

            running = true;
            let uploaded = 0,
                rejected = 0,
                uncertain = false;

            render();

            try {
                for (const item of batch) {
                    item.attempted = true;
                    item.status = "Uploading";
                    item.message = "";
                    render();

                    try {
                        await send(item.file, progress => {
                            item.progress = progress;
                            render();
                        });
                        item.status = "Uploaded";
                        item.message = text("UploadQueue.SavedToThisListing");
                        uploaded++;
                    } catch (error) {
                        const definite = [
                            "ValidationFailed",
                            "StorageFailed",
                            "InvalidState",
                            "Forbidden",
                            "NotFound"
                        ].includes(error.feedback?.status);

                        item.error = true;
                        item.status = definite ? "Rejected" : "Check saved photos";
                        item.message = error.feedback?.errors?.map(entry => entry.message).join(" ") || error.message;

                        if (error.feedback?.status === "CleanupPending") {
                            item.message = text("UploadQueue.CleanupPending", item.message);
                        }

                        if (definite) {
                            rejected++;
                        } else {
                            uncertain = true;
                            item.message = text("UploadQueue.CheckBeforeRetry", item.message);
                        }

                        if (uncertain || ["Forbidden", "NotFound", "InvalidState"].includes(error.feedback?.status)) {
                            break;
                        }
                    } finally {
                        render();
                    }
                }
            } finally {
                running = false;
                render();
                options.changed?.();
            }

            return {
                uploaded,
                rejected,
                uncertain
            };
        }

        input.addEventListener("change", () => add(input.files));
        drop.addEventListener(
            "dragover",
            event => {
                event.preventDefault();

                if (!running) {
                    drop.classList.add("is-drag-over");
                }
            });
        drop.addEventListener("dragleave", () => drop.classList.remove("is-drag-over"));
        drop.addEventListener(
            "drop",
            event => {
                event.preventDefault();
                drop.classList.remove("is-drag-over");
                add(event.dataTransfer?.files);
            });
        window.addEventListener("pagehide", () => items.forEach(release));
        window.addEventListener(
            "pageshow",
            () => {
                for (const item of items) {
                    if (!item.url && !item.error) {
                        item.url = URL.createObjectURL(item.file);
                    }
                }

                render();
            });
        render();

        return {
            add,
            run,

            /**
             * Supplies unsaved local images for shared private previews.
             *
             * @returns {object[]} Local preview descriptors for ready or uploading selections.
             */
            photos() {
                return items.filter(item => item.url && ["Ready", "Uploading"].includes(item.status)).map(item => ({
                    url: item.url,
                    altText: item.file.name,
                    isPrimary: false
                }));
            },

            /**
             * Reports unsent entries without treating rejected or confirmed uploads as new submissions.
             *
             * @returns {boolean} Whether any selected file remains ready to upload.
             */
            hasPending() {
                return items.some(item => item.status === "Ready");
            },

            /**
             * Discards local preview URLs during explicit navigation disposal.
             *
             * @returns {void} Does not return a value.
             */
            dispose() {
                items.forEach(release);
            }
        };
    }

    /**
     * Sends one existing authenticated endpoint request, exposing transfer progress without automatic
     *   retries.
     *
     * @param {string} url - Endpoint or address to use.
     * @param {FormData} data - Authenticated request fields to submit.
     * @param {function(number|null): void} progress - Receives upload percentage or an indeterminate progress
     *   value.
     * @returns {Promise<object>} The parsed endpoint result or a rejected upload outcome.
     */
    function send(url, data, progress) {
        return new Promise((resolve, reject) => {
            const xhr = new XMLHttpRequest();

            xhr.open("POST", url);
            xhr.setRequestHeader("Accept", "application/json");
            xhr.timeout = 120000;
            xhr.upload.addEventListener(
                "progress",
                event => progress(event.lengthComputable ? Math.round(event.loaded / event.total * 100) : null));

            /**
             * Converts safe structured service feedback or an ambiguous connection failure into a queue
             *   outcome.
             *
             * @returns {void} Does not return a value.
             */
            function failure() {
                const error = new Error(text("UploadQueue.UploadOutcomeCouldNotBeConfirmed"));

                try {
                    error.feedback = JSON.parse(xhr.responseText);
                } catch { }

                reject(error);
            }

            xhr.addEventListener(
                "load",
                () => {
                    let body;

                    try {
                        body = JSON.parse(xhr.responseText);
                    } catch { }

                    if (xhr.status >= 200 && xhr.status < 300 && body?.status === "Success") {
                        resolve(body);
                    } else {
                        failure();
                    }
                });
            xhr.addEventListener("error", failure);
            xhr.addEventListener("timeout", failure);
            xhr.addEventListener("abort", failure);
            xhr.send(data);
        });
    }

    window.RentRoadUploadQueue = { connect, send };
})();
