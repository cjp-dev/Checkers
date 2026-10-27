/*! coi-serviceworker v0.1.7 - Guido Zuidhof and contributors, licensed under MIT */
let coepCredentialless = false;
if (typeof window === 'undefined') {
    self.addEventListener("install", () => self.skipWaiting());
    self.addEventListener("activate", (event) => event.waitUntil(self.clients.claim()));

    self.addEventListener("message", (ev) => {
        if (!ev.data) {
            return;
        } else if (ev.data.type === "deregister") {
            self.registration
                .unregister()
                .then(() => self.clients.matchAll())
                .then(clients => {
                    clients.forEach((client) => client.navigate(client.url));
                });
        } else if (ev.data.type === "coepCredentialless") {
            coepCredentialless = ev.data.value;
        }
    });

    self.addEventListener("fetch", function (event) {
        const r = event.request;
        if (r.cache === "only-if-cached" && r.mode !== "same-origin") {
            return;
        }

        const request = (coepCredentialless && r.mode === "no-cors")
            ? new Request(r, { credentials: "omit" })
            : r;

        event.respondWith(
            fetch(request)
                .then((response) => {
                    if (response.status === 0) {
                        return response;
                    }

                    const newHeaders = new Headers(response.headers);
                    newHeaders.set(
                        "Cross-Origin-Embedder-Policy",
                        coepCredentialless ? "credentialless" : "require-corp"
                    );

                    if (!newHeaders.has("Cross-Origin-Opener-Policy")) {
                        newHeaders.set("Cross-Origin-Opener-Policy", "same-origin");
                    }

                    if (!newHeaders.has("Cross-Origin-Resource-Policy")) {
                        newHeaders.set("Cross-Origin-Resource-Policy", "cross-origin");
                    }

                    return new Response(response.body, {
                        status: response.status,
                        statusText: response.statusText,
                        headers: newHeaders,
                    });
                })
                .catch((e) => console.error(e))
        );
    });

} else {
    (async function () {
        if (window.crossOriginIsolated) {
            return;
        }

        const reloadedBySelf = window.sessionStorage.getItem("coiReloadedBySelf");
        window.sessionStorage.removeItem("coiReloadedBySelf");

        const coi = {
            shouldRegister: () => !reloadedBySelf,
            shouldDeregister: () => false,
            coepCredentialless: () => true,
            coepDegrade: () => true,
            doReload: () => window.location.reload(),
            quiet: false,
            ...window.coi
        };

        if (coi.shouldDeregister()) {
            navigator.serviceWorker?.getRegistration().then(registration => {
                if (registration) {
                    registration.unregister().then(() => {
                        window.location.reload();
                    });
                }
            });
            return;
        }

        if (!coi.shouldRegister()) {
            return;
        }

        if (navigator.serviceWorker) {
            try {
                const reg = await navigator.serviceWorker.register(window.document.currentScript.src);

                if (!coi.quiet) {
                    console.log("[coi-serviceworker] Registered", reg.scope);
                }

                reg.addEventListener("updatefound", () => {
                    window.sessionStorage.setItem("coiReloadedBySelf", "true");
                    coi.doReload();
                });

                if (navigator.serviceWorker.controller) {
                    let coepCredentialless = coi.coepCredentialless();
                    navigator.serviceWorker.controller.postMessage({
                        type: "coepCredentialless",
                        value: coepCredentialless,
                    });

                    if (coepCredentialless && coi.coepDegrade()) {
                        fetch(window.location.href).catch(() => {
                            navigator.serviceWorker.controller.postMessage({
                                type: "coepCredentialless",
                                value: false,
                            });
                            window.sessionStorage.setItem("coiReloadedBySelf", "true");
                            coi.doReload();
                        });
                    }
                } else if (!reloadedBySelf) {
                    window.sessionStorage.setItem("coiReloadedBySelf", "true");
                    coi.doReload();
                }
            } catch (err) {
                if (!coi.quiet) {
                    console.error("[coi-serviceworker] Registration failed:", err);
                }
            }
        }
    })();
}

