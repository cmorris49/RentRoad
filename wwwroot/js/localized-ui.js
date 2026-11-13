/**
 * Makes server-resolved shared resources available to the existing frontend without a second translation
 *   catalogue.
 *
 * @returns {void} Does not return a value.
 */
(function connectLocalizedUi() {
    "use strict";
    const payload = document.getElementById("rentroad-ui-strings");
    const strings = payload ? JSON.parse(payload.textContent) : {};

    /**
     * Formats server-provided text as plain text, returning the key when configuration is missing.
     *
     * @param {string} key - Resource or entry key to resolve.
     * @param {...unknown} values - Values substituted into the resource template.
     * @returns {string} The formatted resource text, or the resource key when unavailable.
     */
    window.RentRoadText = function resourceText(key, ...values) {
        const template = Object.prototype.hasOwnProperty.call(strings, key) ? strings[key] : key;

        return template.replace(
            /\{(\d+)(?::N(\d+))?\}/g,
            (placeholder, index, precision) => {
                const value = values[Number(index)];

                if (value === undefined) return placeholder;

                return typeof value === "number"
                    ? value.toLocaleString(
                        document.documentElement?.lang,
                        precision === undefined
                            ? undefined
                            : { minimumFractionDigits: Number(precision), maximumFractionDigits: Number(precision) })
                    : String(value);
            });
    };
})();
