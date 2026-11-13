/**
 * Keeps the authenticated navigation badge authoritative without querying messages during Razor rendering.
 *
 * @returns {void} Does not return a value.
 */
(function messagingUnreadModule() {
    "use strict";
    const REFRESH_INTERVAL = 60000;

    /**
     * Coordinates aggregate counts, shared live hints and restrained active-page fallback refresh.
     */
    class MessagingUnread {
        /**
         * Captures only safe navigation metadata and a count badge, never message content.
         *
         * @param {HTMLAnchorElement} link - Rendered link used by this component.
         * @param {object} config - Server-provided configuration for this component.
         */
        constructor(link, config) {
            this.link = link;
            this.badge = link.querySelector("[data-navigation-unread]");
            this.config = config;
            this.refreshing = false;
            this.refreshAgain = false;
            this.disposed = false;
        }

        /**
         * Loads counts asynchronously and uses the same connection as chat pages, with no endless
         *   initial-start loop.
         *
         * @returns {Promise<void>} Resolves when this operation completes.
         */
        async start() {
            document.addEventListener("visibilitychange", () => this.refresh());
            window.addEventListener("focus", () => this.refresh());
            window.addEventListener("rentroad:messages-read", () => this.readChanged());
            window.addEventListener("pagehide", () => this.stop());
            window.addEventListener("pageshow", event => {
                if (event.persisted) {
                    window.location.reload();
                }
            });

            if (window.BroadcastChannel) {
                this.channel = new window.BroadcastChannel("rentroad-messaging-unread");
                this.channel.onmessage = event => {
                    if (event.data?.type === "refresh") {
                        this.refresh();
                    }
                };
            }

            this.poll = window.setInterval(() => this.refresh(), REFRESH_INTERVAL);
            this.refresh();

            try {
                const transport = window.RentRoadMessagingTransport;

                this.connection = transport.getConnection(this.config.realtime);
                this.connection.on(
                    this.config.realtime.conversationUpdatedEvent,
                    hint => {
                        if (Number.isSafeInteger(hint?.conversationId) && hint.conversationId > 0
                            && Number.isSafeInteger(hint?.messageId)
                            && hint.messageId > 0) {
                            this.refresh();
                        }
                    });
                this.connection.onreconnected(() => this.refresh());
                await transport.start(this.config.realtime);
                await this.refresh();
            } catch {
                this.refresh();
            }
        }

        /**
         * Refreshes all participant conversations after a confirmed read and sends only a cross-tab refresh
         *   hint.
         *
         * @returns {void} Does not return a value.
         */
        readChanged() {
            this.refresh();
            this.channel?.postMessage({ type: "refresh" });
        }

        /**
         * Coalesces refresh hints and checks framework authentication before interpreting the existing JSON
         *   contract.
         *
         * @returns {Promise<void>} Resolves when this operation completes.
         */
        async refresh() {
            if (this.disposed || document.visibilityState !== "visible") return;

            if (this.refreshing) {
                this.refreshAgain = true;

                return;
            }

            this.refreshing = true;

            try {
                do {
                    this.refreshAgain = false;
                    const response = await window.fetch(
                        this.config.unreadCountUrl,
                        {
                            credentials: "same-origin",
                            cache: "no-store",
                            redirect: "error",
                            headers: { Accept: "application/json" }
                        });

                    if ([401, 403].includes(response.status)) {
                        this.badge.hidden = true;
                        this.badge.textContent = "";
                        await this.connection?.stop();
                        throw new Error();
                    }

                    if (!response.ok) {
                        throw new Error();
                    }

                    const result = await response.json();
                    const count = result.data?.unreadCount;

                    if (!result.isSuccess || !Number.isSafeInteger(count) || count < 0) {
                        throw new Error();
                    }

                    this.badge.hidden = count === 0;
                    this.badge.textContent = count.toLocaleString(document.documentElement.lang);
                    this.badge.setAttribute(
                        "aria-label",
                        window.RentRoadText(
                            count === 1
                                ? "Conversations.UnreadCountOne"
                                : "Conversations.UnreadCount",
                            count));
                    this.link.title = window.RentRoadText("Conversations.Navigation");
                } while (this.refreshAgain && !this.disposed && document.visibilityState === "visible");
            } catch {
                this.link.title = window.RentRoadText("Conversations.UnreadUnavailable");
            } finally {
                this.refreshing = false;
            }
        }

        /**
         * Releases only this page's timer and cross-tab refresh channel; the shared transport owns its
         *   connection.
         *
         * @returns {void} Does not return a value.
         */
        stop() {
            this.disposed = true;
            window.clearInterval(this.poll);
            this.channel?.close();
        }
    }

    window.RentRoadMessagingUnread = MessagingUnread;
    const payload = document.getElementById("messages-navigation-config");
    const link = document.querySelector("[data-messages-navigation]");

    if (payload && link) {
        new MessagingUnread(link, JSON.parse(payload.textContent)).start();
    }
})();
