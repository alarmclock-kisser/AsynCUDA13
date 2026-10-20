(() => {
    const storageKey = "asynCUDA13.cad.scene.v1";
    const defaultCamera = () => ({ target: [0, 0, 0], azimuth: 0.75, elevation: 0.62, distance: 12, allowBelowGround: false });
    let shapeNames = {};
    const state = {
        canvas: null,
        context: null,
        list: null,
        fileInput: null,
        abort: null,
        resizeObserver: null,
        width: 0,
        height: 0,
        pixelWidth: 0,
        pixelHeight: 0,
        dpr: 1,
        scene: [],
        selectedId: null,
        tool: "cube",
        moveMode: false,
        sketchPoints: [],
        extrusion: null,
        camera: defaultCamera(),
        pointer: null,
        lastPointerY: 0,
        history: [],
        future: [],
        text: {},
        dotNetRef: null,
        renderTimer: null,
        renderRevision: 0,
        rendering: false
    };

    const vAdd = (a, b) => [a[0] + b[0], a[1] + b[1], a[2] + b[2]];
    const vSub = (a, b) => [a[0] - b[0], a[1] - b[1], a[2] - b[2]];
    const vScale = (a, s) => [a[0] * s, a[1] * s, a[2] * s];
    const vDot = (a, b) => a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
    const vCross = (a, b) => [a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0]];
    const vLength = a => Math.hypot(a[0], a[1], a[2]);
    const vNormalize = a => {
        const length = vLength(a) || 1;
        return vScale(a, 1 / length);
    };
    const getEl = id => document.getElementById(id);

    function text(key, values = {}) {
        const template = state.text[key] ?? key;
        return template
            .replace(/\{\{(\w+)\}\}/g, (_, name) => values[name] ?? "")
            .replace(/\{(\d+)\}/g, (_, index) => values[index] ?? values.count ?? "");
    }

    function setStatus(message) {
        const status = getEl("cad-tool-status");
        if (status) status.textContent = message;
    }

    function makeId() {
        return typeof crypto !== "undefined" && crypto.randomUUID
            ? crypto.randomUUID()
            : `${Date.now()}-${Math.random().toString(36).slice(2)}`;
    }

    function snapshot() {
        return JSON.stringify({ scene: state.scene, selectedId: state.selectedId });
    }

    function saveHistory() {
        state.history.push(snapshot());
        if (state.history.length > 100) state.history.shift();
        state.future.length = 0;
        updateControls();
    }

    function restoreSnapshot(serialized) {
        const snapshotData = JSON.parse(serialized);
        state.scene = snapshotData.scene;
        state.selectedId = snapshotData.selectedId;
        persist();
        render();
    }

    function persist() {
        try {
            constrainCamera();
            localStorage.setItem(storageKey, JSON.stringify({ scene: state.scene, camera: state.camera }));
            const label = getEl("cad-save-status");
            if (label) label.textContent = text("saved");
        } catch (error) {
            const label = getEl("cad-save-status");
            if (label) label.textContent = text("saveFailed");
            setStatus(`${text("saveError")} ${error.message}`);
        }
    }

    function isValidShape(shape) {
        const valid = shape && typeof shape.id === "string" && Object.hasOwn(shapeNames, shape.kind) &&
            Array.isArray(shape.position) && shape.position.length === 3 && shape.position.every(Number.isFinite) &&
            Array.isArray(shape.scale) && shape.scale.length === 3 && shape.scale.every(Number.isFinite);
        return valid && (shape.kind !== "prism" ||
            (Array.isArray(shape.points) && shape.points.length >= 3 && shape.points.length <= 128 &&
                shape.points.every(point => Array.isArray(point) && point.length === 2 && point.every(Number.isFinite))));
    }

    function loadPersisted() {
        const serialized = localStorage.getItem(storageKey);
        if (!serialized) return;
        try {
            const saved = JSON.parse(serialized);
            if (Array.isArray(saved.scene)) state.scene = saved.scene.filter(isValidShape).slice(0, 500);
            restoreCamera(saved.camera);
        } catch (error) {
            setStatus(`${text("savedLoadError")} ${error.message}`);
        }
    }

    function restoreCamera(camera) {
        if (!camera || !Array.isArray(camera.target) || camera.target.length !== 3 || !camera.target.every(Number.isFinite)) return;
        const defaults = defaultCamera();
        state.camera = {
            target: [...camera.target],
            azimuth: Number.isFinite(camera.azimuth) ? camera.azimuth : defaults.azimuth,
            elevation: Number.isFinite(camera.elevation) ? camera.elevation : defaults.elevation,
            distance: Number.isFinite(camera.distance) ? camera.distance : defaults.distance,
            allowBelowGround: camera.allowBelowGround === true
        };
        constrainCamera();
    }

    function constrainCamera() {
        const camera = state.camera;
        camera.distance = Math.max(2.5, Math.min(100, camera.distance));
        camera.elevation = Math.max(camera.allowBelowGround ? -1.35 : 0.01, Math.min(1.35, camera.elevation));
        if (!camera.allowBelowGround) {
            // Keep the actual eye above the world floor, including after pan and zoom.
            const eyeY = camera.target[1] + Math.sin(camera.elevation) * camera.distance;
            if (eyeY < 0.05) camera.target[1] += 0.05 - eyeY;
        }
    }

    function notifyCameraOption() {
        return state.dotNetRef?.invokeMethodAsync("OnCameraBelowGroundChanged", state.camera.allowBelowGround)
            .catch(error => setStatus(error.message));
    }

    function setAllowBelowGround(allowed) {
        state.camera.allowBelowGround = allowed === true;
        constrainCamera();
        persist();
        render();
    }

    function resizeCanvas() {
        if (!state.canvas || !state.context) return;
        const rect = state.canvas.getBoundingClientRect();
        state.width = Math.max(1, rect.width);
        state.height = Math.max(1, rect.height);
        state.dpr = Math.max(1, Math.min(2, window.devicePixelRatio || 1));
        const scale = Math.min(1, 1024 / (state.width * state.dpr), 768 / (state.height * state.dpr));
        state.pixelWidth = Math.max(1, Math.round(state.width * state.dpr * scale));
        state.pixelHeight = Math.max(1, Math.round(state.height * state.dpr * scale));
        state.canvas.width = state.pixelWidth;
        state.canvas.height = state.pixelHeight;
        state.context.setTransform(1, 0, 0, 1, 0, 0);
        render();
    }

    function getCameraBasis() {
        const cosElevation = Math.cos(state.camera.elevation);
        const eyeOffset = [
            Math.sin(state.camera.azimuth) * cosElevation,
            Math.sin(state.camera.elevation),
            Math.cos(state.camera.azimuth) * cosElevation
        ];
        const eye = vAdd(state.camera.target, vScale(eyeOffset, state.camera.distance));
        const forward = vNormalize(vSub(state.camera.target, eye));
        const right = vNormalize(vCross(forward, [0, 1, 0]));
        const up = vNormalize(vCross(right, forward));
        return { eye, forward, right, up };
    }

    function project(point, basis) {
        const relative = vSub(point, basis.eye);
        const depth = vDot(relative, basis.forward);
        if (depth <= 0.05) return null;
        const focalLength = state.height / (2 * Math.tan(Math.PI / 7.2));
        return {
            x: state.width / 2 + vDot(relative, basis.right) * focalLength / depth,
            y: state.height / 2 - vDot(relative, basis.up) * focalLength / depth,
            depth
        };
    }

    function screenToGround(x, y) {
        const basis = getCameraBasis();
        const focalLength = state.height / (2 * Math.tan(Math.PI / 7.2));
        const direction = vNormalize(vAdd(basis.forward, vAdd(
            vScale(basis.right, (x - state.width / 2) / focalLength),
            vScale(basis.up, (state.height / 2 - y) / focalLength))));
        if (Math.abs(direction[1]) < 0.0001) return null;
        const distance = -basis.eye[1] / direction[1];
        if (distance <= 0) return null;
        const point = vAdd(basis.eye, vScale(direction, distance));
        return [point[0], 0, point[2]];
    }

    function worldPerPixel() {
        return 2 * Math.tan(Math.PI / 7.2) * state.camera.distance / state.height;
    }

    function render() {
        if (!state.context || state.width <= 0 || state.height <= 0) return;
        constrainCamera();
        updateControls();
        requestBackendRender();
    }

    function createRenderPacket() {
        const items = state.scene.map(shape => ({ ...shape, renderKind: shape.kind }));
        if (state.extrusion) items.push({ ...state.extrusion, renderKind: "prism" });

        const records = [];
        if (state.tool === "prism" && !state.extrusion) {
            for (const point of state.sketchPoints) {
                records.push({ kind: 5, position: point, scale: [0.08, 0.08, 0.08], points: [], stride: 2, selected: false });
            }
            for (let i = 1; i < state.sketchPoints.length; i++) {
                records.push({ kind: 6, position: [0, 0, 0], scale: [1, 1, 1], points: [...state.sketchPoints[i - 1], ...state.sketchPoints[i]], stride: 3, selected: false });
            }
        }

        const kindIds = { cube: 0, sphere: 1, pyramid: 2, prism: 3, cylinder: 4 };
        for (const shape of items) {
            const kind = kindIds[shape.renderKind];
            if (kind === undefined) continue;
            const points = shape.renderKind === "prism" && Array.isArray(shape.points) ? shape.points.flat() : [];
            records.push({
                kind,
                position: shape.position,
                scale: shape.scale,
                points,
                stride: 2,
                selected: shape.id === state.selectedId
            });
        }

        const descriptorLength = records.length * 10;
        const totalPointLength = records.reduce((sum, record) => sum + record.points.length, 0);
        const packed = new Float32Array(Math.max(1, descriptorLength + totalPointLength));
        let pointOffset = descriptorLength;
        records.forEach((record, index) => {
            const pointCount = record.points.length / record.stride;
            packed.set([
                record.kind,
                record.position[0], record.position[1], record.position[2],
                record.scale[0], record.scale[1], record.scale[2],
                pointCount === 0 ? 0 : pointOffset,
                pointCount,
                record.selected ? 1 : 0
            ], index * 10);
            if (record.points.length > 0) {
                packed.set(record.points, pointOffset);
                pointOffset += record.points.length;
            }
        });

        const basis = getCameraBasis();
        const focalLength = state.pixelHeight / (2 * Math.tan(Math.PI / 7.2));
        return {
            sceneData: Array.from(packed),
            primitiveCount: records.length,
            camera: [...basis.eye, ...basis.right, ...basis.up, ...basis.forward, focalLength]
        };
    }

    function requestBackendRender(immediate = false) {
        if (!state.dotNetRef || !state.canvas) return;
        const revision = ++state.renderRevision;
        state.rendering = true;
        const backendButton = getEl("cad-backend-button");
        if (backendButton) backendButton.disabled = true;
        clearTimeout(state.renderTimer);
        state.renderTimer = setTimeout(() => {
            state.renderTimer = null;
            const packet = createRenderPacket();
            const sceneStream = DotNet.createJSStreamReference(new Blob([JSON.stringify(packet.sceneData)], { type: "application/json" }));
            state.dotNetRef.invokeMethodAsync("QueueBackendRenderAsync", sceneStream, packet.primitiveCount, packet.camera, state.pixelWidth, state.pixelHeight, revision)
                .catch(error => backendRenderFailed(revision, error.message));
        }, immediate ? 0 : 70);
    }

    async function displayBackendFrame(streamReference, width, height, revision, message) {
        // The .NET caller owns and disposes its DotNetStreamReference after this promise completes.
        const bytes = new Uint8ClampedArray(await streamReference.arrayBuffer());
        if (!state.canvas || revision !== state.renderRevision) return;
        state.context.putImageData(new ImageData(bytes, width, height), 0, 0);
        state.rendering = false;
        setStatus(message);
        updateControls();
    }

    function backendRenderFailed(revision, message) {
        if (revision !== state.renderRevision) return;
        state.rendering = false;
        setStatus(message);
        updateControls();
    }

    function updateControls() {
        if (!state.canvas) return;
        const count = state.scene.length;
        const objectCount = getEl("cad-object-count");
        const sideCount = getEl("cad-object-count-side");
        if (objectCount) objectCount.textContent = text(count === 1 ? "objectOne" : "objectMany", { count, 0: count });
        if (sideCount) sideCount.textContent = String(count);
        const selected = state.scene.find(shape => shape.id === state.selectedId);
        const summary = getEl("cad-selection-summary");
        if (summary) summary.textContent = selected ? `${selected.name} · ${shapeNames[selected.kind]}` : text("noSelection");
        const addButton = getEl("cad-add-button");
        if (addButton) addButton.disabled = state.tool === "prism";
        const completeButton = getEl("cad-prism-complete");
        if (completeButton) completeButton.disabled = state.tool !== "prism" || state.sketchPoints.length < 3 || !!state.extrusion;
        const finishButton = getEl("cad-prism-finish");
        if (finishButton) finishButton.disabled = !state.extrusion;
        const moveButton = getEl("cad-move-button");
        if (moveButton) {
            moveButton.disabled = !selected;
            moveButton.classList.toggle("active", state.moveMode);
        }
        const deleteButton = getEl("cad-delete-button");
        if (deleteButton) deleteButton.disabled = !selected;
        const backendButton = getEl("cad-backend-button");
        if (backendButton) backendButton.disabled = state.rendering;
        if (state.list) {
            state.list.replaceChildren();
            for (const shape of [...state.scene].reverse()) {
                const item = document.createElement("li");
                const button = document.createElement("button");
                button.type = "button";
                button.className = `cad-object-item${shape.id === state.selectedId ? " selected" : ""}`;
                button.textContent = shape.name;
                button.setAttribute("aria-pressed", shape.id === state.selectedId ? "true" : "false");
                button.addEventListener("click", () => {
                    state.selectedId = shape.id;
                    state.moveMode = false;
                    render();
                }, { signal: state.abort.signal });
                item.append(button);
                state.list.append(item);
            }
        }
    }

    function addShape(kind, overrides = {}) {
        const next = state.scene.length;
        const offset = next === 0 ? 0 : (Math.ceil(next / 2) * (next % 2 === 0 ? -1 : 1) * 1.8);
        const shape = {
            name: `${shapeNames[kind]} ${next + 1}`,
            position: [state.camera.target[0] + offset, kind === "pyramid" || kind === "prism" ? 0 : 0.5, state.camera.target[2]],
            scale: [1, 1, 1],
            ...overrides,
            id: makeId(),
            kind
        };
        saveHistory();
        state.scene.push(shape);
        state.selectedId = shape.id;
        state.moveMode = false;
        persist();
        render();
        setStatus(text("statusCreated", { name: shape.name }));
    }

    function selectAt(x, y) {
        const basis = getCameraBasis();
        let best = null;
        let bestDistance = 28;
        for (const shape of state.scene) {
            const centerY = shape.kind === "pyramid" || shape.kind === "prism" ? shape.position[1] + shape.scale[1] / 2 : shape.position[1];
            const center = [shape.position[0], centerY, shape.position[2]];
            const projected = project(center, basis);
            if (!projected) continue;
            const distance = Math.hypot(projected.x - x, projected.y - y);
            if (distance < bestDistance) {
                bestDistance = distance;
                best = shape;
            }
        }
        state.selectedId = best?.id ?? null;
        state.moveMode = false;
        render();
        if (best) setStatus(text("statusSelected", { name: best.name }));
    }

    function completePrismSketch() {
        if (state.tool !== "prism" || state.sketchPoints.length < 3 || state.extrusion) {
            setStatus(text("prismMinimum"));
            return;
        }
        const points = state.sketchPoints;
        const centerX = points.reduce((sum, point) => sum + point[0], 0) / points.length;
        const centerZ = points.reduce((sum, point) => sum + point[2], 0) / points.length;
        const localPoints = points.map(point => [point[0] - centerX, point[2] - centerZ]);
        state.extrusion = {
            id: "preview",
            kind: "prism",
            name: `${shapeNames.prism} ${state.scene.length + 1}`,
            position: [centerX, 0, centerZ],
            scale: [1, 0.2, 1],
            points: localPoints
        };
        state.lastPointerY = state.lastPointerY || state.height / 2;
        state.extrusion.startY = state.lastPointerY;
        render();
        setStatus(text("prismHeight"));
    }

    function finishPrism() {
        if (!state.extrusion) {
            setStatus(text("prismMinimum"));
            return;
        }
        const prism = state.extrusion;
        state.extrusion = null;
        state.sketchPoints = [];
        state.tool = "cube";
        addShape("prism", prism);
        const tool = getEl("cad-shape-type");
        if (tool) tool.value = "cube";
        setStatus(text("prismCreated", { name: prism.name }));
    }

    function deleteSelected() {
        if (!state.selectedId) return;
        saveHistory();
        state.scene = state.scene.filter(shape => shape.id !== state.selectedId);
        state.selectedId = null;
        state.moveMode = false;
        persist();
        render();
        setStatus(text("selectedDeleted"));
    }

    function clearScene() {
        if (state.scene.length === 0) return;
        if (!window.confirm(text("confirmClear"))) return;
        saveHistory();
        state.scene = [];
        state.selectedId = null;
        state.moveMode = false;
        persist();
        render();
        setStatus(text("sceneCleared"));
    }

    function undo() {
        if (state.history.length === 0) return;
        state.future.push(snapshot());
        restoreSnapshot(state.history.pop());
        setStatus(text("undo"));
    }

    function redo() {
        if (state.future.length === 0) return;
        state.history.push(snapshot());
        restoreSnapshot(state.future.pop());
        setStatus(text("redo"));
    }

    function setTool(tool) {
        if (!Object.hasOwn(shapeNames, tool)) return;
        state.tool = tool;
        state.sketchPoints = [];
        state.extrusion = null;
        state.moveMode = false;
        render();
        setStatus(text(tool === "prism" ? "statusPrismTool" : "statusSelectedTool"));
    }

    function pointerDown(event) {
        if (event.button !== 0 && event.button !== 1) return;
        event.preventDefault();
        state.canvas.focus({ preventScroll: true });
        state.canvas.setPointerCapture(event.pointerId);
        const x = event.offsetX;
        const y = event.offsetY;
        state.lastPointerY = y;
        if (state.tool === "prism" && !state.extrusion) {
            state.pointer = { id: event.pointerId, x, y, lastX: x, lastY: y, mode: "sketch" };
            return;
        }
        if (state.extrusion) {
            state.pointer = { id: event.pointerId, x, y, lastX: x, lastY: y, mode: "extrusion" };
            return;
        }
        if (state.moveMode && state.selectedId) {
            const ground = screenToGround(x, y);
            const shape = state.scene.find(item => item.id === state.selectedId);
            if (ground && shape) {
                saveHistory();
                state.pointer = { id: event.pointerId, x, y, lastX: x, lastY: y, mode: "move", ground, position: [...shape.position] };
                return;
            }
        }
        const pan = event.button === 1 || event.shiftKey || event.button === 2;
        state.pointer = { id: event.pointerId, x, y, lastX: x, lastY: y, mode: pan ? "pan" : "orbit" };
    }

    function pointerMove(event) {
        state.lastPointerY = event.offsetY;
        const pointer = state.pointer;
        if (!pointer || pointer.id !== event.pointerId) {
            if (state.extrusion) {
                const delta = state.extrusion.startY - event.offsetY;
                state.extrusion.scale[1] = Math.max(0.1, Math.min(100, Math.abs(delta) * worldPerPixel()));
                render();
            }
            return;
        }
        const dx = event.offsetX - pointer.lastX;
        const dy = event.offsetY - pointer.lastY;
        pointer.lastX = event.offsetX;
        pointer.lastY = event.offsetY;
        if (pointer.mode === "extrusion") {
            const delta = state.extrusion.startY - event.offsetY;
            state.extrusion.scale[1] = Math.max(0.1, Math.min(100, Math.abs(delta) * worldPerPixel()));
            render();
        } else if (pointer.mode === "orbit") {
            state.camera.azimuth -= dx * 0.008;
            state.camera.elevation = Math.max(-1.35, Math.min(1.35, state.camera.elevation + dy * 0.008));
            render();
        } else if (pointer.mode === "pan") {
            const basis = getCameraBasis();
            const units = worldPerPixel();
            state.camera.target = vAdd(state.camera.target, vAdd(vScale(basis.right, -dx * units), vScale(basis.up, dy * units)));
            render();
        } else if (pointer.mode === "move") {
            const ground = screenToGround(event.offsetX, event.offsetY);
            const shape = state.scene.find(item => item.id === state.selectedId);
            if (ground && shape) {
                shape.position[0] = pointer.position[0] + ground[0] - pointer.ground[0];
                shape.position[2] = pointer.position[2] + ground[2] - pointer.ground[2];
                shape.meshWorld = null;
                render();
            }
        }
    }

    function pointerUp(event) {
        const pointer = state.pointer;
        if (!pointer || pointer.id !== event.pointerId) return;
        const moved = Math.hypot(event.offsetX - pointer.x, event.offsetY - pointer.y);
        state.pointer = null;
        if (pointer.mode === "sketch" && moved < 4) {
            const point = screenToGround(event.offsetX, event.offsetY);
            if (point) {
                state.sketchPoints.push(point);
                render();
                setStatus(text("prismPointSet", { count: state.sketchPoints.length }));
            }
        } else if (pointer.mode === "move") {
            persist();
            setStatus(text("moveFinished"));
        } else if (pointer.mode === "orbit") {
            if (moved < 4) selectAt(pointer.x, pointer.y); else persist();
        } else if (pointer.mode === "pan") {
            persist();
        }
    }

    function wheel(event) {
        event.preventDefault();
        state.camera.distance = Math.max(2.5, Math.min(100, state.camera.distance * Math.exp(event.deltaY * 0.001)));
        render();
        persist();
    }

    function keyDown(event) {
        const target = event.target;
        if (target instanceof HTMLElement && (target.isContentEditable || ["INPUT", "TEXTAREA", "SELECT"].includes(target.tagName))) return;
        if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === "z") {
            event.preventDefault();
            event.shiftKey ? redo() : undo();
        } else if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === "y") {
            event.preventDefault(); redo();
        } else if (event.key === "Delete" || event.key === "Backspace") {
            event.preventDefault(); deleteSelected();
        } else if (event.key === "Escape") {
            event.preventDefault();
            state.sketchPoints = [];
            state.extrusion = null;
            render();
            setStatus(text("prismCancelled"));
        } else if (event.key.toLowerCase() === "g" && state.selectedId) {
            event.preventDefault(); toggleMoveMode();
        }
    }

    function toggleMoveMode() {
        if (!state.selectedId) {
            setStatus(text("moveSelect"));
            return;
        }
        state.moveMode = !state.moveMode;
        render();
        setStatus(text(state.moveMode ? "moveActive" : "moveFinished"));
    }

    function exportScene() {
        const content = JSON.stringify({ version: 1, scene: state.scene, camera: state.camera }, null, 2);
        const url = URL.createObjectURL(new Blob([content], { type: "application/json" }));
        const link = document.createElement("a");
        link.href = url;
        link.download = "asynCUDA13-cad-scene.json";
        link.click();
        URL.revokeObjectURL(url);
        setStatus(text("sceneExported"));
    }

    function openImport() {
        if (state.fileInput) {
            state.fileInput.value = "";
            state.fileInput.click();
        }
    }

    async function importFile() {
        const file = state.fileInput?.files?.[0];
        if (!file) return;
        if (file.size > 5 * 1024 * 1024) {
            setStatus(text("importTooLarge"));
            return;
        }
        try {
            const imported = JSON.parse(await file.text());
            if (!Array.isArray(imported.scene) || imported.scene.length > 500 || !imported.scene.every(isValidShape)) {
                throw new Error(text("importInvalid"));
            }
            saveHistory();
            state.scene = imported.scene;
            state.scene.forEach((shape, index) => { shape.name = `${shapeNames[shape.kind]} ${index + 1}`; });
            state.selectedId = null;
            restoreCamera(imported.camera);
            await notifyCameraOption();
            state.sketchPoints = [];
            state.extrusion = null;
            persist();
            render();
            setStatus(text("imported", { name: file.name }));
        } catch (error) {
            setStatus(text("importFailed", { message: error.message }));
        }
    }

    function resetView() {
        state.camera = { ...defaultCamera(), allowBelowGround: state.camera.allowBelowGround };
        render();
        persist();
    }

    function initialize(canvasId, listId, importInputId, localizedText, dotNetRef) {
        dispose();
        state.text = localizedText;
        shapeNames = {
            cube: text("shapeCube"),
            sphere: text("shapeSphere"),
            pyramid: text("shapePyramid"),
            prism: text("shapePrism"),
            cylinder: text("shapeCylinder")
        };
        state.canvas = getEl(canvasId);
        state.list = getEl(listId);
        state.fileInput = getEl(importInputId);
        state.dotNetRef = dotNetRef;
        state.context = state.canvas?.getContext("2d");
        if (!state.canvas || !state.context) {
            setStatus(text("canvasInitFailed"));
            return;
        }
        state.abort = new AbortController();
        const signal = state.abort.signal;
        state.canvas.addEventListener("contextmenu", event => event.preventDefault(), { signal });
        state.canvas.addEventListener("pointerdown", pointerDown, { signal });
        state.canvas.addEventListener("pointermove", pointerMove, { signal });
        state.canvas.addEventListener("pointerup", pointerUp, { signal });
        state.canvas.addEventListener("pointercancel", pointerUp, { signal });
        state.canvas.addEventListener("wheel", wheel, { passive: false, signal });
        window.addEventListener("keydown", keyDown, { signal });
        state.fileInput?.addEventListener("change", importFile, { signal });
        state.resizeObserver = new ResizeObserver(resizeCanvas);
        state.resizeObserver.observe(state.canvas);
        state.camera = defaultCamera();
        loadPersisted();
        notifyCameraOption();
        resizeCanvas();
        render();
        setStatus(text("statusNavigation"));
    }

    function dispose() {
        clearTimeout(state.renderTimer);
        state.renderTimer = null;
        state.abort?.abort();
        state.abort = null;
        state.resizeObserver?.disconnect();
        state.resizeObserver = null;
        state.pointer = null;
        state.dotNetRef = null;
        state.rendering = false;
    }

    window.cadEditor = {
        initialize,
        dispose,
        setTool,
        addPrimitive: kind => addShape(kind),
        completePrismSketch,
        finishPrism,
        toggleMoveMode,
        deleteSelected,
        undo,
        redo,
        clearScene,
        exportScene,
        openImport,
        resetView,
        setAllowBelowGround,
        renderNow: () => requestBackendRender(true),
        displayBackendFrame,
        backendRenderFailed
    };
})();
