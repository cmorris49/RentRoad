/** Shares ordered image selection, compact carousels and native modal galleries across public and private previews. */
(function initializeListingGalleries() {
    "use strict";
    const text = window.RentRoadText;

    /** Creates a text-safe DOM element without parsing image metadata as markup. */
    function node(tag, className, text) {
        const element = document.createElement(tag);
        if (className) element.className = className;
        if (text) element.textContent = text;
        return element;
    }

    /** Creates an accessible, explicitly labelled gallery control. */
    function button(label, text, action) {
        const element = node("button", "rr-gallery-button", text);
        element.type = "button";
        element.setAttribute("aria-label", label);
        element.addEventListener("click", action);
        return element;
    }

    /** Displays a failed image as text without losing its gallery position. */
    function watchImage(image) {
        /** Reveals the fallback for an unavailable original or thumbnail. */
        function unavailable() {
            image.hidden = true;
            const fallback = image.parentElement.querySelector("[data-image-fallback]");
            if (fallback) fallback.removeAttribute("aria-hidden");
        }
        image.addEventListener("error", unavailable);
        if (image.complete && image.naturalWidth === 0) unavailable();
    }

    /** Adds horizontal swipe navigation without intercepting vertical page scrolling. */
    function swipes(surface, move) {
        let start = null;
        let suppressClickUntil = 0;
        surface.addEventListener("touchstart", event => {
            suppressClickUntil = 0;
            start = event.touches.length === 1 ? { x: event.touches[0].clientX, y: event.touches[0].clientY } : null;
        }, { passive: true });
        surface.addEventListener("touchend", event => {
            if (!start || !event.changedTouches.length) return;
            const dx = event.changedTouches[0].clientX - start.x;
            const dy = event.changedTouches[0].clientY - start.y;
            start = null;
            if (Math.abs(dx) > 55 && Math.abs(dx) > Math.abs(dy) * 1.5) {
                suppressClickUntil = Date.now() + 500;
                move(dx < 0 ? 1 : -1);
            }
        }, { passive: true });
        surface.addEventListener("touchcancel", () => { start = null; suppressClickUntil = 0; }, { passive: true });
        surface.addEventListener("click", event => {
            if (Date.now() <= suppressClickUntil) { event.preventDefault(); event.stopPropagation(); }
        }, { capture: true });
    }

    /** Enhances existing server markup once, preserving primary-first ordering supplied by its read model. */
    function connectGallery(gallery) {
        if (gallery.galleryController) return gallery.galleryController;
        const slides = Array.from(gallery.querySelectorAll("[data-gallery-slide]"));
        const stage = gallery.querySelector("[data-gallery-stage]");
        const compact = gallery.dataset.galleryCompact === "true";
        if (!stage || !slides.length) return null;
        let selected = 0;
        let lightbox = null;
        let lightboxImage = null;
        let lightboxStatus = null;
        let returnFocus = null;
        const thumbnails = [];
        const status = node("p", "rr-gallery-status");
        status.dataset.galleryStatus = "";
        status.setAttribute("aria-live", "polite");
        status.setAttribute("aria-atomic", "true");
        const navigation = node("div", "rr-gallery-navigation");
        navigation.dataset.galleryControls = "";
        const strip = node("div", "rr-gallery-thumbnails");
        strip.setAttribute("aria-label", text("Gallery.Choose"));

        /** Selects a photo in the main view, thumbnail strip and any open lightbox. */
        function showPhoto(index) {
            if (!Number.isInteger(index)) return;
            selected = (index % slides.length + slides.length) % slides.length;
            slides.forEach((slide, position) => {
                slide.hidden = position !== selected;
                thumbnails[position]?.setAttribute("aria-pressed", String(position === selected));
            });
            status.textContent = text("Gallery.Position", selected + 1, slides.length);
            const chosen = thumbnails[selected];
            if (chosen && strip.clientWidth) {
                strip.scrollTo({ left: chosen.offsetLeft - strip.offsetLeft - (strip.clientWidth - chosen.offsetWidth) / 2, behavior: "auto" });
            }
            if (lightbox?.open) {
                const image = slides[selected].querySelector("img");
                lightboxImage.hidden = false;
                lightboxImage.parentElement.querySelector("[data-image-fallback]").setAttribute("aria-hidden", "true");
                lightboxImage.src = image.src;
                lightboxImage.alt = image.alt;
                lightboxStatus.textContent = text("Gallery.PhotoOf", selected + 1, slides.length);
            }
        }

        /** Moves through photos with wrapping, retaining the user's existing focus. */
        function move(offset) { showPhoto(selected + offset); }

        /** Handles only unmodified gallery navigation keys. */
        function navigate(event) {
            if (event.altKey || event.ctrlKey || event.metaKey || /^(INPUT|TEXTAREA|SELECT)$/.test(event.target.tagName)) return;
            const indices = { ArrowLeft: selected - 1, ArrowRight: selected + 1, Home: 0, End: slides.length - 1 };
            if (!(event.key in indices)) return;
            event.preventDefault();
            event.stopPropagation();
            showPhoto(indices[event.key]);
            if (thumbnails.includes(event.target)) thumbnails[selected].focus();
        }

        /** Restores scroll and the actual opener after native Escape or explicit close. */
        function closed() {
            if (!document.querySelector(".rr-lightbox[open]")) document.body.classList.remove("rr-lightbox-open");
            const opener = returnFocus?.closest("[data-gallery-slide]")?.hidden
                ? slides[selected].querySelector("[data-gallery-open]") : returnFocus;
            if (opener?.isConnected) opener.focus({ preventScroll: true });
        }

        /** Creates a native modal, including a focus cycle for consistent keyboard behaviour. */
        function createLightbox() {
            lightbox = node("dialog", "rr-lightbox");
            lightbox.setAttribute("aria-label", text("Gallery.Viewer"));
            const toolbar = node("div", "rr-lightbox-toolbar");
            lightboxStatus = node("p", "rr-gallery-status");
            lightboxStatus.setAttribute("aria-live", "polite");
            toolbar.append(lightboxStatus, button(text("Gallery.CloseLabel"), text("Gallery.Close"), () => lightbox.close()));
            const media = node("div", "rr-lightbox-media");
            const fallback = node("span", "rr-lightbox-fallback", text("Gallery.Unavailable"));
            fallback.dataset.imageFallback = "";
            fallback.setAttribute("aria-hidden", "true");
            lightboxImage = node("img");
            media.append(fallback, lightboxImage);
            watchImage(lightboxImage);
            lightbox.append(toolbar, media);
            if (slides.length > 1) {
                const controls = node("div", "rr-lightbox-navigation");
                controls.append(button(text("Gallery.Previous"), "←", () => move(-1)), button(text("Gallery.Next"), "→", () => move(1)));
                lightbox.append(controls);
            }
            lightbox.addEventListener("keydown", event => {
                navigate(event);
                if (event.key === "Escape") { event.preventDefault(); lightbox.close(); }
                if (event.key === "Tab") {
                    const controls = Array.from(lightbox.querySelectorAll("button"));
                    const first = controls[0], last = controls[controls.length - 1];
                    if (event.shiftKey && document.activeElement === first) { event.preventDefault(); last.focus(); }
                    else if (!event.shiftKey && document.activeElement === last) { event.preventDefault(); first.focus(); }
                }
                event.stopPropagation();
            });
            lightbox.addEventListener("close", closed);
            swipes(media, move);
            document.body.append(lightbox);
        }

        /** Opens the currently selected full-size image, including from a modal editor preview. */
        function openLightbox(event) {
            event.preventDefault();
            if (!lightbox) createLightbox();
            returnFocus = event.currentTarget;
            document.body.classList.add("rr-lightbox-open");
            lightbox.showModal();
            showPhoto(selected);
            lightbox.querySelector("button").focus();
        }

        slides.forEach((slide, index) => {
            const image = slide.querySelector("img");
            watchImage(image);
            if (!compact) slide.querySelector("[data-gallery-open]").addEventListener("click", openLightbox);
            if (!compact && slides.length > 1) {
                const thumbnail = button(text("Gallery.Show", index + 1, slides.length), "", () => showPhoto(index));
                thumbnail.dataset.galleryIndex = String(index);
                const small = node("img");
                small.src = image.src;
                small.alt = "";
                small.loading = "lazy";
                const fallback = node("span", "rr-thumbnail-fallback", String(index + 1));
                fallback.dataset.imageFallback = "";
                fallback.setAttribute("aria-hidden", "true");
                thumbnail.append(fallback, small);
                watchImage(small);
                strip.append(thumbnail);
                thumbnails.push(thumbnail);
            }
        });
        if (slides.length > 1) {
            navigation.append(button(text("Gallery.Previous"), "←", () => move(-1)), status, button(text("Gallery.Next"), "→", () => move(1)));
            stage.append(navigation);
            if (!compact) gallery.append(strip);
        }
        gallery.addEventListener("keydown", navigate);
        swipes(stage, move);
        gallery.dataset.galleryReady = "true";
        showPhoto(0);
        const controller = {
            show: showPhoto,
            /** Disposes a dynamic preview's modal and restores its previous document state. */
            dispose() { if (lightbox?.open) lightbox.close(); lightbox?.remove(); }
        };
        gallery.galleryController = controller;
        return controller;
    }

    /** Rebuilds a private preview using the same markup contract and interactions as the public gallery. */
    function setPhotos(gallery, photos, options = {}) {
        gallery.galleryController?.dispose();
        gallery.galleryController = null;
        gallery.replaceChildren();
        gallery.className = "rr-gallery" + (options.compact ? " rr-gallery-compact" : "");
        gallery.dataset.galleryCompact = String(Boolean(options.compact));
        const stage = node("div", "rr-gallery-stage");
        stage.dataset.galleryStage = "";
        gallery.append(stage);
        if (!photos.length) stage.append(node("div", "rr-gallery-empty", text("Gallery.Empty")));
        photos.forEach((photo, index) => {
            const slide = node("figure", "rr-gallery-slide");
            slide.dataset.gallerySlide = "";
            slide.hidden = index !== 0;
            const open = node("a", "rr-gallery-open");
            open.dataset.galleryOpen = "";
            open.href = photo.url;
            open.setAttribute("aria-label", text("Gallery.OpenPhoto", index + 1));
            if (options.compact) { open.removeAttribute("href"); open.removeAttribute("aria-label"); }
            const fallback = node("span", "listing-image-fallback", text("Gallery.Unavailable"));
            fallback.dataset.imageFallback = "";
            fallback.setAttribute("aria-hidden", "true");
            const image = node("img");
            image.src = photo.url;
            image.alt = photo.altText || options.title || text("Gallery.Photo");
            open.append(fallback, image);
            slide.append(open);
            if (photo.isPrimary && !options.compact) slide.append(node("span", "rr-gallery-primary badge-soft", text("Gallery.Primary")));
            stage.append(slide);
        });
        return connectGallery(gallery);
    }

    window.RentRoadGallery = { connect: connectGallery, setPhotos };
    for (const gallery of document.querySelectorAll("[data-listing-gallery]")) connectGallery(gallery);
})();
