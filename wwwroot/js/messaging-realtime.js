/** Shares one official cookie-authenticated SignalR connection between navigation and an optional messaging page. */
(function messagingRealtimeModule() {
    "use strict";
    let connection;
    let starting;
    const subscribers = new Set();

    /** Creates the connection lazily from server-supplied metadata, retaining finite built-in reconnection attempts. */
    function getConnection(realtime) {
        if (!connection) {
            connection = new window.signalR.HubConnectionBuilder().withUrl(realtime.hubUrl)
                .configureLogging(window.signalR.LogLevel.None).withAutomaticReconnect().build();
            const hint = payload => { for (const subscriber of subscribers) subscriber.hint?.(payload); };
            connection.on(realtime.messageAvailableEvent, hint);
            connection.on(realtime.conversationUpdatedEvent, hint);
            connection.onreconnecting(() => { for (const subscriber of subscribers) subscriber.reconnecting?.(); });
            connection.onreconnected(() => { for (const subscriber of subscribers) subscriber.reconnected?.(); });
            connection.onclose(() => { for (const subscriber of subscribers) subscriber.closed?.(); });
        }
        return connection;
    }

    /** Coalesces simultaneous navigation/chat starts and refuses to compete with automatic reconnection. */
    async function start(realtime) {
        const current = getConnection(realtime);
        if (starting) return starting;
        if (current.state === window.signalR.HubConnectionState.Connected) return;
        if (current.state !== window.signalR.HubConnectionState.Disconnected) throw new Error();
        starting = current.start();
        try { await starting; }
        finally { starting = null; }
    }

    /** Registers a removable page consumer so conversation switching cannot accumulate stale live callbacks. */
    function subscribe(realtime, subscriber) {
        getConnection(realtime);
        subscribers.add(subscriber);
        return () => subscribers.delete(subscriber);
    }

    /** Stops only this page's ephemeral connection, without persisting messages or browser authentication data. */
    function stop() { return connection?.stop(); }

    window.RentRoadMessagingTransport = { getConnection, start, stop, subscribe };
    window.addEventListener("pagehide", stop);
})();
