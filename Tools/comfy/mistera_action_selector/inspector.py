"""Inspect resolved action data and add literal, actor-scoped annotations."""

from copy import deepcopy
import json

from .action_data import add_unique, require
from .actor_package import finalize_action_package, set_template, template_text


class MistEraActionDataInspector:
    CATEGORY = "MistEra/Actions"
    FUNCTION = "inspect"
    RETURN_TYPES = ("MISTERA_ACTION_DATA", "STRING", "STRING")
    RETURN_NAMES = ("action_data", "inspection_json", "prompt")
    OUTPUT_NODE = True

    @classmethod
    def INPUT_TYPES(cls):
        return {
            "required": {"action_data": ("MISTERA_ACTION_DATA",)},
            "optional": {
                "actor_binding": ("STRING", {"default": "main"}),
                "add_tags": ("STRING", {"default": ""}),
                "add_groups": ("STRING", {"default": ""}),
                "append_prompt": ("STRING", {"default": "", "multiline": True}),
            },
        }

    def inspect(self, action_data, actor_binding="main", add_tags="", add_groups="", append_prompt=""):
        require(isinstance(action_data, dict) and type(action_data.get("version")) is int
                and action_data["version"] == 1, "action_data must be a version 1 resolved action object")

        def string_list(values, name):
            require(isinstance(values, list)
                    and all(isinstance(value, str) and value.strip() for value in values),
                    f"action_data {name} must be an array of nonempty strings")

        bindings = action_data.get("bindings")
        require(isinstance(bindings, dict), "action_data bindings must be an object")
        for alias, target in bindings.items():
            require(isinstance(alias, str) and alias.strip()
                    and isinstance(target, str) and target.strip(),
                    "action_data bindings must map nonempty aliases to binding names")
        for name in ("actor_action_tags", "actor_requests"):
            table = action_data.get(name)
            require(isinstance(table, dict), f"action_data {name} must be an object")
            for binding, values in table.items():
                require(isinstance(binding, str) and binding.strip(),
                        f"action_data {name} keys must be nonempty binding names")
                string_list(values, f"{name}/{binding}")
        string_list(action_data.get("action_tags"), "action_tags")
        if "unresolved_bindings" in action_data:
            string_list(action_data["unresolved_bindings"], "unresolved_bindings")
        require(isinstance(action_data.get("resolved_prompt"), str),
                "action_data resolved_prompt must be a string")
        require(isinstance(actor_binding, str) and actor_binding.strip(),
                "actor_binding must be a nonempty alias string")

        additions = []
        for name, text in (("add_tags", add_tags), ("add_groups", add_groups),
                           ("append_prompt", append_prompt)):
            require(isinstance(text, str), f"{name} must be a comma-separated string")
            require("$" not in text,
                    f"{name} accepts literal values, not '$' deferred tokens; use add_groups for actor group requests")
            values = []
            for value in text.split(","):
                if value.strip():
                    add_unique(values, value.strip())
            additions.append(values)
        tags, groups, prompt_tags = additions
        binding = bindings.get(actor_binding, actor_binding if actor_binding in action_data.get("role_groups", {}) else None)
        if tags or groups:
            require(binding is not None,
                    f"Unknown actor_binding {actor_binding!r}; use an existing alias from {list(bindings)!r}")

        edited = deepcopy(action_data)
        if tags:
            edited["actor_action_tags"].setdefault(binding, [])
        for tag in tags:
            add_unique(edited["actor_action_tags"][binding], tag)
            add_unique(edited["action_tags"], tag)
        for group in groups:
            requests = edited["actor_requests"].setdefault(binding, [])
            if group.casefold() not in {value.casefold() for value in requests}:
                requests.append(group)
                if binding in ("doer", "receiver"):
                    add_unique(edited.setdefault("unresolved_bindings", []), binding)

        # Edit the canonical template, never a preview finalized with an incomplete actor registry.
        prompt = template_text(edited)
        seen = {value.strip().casefold() for value in prompt.split(",")}
        for tag in prompt_tags:
            if tag.casefold() not in seen:
                separator = "" if not prompt.strip() else (" " if prompt.rstrip().endswith(",") else ", ")
                prompt += separator + tag
                seen.add(tag.casefold())
        # Character selectors now resolve literal tokens, rather than relying only on the request audit table.
        for group in groups:
            token = f"${actor_binding}.{group}$"
            if token not in {value.strip() for value in prompt.split(",")}:
                separator = "" if not prompt.strip() else (" " if prompt.rstrip().endswith(",") else ", ")
                prompt += separator + token
        set_template(edited, prompt)
        try:
            inspection = json.dumps(edited, ensure_ascii=False, indent=2, allow_nan=False)
        except (TypeError, ValueError, OverflowError, RecursionError) as exc:
            raise ValueError(f"action_data must contain JSON-serializable values: {exc}") from exc
        return {"ui": {"action_data_json": [inspection]},
                "result": (edited, inspection, finalize_action_package(edited))}
