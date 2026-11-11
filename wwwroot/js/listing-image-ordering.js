/** Adds persisted mouse, pen and touch thumbnail ordering while retaining ordinary Move up/down buttons. */
(function initializeImageOrdering() {
    "use strict";

    /** Connects a stable collection whose saved items may be rerendered after mutations. */
    function connect(collection, options) {
        let source = null, target = null, after = false, pointer = null;

        /** Clears only transient drag indicators without changing saved order. */
        function clear() {
            for (const item of collection.querySelectorAll("[data-order-id]")) item.classList.remove("is-dragging", "drop-before", "drop-after");
            source = target = null;
            pointer = null;
        }

        /** Resolves a trusted rendered image card and its proposed insertion boundary. */
        function indicate(element, x, y) {
            const card = element?.closest("[data-order-id]");
            for (const item of collection.querySelectorAll("[data-order-id]")) item.classList.remove("drop-before", "drop-after");
            target = card && collection.contains(card) && card !== source ? card : null;
            if (!target) return;
            const box = target.getBoundingClientRect();
            after = y > box.bottom - box.height * .25 || (y >= box.top + box.height * .25 && x > box.left + box.width / 2);
            target.classList.add(after ? "drop-after" : "drop-before");
        }

        /** Submits a complete image-ID order once; the caller rerenders only confirmed database state. */
        function commit() {
            const ids = Array.from(collection.querySelectorAll("[data-order-id]")).map(card => Number(card.dataset.orderId));
            const previousOrder = ids.join(",");
            const moving = Number(source?.dataset.orderId), destination = Number(target?.dataset.orderId);
            if (moving && destination && options.canMove()) {
                ids.splice(ids.indexOf(moving), 1);
                ids.splice(ids.indexOf(destination) + (after ? 1 : 0), 0, moving);
                clear();
                if (ids.join(",") !== previousOrder) options.save(ids, moving);
            } else clear();
        }

        collection.addEventListener("pointerdown", event => {
            const surface = event.target.closest("[data-order-surface]");
            if ((event.button !== undefined && event.button !== 0) || !options.canMove() || !surface) return;
            source = surface.closest("[data-order-id]");
            pointer = { id: event.pointerId, x: event.clientX, y: event.clientY };
            surface.setPointerCapture(event.pointerId);
            surface.focus({ preventScroll: true });
            event.preventDefault();
        });
        collection.addEventListener("pointermove", event => {
            if (!pointer || pointer.id !== event.pointerId || Math.hypot(event.clientX - pointer.x, event.clientY - pointer.y) < 8) return;
            source.classList.add("is-dragging");
            indicate(document.elementFromPoint(event.clientX, event.clientY), event.clientX, event.clientY);
        });
        collection.addEventListener("pointerup", event => { if (pointer?.id === event.pointerId) commit(); });
        collection.addEventListener("pointercancel", clear);
        collection.addEventListener("lostpointercapture", clear);
        collection.addEventListener("keydown", event => { if (event.key === "Escape") clear(); });
    }
    window.RentRoadImageOrdering = { connect };
})();
