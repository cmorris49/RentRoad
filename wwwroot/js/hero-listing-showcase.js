/**
 * Animates the initial public Home selection without requests or rebuilding listing cards.
 *
 * @returns {void} Does not return a value.
 */
(function initializeHeroShowcases() {
    "use strict";

    const ROTATION_INTERVAL_MS = 5000;
    const TRANSITION_DURATION_MS = 550;

    /**
     * Maintains stable card identities, interaction pauses and a single rotation timer.
     */
    class HeroListingShowcase {
        /**
         * Connects a server-rendered showcase and its page-local lifecycle.
         *
         * @param {HTMLElement} root - The Home showcase section.
         */
        constructor(root) {
            this.root = root;
            this.cards = [...root.querySelectorAll("[data-showcase-card]")];
            this.controls = root.querySelector("[data-showcase-controls]");
            this.toggle = root.querySelector("[data-showcase-toggle]");
            this.previous = root.querySelector("[data-showcase-previous]");
            this.next = root.querySelector("[data-showcase-next]");
            this.centre = Number(root.dataset.showcaseCentre ?? 0);
            this.motion = window.matchMedia("(prefers-reduced-motion: reduce)");
            this.desktop = window.matchMedia("(min-width: 1200px)");
            this.hovered = root.matches(":hover");
            this.focused = root.contains(document.activeElement);
            this.paused = root.dataset.showcasePaused === "true";
            this.disposed = false;
            this.timer = null;
            this.settling = null;
            this.listeners = [];
            root.style.setProperty("--showcase-motion-duration", `${TRANSITION_DURATION_MS}ms`);

            this.listen(root, "mouseenter", () => {
                this.hovered = true;
                this.sync();
            });
            this.listen(root, "mouseleave", () => {
                this.hovered = false;
                this.sync();
            });
            this.listen(root, "focusin", () => {
                this.focused = true;
                this.sync();
            });
            this.listen(root, "focusout", event => {
                this.focused = root.contains(event.relatedTarget);
                this.sync();
            });
            this.listen(this.toggle, "click", () => {
                this.paused = !this.paused;
                root.dataset.showcasePaused = String(this.paused);
                this.sync();
            });
            this.listen(this.previous, "click", () => this.move(-1));
            this.listen(this.next, "click", () => this.move(1));
            this.listen(this.motion, "change", () => this.sync());
            this.listen(this.desktop, "change", () => {
                this.hovered = root.matches(":hover");
                this.focused = root.contains(document.activeElement);
                this.sync();
            });
            this.listen(document, "visibilitychange", () => this.sync());
            this.render();
            this.sync();
        }

        /**
         * Records a listener so page teardown can release the exact registered callback.
         *
         * @param {EventTarget|null} target - The element, document or media query to observe.
         * @param {string} name - Native event name.
         * @param {Function} handler - Callback registered for this instance.
         * @returns {void} Does not return a value.
         */
        listen(target, name, handler) {
            if (!target) return;

            target.addEventListener(name, handler);
            this.listeners.push({ target, name, handler });
        }

        /**
         * Finds a card index around the existing ordered public collection.
         *
         * @param {number} offset - Signed distance from the featured card.
         * @returns {number} The wrapped card index.
         */
        index(offset) {
            return (this.centre + offset + this.cards.length) % this.cards.length;
        }

        /**
         * Loads only the selected listing's first photo and retains the existing missing-image treatment.
         *
         * @param {HTMLElement} card - The card being shown or prepared for the next transition.
         * @returns {void} Does not return a value.
         */
        loadImage(card) {
            const image = card.querySelector("[data-showcase-image]");

            if (!image || image.dataset.showcaseLoaded) return;

            image.dataset.showcaseLoaded = "true";
            const fallback = card.querySelector("[data-image-fallback]");

            this.listen(image, "error", () => {
                image.hidden = true;
                fallback.removeAttribute("aria-hidden");
            });

            if (!image.getAttribute("src")) {
                image.src = image.dataset.showcaseSrc;
            } else if (image.complete && image.naturalWidth === 0) {
                image.hidden = true;
                fallback.removeAttribute("aria-hidden");
            }
        }

        /**
         * Positions one existing card and excludes background transition cards from keyboard navigation.
         *
         * @param {number} index - The existing card index.
         * @param {string} position - Visual position within the showcase.
         * @param {boolean} interactive - Whether the visible card can receive keyboard focus.
         * @returns {void} Does not return a value.
         */
        position(index, position, interactive) {
            const card = this.cards[index];

            card.hidden = false;
            card.dataset.position = position;
            card.setAttribute("aria-hidden", String(!interactive));
            card.tabIndex = interactive ? 0 : -1;
            card.inert = !interactive;
            this.loadImage(card);
        }

        /**
         * Restores the stable three-card arrangement, or the small collection's static arrangement.
         *
         * @returns {void} Does not return a value.
         */
        render() {
            if (!this.cards.length) return;

            const positions = new Map();

            if (this.desktop.matches) {
                positions.set(this.centre, "centre");

                if (this.cards.length > 1) {
                    positions.set(this.index(1), "left");

                    if (this.cards.length > 2) {
                        positions.set(this.index(-1), "right");
                    }
                }
            }

            for (const [index, card] of this.cards.entries()) {
                card.removeAttribute("data-recycling");

                if (positions.has(index)) {
                    this.position(index, positions.get(index), true);
                } else {
                    card.hidden = true;
                    card.inert = true;
                    card.tabIndex = -1;
                    card.setAttribute("aria-hidden", "true");
                }
            }
        }

        /**
         * Updates localized icon controls without replacing their elements or moving keyboard focus.
         *
         * @returns {void} Does not return a value.
         */
        updateControls() {
            if (!this.controls) return;

            this.controls.hidden = !this.desktop.matches || this.cards.length < 3;
            this.toggle.hidden = this.motion.matches;
            const label = this.paused ? this.toggle.dataset.playLabel : this.toggle.dataset.pauseLabel;

            this.toggle.setAttribute("aria-label", label);
            this.toggle.setAttribute("title", label);
            this.toggle.setAttribute("aria-pressed", String(this.paused));
            this.toggle.querySelector("[data-showcase-pause-icon]").hidden = this.paused;
            this.toggle.querySelector("[data-showcase-play-icon]").hidden = !this.paused;

            for (const control of [this.previous, this.next]) {
                control.setAttribute("aria-disabled", String(this.settling !== null));
            }
        }

        /**
         * Reconciles motion preferences, manual choice, interaction and visibility without duplicate timers.
         *
         * @returns {void} Does not return a value.
         */
        sync() {
            window.clearInterval(this.timer);
            this.timer = null;

            if (this.disposed) return;

            const available = this.cards.length >= 3 && !this.motion.matches && this.desktop.matches;

            if ((!this.desktop.matches || this.motion.matches) && this.settling !== null) {
                window.clearTimeout(this.settling);
                this.settling = null;
            }

            this.updateControls();

            if (!this.settling) {
                this.render();
            }

            if (!available || this.paused || this.hovered || this.focused || document.hidden) return;

            if (!this.settling) {
                this.prepare();
            }

            this.timer = window.setInterval(() => this.rotate(), ROTATION_INTERVAL_MS);
        }

        /**
         * Commits one offstage position before movement without exposing its link to keyboard navigation.
         *
         * @param {number} direction - One for forward movement, or minus one for backward movement.
         * @returns {void} Does not return a value.
         */
        prepare(direction = 1) {
            if (this.cards.length > 3) {
                const index = this.index(2 * direction);
                const card = this.cards[index];

                card.setAttribute("data-preparing", "");
                this.position(index, direction === 1 ? "incoming" : "incoming-right", false);
                card.getBoundingClientRect();
                card.removeAttribute("data-preparing");
            }
        }

        /**
         * Performs one manual step without changing the user's explicit Pause choice.
         *
         * @param {number} direction - One for Next, or minus one for Previous.
         * @returns {void} Does not return a value.
         */
        move(direction) {
            if (this.disposed || this.settling !== null || !this.desktop.matches || this.cards.length < 3) return;

            window.clearInterval(this.timer);
            this.timer = null;
            this.rotate(direction);
        }

        /**
         * Moves existing cards through the three positions, reversing direction for Previous navigation.
         *
         * @param {number} direction - One for rightward forward rotation, or minus one for backward rotation.
         * @returns {void} Does not return a value.
         */
        rotate(direction = 1) {
            if (this.root.isConnected === false) {
                this.dispose();
                return;
            }

            if (this.disposed || this.settling !== null || !this.desktop.matches || this.cards.length < 3) return;

            if (this.motion.matches) {
                this.centre = this.index(direction);
                this.root.dataset.showcaseCentre = String(this.centre);
                this.render();
                this.sync();
                return;
            }

            const outgoing = this.index(-direction);
            const featured = this.index(direction);
            const incoming = this.index(2 * direction);

            this.prepare(direction);
            this.position(this.centre, direction === 1 ? "right" : "left", true);
            this.position(featured, "centre", true);

            if (this.cards.length === 3) {
                this.cards[outgoing].setAttribute("data-recycling", direction === 1 ? "forward" : "backward");
                this.position(outgoing, direction === 1 ? "left" : "right", true);
            } else {
                this.position(outgoing, direction === 1 ? "outgoing" : "outgoing-left", false);
                this.position(incoming, direction === 1 ? "left" : "right", true);
            }

            this.centre = featured;
            this.root.dataset.showcaseCentre = String(featured);
            this.settling = window.setTimeout(() => {
                this.settling = null;
                this.render();
                this.updateControls();

                if (this.timer !== null) {
                    this.prepare();
                } else {
                    this.sync();
                }
            }, TRANSITION_DURATION_MS);
            this.updateControls();
        }

        /**
         * Releases timers and listeners when the page leaves or the component is detached.
         *
         * @returns {void} Does not return a value.
         */
        dispose() {
            this.disposed = true;
            window.clearInterval(this.timer);
            window.clearTimeout(this.settling);
            this.render();

            for (const { target, name, handler } of this.listeners) {
                target.removeEventListener(name, handler);
            }

            this.listeners = [];
        }
    }

    let instances = [];

    /**
     * Connects Home showcases once, including restoration from the browser back-forward cache.
     *
     * @returns {void} Does not return a value.
     */
    function connect() {
        if (instances.length) return;

        instances = [...document.querySelectorAll("[data-hero-showcase]")]
            .map(root => new HeroListingShowcase(root));
    }

    window.addEventListener("pagehide", () => {
        for (const instance of instances) {
            instance.dispose();
        }

        instances = [];
    });
    window.addEventListener("pageshow", connect);
    window.RentRoadHeroShowcase = { HeroListingShowcase };
    connect();
})();
