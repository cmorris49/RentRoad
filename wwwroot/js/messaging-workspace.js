/**
 * Progressively enhances authorized messaging routes into one viewport-sized, history-aware workspace.
 *
 * @returns {void} Does not return a value.
 */
(function messagingWorkspaceModule() {
    "use strict";
    const REFRESH_INTERVAL = 30000;

    /**
     * Resolves text through the existing server-provided resource formatter.
     *
     * @param {...unknown} args - Arguments forwarded to the wrapped operation.
     * @returns {string} The formatted resource text.
     */
    const text = (...args) => window.RentRoadText(...args);

    /**
     * Coordinates route navigation, removable conversation consumers and in-memory unsent drafts.
     */
    class MessagingWorkspace {
        /**
         * Captures only the existing server-rendered workspace and safe MVC transport configuration.
         *
         * @param {HTMLElement} root - Rendered root used by this component.
         * @param {object} config - Server-provided configuration for this component.
         */
        constructor(root, config) {
            this.root = root;
            this.config = config;
            this.currentUrl = window.location.href;
            this.drafts = new Map();
            this.navigation = 0;
            this.disposed = false;
        }

        /**
         * Starts one workspace lifecycle while leaving ordinary links functional when enhancement is
         *   unavailable.
         *
         * @returns {Promise<void>} Resolves when this operation completes.
         */
        async start() {
            this.root.addEventListener("click", event => this.click(event));
            window.addEventListener("popstate", () => this.navigate(window.location.href, false));
            window.addEventListener("focus", () => this.resume());
            document.addEventListener("visibilitychange", () => this.resume());
            window.addEventListener("resize", () => this.resize());
            window.visualViewport?.addEventListener("resize", () => this.resize());
            window.visualViewport?.addEventListener("scroll", () => this.resize());
            this.root.addEventListener("focusin", () => this.resize());
            this.root.addEventListener("focusout", () => window.requestAnimationFrame(() => this.resize()));
            window.addEventListener("pagehide", () => this.stop());
            window.addEventListener("pageshow", event => {
                if (event.persisted) {
                    window.location.reload();
                }
            });
            window.history.replaceState({ messaging: true }, "", this.currentUrl);
            this.resize();
            this.prepareSidebar();
            await this.activate(this.config, false);
            this.poll = window.setInterval(() => this.resume(), REFRESH_INTERVAL);

            try {
                const transport = window.RentRoadMessagingTransport;

                this.unsubscribe = transport.subscribe(
                    this.config.realtime,
                    {
                        hint: payload => {
                            if (Number.isSafeInteger(payload?.conversationId) && payload.conversationId > 0
                                && Number.isSafeInteger(payload?.messageId)
                                && payload.messageId > 0) {
                                this.sidebar.refreshInbox();
                            }
                        },

                        /**
                         * Refreshes the sidebar and reports reconnection when no conversation is active.
                         *
                         * @returns {void} Does not return a value.
                         */
                        reconnected: () => {
                            this.sidebar.refreshInbox();

                            if (!this.current) {
                                this.emptyStatus("Messages.Reconnected");
                            }
                        },
                        reconnecting: () => {
                            if (!this.current) {
                                this.emptyStatus("Messages.Reconnecting");
                            }
                        },
                        closed: () => {
                            if (!this.current) {
                                this.emptyStatus("Messages.Disconnected", true);
                            }
                        }
                    });
                await transport.start(this.config.realtime);

                if (!this.current) {
                    this.emptyStatus("Messages.Connected");
                }
            } catch {
                if (!this.current) {
                    this.emptyStatus("Messages.Disconnected", true);
                }
            }
        }

        /**
         * Reuses existing bounded inbox refresh code against only the independently scrolling sidebar.
         *
         * @returns {void} Does not return a value.
         */
        prepareSidebar() {
            this.sidebar = new window.RentRoadMessaging.MessagingPage(
                this.root.querySelector("[data-conversation-sidebar]"),
                this.config);
            this.sidebar.onInboxUpdated = () => this.select();
            this.sidebar.onInboxError = value => this.error(value);
            this.sidebar.localizeTimes(this.sidebar.root);
            this.sidebar.prepareImages();
            this.select();
        }

        /**
         * Intercepts only ordinary same-origin messaging links; modified clicks retain normal browser
         *   behavior.
         *
         * @param {MouseEvent} event - Native event supplied to this handler.
         * @returns {void} Does not return a value.
         */
        click(event) {
            const link = event.target.closest("a[data-workspace-link]");

            if (link && !event.defaultPrevented && !event.ctrlKey && !event.metaKey && !event.shiftKey && !event.altKey
                && event.button === 0) {
                const url = new URL(link.href, window.location.href);

                if (url.origin !== window.location.origin) return;

                event.preventDefault();
                this.navigate(url.href, true);
            }

            if (!this.current && event.target.closest("[data-reconnect]")) {
                this.reconnect();
            }
        }

        /**
         * Fetches the same authorized MVC route and replaces only workspace panels, preserving browser
         *   navigation and exact draft tokens.
         *
         * @param {string} address - Messaging route to load.
         * @param {boolean} push - Whether successful navigation adds a browser history entry.
         * @returns {Promise<boolean>} Whether the requested messaging route was activated successfully.
         */
        async navigate(address, push = true) {
            if (this.disposed) return false;

            const target = new URL(address, window.location.href);
            const base = new URL(this.config.workspaceUrl, window.location.href);

            if (target.origin !== base.origin
                || !(target.pathname === base.pathname || target.pathname.startsWith(base.pathname + "/"))) {
                return false;
            }

            if (this.committing || this.current?.sending) {
                this.error(text(this.committing ? "Messages.Switching" : "Messages.WaitForSend"));

                if (!push) {
                    window.history.replaceState({ messaging: true }, "", this.currentUrl);
                }

                return false;
            }

            const navigation = ++this.navigation;

            this.fetching?.abort();
            this.fetching = new window.AbortController();
            const panel = this.root.querySelector("[data-active-panel]");

            panel.setAttribute("aria-busy", "true");
            this.error("");

            try {
                const response = await window.fetch(
                    target.href,
                    {
                        credentials: "same-origin",
                        cache: "no-store",
                        redirect: "error",
                        signal: this.fetching.signal
                    });

                if (!response.ok) {
                    throw new Error([401, 403].includes(response.status)
                        ? "session"
                        : response.status === 404 ? "missing" : "unavailable");
                }

                const parsed = new DOMParser().parseFromString(await response.text(), "text/html");
                const payload = parsed.getElementById("messaging-config");
                const replacement = parsed.querySelector("[data-active-panel]");
                const inbox = parsed.querySelector("[data-inbox-content]");

                if (!payload || !replacement || !inbox) {
                    throw new Error("session");
                }

                const config = JSON.parse(payload.textContent);

                if (navigation !== this.navigation || this.disposed) return false;

                if (this.current?.sending) {
                    throw new Error("sending");
                }

                this.committing = true;
                this.captureDraft();
                await this.current?.deactivate();

                if (navigation !== this.navigation || this.disposed) return false;

                panel.replaceChildren(...replacement.childNodes);
                const scroller = this.root.querySelector("[data-inbox-content]");
                const scrollTop = scroller.scrollTop;

                scroller.replaceChildren(...inbox.childNodes);
                scroller.scrollTop = scrollTop;
                this.config = config;
                this.sidebar.config = config;
                this.sidebar.localizeTimes(this.sidebar.root);
                this.sidebar.prepareImages();
                this.currentUrl = target.href;

                if (push) {
                    window.history.pushState({ messaging: true }, "", target.href);
                }

                document.title = parsed.title;
                await this.activate(config, true);

                return true;
            } catch (error) {
                if (navigation !== this.navigation || error.name === "AbortError") {
                    return false;
                }

                this.error(text(error.message === "session"
                    ? "Messages.SessionExpired"
                    : error.message === "missing"
                        ? "Conversations.NotAvailable"
                        : error.message === "sending" ? "Messages.WaitForSend" : "Messages.ReadUnavailable"));

                if (!push) {
                    window.history.replaceState({ messaging: true }, "", this.currentUrl);
                }

                return false;
            } finally {
                if (navigation === this.navigation) {
                    this.committing = false;
                    panel.setAttribute("aria-busy", "false");
                }
            }
        }

        /**
         * Retains only the unsent composition and stable retry object in this tab, separately for each
         *   participant context.
         *
         * @returns {void} Does not return a value.
         */
        captureDraft() {
            if (!this.current?.input) return;

            this.drafts.set(
                this.config.conversationId,
                {
                    content: this.current.input.value,
                    pending: this.current.pending,
                    isOwner: this.config.isOwner
                });
        }

        /**
         * Activates fresh server-authorized markup and changes only the active conversation's live
         *   subscription.
         *
         * @param {object} config - Server-provided configuration for this component.
         * @param {boolean} focus - Whether to move focus into the activated conversation.
         * @returns {Promise<void>} Resolves when this operation completes.
         */
        async activate(config, focus) {
            this.config = config;

            if (this.sidebar) {
                this.sidebar.config = config;
            }

            const panel = this.root.querySelector("[data-active-panel]");

            this.current = null;
            this.root.classList[config.conversationId ? "add" : "remove"]("has-active-conversation");

            if (config.conversationId) {
                const current = new window.RentRoadMessaging.MessagingPage(panel, config);

                current.managed = true;
                this.current = current;
                const starting = current.start();
                const draft = this.drafts.get(config.conversationId);

                if (draft && draft.isOwner === config.isOwner) {
                    current.input.value = draft.content;
                    current.pending = draft.pending;
                    current.input.readOnly = !!draft.pending;
                    current.sendButton.textContent = text(draft.pending ? "Messages.RetrySend" : "Messages.Send");
                    panel.querySelector("[data-pending-help]").hidden = !draft.pending;
                    current.updateCount();
                }

                starting.catch(() => {
                    if (this.current === current && !current.disposed) {
                        this.error(text("Messages.ReadUnavailable"));
                    }
                });
                current.prepareImages();

                if (focus) {
                    panel.querySelector("[data-conversation-heading]")?.focus({ preventScroll: true });
                }
            } else {
                if (focus) {
                    this.root.querySelector(".messages-sidebar-header h1")?.focus({ preventScroll: true });
                }

                this.emptyStatus("Messages.Connected");
            }

            this.select();
            this.resize();
        }

        /**
         * Highlights the selected row, or identifies the active context when its row is outside the current
         *   inbox page.
         *
         * @returns {void} Does not return a value.
         */
        select() {
            let found = false;

            for (const row of this.root.querySelectorAll("[data-conversation-id]")) {
                const selected = Number(row.dataset.conversationId) === this.config.conversationId;

                row.classList[selected ? "add" : "remove"]("is-selected");
                const link = row.querySelector("a[data-workspace-link]");

                if (selected) {
                    link.setAttribute("aria-current", "page");
                    found = true;
                } else {
                    link.removeAttribute("aria-current");
                }
            }

            const label = this.root.querySelector("[data-current-selection]");

            label.hidden = !this.config.conversationId || found;
            label.textContent = label.hidden
                ? ""
                : text("Conversations.CurrentSelection", this.config.participantName, this.config.listingTitle);
        }

        /**
         * Refetches authoritative sidebar/history after visibility changes and through a restrained
         *   active-page fallback.
         *
         * @returns {Promise<void>} Resolves when this operation completes.
         */
        async resume() {
            if (this.disposed || document.visibilityState !== "visible") return;

            await Promise.allSettled([this.sidebar.refreshInbox(), this.current?.resume()]);
        }

        /**
         * Fits the fixed document shell to the visible viewport when mobile keyboards resize or pan it.
         *
         * @returns {void} Does not return a value.
         */
        resize() {
            const viewport = window.visualViewport;

            if (viewport) {
                document.body.style.setProperty("--messages-viewport-height", viewport.height + "px");
                document.body.style.setProperty("--messages-viewport-top", viewport.offsetTop + "px");
            }

            const keyboard = !!viewport && viewport.scale === 1 && viewport.height < window.innerHeight * .75
                && !!document.activeElement?.closest("[data-message-form]");

            document.body.classList[keyboard ? "add" : "remove"]("messages-keyboard-open");
            this.current?.observeVisible();
        }

        /**
         * Restarts the existing shared transport explicitly when the empty workspace has no active
         *   conversation consumer.
         *
         * @returns {Promise<void>} Resolves when this operation completes.
         */
        async reconnect() {
            try {
                await window.RentRoadMessagingTransport.start(this.config.realtime);
                this.emptyStatus("Messages.Connected");
                await this.resume();
            } catch {
                this.emptyStatus("Messages.Disconnected", true);
            }
        }

        /**
         * Announces connection recovery without a permanent visible connected-state banner.
         *
         * @param {string} key - Resource or entry key to resolve.
         * @param {boolean} retry - Whether to offer an explicit retry action.
         * @returns {void} Does not return a value.
         */
        emptyStatus(key, retry = false) {
            const view = new window.RentRoadMessaging.MessagingPage(
                this.root.querySelector("[data-active-panel]"),
                this.config);

            view.connectionStatus(key, retry);
        }

        /**
         * Keeps navigation failures visible within the workspace without exposing response bodies or
         *   exception details.
         *
         * @param {string} value - Text to display safely.
         * @returns {void} Does not return a value.
         */
        error(value) {
            const node = this.root.querySelector("[data-page-error]");

            node.textContent = value;
            node.hidden = !value;
        }

        /**
         * Releases active listeners/groups and sidebar refresh while the shared transport handles document
         *   exit.
         *
         * @returns {void} Does not return a value.
         */
        stop() {
            this.disposed = true;
            this.fetching?.abort();
            window.clearInterval(this.poll);
            this.unsubscribe?.();
            this.current?.deactivate();
            this.sidebar.disposed = true;
        }
    }

    window.RentRoadMessagingWorkspace = MessagingWorkspace;
    const root = document.querySelector("[data-messaging-workspace]");
    const payload = document.getElementById("messaging-config");

    if (root && payload) {
        new MessagingWorkspace(root, JSON.parse(payload.textContent)).start();
    }
})();
