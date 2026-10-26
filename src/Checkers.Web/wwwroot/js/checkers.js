// Browser helpers called from .NET through JS interop.
window.checkers = (() => {
    const maxFileSize = 1_000_000;
    const shortcutKeys = 'oszyman';
    const sounds = new Map();
    let queue = Promise.resolve();
    let keydown = null;
    let menuCleanup = null;
    let audio = null;

    return {
        registerShortcuts(dotnet) {
            keydown = e => {
                if (e.key === 'F1') {
                    if (e.target instanceof HTMLInputElement || e.target instanceof HTMLSelectElement) {
                        return;
                    }
                    e.preventDefault();
                    if (e.shiftKey) {
                        dotnet.invokeMethodAsync('OnShortcut', 'shift+f1');
                    } else if (!e.ctrlKey && !e.altKey && !e.metaKey) {
                        dotnet.invokeMethodAsync('OnShortcut', 'f1');
                    }
                    return;
                }
                // F2 opens Settings; Escape closes an open menu or the mobile drawer (dialogs handle their own Escape).
                if ((e.key === 'F2' || e.key === 'Escape') && !e.ctrlKey && !e.altKey && !e.metaKey && !e.shiftKey) {
                    if (e.target instanceof HTMLInputElement || e.target instanceof HTMLSelectElement) {
                        return;
                    }
                    if (e.key === 'F2') {
                        e.preventDefault();
                    }
                    dotnet.invokeMethodAsync('OnShortcut', e.key.toLowerCase());
                    return;
                }
                if (!(e.ctrlKey || e.metaKey) || e.altKey || e.shiftKey || e.key.length !== 1) {
                    return;
                }
                const key = e.key.toLowerCase();
                if (!shortcutKeys.includes(key) || e.target instanceof HTMLInputElement || e.target instanceof HTMLSelectElement) {
                    return;
                }
                e.preventDefault();
                dotnet.invokeMethodAsync('OnShortcut', key);
            };
            document.addEventListener('keydown', keydown);
        },

        unregisterShortcuts() {
            if (keydown) {
                document.removeEventListener('keydown', keydown);
                keydown = null;
            }
        },

        // Keeps the newest move in view by scrolling only the history list, never the page.
        scrollHistoryToEnd(list) {
            if (list) {
                list.scrollTo({ top: list.scrollHeight, behavior: 'smooth' });
            }
        },

        // Resolves to [name, text], or null if the user cancels.
        openTextFile(accept) {
            return new Promise((resolve, reject) => {
                const input = document.createElement('input');
                input.type = 'file';
                input.accept = accept;
                input.addEventListener('cancel', () => resolve(null));
                input.addEventListener('change', async () => {
                    const file = input.files[0];
                    if (!file) {
                        resolve(null);
                    } else if (file.size > maxFileSize) {
                        reject(new Error('The file is too large to be a checkers game.'));
                    } else {
                        resolve([file.name, await file.text()]);
                    }
                });
                input.click();
            });
        },

        downloadTextFile(name, text) {
            const url = URL.createObjectURL(new Blob([text], { type: 'text/plain' }));
            const link = document.createElement('a');
            link.href = url;
            link.download = name;
            document.body.appendChild(link);
            link.click();
            link.remove();
            setTimeout(() => URL.revokeObjectURL(url), 10_000);
        },

        beep() {
            audio ??= new (window.AudioContext || window.webkitAudioContext)();
            const oscillator = audio.createOscillator();
            const gain = audio.createGain();
            oscillator.frequency.value = 880;
            gain.gain.value = 0.1;
            oscillator.connect(gain).connect(audio.destination);
            oscillator.start();
            oscillator.stop(audio.currentTime + 0.1);
        },

        registerSound(name, bytes) {
            sounds.set(name, URL.createObjectURL(new Blob([bytes], { type: 'audio/wav' })));
        },

        playSound(name) {
            const url = sounds.get(name);
            if (!url) {
                return;
            }
            queue = queue.then(() => new Promise(resolve => {
                const sound = new Audio(url);
                sound.addEventListener('ended', resolve);
                sound.addEventListener('error', resolve);
                sound.play().catch(resolve);
            }));
        },

        initMenuAlignment() {
            this.cleanupMenuAlignment();
            const menubar = document.querySelector('.menubar');
            const boardArea = document.querySelector('.board-area');
            const content = document.querySelector('.content');
            if (!menubar || !boardArea) {
                return;
            }

            const update = () => this.updateMenuAlignment();
            update();

            const resizeObserver = new ResizeObserver(update);
            resizeObserver.observe(boardArea);
            if (content) {
                resizeObserver.observe(content);
                content.addEventListener('scroll', update, { passive: true });
            }
            window.addEventListener('resize', update, { passive: true });

            menuCleanup = () => {
                resizeObserver.disconnect();
                if (content) {
                    content.removeEventListener('scroll', update);
                }
                window.removeEventListener('resize', update);
                document.documentElement.style.removeProperty('--menu-left');
            };
        },

        updateMenuAlignment() {
            const menubar = document.querySelector('.menubar');
            const boardArea = document.querySelector('.board-area');
            if (!menubar || !boardArea) {
                return;
            }
            const boardRect = boardArea.getBoundingClientRect();
            let itemsWidth = 0;
            menubar.querySelectorAll('.desktop-menu-bar .menu').forEach(m => itemsWidth += m.offsetWidth);
            const maxLeft = Math.max(0, window.innerWidth - itemsWidth);
            const left = Math.min(Math.max(0, Math.round(boardRect.left)), maxLeft);
            document.documentElement.style.setProperty('--menu-left', `${left}px`);
        },

        cleanupMenuAlignment() {
            if (menuCleanup) {
                menuCleanup();
                menuCleanup = null;
            }
        },
    };
})();
