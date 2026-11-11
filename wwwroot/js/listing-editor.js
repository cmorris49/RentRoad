/** Connects the unified owner editor without replacing entered fields or trusting submitted ownership. */
(function initializeListingEditors() {
    "use strict";
    const text = window.RentRoadText;

    /** Manages one editor's owner-bound receipt, inline operations, and private previews. */
    async function connectEditor(root) {
        const fields = root.querySelector("[data-editor-fields]");
        const dialog = root.querySelector("[data-editor-dialog]");
        const notice = root.querySelector("[data-editor-notice]");
        const errors = root.querySelector("[data-editor-errors]");
        const upload = root.querySelector("[data-editor-upload]");
        const fileInput = upload?.querySelector('[name="File"]');
        const archived = root.dataset.listingStatus === "Archived";
        let state = null;
        let busy = false;
        let ready = false;
        let uploadQueue = null;
        let nonce = null;
        let wroteDetails = false;
        let focusAfterAction = null;
        let savedSignature = signature();
        const storageKey = "rentroad-new-editor";

        /** Reads editable scalar values without including tokens, IDs, or relationships. */
        function currentFields() {
            const values = {};
            for (const name of ["Title", "Description", "Category", "Condition", "DailyPriceCad", "PickupArea", "Latitude", "Longitude"]) {
                values[name[0].toLowerCase() + name.slice(1)] = fields.elements.namedItem(name)?.value ?? "";
            }
            return values;
        }

        /** Identifies the exact field version saved by a completed request. */
        function signature() { return JSON.stringify(currentFields()); }

        /** Gets the existing Razor-generated anti-forgery token for same-origin requests. */
        function token() { return fields.querySelector('[name="__RequestVerificationToken"]').value; }

        /** Sends explicit requests once; ambiguous writes are never retried automatically. */
        async function request(url, data) {
            const response = await fetch(url, { method: data ? "POST" : "GET", body: data, credentials: "same-origin", headers: { Accept: "application/json" } });
            const json = response.headers.get("content-type")?.includes("json") ? await response.json() : null;
            if (response.redirected || !response.ok || !json) {
                const failure = new Error(response.redirected ? text("ListingEditor.SignInAgainBeforeSavingYourEnteredDetailsAre") : text("ListingEditor.TheOperationCouldNotBeConfirmedReviewTheSaved"));
                failure.feedback = json;
                throw failure;
            }
            return json;
        }

        /** Enables only valid lifecycle controls and prevents simultaneous mutations. */
        function updateControls() {
            for (const button of root.querySelectorAll("[data-editor-write]")) button.disabled = !ready || busy || archived || state?.status === "Archived";
            if (upload) upload.querySelector('button[type="submit"]').disabled = !ready || busy || archived || state?.status === "Archived" || !uploadQueue?.hasPending();
            root.querySelector("[data-editor-confirm]").disabled = busy || !nonce;
            root.setAttribute("aria-busy", String(busy));
        }

        /** Clears only feedback created by this script, retaining all entered values. */
        function clearFeedback() {
            root.querySelector("[data-editor-server-feedback]").hidden = true;
            errors.hidden = true;
            errors.querySelector("ul").replaceChildren();
            for (const node of root.querySelectorAll("[data-editor-field-error]")) node.remove();
            for (const node of root.querySelectorAll('[aria-invalid="true"]')) node.removeAttribute("aria-invalid");
            notice.hidden = true;
        }

        /** Shows safe text feedback and associates service errors with their submitted form. */
        function showFailure(error, form) {
            const feedback = error.feedback;
            const messages = feedback?.errors?.length ? feedback.errors : [{ field: "", message: error.message }];
            if (["CleanupPending", "ReconciliationRequired"].includes(feedback?.status)) {
                messages.push({ field: "", message: feedback.metadataRemoved === true
                    ? text("ListingEditor.ThePhotoMetadataWasRemovedButStorageCleanupIs")
                    : text("ListingEditor.ThisActionDidNotFullyCompleteReviewCleanupOr") });
            }
            errors.className = "alert " + (["CleanupPending", "ReconciliationRequired", "Conflict"].includes(feedback?.status) ? "alert-warning" : "alert-danger");
            const list = errors.querySelector("ul");
            list.replaceChildren();
            for (const item of messages) {
                const entry = document.createElement("li");
                entry.textContent = item.message;
                list.append(entry);
                const field = item.field === "DailyPriceCents" ? "DailyPriceCad" : item.field;
                const input = form?.elements.namedItem(field) || fields.elements.namedItem(field);
                if (input?.insertAdjacentElement) {
                    input.setAttribute("aria-invalid", "true");
                    const span = document.createElement("span");
                    span.className = "field-validation-error";
                    span.dataset.editorFieldError = "";
                    span.textContent = item.message;
                    (input.closest(".input-group") || input).insertAdjacentElement("afterend", span);
                }
            }
            errors.hidden = false;
            if (dialog.open) {
                const reviewErrors = root.querySelector("[data-review-errors]");
                reviewErrors.querySelector("ul").replaceChildren(list.cloneNode(true));
                reviewErrors.hidden = false;
            }
            errors.scrollIntoView({ block: "nearest" });
        }

        /** Announces the actual save state and optionally links to a confirmed public listing. */
        function announce(message, published = false, partial = false) {
            notice.className = "alert " + (partial ? "alert-info" : "alert-success");
            if (signature() !== savedSignature) message = text("ListingEditor.NewerEdits", message);
            notice.replaceChildren(document.createTextNode(message));
            if (published) {
                const link = document.createElement("a");
                link.href = "/listings/" + state.listingId;
                link.textContent = text("ListingEditor.PublicLink");
                notice.append(link);
            }
            notice.hidden = false;
        }

        /** Tracks a confirmed ID on the same page so reloads cannot intentionally allocate another draft. */
        function remember(next) {
            state = next;
            if (state.listingId) {
                root.dataset.listingId = String(state.listingId);
                fields.action = "/my/listings/" + state.listingId + "/edit";
                history.replaceState(null, "", fields.action + location.hash);
                try { sessionStorage.removeItem(storageKey); } catch { }
            }
            root.querySelector("[data-editor-save-state]").textContent = state.status === "Published"
                ? text("ListingEditor.PublishedHelp")
                : state.status === "Archived" ? text("ListingEditor.ArchivedHelp") : text("ListingEditor.YourDraftIsPrivateUntilYouReviewAndConfirm");
            const badge = root.querySelector("[data-editor-status]");
            badge.textContent = text("ListingStatus." + state.status);
            badge.className = "owner-status owner-status-" + state.status.toLowerCase();
            const save = root.querySelector("[data-editor-save]");
            if (save) save.textContent = state.status === "Published" ? text("Common.SaveChanges") : text("ListingEditor.SaveDraft");
            const review = root.querySelector("[data-editor-review]");
            if (review) review.textContent = state.status === "Published" ? text("ListingEditor.ReviewChanges") : text("ListingEditor.UploadListing");
            updateControls();
        }

        /** Saves a captured scalar version while allowing newer edits to remain in the form. */
        async function saveFields() {
            const captured = signature();
            const result = await request("/my/listings/editor/" + state.token + "/save", new FormData(fields));
            remember(result.state);
            savedSignature = captured;
            wroteDetails = true;
            nonce = null;
        }

        /** Preserves all other date forms when an inline operation refreshes saved records. */
        function capturePeriods() {
            const retained = new Map();
            for (const form of root.querySelectorAll("form[data-editor-period]")) {
                retained.set(form.dataset.group + ":" + form.dataset.recordId, { entries: Array.from(new FormData(form).entries()), open: form.closest("details")?.open });
            }
            return retained;
        }

        /** Rebuilds only current saved metadata; private scalar and file inputs remain untouched. */
        async function refresh(section = "all", changedKey = null) {
            const result = await request("/my/listings/editor/session?token=" + encodeURIComponent(state.token));
            const retained = capturePeriods();
            remember(result.state);
            if (section === "all" || section === "photos") renderPhotos();
            if (section === "all" || section === "dates") renderPeriods(retained, changedKey);
        }

        /** Serializes intentional actions and reads state after ambiguous failures without repeating writes. */
        async function perform(work, form = fields) {
            if (busy || !ready) return;
            busy = true;
            wroteDetails = false;
            clearFeedback();
            updateControls();
            try { await work(); }
            catch (error) {
                if (state) {
                    try { await refresh(); } catch { }
                }
                const target = form?.dataset.group ? Array.from(root.querySelectorAll("form[data-editor-period]")).find(item => item.dataset.group === form.dataset.group && item.dataset.recordId === form.dataset.recordId) : form;
                showFailure(error, target || form);
                if (wroteDetails) announce(text("ListingEditor.ItemDetailsWereSavedTheFollowingPhotoOrDate"), false, true);
            }
            finally {
                busy = false;
                updateControls();
                if (focusAfterAction !== null) root.querySelector('[data-order-id="' + focusAfterAction + '"] [data-order-surface]')?.focus({ preventScroll: true });
                focusAfterAction = null;
            }
        }

        /** Adds authenticated form data for an existing image or availability endpoint. */
        function mutationData() {
            const data = new FormData();
            data.set("__RequestVerificationToken", token());
            return data;
        }

        /** Uses the existing secure delivery URL and provides a local missing-image fallback. */
        function imageUnavailable(event) {
            event.target.hidden = true;
            const fallback = event.target.parentElement.querySelector("[data-editor-image-fallback]");
            if (fallback) { fallback.hidden = false; fallback.textContent = text("Gallery.Unavailable"); fallback.setAttribute("aria-hidden", "false"); }
        }

        /** Renders lightweight thumbnails and existing primary/order/removal actions. */
        function renderPhotos() {
            const collection = root.querySelector("[data-editor-photos]");
            collection.replaceChildren();
            root.querySelector("[data-editor-no-images]").hidden = state.images.length > 0;
            state.images.forEach((photo, index) => {
                const card = root.querySelector("[data-photo-template]").content.firstElementChild.cloneNode(true);
                card.dataset.orderId = String(photo.id);
                if (!archived && state.status !== "Archived") card.dataset.orderEditable = "";

                const image = card.querySelector("img");
                const fallback = document.createElement("span");
                fallback.dataset.editorImageFallback = "";
                fallback.className = "listing-image-fallback";
                fallback.textContent = text("Gallery.Unavailable");
                fallback.hidden = true;
                image.parentElement.append(fallback);
                image.draggable = false;
                image.src = photo.url;
                image.alt = photo.altText || text("ListingEditor.PhotoAlt", index + 1);
                image.addEventListener("error", imageUnavailable);
                card.querySelector("[data-photo-label]").textContent = text("ListingEditor.PhotoNumber", index + 1);
                card.querySelector("[data-primary-badge]").hidden = !photo.isPrimary;
                for (const button of card.querySelectorAll("[data-photo-action]")) {
                    button.dataset.imageId = String(photo.id);
                    button.dataset.editorWrite = "";
                    if (button.dataset.photoAction === "up") button.setAttribute("aria-label", text("ListingEditor.MoveEarlier", index + 1));
                    if (button.dataset.photoAction === "down") button.setAttribute("aria-label", text("ListingEditor.MoveLater", index + 1));
                    if (button.dataset.photoAction === "primary") button.hidden = photo.isPrimary;
                    if (button.dataset.photoAction === "up") button.hidden = index === 0;
                    if (button.dataset.photoAction === "down") button.hidden = index === state.images.length - 1;
                }
                if (archived) {
                    card.querySelector(".editor-photo-actions").remove();
                    card.querySelector("details").remove();
                }
                collection.append(card);
            });
            updateControls();
        }

        /** Restores unsubmitted availability edits after another period is saved or removed. */
        function restorePeriod(form, retained, changedKey) {
            const key = form.dataset.group + ":" + form.dataset.recordId;
            const previous = key !== changedKey ? retained?.get(key) : null;
            if (!previous) return;
            for (const [name, value] of previous.entries) {
                const input = form.elements.namedItem(name);
                if (input) input.value = value;
            }
            if (form.closest("details")) form.closest("details").open = previous.open;
        }

        /** Renders date records with included-day summaries and owner-only reasons. */
        function renderPeriods(retained, changedKey) {
            for (const group of ["windows", "blocks"]) {
                const list = root.querySelector('[data-editor-periods="' + group + '"]');
                list.replaceChildren();
                for (const period of state[group]) {
                    const row = root.querySelector("[data-period-template]").content.firstElementChild.cloneNode(true);
                    row.querySelector(".editor-period-range").textContent = period.displayRange;
                    row.querySelector(".editor-period-reason").textContent = group === "blocks" ? period.reason || "" : "";
                    const form = row.querySelector("form");
                    form.dataset.group = group;
                    form.dataset.recordId = String(period.id);
                    for (const [name, value] of [["StartDate", period.startDate], ["LastDay", period.lastDay], ["Reason", period.reason || ""]]) {
                        const input = form.elements.namedItem(name);
                        input.value = value;
                        input.defaultValue = value;
                    }
                    row.querySelector("[data-reason-label]").hidden = group !== "blocks";
                    row.querySelector("[data-period-delete]").dataset.group = group;
                    row.querySelector("[data-period-delete]").dataset.recordId = String(period.id);
                    for (const button of row.querySelectorAll("button")) button.dataset.editorWrite = "";
                    restorePeriod(form, retained, changedKey);
                    if (archived) for (const details of row.querySelectorAll("details")) details.remove();
                    list.append(row);
                }
                if (state[group].length === 0) {
                    const empty = document.createElement("p");
                    empty.className = "form-text";
                    empty.textContent = group === "windows" ? text("ListingEditor.NoAvailabilityWindowsYet") : text("ListingEditor.NoBlockedDates");
                    list.append(empty);
                }
            }
            updateControls();
        }

        /** Performs existing image mutations after saving the currently entered basic information. */
        async function changePhoto(id, action) {
            await saveFields();
            const data = mutationData();
            let suffix = "/" + id + "/" + (action === "primary" ? "primary" : "delete");
            if (action === "up" || action === "down") {
                const ids = state.images.map(photo => photo.id);
                const index = ids.indexOf(id);
                const next = index + (action === "up" ? -1 : 1);
                if (index < 0 || next < 0 || next >= ids.length) return;
                [ids[index], ids[next]] = [ids[next], ids[index]];
                for (const imageId of ids) data.append("ImageIds", String(imageId));
                suffix = "/order";
            }
            await request("/my/listings/" + state.listingId + "/images" + suffix, data);
            await refresh("photos");
            focusAfterAction = action === "delete" ? state.images[0]?.id ?? null : id;
            announce(text(state.status === "Draft" ? "ListingEditor.PhotoSavedDraft" : "ListingEditor.PhotoChangesSaved"));
        }

        /** Submits a captured period to the existing owner endpoint without adjusting its exclusive end. */
        async function changePeriod(form, remove = false, group = form?.dataset.group, recordId = form?.dataset.recordId) {
            const data = form ? new FormData(form) : mutationData();
            const captured = form ? JSON.stringify(Array.from(new FormData(form).entries())) : null;
            data.set("__RequestVerificationToken", token());
            await saveFields();
            const suffix = recordId ? "/" + recordId + (remove ? "/remove" : "/edit") : "";
            await request("/my/listings/" + state.listingId + "/availability/" + group + suffix, data);
            const unchanged = !form || JSON.stringify(Array.from(new FormData(form).entries())) === captured;
            if (form && !recordId && unchanged) { form.reset(); }
            await refresh("dates", unchanged ? group + ":" + recordId : null);
            announce(text(!unchanged ? "ListingEditor.AvailabilitySavedNewer" : state.status === "Draft" ? "ListingEditor.AvailabilitySavedDraft" : "ListingEditor.AvailabilitySaved"));
        }

        /** Persists a complete drag or touch order before refreshing the saved thumbnail positions. */
        async function savePhotoOrder(ids, moving) {
            await saveFields();
            const data = mutationData();
            for (const id of ids) data.append("ImageIds", String(id));
            await request("/my/listings/" + state.listingId + "/images/order", data);
            await refresh("photos");
            focusAfterAction = moving;
            announce(text("ListingEditor.PhotoOrderSavedPrimaryPhotoUnchanged"));
        }

        /** Uploads the captured selection in sequence and reports confirmed, rejected and uncertain outcomes separately. */
        async function uploadPhotos() {
            if (!uploadQueue?.hasPending()) throw new Error(text("ListingEditor.ChooseAtLeastOneValidPhotoToUpload"));
            const alt = upload.elements.namedItem("AltText").value;
            await saveFields();
            const result = await uploadQueue.run(async (file, progress) => {
                const data = mutationData();
                data.set("File", file, file.name);
                data.set("AltText", alt);
                return window.RentRoadUploadQueue.send("/my/listings/" + state.listingId + "/images/upload", data, progress);
            });
            await refresh("photos");
            announce(text(result.uncertain ? "ListingEditor.UploadSummaryUncertain" : "ListingEditor.UploadSummary", result.uploaded, result.rejected), false, result.rejected > 0 || result.uncertain);
        }

        /** Builds safe, presentation-only previews from current unsaved fields or a frozen server review. */
        function showPreview(mode, reviewed = null) {
            dialog.dataset.previewMode = mode;
            const input = reviewed?.fields || currentFields();
            const source = reviewed || state;
            const optionText = (name, value) => Array.from(fields.elements.namedItem(name).options).find(option => option.value === value)?.textContent || text("ListingEditor.NotSelected");
            const amount = Number(input.dailyPriceCad);
            const values = {
                title: input.title?.trim() || text("MyListings.Untitled"), description: input.description || text("ListingEditor.NoDescriptionYet"),
                category: optionText("Category", input.category), condition: optionText("Condition", input.condition),
                location: input.pickupArea || text("MyListings.NoPickupArea"), owner: source?.ownerDisplayName || text("ListingDetails.OwnerFallback"),
                price: input.dailyPriceCad !== "" && input.dailyPriceCad !== null && Number.isFinite(amount) ? text("Currency.CadAmount", amount) : text("MyListings.NoPrice")
            };
            for (const [key, value] of Object.entries(values)) for (const node of dialog.querySelectorAll("[data-preview-" + key + "]")) node.textContent = value;
            const photos = [...(source?.images || [])].sort((a, b) => Number(b.isPrimary) - Number(a.isPrimary) || a.displayOrder - b.displayOrder || a.id - b.id);
            if (!reviewed) photos.push(...(uploadQueue?.photos() || []));
            window.RentRoadGallery.setPhotos(dialog.querySelector("[data-preview-card-gallery]"), photos, { compact: true, title: values.title });
            window.RentRoadGallery.setPhotos(dialog.querySelector("[data-preview-details-gallery]"), photos, { title: values.title });
            for (const group of ["windows", "blocks"]) {
                const list = dialog.querySelector("[data-preview-" + group + "]");
                list.replaceChildren();
                for (const period of source?.[group] || []) {
                    const entry = document.createElement("li");
                    entry.textContent = period.displayRange;
                    list.append(entry);
                }
                if (!list.children.length) { const entry = document.createElement("li"); entry.textContent = text("ListingEditor.NoneAdded"); list.append(entry); }
            }
            dialog.querySelector("[data-card-preview]").hidden = mode !== "card";
            dialog.querySelector("[data-details-preview]").hidden = mode === "card";
            dialog.querySelector("[data-review-footer]").hidden = mode !== "review";
            dialog.querySelector("[data-review-errors]").hidden = true;
            dialog.querySelector("[data-review-success]").hidden = true;
            dialog.querySelector("[data-preview-context]").textContent = text(mode === "review"
                ? uploadQueue?.hasPending() ? "ListingEditor.ReviewContextPending" : "ListingEditor.CheckTheseSavedPhotosAndProposedDetailsNothingIs"
                : uploadQueue?.hasPending() ? "ListingEditor.PreviewContextPending" : "ListingEditor.PreviewContext");
            dialog.querySelector("#editor-dialog-title").textContent = mode === "review" ? text("ListingEditor.ReviewHeading") : mode === "card" ? text("ListingEditor.BrowseCardPreview") : text("ListingEditor.DetailsPreview");
            if (!dialog.open) dialog.showModal();
            updateControls();
        }

        /** Reviews captured form values and displays authoritative missing publication requirements. */
        async function reviewListing() {
            const captured = signature();
            const wasNew = !state.listingId;
            const result = await request("/my/listings/editor/" + state.token + "/review", new FormData(fields));
            remember(result.review.state);
            if (wasNew) savedSignature = captured;
            if (signature() !== captured) throw new Error(text("ListingEditor.YourEditsChangedWhilePreparingReviewChooseUploadListing"));
            nonce = result.review.nonce;
            showPreview("review", result.review.state);
            const panel = dialog.querySelector("[data-review-errors]");
            const list = panel.querySelector("ul");
            list.replaceChildren();
            for (const error of result.review.errors) { const entry = document.createElement("li"); entry.textContent = error.message; list.append(entry); }
            panel.hidden = result.review.errors.length === 0;
            updateControls();
        }

        /** Confirms the server-reviewed snapshot and gives a secure public link only after verified success. */
        async function confirmListing() {
            const data = mutationData();
            data.set("nonce", nonce);
            const result = await request("/my/listings/editor/" + state.token + "/confirm", data);
            remember(result.state);
            savedSignature = signature();
            nonce = null;
            announce(text("ListingEditor.YourListingIsPublished"), true);
            const success = dialog.querySelector("[data-review-success]");
            success.replaceChildren(...Array.from(notice.childNodes).map(node => node.cloneNode(true)));
            success.hidden = false;
            updateControls();
        }

        /** Intercepts only editor-owned forms, leaving separate archive and Identity forms intact. */
        function handleSubmit(event) {
            const form = event.target;
            if (form !== fields && form !== upload && !form.matches("[data-editor-period]")) return;
            event.preventDefault();
            if (!form.checkValidity()) { form.reportValidity(); return; }
            if (form === fields) {
                perform(async () => { await saveFields(); announce(state.status === "Published" ? text("ListingEditor.ChangesSavedToYourPublishedListing") : text("ListingEditor.DraftSavedOnlyYouCanSeeItUntilPublication")); });
            } else if (form === upload) {
                perform(uploadPhotos, form);
            } else perform(() => changePeriod(form), form);
        }

        /** Delegates accessible buttons to existing mutation contracts and private dialogs. */
        function handleClick(event) {
            const button = event.target.closest("button");
            if (!button || !root.contains(button)) return;
            if (button.hasAttribute("data-editor-close")) { dialog.close(); return; }
            if (button.dataset.editorPreview) { nonce = null; showPreview(button.dataset.editorPreview); return; }
            if (button.hasAttribute("data-editor-review")) perform(reviewListing);
            if (button.hasAttribute("data-editor-confirm")) perform(confirmListing);
            if (button.dataset.photoAction) perform(() => changePhoto(Number(button.dataset.imageId), button.dataset.photoAction));
            if (button.hasAttribute("data-period-delete")) perform(() => changePeriod(null, true, button.dataset.group, button.dataset.recordId));
        }

        /** Invalidates confirmation after editing any submitted value. */
        function handleInput(event) {
            nonce = null;
        }

        /** Warns before leaving while current scalar fields or an upload selection remain unsaved. */
        function protectUnsaved(event) {
            const pendingDates = Array.from(root.querySelectorAll("form[data-editor-period] input")).some(input => input.type !== "hidden" && input.value !== input.defaultValue);
            if (!archived && (busy || signature() !== savedSignature || uploadQueue?.hasPending() || pendingDates)) { event.preventDefault(); event.returnValue = ""; }
        }

        root.addEventListener("submit", handleSubmit);
        root.addEventListener("click", handleClick);
        root.addEventListener("input", handleInput);
        dialog.addEventListener("close", () => { nonce = null; });
        window.addEventListener("beforeunload", protectUnsaved);
        updateControls();
        try {
            let existingToken = new URL(location.href).searchParams.get("editor");
            if (!existingToken && !Number(root.dataset.listingId)) { try { existingToken = sessionStorage.getItem(storageKey); } catch { } }
            const query = new URLSearchParams();
            if (existingToken) query.set("token", existingToken);
            if (Number(root.dataset.listingId)) query.set("listingId", root.dataset.listingId);
            const opened = await request("/my/listings/editor/session?" + query);
            if (!Number(root.dataset.listingId) && opened.state.listingId && Object.values(currentFields()).every(value => value === "")) {
                for (const [name, value] of Object.entries(opened.state.fields)) {
                    const field = fields.elements.namedItem(name[0].toUpperCase() + name.slice(1));
                    if (field) field.value = value ?? "";
                }
                savedSignature = signature();
            }
            remember(opened.state);
            if (!state.listingId) {
                history.replaceState(null, "", "/my/listings/create?editor=" + encodeURIComponent(state.token));
                try { sessionStorage.setItem(storageKey, state.token); } catch { }
            }
            if (upload) root.querySelector("[data-editor-file-help]").textContent = text("ListingEditor.UploadLimit", (opened.maxImageSizeBytes / 1048576).toLocaleString(document.documentElement?.lang, { maximumFractionDigits: 2 }));
            if (upload) {
                uploadQueue = window.RentRoadUploadQueue.connect(upload, { maxSizeBytes: opened.maxImageSizeBytes, changed: () => { nonce = null; updateControls(); } });
                if (fileInput.files?.length) uploadQueue.add(fileInput.files);
            }
            window.RentRoadImageOrdering.connect(root.querySelector("[data-editor-photos]"), {
                canMove: () => ready && !busy && !archived && state?.status !== "Archived",
                save: (ids, moving) => perform(() => savePhotoOrder(ids, moving))
            });
            ready = true;
            renderPhotos();
            renderPeriods();
        } catch (error) { showFailure(error); }
        updateControls();
    }

    for (const root of document.querySelectorAll("[data-listing-editor]")) connectEditor(root);
})();
