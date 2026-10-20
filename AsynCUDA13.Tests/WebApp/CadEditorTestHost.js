globalThis.window = globalThis;
const test = {
    timer: null,
    packet: null,
    listeners: {},
    storage: null,
    cameraOption: null,
    frameError: null,
    frameCompleted: false,
    drawnFrames: 0,
    status: { textContent: "" }
};
const context = {
    setTransform() {},
    putImageData(image) { test.drawnFrames++; test.image = image; }
};
const canvas = {
    width: 0,
    height: 0,
    getContext() { return context; },
    getBoundingClientRect() { return { width: 320, height: 240 }; },
    addEventListener(name, callback) { test.listeners[name] = callback; },
    focus() {},
    setPointerCapture() {}
};
const list = { replaceChildren() {}, append() {} };
const fileInput = {
    files: [],
    addEventListener(name, callback) { test.importHandler = callback; }
};
globalThis.URL = {
    createObjectURL(blob) { test.exported = blob.parts.join(""); return "blob:scene"; },
    revokeObjectURL() {}
};
globalThis.document = {
    getElementById(id) {
        if (id === "canvas") return canvas;
        if (id === "list") return list;
        if (id === "file") return fileInput;
        if (id === "cad-tool-status") return test.status;
        return null;
    },
    createElement() {
        return { setAttribute() {}, addEventListener() {}, append() {}, click() {} };
    }
};
globalThis.devicePixelRatio = 1;
globalThis.addEventListener = () => {};
globalThis.AbortController = class { constructor() { this.signal = {}; } abort() {} };
globalThis.ResizeObserver = class { observe() {} disconnect() {} };
globalThis.Blob = class { constructor(parts) { this.parts = parts; } };
globalThis.ImageData = class {
    constructor(data, width, height) { this.data = data; this.width = width; this.height = height; }
};
globalThis.DotNet = { createJSStreamReference(value) { return value; } };
globalThis.localStorage = {
    getItem() { return test.storage; },
    setItem(key, value) { test.storage = value; }
};
globalThis.setTimeout = callback => { test.timer = callback; return 1; };
globalThis.clearTimeout = () => { test.timer = null; };
const dotNetReference = {
    invokeMethodAsync(method, ...args) {
        if (method === "QueueBackendRenderAsync") {
            test.packet = { camera: args[2], width: args[3], height: args[4], revision: args[5] };
        } else if (method === "OnCameraBelowGroundChanged") {
            test.cameraOption = args[0];
        }
        return Promise.resolve();
    }
};
test.initialize = () => {
    cadEditor.initialize("canvas", "list", "file", {}, dotNetReference);
    test.flushRender();
};
test.flushRender = () => {
    const callback = test.timer;
    test.timer = null;
    if (callback) callback();
};
test.orbit = deltaY => {
    const event = { pointerId: 1, offsetX: 160, offsetY: 120, button: 0, preventDefault() {} };
    test.listeners.pointerdown(event);
    const moved = { ...event, offsetY: event.offsetY + deltaY };
    test.listeners.pointermove(moved);
    test.listeners.pointerup(moved);
    test.flushRender();
};
test.pan = deltaY => {
    const event = { pointerId: 2, offsetX: 160, offsetY: 120, button: 1, preventDefault() {} };
    test.listeners.pointerdown(event);
    const moved = { ...event, offsetY: event.offsetY + deltaY };
    test.listeners.pointermove(moved);
    test.listeners.pointerup(moved);
    test.flushRender();
};
test.zoom = deltaY => {
    test.listeners.wheel({ deltaY, preventDefault() {} });
    test.flushRender();
};
test.importScene = content => {
    fileInput.files = [{ name: "scene.json", size: content.length, text: async () => content }];
    test.importHandler().then(() => test.flushRender());
};
test.deliverFrame = stale => {
    test.frameError = null;
    test.frameCompleted = false;
    // A .NET-to-JS stream reference provides arrayBuffer, not a JS dispose method.
    const stream = { arrayBuffer: async () => new ArrayBuffer(320 * 240 * 4) };
    const revision = test.packet.revision - (stale ? 1 : 0);
    cadEditor.displayBackendFrame(stream, 320, 240, revision, "rendered").then(
        () => { test.frameCompleted = true; },
        error => { test.frameError = error.message; });
};
