// Renders the formulas (KaTeX) and diagrams (Mermaid) of a documentation page.
const cdn = 'https://cdn.jsdelivr.net/npm/';
const katexFiles = {
    css: [`${cdn}katex@0.18.9/dist/katex.min.css`, 'sha384-lPx0C4zIUZLpveABMwOFcFeGZwsvKBJfhJ85FN1PYOV7xApBcFMhcAEMVKF8loOI'],
    js: [`${cdn}katex@0.18.9/dist/katex.min.js`, 'sha384-19KE2cFb3U+RUWmyhBz7aLOGDG8WrRC6hE3oY/HTZZlAAVWYTdmvLC//+TIV3zUx'],
};
const mermaidFile = [`${cdn}mermaid@11.17.2/dist/mermaid.min.js`, 'sha384-EOXBFmc3gx5mb+vn0vPvvGqACToJD24hhacX5Yx+8NUUQrHIle/Qi5Bg9o3zKwW2'];

const loading = new Map();
let mermaidReady = false;

function load(tag, [url, integrity]) {
    if (!loading.has(url)) {
        loading.set(url, new Promise((resolve, reject) => {
            const element = document.createElement(tag);
            if (tag === 'link') {
                element.rel = 'stylesheet';
                element.href = url;
            } else {
                element.src = url;
            }
            element.integrity = integrity;
            element.crossOrigin = 'anonymous';
            element.onload = resolve;
            element.onerror = () => {
                loading.delete(url);
                element.remove();
                reject(new Error(`Could not load ${url}.`));
            };
            document.head.appendChild(element);
        }));
    }
    return loading.get(url);
}

function isDisplay(node, raw) {
    if (node.tagName === 'DIV' || raw.startsWith('\\[')) {
        return true;
    }
    const parent = node.parentElement;
    return parent.tagName === 'P' && [...parent.childNodes].every(child =>
        child === node || (child.nodeType === Node.TEXT_NODE && child.textContent.trim() === ''));
}

async function renderMath(article) {
    const nodes = article.querySelectorAll('.math');
    if (nodes.length === 0) {
        return;
    }
    await Promise.all([load('link', katexFiles.css), load('script', katexFiles.js)]);
    for (const node of nodes) {
        const raw = node.textContent.trim();
        const displayMode = isDisplay(node, raw);
        const tex = raw.replace(/^\\[([]/, '').replace(/\\[)\]]$/, '');
        node.classList.toggle('math-display', displayMode);
        window.katex.render(tex, node, { displayMode, throwOnError: false });
    }
}

async function renderDiagrams(article) {
    const nodes = article.querySelectorAll('.mermaid');
    if (nodes.length === 0) {
        return;
    }
    await load('script', mermaidFile);
    if (!mermaidReady) {
        window.mermaid.initialize({ startOnLoad: false, securityLevel: 'strict', theme: 'neutral' });
        mermaidReady = true;
    }
    await window.mermaid.run({ nodes, suppressErrors: true });
}

// Returns an error message, or an empty string when everything was shown.
export async function render(article, fragment) {
    const errors = [];
    for (const step of [renderMath, renderDiagrams]) {
        try {
            await step(article);
        } catch (error) {
            errors.push(error.message);
        }
    }
    scrollTo(fragment);
    return errors.join(' ');
}

export function scrollTo(fragment) {
    const target = fragment ? document.getElementById(decodeURIComponent(fragment)) : null;
    if (target) {
        target.scrollIntoView();
    } else {
        window.scrollTo(0, 0);
    }
}
