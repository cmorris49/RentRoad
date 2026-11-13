/**
 * Enhances the existing rental form with calendar guidance and exact, display-only integer-cent estimates.
 *
 * @returns {void} Does not return a value.
 */
(function initializeRentalRequest() {
    "use strict";
    const MAX_CENTS = 9223372036854775807n;
    const MAX_DAY = "9999-12-30";
    const text = window.RentRoadText;

    /**
     * Parses a genuine ISO calendar day without local time shifts or the Date constructor's year-0-to-99
     *   shortcut.
     *
     * @param {string} value - ISO calendar-date input to validate.
     * @returns {number|null} The UTC calendar-day index, or null when the input is invalid.
     */
    function calendarDay(value) {
        if (!/^\d{4}-\d{2}-\d{2}$/.test(value) || value < "0001-01-02" || value > MAX_DAY) {
            return null;
        }

        const [year, month, day] = value.split("-").map(Number);
        const date = new Date(0);

        date.setUTCFullYear(year, month - 1, day);
        date.setUTCHours(0, 0, 0, 0);

        return date.getUTCFullYear() === year && date.getUTCMonth() === month - 1 && date.getUTCDate() === day
            ? Math.floor(date.getTime() / 86400000)
            : null;
    }

    /**
     * Formats exact cents using the shared CAD template, converting only punctuation through Intl rather than
     *   rounding money.
     *
     * @param {bigint} cents - Exact amount in integer cents.
     * @returns {string} The localized CAD amount without rounding the cent value.
     */
    function formatCents(cents) {
        const language = document.documentElement.lang;
        const decimal = new Intl.NumberFormat(language).formatToParts(0.1).find(part => part.type === "decimal").value;
        const amount = (cents / 100n).toLocaleString(language) + decimal + (cents % 100n).toString().padStart(2, "0");

        return text("Currency.CadAmount", amount);
    }

    /**
     * Updates one existing form without submitting calculated prices or replicating availability decisions.
     *
     * @param {HTMLFormElement} form - Rendered form used by this component.
     * @returns {void} Does not return a value.
     */
    function connectForm(form) {
        const first = form.querySelector('[name="FirstDay"]');
        const last = form.querySelector('[name="LastDay"]');
        const page = form.closest(".rental-request-page");
        const daysOutput = page.querySelector("[data-estimate-days]");
        const totalOutput = page.querySelector("[data-estimate-total]");
        const status = page.querySelector("[data-estimate-status]");
        const submit = form.querySelector("[data-request-submit]");
        const minimum = form.dataset.minimumFirstDay;
        const price = /^\d+$/.test(form.dataset.dailyCents) ? BigInt(form.dataset.dailyCents) : 0n;
        let submitting = false;

        /**
         * Refreshes inclusive dates, dynamic minimums and accessible estimates, leaving availability to the
         *   server.
         *
         * @returns {void} Does not return a value.
         */
        function updateEstimate() {
            const firstDay = calendarDay(first.value);
            const lastDay = calendarDay(last.value);

            first.min = minimum;
            last.min = firstDay !== null && first.value >= minimum ? first.value : minimum;
            first.setCustomValidity(first.value && firstDay === null
                ? text("RentalRequestUi.DateInvalid")
                : firstDay !== null && first.value < minimum ? text("RentalRequest.FutureStartRequired") : "");
            last.setCustomValidity(last.value && lastDay === null
                ? text("RentalRequestUi.DateInvalid")
                : firstDay !== null && lastDay !== null && lastDay < firstDay
                    ? text("Availability.LastDayBeforeFirst")
                    : "");
            page.querySelector("[data-estimate-first]").textContent = firstDay === null ? "—" : first.value;
            page.querySelector("[data-estimate-last]").textContent = lastDay === null ? "—" : last.value;
            daysOutput.textContent = "—";
            totalOutput.textContent = "—";

            if (!first.value || !last.value) {
                status.textContent = text("RentalRequestUi.ChooseDates");

                return;
            }

            if (firstDay === null || lastDay === null) {
                status.textContent = text("RentalRequestUi.DateInvalid");

                return;
            }

            if (lastDay < firstDay) {
                status.textContent = text("Availability.LastDayBeforeFirst");

                return;
            }

            const days = lastDay - firstDay + 1;
            const duration = text(days === 1 ? "RentalRequest.DurationOne" : "RentalRequest.DurationMany", days);

            daysOutput.textContent = duration;

            if (price <= 0n || price > MAX_CENTS) {
                status.textContent = text("RentalRequest.InvalidPrice");

                return;
            }

            const total = price * BigInt(days);

            if (total > MAX_CENTS) {
                status.textContent = text("RentalRequest.PriceOverflow");

                return;
            }

            totalOutput.textContent = formatCents(total);
            status.textContent = text("RentalRequestUi.EstimateAnnouncement", duration, totalOutput.textContent);
        }

        /**
         * Prevents repeated clicks only after browser and existing unobtrusive validation have accepted the
         *   form.
         *
         * @param {SubmitEvent} event - Native event supplied to this handler.
         * @returns {void} Does not return a value.
         */
        function submitRequest(event) {
            updateEstimate();

            if (submitting) {
                event.preventDefault();

                return;
            }

            if (event.defaultPrevented || !form.reportValidity() || window.jQuery && !window.jQuery(form).valid()) {
                event.preventDefault();

                return;
            }

            submitting = true;
            submit.disabled = true;
            submit.textContent = text("RentalRequestUi.Submitting");
            form.setAttribute("aria-busy", "true");
        }

        /**
         * Restores an operable form after back/forward-cache navigation without automatically retrying a
         *   request.
         *
         * @returns {void} Does not return a value.
         */
        function restoreForm() {
            submitting = false;
            submit.disabled = false;
            submit.textContent = text("RentalRequestUi.Submit");
            form.removeAttribute("aria-busy");
        }

        first.addEventListener("input", updateEstimate);
        last.addEventListener("input", updateEstimate);
        form.addEventListener("submit", submitRequest);
        window.addEventListener("pageshow", restoreForm);
        updateEstimate();
        const errors = form.querySelector("[data-request-errors]");

        if (errors?.classList.contains("validation-summary-errors")) {
            errors.focus();
        }
    }

    for (const form of document.querySelectorAll("[data-rental-request]")) {
        connectForm(form);
    }
})();
