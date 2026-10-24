import { app } from "../../../scripts/app.js";
import { api } from "../../../scripts/api.js";

app.registerExtension({
    name: "MistEra.CharacterSelector",
    async beforeRegisterNodeDef(nodeType, nodeData) {
        if (nodeData.name !== "MistEraCharacterSelector") return;
        const created = nodeType.prototype.onNodeCreated;
        nodeType.prototype.onNodeCreated = function () {
            const result = created?.apply(this, arguments);
            install(this);
            return result;
        };
    },
});

function install(node) {
    const path = node.widgets.find(item => item.name === "db_path");
    const character = node.widgets.find(item => item.name === "character_id");
    const config = node.widgets.find(item => item.name === "config_path");
    const emotion = node.widgets.find(item => item.name === "emotion_id");
    const persistent = [path, character, ...["actor_binding", "is_receiver", "is_doer", "config_path", "emotion_id", "custom_prompt"]
        .map(name => node.widgets.find(item => item.name === name))].filter(Boolean);
    let catalog = null;
    let catalogPath = null;
    let catalogConfigPath = null;
    let labels = new Map();
    let ids = new Map();
    let requestId = 0;
    let removed = false;
    let internal = false;
    let backendFocus = null;
    let backendEmotion = null;
    let statusText = "Load database to browse characters";

    function transient(item) {
        item.options = { ...item.options, serialize: false };
        item.serializeValue = () => undefined;
        return item;
    }

    transient(node.addWidget("button", "Load / Refresh", null, () => { void refresh(); }));
    const browser = transient(node.addWidget("combo", "character_browser", "", value => {
        if (internal || !catalog || catalogPath !== path.value || !ids.has(value)) return;
        if (!wired("character_id")) {
            internal = true;
            try {
                character.value = ids.get(value);
                character.callback?.call(character, character.value);
            } finally {
                internal = false;
            }
        }
        render();
    }, { values: [""] }));
    browser.disabled = true;
    const emotionBrowser = transient(node.addWidget("combo", "emotion_browser", emotion?.value || "none", value => {
        if (internal || !emotion || wired("emotion_id") || !(catalog?.emotions ?? emotionBrowser.options.values).includes(value)) return;
        internal = true;
        try {
            emotion.value = value;
            emotion.callback?.call(emotion, value);
        } finally {
            internal = false;
        }
        render();
    }, { values: ["none"] }));
    emotionBrowser.disabled = true;
    const status = transient(node.addWidget("text", "character_status", statusText, () => {
        status.value = statusText;
    }, { multiline: false }));
    status.disabled = true;

    function wired(name) {
        return node.inputs?.some(input => input.name === name && input.link != null);
    }

    function message(text) {
        statusText = text;
        status.value = text;
        status.options.tooltip = text;
        node.setDirtyCanvas(true, true);
    }

    function clear() {
        catalog = null;
        catalogPath = null;
        catalogConfigPath = null;
        labels = new Map();
        ids = new Map();
        browser.options.values = [""];
        browser.value = "";
        browser.disabled = true;
        emotionBrowser.options.values = ["none"];
        emotionBrowser.value = emotion?.value || "none";
        emotionBrowser.disabled = true;
    }

    function render() {
        if (!catalog || catalogPath !== path.value || catalogConfigPath !== config?.value) return;
        const connected = wired("character_id");
        const selected = connected ? backendFocus : (character.value || catalog.characters[0].id);
        browser.value = labels.get(selected) ?? "";
        browser.disabled = connected;
        browser.options.tooltip = connected
            ? "Read-only: character_id is connected. Execute to display the selected character."
            : "Choose a name to set the canonical portrait template ID.";
        const selectedEmotion = wired("emotion_id") ? backendEmotion : (emotion?.value || "none");
        emotionBrowser.options.values = catalog.emotions;
        emotionBrowser.value = selectedEmotion ?? "";
        emotionBrowser.disabled = !emotion || wired("emotion_id");
        const valid = catalog.characters.find(record => record.id === selected)?.valid_emotions ?? [];
        emotionBrowser.options.tooltip = wired("emotion_id")
            ? "Read-only: emotion_id is connected. Execute to display the selected emotion."
            : `Valid for this character: ${valid.join(", ")}. Invalid choices add nothing.`;
        if (connected && selected === null) {
            message(`Loaded ${labels.size} characters; execute to display connected character_id`);
        } else if (!labels.has(selected)) {
            message(`Error: unknown character_id ${JSON.stringify(selected)}; refresh or correct the input`);
        } else {
            const note = selectedEmotion === null ? "; execute to display connected emotion"
                : valid.includes(selectedEmotion) ? `; emotion: ${selectedEmotion}` : `; emotion ${selectedEmotion} is invalid and will be ignored`;
            message(`${connected ? "Connected: " : "Selected: "}${labels.get(selected)} [ID: ${selected}]${note}`);
        }
    }

    function buildLabels(records) {
        const counts = new Map();
        const seenIds = new Set();
        for (const record of records) {
            if (!record || typeof record.id !== "string" || !record.id ||
                typeof record.name !== "string" || seenIds.has(record.id)) {
                throw new Error("Invalid character database response");
            }
            seenIds.add(record.id);
            counts.set(record.name, (counts.get(record.name) ?? 0) + 1);
        }
        // Reserve unique plain names before generating duplicate-name labels, which can themselves collide.
        const used = new Set(records.filter(record => counts.get(record.name) === 1).map(record => record.name));
        for (const record of records) {
            let label = record.name;
            if (counts.get(record.name) > 1) {
                do { label += ` [${record.id}]`; } while (used.has(label));
                used.add(label);
            }
            labels.set(record.id, label);
            ids.set(label, record.id);
        }
    }

    async function refresh() {
        const ticket = ++requestId;
        const requestedPath = path.value;
        const requestedConfig = config?.value;
        clear();
        message("Loading character database...");
        try {
            if (typeof requestedPath !== "string" || !requestedPath.trim()) {
                throw new Error("db_path must be a nonempty string");
            }
            if (wired("db_path")) throw new Error("db_path is connected; browse using a local path input");
            if (wired("config_path")) throw new Error("config_path is connected; execute to display emotions");
            const response = await api.fetchApi("/mistera/characters/config", {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({ db_path: requestedPath,
                    ...(requestedConfig === undefined ? {} : { config_path: requestedConfig }) }),
            });
            const data = await response.json();
            if (removed || ticket !== requestId || requestedPath !== path.value || requestedConfig !== config?.value) return;
            if (!response.ok) throw new Error(data.error || `HTTP ${response.status}`);
            if (typeof data.db_path !== "string" || !Array.isArray(data.characters) || !data.characters.length
                || !Array.isArray(data.emotions) || data.emotions.some(id => typeof id !== "string")
                || data.characters.some(record => !Array.isArray(record.valid_emotions))) {
                throw new Error("Invalid or empty character database response; export from Unity first");
            }
            buildLabels(data.characters);
            catalog = data;
            catalogPath = requestedPath;
            catalogConfigPath = requestedConfig;
            browser.options.values = [...ids.keys()];
            render();
        } catch (error) {
            if (removed || ticket !== requestId || requestedPath !== path.value || requestedConfig !== config?.value) return;
            clear();
            message(`Error loading characters: ${error.message}`);
        }
    }

    function wrapCallback(item, callback) {
        const previous = item.callback;
        item.callback = function () {
            const result = previous?.apply(this, arguments);
            if (!internal) callback();
            return result;
        };
    }

    wrapCallback(path, () => {
        ++requestId;
        backendFocus = null;
        backendEmotion = null;
        clear();
        message("Database path changed; Load / Refresh required");
    });
    wrapCallback(character, render);
    if (emotion) wrapCallback(emotion, render);
    if (config) wrapCallback(config, () => {
        ++requestId;
        backendFocus = null;
        backendEmotion = null;
        clear();
        message("Emotion config path changed; Load / Refresh required");
    });

    const configure = node.onConfigure;
    node.onConfigure = function (info) {
        ++requestId;
        clear();
        const result = configure?.apply(this, arguments);
        const saved = info.properties?.mistera_character_inputs;
        if (saved && typeof saved === "object") {
            for (const item of persistent) {
                if (typeof saved[item.name] === typeof item.value) item.value = saved[item.name];
            }
        }
        backendFocus = null;
        backendEmotion = null;
        void refresh();
        return result;
    };

    const serialize = node.onSerialize;
    node.onSerialize = function (info) {
        const result = serialize?.apply(this, arguments);
        info.properties ??= {};
        info.properties.mistera_character_inputs = Object.fromEntries(persistent.map(item => [item.name, item.value]));
        return result;
    };

    const executed = node.onExecuted;
    node.onExecuted = function (payload) {
        const result = executed?.apply(this, arguments);
        const metadata = payload?.character_selector?.[0];
        if (!metadata || removed || typeof metadata.character_id !== "string") return result;
        backendEmotion = metadata.emotion_id ?? null;
        if (wired("db_path") || wired("config_path")) {
            ++requestId;
            clear();
            emotionBrowser.options.values = metadata.emotion_ids ?? ["none"];
            emotionBrowser.value = backendEmotion ?? "";
            emotionBrowser.disabled = !emotion || wired("emotion_id");
            emotionBrowser.options.tooltip = `Valid emotions: ${(metadata.valid_emotion_ids ?? []).join(", ")}`;
            message(`Executed: ${metadata.name} [ID: ${metadata.character_id}]; emotion: ${backendEmotion}${metadata.emotion_valid ? "" : " (ignored)"}`);
            return result;
        }
        if (metadata.input_db_path !== path.value) return result;
        if (config && metadata.input_config_path !== config.value) return result;
        if (catalog && metadata.db_path !== catalog.db_path) return result;
        backendFocus = metadata.character_id;
        if (wired("character_id")) {
            message(`Executed: ${metadata.name} [ID: ${metadata.character_id}]`);
        }
        void refresh();
        return result;
    };

    const connections = node.onConnectionsChange;
    node.onConnectionsChange = function () {
        const result = connections?.apply(this, arguments);
        queueMicrotask(() => {
            if (removed) return;
            backendFocus = null;
            backendEmotion = null;
            ++requestId;
            clear();
            void refresh();
        });
        return result;
    };

    const onRemoved = node.onRemoved;
    node.onRemoved = function () {
        removed = true;
        ++requestId;
        return onRemoved?.apply(this, arguments);
    };
    node.setSize([Math.max(node.size[0], 360), node.computeSize()[1]]);
    node.setDirtyCanvas(true, true);
}
