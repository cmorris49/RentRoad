/**
 * Connects inline review panels, native five-radio ratings and progressive form submission.
 *
 * @returns {void} Does not return a value.
 */
(function connectReviewForms() {
    "use strict";

    /**
     * Updates one panel and the trigger's expanded state without moving other card actions.
     *
     * @param {HTMLElement} panel - The dedicated review panel.
     * @param {boolean} open - Whether to expose the panel.
     * @returns {void} Does not return a value.
     */
    function showPanel(panel, open) {
        panel.hidden = !open;
        const trigger = [...document.querySelectorAll("[data-review-toggle]")]
            .find(link => link.getAttribute("aria-controls") === panel.id);

        trigger?.setAttribute("aria-expanded", String(open));

        if (open) {
            panel.querySelector('select, input[type="radio"]:checked, input[type="radio"]')?.focus();
        } else {
            trigger?.focus();
        }
    }

    for (const trigger of document.querySelectorAll("[data-review-toggle]")) {
        trigger.addEventListener("click", event => {
            const panel = document.getElementById(trigger.getAttribute("aria-controls"));

            if (!panel) return;

            event.preventDefault();
            showPanel(panel, panel.hidden);
        });
    }

    for (const panel of document.querySelectorAll("[data-review-panel]")) {
        panel.querySelector("[data-review-cancel]")?.addEventListener("click", event => {
            const trigger = [...document.querySelectorAll("[data-review-toggle]")]
                .some(link => link.getAttribute("aria-controls") === panel.id);

            if (!trigger) return;

            event.preventDefault();
            showPanel(panel, false);
        });
    }

    for (const group of document.querySelectorAll("[data-review-rating]")) {
        const stars = [...group.querySelectorAll("[data-rating-value]")];

        for (const star of stars) {
            star.addEventListener("mouseenter", () => {
                group.dataset.preview = "true";

                for (const candidate of stars) {
                    candidate.dataset.filled = String(Number(candidate.dataset.ratingValue) <= Number(star.dataset.ratingValue));
                }
            });
        }

        group.addEventListener("mouseleave", () => {
            delete group.dataset.preview;
        });
        group.addEventListener("change", () => {
            delete group.dataset.preview;
        });
    }

    for (const form of document.querySelectorAll("[data-review-form]")) {
        form.addEventListener("submit", async event => {
            event.preventDefault();

            if (form.dataset.submitting === "true" || !form.reportValidity()) return;

            const body = new FormData(form);
            const button = form.querySelector('button[type="submit"]');
            const errors = form.querySelector("[data-review-errors]");
            form.dataset.submitting = "true";
            button.disabled = true;
            errors.replaceChildren();

            for (const invalid of form.querySelectorAll('[aria-invalid="true"]')) {
                invalid.removeAttribute("aria-invalid");
            }

            try {
                const response = await fetch(form.action, {
                    method: "POST", body, credentials: "same-origin", headers: { Accept: "application/json" }
                });
                const result = await response.json();

                if (response.ok && result.redirectUrl) {
                    window.location.assign(result.redirectUrl);
                    return;
                }

                for (const error of result.errors ?? []) {
                    const message = document.createElement("p");
                    message.textContent = error.message;
                    errors.append(message);
                    const field = form.querySelector(`[name="${error.field === "Comment" ? "Comment" : error.field === "Rating" ? "Rating" : "RentalRequestId"}"]`);
                    field?.setAttribute("aria-invalid", "true");
                }

                if (!errors.childElementCount) throw new Error("review-response");

                errors.tabIndex = -1;
                errors.focus();
            } catch {
                errors.textContent = form.dataset.failureMessage;
                errors.tabIndex = -1;
                errors.focus();
            } finally {
                form.dataset.submitting = "false";
                button.disabled = false;
            }
        });
    }

    for (const form of document.querySelectorAll("[data-review-delete]")) {
        form.addEventListener("submit", event => {
            if (form.dataset.submitting === "true") {
                event.preventDefault();
                return;
            }

            form.dataset.submitting = "true";
            form.querySelector('button[type="submit"]').disabled = true;
        });
    }
})();
