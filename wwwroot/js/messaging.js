/**
 * Connects safe Razor messaging projections to authorized HTTP persistence and SignalR ID hints.
 *
 * @returns {void} Does not return a value.
 */
(function messagingModule() {
    "use strict";
    const HISTORY_LIMIT = 100;
    const POLL_INTERVAL = 30000;

    /**
     * Resolves the existing server-provided resource catalogue without a separate translation store.
     *
     * @param {...unknown} args - Arguments forwarded to the wrapped operation.
     * @returns {string} The formatted resource text.
     */
    const text = (...args) => window.RentRoadText(...args);

    /**
     * Carries only a safe user-facing failure and its HTTP category.
     */
    class MessagingFailure extends Error {
        /**
         * Retains presentation feedback without response bodies or submitted message text.
         *
         * @param {string} message - Text describing the result.
         * @param {number} status - HTTP status for the response or failure.
         */
        constructor(message, status = 0) {
            super(message);
            this.status = status;
        }
    }

    /**
     * Coordinates one inbox or conversation without persisting private content in browser storage.
     */
    class MessagingPage {
        /**
         * Captures existing server contracts and independent history, retry and observed-read state.
         *
         * @param {HTMLElement} root - Rendered root used by this component.
         * @param {object} config - Server-provided configuration for this component.
         */
        constructor(root, config) {
            this.root = root;
            this.config = config;
            this.conversationId = config.conversationId;
            this.messages = new Map();
            this.nodes = new Map();
            this.observed = new Set();
            this.observationRanges = new Map();
            this.pending = null;
            this.sending = false;
            this.composing = false;
            this.syncCursor = null;
            this.olderCursor = config.history?.nextCursor;
            this.hasOlder = config.history?.hasMore || false;
            this.acknowledged = 0;
            this.recovering = false;
            this.recoverAgain = false;
            this.refreshingInbox = false;
            this.inboxAgain = false;
            this.disposed = false;
            this.blocked = false;
            this.listeners = [];
            this.reads = new window.AbortController();
            this.managed = false;
            this.history = root.querySelector("[data-history]");
            this.list = root.querySelector("[data-message-list]");
            this.input = root.querySelector("[data-message-input]");
            this.sendButton = root.querySelector("[data-send]");
            this.form = root.querySelector("[data-message-form]");
        }

        /**
         * Initializes only existing markup, leaving server-rendered history readable without a live
         *   connection.
         *
         * @returns {Promise<void>} Resolves when this operation completes.
         */
        async start() {
            this.localizeTimes(this.root);

            if (this.history) {
                this.prepareConversation();
            }

            this.listen(this.root.querySelector("[data-reconnect]"), "click", () => this.connect());

            if (!this.managed) {
                this.listen(document, "visibilitychange", () => this.resume());
                this.listen(window, "focus", () => this.resume());
                this.listen(window, "pagehide", () => this.stop());
                this.listen(window, "pageshow", event => {
                    if (event.persisted) {
                        window.location.reload();
                    }
                });
                this.poll = window.setInterval(() => this.resume(), POLL_INTERVAL);
            }

            await this.connect();
            await this.resume();
        }

        /**
         * Wires composition and viewport observation while retaining exact initial chronological history.
         *
         * @returns {void} Does not return a value.
         */
        prepareConversation() {
            for (const message of this.config.history.messages) {
                this.messages.set(message.messageId, message);
                this.nodes.set(message.messageId, this.list.querySelector(`[data-message-id="${message.messageId}"]`));
            }

            this.syncCursor = Math.max(0, ...this.messages.keys()) || null;
            this.input.disabled = false;
            this.sendButton.disabled = false;
            this.listen(this.input, "keydown", event => this.composeKey(event));
            this.listen(this.input, "compositionstart", () => {
                this.composing = true;
            });
            this.listen(this.input, "compositionend", () => {
                this.composing = false;
            });
            this.listen(this.form, "submit", event => {
                event.preventDefault();
                this.send();
            });
            this.listen(this.root.querySelector("[data-new-submission]"), "click", () => this.newSubmission());
            this.listen(this.root.querySelector("[data-load-older]"), "click", () => this.loadOlder());
            this.listen(this.root.querySelector("[data-latest]"), "click", () => this.scrollLatest());
            this.listen(this.history, "scroll", () => this.observeVisible());
            this.listen(window, "resize", () => this.observeVisible());
            this.listen(window, "scroll", () => this.observeVisible(), { passive: true });

            if (window.IntersectionObserver) {
                this.observer = new window.IntersectionObserver(
                    () => this.observeVisible(),
                    { root: this.history, threshold: [0, 1] });

                for (const node of this.nodes.values()) {
                    this.observer.observe(node);
                }
            }

            this.scrollLatest();
        }

        /**
         * Sends on an unmodified Enter while preserving multiline input and IME composition.
         *
         * @param {KeyboardEvent} event - Composer keyboard event.
         * @returns {void} Does not return a value.
         */
        composeKey(event) {
            if (event.key !== "Enter" || event.shiftKey || event.ctrlKey || event.altKey || event.metaKey
                || this.composing || event.isComposing || event.keyCode === 229) {
                return;
            }

            event.preventDefault();

            if (event.repeat || this.sending || this.disposed || this.blocked || this.input.disabled
                || this.input.readOnly || !this.input.value.trim()) {
                return;
            }

            this.send();
        }

        /**
         * Fetches only same-origin authorized JSON and checks framework statuses before parsing an envelope.
         *
         * @param {string} url - Endpoint or address to use.
         * @param {object|undefined} body - Request payload to serialize, or undefined for a read request.
         * @returns {Promise<object>} The parsed, validated response envelope from the existing endpoint.
         */
        async request(url, body) {
            this.ensureActive();
            const options = {
                credentials: "same-origin",
                cache: "no-store",
                redirect: "error",
                headers: { Accept: "application/json" }
            };

            if (body !== undefined) {
                options.method = "POST";
                options.headers["Content-Type"] = "application/json";
                options.headers[this.config.antiforgeryHeaderName || "RequestVerificationToken"] =
                    this.form.querySelector('input[name="__RequestVerificationToken"]').value;
                options.body = JSON.stringify(body);
            } else {
                options.signal = this.reads.signal;
            }

            let response;

            try {
                response = await window.fetch(url, options);
            } catch (error) {
                if (this.disposed || options.signal?.aborted) {
                    throw error;
                }

                throw new MessagingFailure(text("Messages.ReadUnavailable"));
            }

            this.ensureActive();

            if ([401, 403].includes(response.status)) {
                this.blocked = true;
                await this.connection?.stop();
                this.connectionStatus("Messages.SessionExpired", true);
                throw new MessagingFailure(text("Messages.SessionExpired"), response.status);
            }

            if (response.status === 404) {
                this.blocked = true;
                await this.connection?.stop();
                throw new MessagingFailure(text("Conversations.NotAvailable"), 404);
            }

            let envelope;

            try {
                envelope = await response.json();
            } catch (error) {
                if (this.disposed || options.signal?.aborted) {
                    throw error;
                }

                throw new MessagingFailure(text("Messages.ReadUnavailable"), response.status);
            }

            this.ensureActive();

            if (!response.ok || !envelope.isSuccess) {
                const feedback = envelope.errors?.map(error => error.message).filter(value => typeof value === "string")
                    .join(" ");

                throw new MessagingFailure(feedback || text("Messages.ReadUnavailable"), response.status);
            }

            return envelope;
        }

        /**
         * Rejects late work from a released conversation without affecting the shared transport.
         *
         * @throws {Error} An AbortError when this page consumer has been deactivated.
         * @returns {void} Does not return a value.
         */
        ensureActive() {
            if (this.disposed) {
                const error = new Error();

                error.name = "AbortError";
                throw error;
            }
        }

        /**
         * Starts the official cookie-authenticated client, without coupling HTTP submission to live
         *   transport.
         *
         * @returns {Promise<void>} Resolves when this operation completes.
         */
        async connect() {
            if (this.disposed || this.connecting) return;

            this.connecting = true;
            this.connectionStatus("Messages.Connecting");

            try {
                this.blocked = false;

                if (!window.signalR) {
                    throw new Error();
                }

                if (!this.connection) {
                    this.connection = window.RentRoadMessagingTransport
                        ? window.RentRoadMessagingTransport.getConnection(this.config.realtime)
                        : new window.signalR.HubConnectionBuilder().withUrl(this.config.realtime.hubUrl)
                            .configureLogging(window.signalR.LogLevel.None).withAutomaticReconnect().build();
                    const subscriber = {
                        hint: payload => {
                            if (!this.disposed) {
                                this.onHint(payload);
                            }
                        },
                        reconnecting: () => {
                            if (!this.disposed) {
                                this.connectionStatus("Messages.Reconnecting");
                            }
                        },
                        reconnected: () => {
                            if (!this.disposed) {
                                this.join(true);
                            }
                        },
                        closed: () => {
                            if (!this.disposed) {
                                this.connectionStatus(
                                    this.blocked ? "Messages.SessionExpired" : "Messages.Disconnected",
                                    true);
                            }
                        }
                    };

                    if (window.RentRoadMessagingTransport?.subscribe) {
                        this.unsubscribe = window.RentRoadMessagingTransport.subscribe(this.config.realtime, subscriber);
                    } else {
                        this.connection.on(this.config.realtime.messageAvailableEvent, subscriber.hint);
                        this.connection.on(this.config.realtime.conversationUpdatedEvent, subscriber.hint);
                        this.connection.onreconnecting(subscriber.reconnecting);
                        this.connection.onreconnected(subscriber.reconnected);
                        this.connection.onclose(subscriber.closed);
                    }
                }

                if (window.RentRoadMessagingTransport) {
                    await window.RentRoadMessagingTransport.start(this.config.realtime);
                } else if (this.connection.state === window.signalR.HubConnectionState.Disconnected) {
                    await this.connection.start();
                }

                await this.join(false);
            } catch {
                this.connectionStatus("Messages.Disconnected", true);
            } finally {
                this.connecting = false;
            }
        }

        /**
         * Reauthorizes group membership after every connection and then recovers authoritative persisted
         *   gaps.
         *
         * @param {boolean} reconnected - Whether this subscription follows transport reconnection.
         * @returns {Promise<void>} Resolves when this operation completes.
         */
        async join(reconnected) {
            if (this.disposed) return;

            try {
                if (this.conversationId) {
                    const joined = await this.connection.invoke(this.config.realtime.joinMethod, this.conversationId);

                    if (this.disposed) return;

                    if (!joined?.isSuccess) {
                        const unauthorized = joined?.errors?.some(error => ["Conversations.NotAvailable", "Messages.SignInRequired"].includes(error.resourceKey))
                            || false;

                        this.blocked = unauthorized;
                        await this.connection.stop();
                        this.connectionStatus(unauthorized ? "Conversations.NotAvailable" : "Messages.Disconnected", true);

                        return;
                    }
                }

                this.connectionStatus(reconnected ? "Messages.Reconnected" : "Messages.Connected");
                await this.resume();
            } catch {
                this.connectionStatus("Messages.Disconnected", true);
            }
        }

        /**
         * Treats the existing two-ID event as a scoped refresh hint, never as message content or a read
         *   acknowledgment.
         *
         * @param {unknown} payload - Server hint forwarded to the subscribed consumers.
         * @returns {void} Does not return a value.
         */
        onHint(payload) {
            if (!Number.isSafeInteger(payload?.conversationId) || !Number.isSafeInteger(payload?.messageId)
                || payload.conversationId <= 0
                || payload.messageId <= 0) {
                return;
            }

            if (this.conversationId && payload.conversationId !== this.conversationId) {
                return;
            }

            if (this.conversationId) {
                this.recover();
            } else {
                this.refreshInbox();
            }
        }

        /**
         * Coalesces reconnect, active-tab and periodic recovery without acknowledging hidden conversations.
         *
         * @returns {Promise<void>} Resolves when this operation completes.
         */
        async resume() {
            if (this.disposed || this.blocked || document.visibilityState !== "visible") {
                return;
            }

            if (this.conversationId) {
                await this.recover();
                this.observeVisible();
            } else {
                await this.refreshInbox();
            }
        }

        /**
         * Follows actual afterMessageId cursors from an independent recovery watermark, including send/event
         *   races.
         *
         * @returns {Promise<void>} Resolves when this operation completes.
         */
        async recover() {
            if (this.blocked || this.disposed) return;

            if (this.recovering) {
                this.recoverAgain = true;

                return;
            }

            this.recovering = true;

            try {
                do {
                    this.recoverAgain = false;

                    if (this.syncCursor) {
                        let cursor = this.syncCursor;
                        let page;

                        do {
                            page = (await this.request(this.historyUrl("afterMessageId", cursor))).data;
                            this.merge(page.messages);
                            this.syncCursor = Math.max(this.syncCursor, ...page.messages.map(message => message.messageId));

                            if (page.hasMore && (!page.nextCursor || page.nextCursor <= cursor)) {
                                throw new Error();
                            }

                            cursor = page.nextCursor;
                        } while (page.hasMore && !this.blocked && !this.disposed);
                    } else {
                        const page = (await this.request(this.historyUrl())).data;

                        this.merge(page.messages);
                        const newest = Math.max(0, ...page.messages.map(message => message.messageId)) || null;
                        let older = page;

                        while (older.hasMore && !this.disposed && !this.blocked) {
                            older = (await this.request(this.historyUrl("beforeMessageId", older.nextCursor))).data;
                            this.merge(older.messages);
                        }

                        this.syncCursor = newest;
                    }

                    const snapshot = (await this.request(this.historyUrl())).data;

                    for (const message of snapshot.messages) {
                        if (this.messages.has(message.messageId)) {
                            this.updateReadNode(this.remember(message));
                        }
                    }

                    this.feedback("[data-history-status]", this.hasOlder ? "" : text("Messages.NoOlder"));
                    await this.refreshUnread();
                } while (this.recoverAgain && !this.blocked && !this.disposed);
            } catch (error) {
                this.feedback("[data-history-status]", this.safeError(error, "Messages.HistoryFailed"));
            } finally {
                this.recovering = false;
            }
        }

        /**
         * Builds a bounded URL from the MVC-generated endpoint, using only the documented directional cursor.
         *
         * @param {string|undefined} direction - Directional cursor parameter name, when a boundary is
         *   requested.
         * @param {number|undefined} cursor - Persisted message identifier used as the pagination boundary.
         * @returns {string} The bounded history endpoint with its cursor parameters.
         */
        historyUrl(direction, cursor) {
            const url = new URL(this.config.endpoints.historyUrl, window.location.href);

            url.searchParams.set("limit", HISTORY_LIMIT);

            if (direction && cursor) {
                url.searchParams.set(direction, cursor);
            }

            return url.href;
        }

        /**
         * Loads one older page while preserving the current reading position and original older cursor.
         *
         * @returns {Promise<void>} Resolves when this operation completes.
         */
        async loadOlder() {
            if (this.disposed || this.loadingOlder || !this.hasOlder || this.blocked) return;

            this.loadingOlder = true;
            const button = this.root.querySelector("[data-load-older]");

            button.disabled = true;
            this.feedback("[data-history-status]", text("Messages.LoadingHistory"));

            try {
                const page = (await this.request(this.historyUrl("beforeMessageId", this.olderCursor))).data;

                this.merge(page.messages, true);
                this.olderCursor = page.nextCursor;
                this.hasOlder = page.hasMore;
                button.hidden = !this.hasOlder;
                this.feedback("[data-history-status]", this.hasOlder ? "" : text("Messages.NoOlder"));
            } catch (error) {
                this.feedback("[data-history-status]", this.safeError(error, "Messages.HistoryFailed"));
            } finally {
                this.loadingOlder = false;
                button.disabled = false;
            }
        }

        /**
         * Reconciles persisted IDs using safe DOM text, without skipping recovery gaps or duplicating sent
         *   responses.
         *
         * @param {object[]} messages - Persisted message projections to reconcile by their server
         *   identifiers.
         * @param {boolean} older - Whether the projections extend the older history page.
         * @returns {void} Does not return a value.
         */
        merge(messages, older = false) {
            if (this.disposed) return;

            const height = this.history.scrollHeight;
            const top = this.history.scrollTop;
            const nearBottom = height - top - this.history.clientHeight < 80;
            let added = false;

            for (const incoming of messages) {
                if (incoming.conversationId !== this.conversationId || !Number.isSafeInteger(incoming.messageId)
                    || incoming.messageId <= 0) {
                    continue;
                }

                const message = this.remember(incoming);

                if (this.nodes.has(message.messageId)) {
                    this.updateReadNode(message);
                    continue;
                }

                const node = this.messageNode(message);

                this.nodes.set(message.messageId, node);
                const next = [...this.nodes.keys()].filter(id => id > message.messageId).sort((a, b) => a - b)[0];

                this.list.insertBefore(node, next ? this.nodes.get(next) : null);
                this.observer?.observe(node);
                added = true;
            }

            this.root.querySelector("[data-empty-history]").hidden = this.nodes.size > 0;

            if (older) {
                this.history.scrollTop = top + this.history.scrollHeight - height;
            } else if (added && nearBottom) {
                this.scrollLatest();
            } else if (added) {
                this.root.querySelector("[data-latest]").hidden = false;
                this.feedback("[data-arrival-status]", text("Messages.NewMessages"));
            }

            this.observeVisible();
        }

        /**
         * Retains confirmed read state when an earlier in-flight history snapshot arrives after an
         *   acknowledgment.
         *
         * @param {object} message - Persisted message projection to render or retain.
         * @returns {object} The retained message with its server-confirmed read state.
         */
        remember(message) {
            const previous = this.messages.get(message.messageId);
            const retained = previous?.isRead && !message.isRead
                ? {
                    ...message,
                    isRead: true,
                    readAtUtc: previous.readAtUtc
                }
                : message;

            this.messages.set(message.messageId, retained);

            return retained;
        }

        /**
         * Creates one persisted message with textContent only, preserving whitespace without interpreting
         *   markup.
         *
         * @param {object} message - Persisted message projection to render or retain.
         * @returns {HTMLLIElement} A safely rendered message row.
         */
        messageNode(message) {
            const row = document.createElement("li");

            row.className = "message-row " + (message.isMine ? "message-row-mine" : "message-row-received");
            row.dataset.messageId = message.messageId;
            row.dataset.mine = String(message.isMine);
            row.dataset.read = String(message.isRead);
            const bubble = document.createElement("article");

            bubble.className = "message-bubble";
            const sender = document.createElement("p");

            sender.className = "message-sender";
            sender.textContent = message.isMine ? text("Messages.You") : message.senderDisplayName;
            const content = document.createElement("p");

            content.className = "message-content";
            content.textContent = message.content;
            const meta = document.createElement("footer");

            meta.className = "message-meta";
            const time = document.createElement("time");

            time.dateTime = message.sentAtUtc;
            time.setAttribute("data-message-time", "");
            const status = document.createElement("span");

            status.setAttribute("data-read-status", "");
            meta.append(time, status);
            bubble.append(sender, content, meta);
            row.append(bubble);
            this.localizeTimes(row);
            this.updateReadNode(message, row);

            return row;
        }

        /**
         * Refreshes read presentation using only server-confirmed message state.
         *
         * @param {object} message - Persisted message projection to render or retain.
         * @param {HTMLElement|undefined} node - Rendered node used by this component.
         * @returns {void} Does not return a value.
         */
        updateReadNode(message, node = this.nodes.get(message.messageId)) {
            node.dataset.read = String(message.isRead);
            const status = node.querySelector("[data-read-status]");

            if (!status) return;

            status.className = message.isMine ? "" : "visually-hidden";
            status.textContent = message.isMine
                ? text(message.isRead ? "Messages.Read" : "Messages.Saved")
                : message.isRead ? "" : text("Messages.UnreadIncoming");
        }

        /**
         * Sends an exact browser-generated submission, retaining its GUID and text until persistence is
         *   confirmed.
         *
         * @returns {Promise<void>} Resolves when this operation completes.
         */
        async send() {
            if (this.sending || this.disposed || this.blocked || this.composing) return;

            if (!this.pending) {
                const content = this.input.value;

                if (!content.trim()) {
                    this.sendFeedback(text("Messages.InvalidContent"), true);

                    return;
                }

                if (content.length > this.config.maximumLength) {
                    this.sendFeedback(text("Messages.ContentTooLong", this.config.maximumLength), true);

                    return;
                }

                let clientMessageId;

                try {
                    clientMessageId = this.newGuid();
                } catch {
                    this.sendFeedback(text("Messages.CryptoUnavailable"), true);

                    return;
                }

                this.pending = { content, clientMessageId };
            }

            this.sending = true;
            this.input.readOnly = true;
            this.sendButton.disabled = true;
            this.sendButton.textContent = text("Messages.Sending");
            this.sendFeedback(text("Messages.Sending"));

            try {
                const response = await this.request(this.config.endpoints.sendUrl, this.pending);
                const saved = response.data;

                if (saved?.clientMessageId?.toLowerCase() !== this.pending.clientMessageId.toLowerCase()
                    || saved.content !== this.pending.content
                    || saved.conversationId !== this.conversationId
                    || !saved.isMine) {
                    throw new MessagingFailure(text("Messages.Uncertain"));
                }

                this.merge([saved]);
                this.pending = null;
                this.input.value = "";
                this.input.readOnly = false;
                this.root.querySelector("[data-pending-help]").hidden = true;
                this.root.querySelector("[data-new-submission]").hidden = true;
                this.sendFeedback(text(response.notificationStatus === "Unavailable"
                    ? "Messages.SavedWithoutLive"
                    : "Messages.Sent"), false, response.notificationStatus === "Unavailable");
                this.input.focus();
                await this.recover();
            } catch (error) {
                this.sendFeedback(
                    !error.status ? text("Messages.Uncertain") : this.safeError(error, "Messages.Uncertain"),
                    true);
                this.root.querySelector("[data-pending-help]").hidden = false;
                this.root.querySelector("[data-new-submission]").hidden = error.status !== 409 && error.status !== 400;

                if (error.status === 409) {
                    this.sendFeedback(text("Messages.ConflictHelp"), true);
                }
            } finally {
                this.sending = false;
                this.sendButton.disabled = false;
                this.sendButton.textContent = text(this.pending ? "Messages.RetrySend" : "Messages.Send");
            }
        }

        /**
         * Starts a deliberate different submission only after an explicit conflict/validation recovery
         *   decision.
         *
         * @returns {void} Does not return a value.
         */
        newSubmission() {
            if (this.sending || !window.confirm(text("Messages.NewSubmissionConfirm"))) {
                return;
            }

            this.pending = null;
            this.input.readOnly = false;
            this.root.querySelector("[data-pending-help]").hidden = true;
            this.root.querySelector("[data-new-submission]").hidden = true;
            this.sendButton.textContent = text("Messages.Send");
            this.input.focus();
        }

        /**
         * Uses the browser's cryptographic UUID support, never timestamps, random floats or server-generated
         *   retry tokens.
         *
         * @returns {string} A cryptographically generated submission identifier.
         */
        newGuid() {
            if (window.crypto.randomUUID) {
                return window.crypto.randomUUID();
            }

            const bytes = window.crypto.getRandomValues(new Uint8Array(16));

            bytes[6] = (bytes[6] & 15) | 64;
            bytes[8] = (bytes[8] & 63) | 128;
            const hex = [...bytes].map(value => value.toString(16).padStart(2, "0")).join("");

            return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20)}`;
        }

        /**
         * Records only received messages actually visible inside both the history and active browser
         *   viewport.
         *
         * @returns {void} Does not return a value.
         */
        observeVisible() {
            if (!this.history || this.disposed || this.blocked || document.visibilityState !== "visible"
                || !document.hasFocus()) {
                return;
            }

            const viewport = this.history.getBoundingClientRect();

            for (const [id, node] of this.nodes) {
                const message = this.messages.get(id);

                if (message.isMine || message.isRead) {
                    continue;
                }

                const bounds = node.getBoundingClientRect();
                const top = Math.max(bounds.top, viewport.top, 0);
                const bottom = Math.min(bounds.bottom, viewport.bottom, window.innerHeight);

                if (bottom <= top) {
                    continue;
                }

                const ranges = [
                    ...(this.observationRanges.get(id) || []),
                    [top - bounds.top, bottom - bounds.top]
                ].sort((a, b) => a[0] - b[0]);
                const coverage = [];

                for (const range of ranges) {
                    const previous = coverage[coverage.length - 1];

                    if (previous && range[0] <= previous[1] + 1) {
                        previous[1] = Math.max(previous[1], range[1]);
                    } else {
                        coverage.push([...range]);
                    }
                }

                this.observationRanges.set(id, coverage);

                if (coverage.length === 1 && coverage[0][0] <= 1 && coverage[0][1] >= bounds.height - 1) {
                    this.observed.add(id);
                }
            }

            this.acknowledge();
        }

        /**
         * Serializes recipient-only prefix acknowledgments, stopping before any loaded but unobserved
         *   incoming message.
         *
         * @returns {Promise<void>} Resolves when this operation completes.
         */
        async acknowledge() {
            if (this.disposed || this.reading || this.blocked || document.visibilityState !== "visible" || !document.hasFocus()) {
                return;
            }

            let boundary = this.acknowledged;

            for (const message of [...this.messages.values()].sort((a, b) => a.messageId - b.messageId)) {
                if (message.isMine || message.isRead || message.messageId <= this.acknowledged) {
                    continue;
                }

                if (!this.observed.has(message.messageId)) {
                    break;
                }

                boundary = message.messageId;
            }

            if (!boundary || boundary <= this.acknowledged) return;

            this.reading = true;

            try {
                await this.request(this.config.endpoints.readUrl, { throughMessageId: boundary });

                if (this.disposed) return;

                this.acknowledged = boundary;
                window.dispatchEvent(new Event("rentroad:messages-read"));

                for (const message of this.messages.values()) {
                    if (!message.isMine && message.messageId <= boundary) {
                        message.isRead = true;
                        this.updateReadNode(message);
                    }
                }

                this.feedback("[data-read-feedback]", "");
                await this.refreshUnread();
            } catch (error) {
                this.feedback(
                    "[data-read-feedback]",
                    !error.status ? text("Messages.ReadFailed") : this.safeError(error, "Messages.ReadFailed"));
            } finally {
                this.reading = false;
            }

            if (this.acknowledged === boundary) {
                this.observeVisible();
            }
        }

        /**
         * Retrieves server-authoritative received-unread counts without inventing local per-user totals.
         *
         * @returns {Promise<void>} Resolves when this operation completes.
         */
        async refreshUnread() {
            if (this.disposed || !this.root.querySelector("[data-unread-count]")) return;

            try {
                const url = this.config.endpoints?.unreadCountUrl || this.config.unreadCountUrl;
                const result = await this.request(url);

                if (this.disposed) return;

                const badge = this.root.querySelector("[data-unread-count]");

                badge.hidden = result.data.unreadCount === 0;
                badge.textContent = text(
                    result.data.unreadCount === 1 ? "Conversations.UnreadCountOne" : "Conversations.UnreadCount",
                    result.data.unreadCount);
            } catch (error) {
                this.feedback(
                    this.history ? "[data-read-feedback]" : "[data-page-error]",
                    this.safeError(error, "Messages.UnreadFailed"));
            }
        }

        /**
         * Refreshes only the paginated inbox fragment from authenticated Razor, preserving server ordering
         *   and safe encoding.
         *
         * @returns {Promise<void>} Resolves when this operation completes.
         */
        async refreshInbox() {
            if (this.disposed || this.blocked || document.visibilityState !== "visible") {
                return;
            }

            if (this.refreshingInbox) {
                this.inboxAgain = true;

                return;
            }

            this.refreshingInbox = true;

            try {
                do {
                    this.inboxAgain = false;
                    const inboxUrl = this.config.inboxUrl;
                    const response = await window.fetch(inboxUrl, {
                        credentials: "same-origin",
                        cache: "no-store",
                        redirect: "error",
                        signal: this.reads.signal
                    });

                    if (!response.ok) {
                        throw new MessagingFailure(
                            text([401, 403].includes(response.status)
                                ? "Messages.SessionExpired"
                                : "Messages.ReadUnavailable"),
                            response.status);
                    }

                    const html = await response.text();

                    if (this.disposed) return;

                    if (inboxUrl !== this.config.inboxUrl) {
                        this.inboxAgain = true;
                        continue;
                    }

                    const parsed = new DOMParser().parseFromString(html, "text/html");
                    const replacement = parsed.querySelector("[data-inbox-content]");

                    if (!replacement) {
                        throw new MessagingFailure(text("Messages.SessionExpired"));
                    }

                    const focused = document.activeElement?.closest("[data-conversation-id]")?.dataset.conversationId;

                    this.root.querySelector("[data-inbox-content]").replaceChildren(...replacement.childNodes);
                    this.localizeTimes(this.root);
                    this.prepareImages();
                    this.onInboxUpdated?.();

                    if (focused) {
                        this.root.querySelector(`[data-conversation-id="${focused}"] .messages-inbox-open`)?.focus();
                    }

                    this.feedback("[data-page-error]", "");
                    await this.refreshUnread();
                } while (this.inboxAgain && !this.disposed && !this.blocked);
            } catch (error) {
                if (this.disposed) return;

                const message = this.safeError(error, "Messages.ReadUnavailable");

                this.feedback("[data-page-error]", message);
                this.onInboxError?.(message);
            } finally {
                this.refreshingInbox = false;
            }
        }

        /**
         * Reattaches the established private-image fallback to refreshed inbox thumbnails without changing
         *   delivery authorization.
         *
         * @returns {void} Does not return a value.
         */
        prepareImages() {
            for (const image of this.root.querySelectorAll("[data-listing-image]")) {
                /**
                 * Hides unavailable image bytes and reveals the existing thumbnail fallback.
                 *
                 * @returns {void} Does not return a value.
                 */
                const fallback = () => {
                    image.hidden = true;
                    image.parentElement.querySelector("[data-image-fallback]")?.removeAttribute("aria-hidden");
                };

                image.addEventListener("error", fallback, { once: true });

                if (image.complete && image.naturalWidth === 0) {
                    fallback();
                }
            }
        }

        /**
         * Localizes absolute persisted timestamps without using client clocks as message order.
         *
         * @param {HTMLElement} root - Rendered root used by this component.
         * @returns {void} Does not return a value.
         */
        localizeTimes(root) {
            const formatter = new Intl.DateTimeFormat(
                document.documentElement.lang || "en",
                { dateStyle: "medium", timeStyle: "short" });

            for (const time of root.querySelectorAll("[data-message-time]")) {
                const date = new Date(time.dateTime || time.getAttribute("datetime"));

                if (!Number.isNaN(date.getTime())) {
                    time.textContent = formatter.format(date);
                    time.title = date.toISOString();
                }
            }
        }

        /**
         * Moves to the latest persisted message only on explicit request or when already following the
         *   bottom.
         *
         * @returns {void} Does not return a value.
         */
        scrollLatest() {
            this.history.scrollTop = this.history.scrollHeight;
            this.root.querySelector("[data-latest]").hidden = true;
            this.feedback("[data-arrival-status]", "");
            window.requestAnimationFrame(() => this.observeVisible());
        }

        /**
         * Announces live transport state separately from persistence and preserves an explicit reconnect
         *   action.
         *
         * @param {string} key - Resource or entry key to resolve.
         * @param {boolean} retry - Whether to offer an explicit retry action.
         * @returns {void} Does not return a value.
         */
        connectionStatus(key, retry = false) {
            if (this.disposed) return;

            this.feedback("[data-connection-status]", text(key));
            const problem = !["Messages.Connecting", "Messages.Connected", "Messages.Reconnected"].includes(key);
            const status = this.root.querySelector("[data-connection-status]");

            if (status) {
                status.classList[problem ? "remove" : "add"]("visually-hidden");
            }

            const retryButton = this.root.querySelector("[data-reconnect]");

            if (retryButton) {
                retryButton.hidden = !retry;
            }
        }

        /**
         * Displays trusted localized status or validation wording as plain text.
         *
         * @param {string} value - Text to display safely.
         * @param {boolean} error - Whether to display error feedback.
         * @param {boolean} visible - Whether non-error feedback needs visible attention.
         * @returns {void} Does not return a value.
         */
        sendFeedback(value, error = false, visible = false) {
            if (this.disposed) return;

            const node = this.root.querySelector("[data-send-status]");

            node.textContent = value;
            node.dataset.error = String(error);
            node.classList[error || visible ? "remove" : "add"]("visually-hidden");
            this.input.setAttribute("aria-invalid", String(error));
        }

        /**
         * Writes a status without interpreting any server or resource string as HTML.
         *
         * @param {string} selector - Selector used to locate matching elements.
         * @param {string} value - Text to display safely.
         * @returns {void} Does not return a value.
         */
        feedback(selector, value) {
            if (this.disposed) return;

            const node = this.root.querySelector(selector);

            if (node) {
                node.textContent = value;
                node.hidden = !value;

                if (selector === "[data-history-status]") {
                    node.classList[value === text("Messages.NoOlder") ? "add" : "remove"]("visually-hidden");
                }
            }
        }

        /**
         * Excludes unexpected technical exceptions and response bodies from user feedback.
         *
         * @param {unknown} error - Failure whose safe feedback should be displayed.
         * @param {string} fallback - Resource key used when the failure has no safe presentation message.
         * @returns {string} Safe user feedback from the known failure or fallback resource.
         */
        safeError(error, fallback) {
            return error instanceof MessagingFailure ? error.message : text(fallback);
        }

        /**
         * Tracks removable DOM listeners so switching conversations releases inactive views.
         *
         * @param {EventTarget|null} target - Rendered target used by this component.
         * @param {string} name - Name of the requested field, attribute, or event.
         * @param {EventListener} handler - Handler registered and later removed with this page.
         * @param {AddEventListenerOptions|boolean|undefined} options - Native listener options preserved for
         *   later removal.
         * @returns {void} Does not return a value.
         */
        listen(target, name, handler, options) {
            if (!target) return;

            target.addEventListener(name, handler, options);
            this.listeners.push({
                target,
                name,
                handler,
                options
            });
        }

        /**
         * Releases this conversation's listeners and authorized group without closing the shared navigation
         *   connection.
         *
         * @returns {Promise<void>} Resolves when this operation completes.
         */
        async deactivate() {
            this.disposed = true;
            this.reads.abort();

            if (this.input) {
                this.input.disabled = true;
            }

            if (this.sendButton) {
                this.sendButton.disabled = true;
            }

            window.clearInterval(this.poll);
            this.observer?.disconnect();
            this.unsubscribe?.();

            for (const listener of this.listeners) {
                listener.target.removeEventListener?.(listener.name, listener.handler, listener.options);
            }

            this.listeners = [];

            if (this.conversationId && this.connection
                && this.connection.state === window.signalR?.HubConnectionState.Connected) {
                try {
                    await this.connection.invoke(this.config.realtime.leaveMethod, this.conversationId);
                } catch {
                    this.connectionStatus("Messages.Disconnected", true);
                }
            }
        }

        /**
         * Ends page-local resources and only closes transport when this is a standalone page.
         *
         * @returns {void} Does not return a value.
         */
        stop() {
            this.deactivate();

            if (!this.managed) {
                this.connection?.stop();
            }
        }
    }

    window.RentRoadMessaging = { MessagingPage, MessagingFailure };
    const config = document.getElementById("messaging-config");
    const root = document.querySelector("[data-messaging-page]");

    if (config && root && !root.hasAttribute("data-messaging-workspace")) {
        const page = new MessagingPage(root, JSON.parse(config.textContent));

        page.start();
    }
})();
