import { app } from "../../../scripts/app.js";
import { ComfyWidgets } from "../../../scripts/widgets.js";

app.registerExtension({
    name: "MistEra.ActionDataInspector",
    async beforeRegisterNodeDef(nodeType, nodeData) {
        if (nodeData.name !== "MistEraActionDataInspector") return;

        const created = nodeType.prototype.onNodeCreated;
        nodeType.prototype.onNodeCreated = function () {
            const result = created?.apply(this, arguments);
            const preview = ComfyWidgets.STRING(this, "action_data_json", ["STRING", {
                default: "",
                multiline: true,
            }], app).widget;
            preview.options = { ...preview.options, serialize: false };
            preview.serializeValue = () => undefined;
            preview.inputEl.readOnly = true;
            preview.inputEl.style.fontFamily = "monospace";
            preview.inputEl.setAttribute("aria-label", "Resolved action data JSON (read-only)");

            const executed = this.onExecuted;
            this.onExecuted = function (message) {
                const result = executed?.apply(this, arguments);
                preview.value = message?.action_data_json?.[0] ?? "";
                this.setDirtyCanvas(true, true);
                return result;
            };
            this.setSize([Math.max(this.size[0], 420), Math.max(this.size[1], 480)]);
            return result;
        };
    },
});
