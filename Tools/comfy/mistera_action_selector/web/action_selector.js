import { app } from "../../../scripts/app.js";
import { api } from "../../../scripts/api.js";

const TYPES = new Set(["MistEraActionRuleDefinitions", "MistEraActionSelector"]);
const own = (object, key) => Object.prototype.hasOwnProperty.call(object, key);

app.registerExtension({
    name: "MistEra.ActionSelector",
    async beforeRegisterNodeDef(nodeType, nodeData) {
        if (!TYPES.has(nodeData.name)) return;
        const selector = nodeData.name === "MistEraActionSelector";
        const created = nodeType.prototype.onNodeCreated;
        nodeType.prototype.onNodeCreated = function () {
            const result = created?.apply(this, arguments);
            install(this, selector);
            return result;
        };
    },
});

function install(node, selector) {
    const widget = (name) => node.widgets.find((item) => item.name === name);
    const path = widget("config_path");
    const storage = widget(selector ? "selections_json" : "choices_json");
    const action = selector ? widget("action_id") : null;
    const persistent = [path, ...(selector ? [action] : []), storage,
        ...(selector ? [widget("seed")].filter(Boolean) : [])];
    // Keep the actual STRING widget, value and backend input; only suppress its drawing.
    storage.type = "converted-widget";
    storage.computeSize = () => [0, -4];
    storage.draw = () => {};

    let catalog = null;
    let catalogPath = null;
    let requestId = 0;
    let removed = false;
    let internal = false;
    let dynamic = [];
    let focus = "";
    let backendFocus = "";
    let backendOptions = {};
    let statusText = "Load config to edit preferences";

    function transient(item) {
        item.options = { ...item.options, serialize: false };
        item.serializeValue = () => undefined;
        return item;
    }

    transient(node.addWidget("button", "Load / Refresh", null, () => refresh()));
    const browser = selector ? transient(node.addWidget("combo", "action_browser", "", (value) => {
        if (internal || !catalog) return;
        focus = value;
        if (!wired()) setAction(value);
        render();
    }, { values: [""] })) : null;
    if (browser) browser.disabled = true;
    const status = transient(node.addWidget("text", "config_status", statusText, () => {
        status.value = statusText;
    }, { multiline: false }));
    status.disabled = true;

    function wired() {
        return selector && node.inputs?.some((input) => input.name === "action_id" && input.link != null);
    }

    function configured() {
        return selector && node.inputs?.some((input) => input.name === "rule_config" && input.link != null);
    }

    function inheritedOptions(id) {
        const graph = node.graph ?? app.graph;
        const input = node.inputs?.find(input => input.name === "rule_config");
        const link = graph?.links?.get?.(input?.link) ?? graph?.links?.[input?.link];
        const source = link && graph?.getNodeById?.(link.origin_id);
        const choices = source?.widgets?.find(widget => widget.name === "choices_json");
        const external = source?.inputs?.some(input => input.name === "choices_json" && input.link != null);
        if (choices && !external) {
            const options = JSON.parse(choices.value || "{}");
            validateChoices(options, catalog.registry, "Connected rule_config");
            return Object.fromEntries(Object.keys(catalog.action_groups[id]).map(group => [group, options[group] ?? "auto"]));
        }
        return backendOptions[id] ?? null;
    }

    function dirty(resize = false) {
        if (resize) node.setSize([Math.max(node.size[0], 340), node.computeSize()[1]]);
        node.setDirtyCanvas(true, true);
        app.graph?.setDirtyCanvas(true, true);
    }

    function message(text) {
        statusText = text;
        status.value = text;
        status.options.tooltip = text;
        dirty();
    }

    function readState() {
        const value = JSON.parse(storage.value || "{}");
        if (!value || typeof value !== "object" || Array.isArray(value)) {
            throw new Error(`${storage.name} must contain an object`);
        }
        return value;
    }

    function validateChoices(value, groups, context) {
        if (!value || typeof value !== "object" || Array.isArray(value)) {
            throw new Error(`${context}: expected a group-to-choice object`);
        }
        for (const [group, choice] of Object.entries(value)) {
            if (!own(groups, group)) throw new Error(`${context}: unknown/inapplicable group ${group}`);
            if (choice !== "auto" && !groups[group].includes(choice)) {
                throw new Error(`${context}/${group}: unknown choice ${String(choice)}`);
            }
        }
    }

    function validateState(state) {
        if (!selector) return validateChoices(state, catalog.registry, "Global defaults");
        for (const [id, options] of Object.entries(state)) {
            if (!own(catalog.action_groups, id)) throw new Error(`Stale/unknown action override ${id}`);
            validateChoices(options, catalog.action_groups[id], id);
        }
    }

    function clearDynamic() {
        for (const item of dynamic) {
            const index = node.widgets.indexOf(item);
            if (index !== -1) node.widgets.splice(index, 1);
            item.onRemove?.();
        }
        dynamic = [];
    }

    function setAction(value) {
        if (!action || wired() || action.value === value) return;
        internal = true;
        try {
            action.value = value;
            action.callback?.call(action, value);
        } finally {
            internal = false;
        }
    }

    function render() {
        clearDynamic();
        if (!catalog || catalogPath !== path.value) return dirty(true);
        try {
            const state = configured() ? {} : readState();
            if (!configured()) validateState(state);
            let groups = catalog.registry;
            let selected = state;
            if (selector) {
                const id = wired() ? (focus || backendFocus || catalog.action_ids[0])
                    : (action?.value || focus || catalog.action_ids[0]);
                if (!own(catalog.action_groups, id)) throw new Error(`Unknown action_id ${id}; refresh or correct the input`);
                focus = id;
                internal = true;
                try {
                    browser.options.values = catalog.action_ids;
                    browser.value = id;
                } finally {
                    internal = false;
                }
                if (!wired()) setAction(id);
                groups = catalog.action_groups[id];
                selected = own(state, id) ? state[id] : {};
            }
            const readOnly = configured();
            if (readOnly) selected = inheritedOptions(focus);
            for (const [group, choices] of Object.entries(groups)) {
                const id = focus;
                const combo = transient(node.addWidget("combo", group,
                    selected ? (own(selected, group) ? selected[group] : "auto") : "await execution", (value) => {
                        if (internal) return;
                        if (configured()) { render(); return; }
                        try {
                            const latest = readState();
                            validateState(latest);
                            if (selector) {
                                if (!own(latest, id)) {
                                    Object.defineProperty(latest, id, { value: {}, enumerable: true, configurable: true, writable: true });
                                }
                                Object.defineProperty(latest[id], group, { value, enumerable: true, configurable: true, writable: true });
                            } else {
                                Object.defineProperty(latest, group, { value, enumerable: true, configurable: true, writable: true });
                            }
                            storage.value = JSON.stringify(latest);
                            storage.callback?.call(storage, storage.value);
                            dirty();
                        } catch (error) {
                            message(`Error: ${error.message}`);
                            render();
                        }
                    }, { values: [...(selected ? [] : ["await execution"]), "auto", ...choices] }));
                combo.disabled = readOnly;
                combo.options.tooltip = readOnly
                    ? "Read-only: selection supplied by connected rule_config."
                    : selector
                    ? "auto inherits the global default, or keeps automatic local selection. Preferences do not bypass predicates."
                    : "auto keeps automatic action-local selection; no global predicates or prompts.";
                dynamic.push(combo);
            }
            message(readOnly ? (selected ? "Read-only options from connected rule_config" : "Read-only options; execute to read connected rule_config")
                : selector && wired() ? `Loaded ${catalog.action_ids.length} actions; wired action_id, browsing ${focus}`
                : `Loaded ${catalog.action_ids.length} actions`);
        } catch (error) {
            message(`Error: ${error.message}`);
        }
        dirty(true);
    }

    async function refresh() {
        const ticket = ++requestId;
        const requestedPath = path.value;
        catalog = null;
        if (browser) browser.disabled = true;
        clearDynamic();
        message("Loading config...");
        dirty(true);
        try {
            const response = await api.fetchApi("/mistera/actions/config", {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({ config_path: requestedPath }),
            });
            const data = await response.json();
            if (removed || ticket !== requestId || requestedPath !== path.value) return;
            if (!response.ok) throw new Error(data.error || `HTTP ${response.status}`);
            if (!data.registry || !data.action_ids?.length || !data.action_groups) {
                throw new Error("Invalid config response");
            }
            catalog = data;
            catalogPath = requestedPath;
            if (browser) {
                browser.options.values = data.action_ids;
                browser.disabled = false;
            }
            render();
        } catch (error) {
            if (removed || ticket !== requestId || requestedPath !== path.value) return;
            catalog = null;
            clearDynamic();
            message(`Error loading config: ${error.message}`);
            dirty(true);
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
        catalog = null;
        backendFocus = "";
        backendOptions = {};
        if (browser) browser.disabled = true;
        clearDynamic();
        message("Config path changed; Load / Refresh required");
        dirty(true);
    });
    if (action) wrapCallback(action, () => render());
    node.misteraRefreshActionOptions = () => { if (!removed) render(); };
    if (!selector) wrapCallback(storage, () => {
        for (const target of (node.graph ?? app.graph)?._nodes ?? []) target.misteraRefreshActionOptions?.();
    });

    const configure = node.onConfigure;
    node.onConfigure = function (info) {
        ++requestId;
        clearDynamic();
        const result = configure?.apply(this, arguments);
        // A named snapshot also protects against positional transient widget values in older frontends.
        const saved = info.properties?.mistera_action_inputs;
        if (saved && typeof saved === "object") {
            for (const item of persistent) {
                if (own(saved, item.name) && typeof saved[item.name] === typeof item.value) item.value = saved[item.name];
            }
        }
        backendFocus = "";
        backendOptions = {};
        focus = "";
        void refresh();
        return result;
    };

    const serialize = node.onSerialize;
    node.onSerialize = function (info) {
        const result = serialize?.apply(this, arguments);
        info.properties ??= {};
        info.properties.mistera_action_inputs = Object.fromEntries(persistent.map((item) => [item.name, item.value]));
        return result;
    };

    const executed = node.onExecuted;
    node.onExecuted = function (payload) {
        const result = executed?.apply(this, arguments);
        const metadata = payload?.action_selector?.[0];
        if (selector && metadata) {
            if (catalogPath !== null && catalogPath !== path.value) return result;
            if (catalog && metadata.config_path !== catalog.config_path) return result;
            backendOptions = metadata.option_selections ?? {};
            if (wired()) {
                backendFocus = metadata.selected_action;
                focus = backendFocus;
            }
            // Refresh on execution too, so edits to the file are reflected in the local widget schema.
            void refresh();
        }
        return result;
    };

    const connections = node.onConnectionsChange;
    node.onConnectionsChange = function () {
        const result = connections?.apply(this, arguments);
        backendOptions = {};
        queueMicrotask(() => { if (!removed) render(); });
        return result;
    };

    const onRemoved = node.onRemoved;
    node.onRemoved = function () {
        removed = true;
        ++requestId;
        return onRemoved?.apply(this, arguments);
    };
    dirty(true);
}
