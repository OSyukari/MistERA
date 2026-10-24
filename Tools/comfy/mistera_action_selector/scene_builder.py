"""Final scene rendering from a portable action package and registered actors."""

import logging
import re

from .nodes import require
from .action_data import parse_inline
from .scene_context import _tokens, _unique, render_composition


class MistEraSceneBuilder:
    CATEGORY = "MistEra/Scenes"
    FUNCTION = "build"
    RETURN_TYPES = ("STRING", "STRING")
    RETURN_NAMES = ("action_prompt", "scene_prompt")

    @classmethod
    def INPUT_TYPES(cls):
        return {"required": {"action_data": ("MISTERA_ACTION_DATA",)}}

    def build(self, action_data):
        require(isinstance(action_data, dict) and type(action_data.get("version")) is int
                and action_data["version"] == 1, "Scene Builder requires a version 1 action_data package.")
        require(isinstance(action_data.get("scene_rules"), dict),
                "action_data has no scene_rules snapshot. Regenerate it with Action Selector before building the scene.")
        require(bool(action_data.get("actor_registrations")),
                "Scene Builder has no registered actors. Enable is_doer / is_receiver on Character Selector nodes, or assign explicit advanced actor bindings, and connect their final action_data.")
        # Keep this import lazy: actor rendering also consumes scene_context facts.
        from .actor_package import render_action_package

        rendered = render_action_package(action_data)
        context = rendered["scene_context"]
        action = rendered["action_prompt"]
        scene = render_composition(action_data, context)
        registry = action_data["scene_rules"].get("scene_pair_prompts", {})
        actual_flags = {tag.casefold() for tag in context["tags"]}
        actual_flags.update(tag.casefold() for pair in context["pairs"] for tag in pair["tags"])
        promoted = _unique(prompt.strip() for flag, prompts in registry.items()
                           if flag.casefold() in actual_flags for prompt in prompts)
        promoted_keys = {prompt.casefold() for prompt in promoted}
        scene = ", ".join(_unique([*_tokens([scene]), *promoted]))

        def remove_promoted(text):
            def literal(nodes):
                return "".join(node if isinstance(node, str) else
                               "{" + "|".join(literal(branch) for branch in node[1]) + "}" for node in nodes)

            def clean(nodes):
                parts, parens, brackets = [], 0, 0
                for node in nodes:
                    if isinstance(node, str):
                        parts.append(node)
                        for char in node:
                            if char == "(":
                                parens += 1
                            elif char == ")":
                                parens = max(0, parens - 1)
                            elif char == "[":
                                brackets += 1
                            elif char == "]":
                                brackets = max(0, brackets - 1)
                    elif node[0] == "choice" and parens == brackets == 0:
                        # Keep every branch, including a newly empty one; no roll or probability changes.
                        parts.append("{" + "|".join(clean(branch) for branch in node[1]) + "}")
                    else:
                        parts.append(literal([node]))
                return ", ".join(token for token in _tokens(["".join(parts)]) if token.casefold() not in promoted_keys)

            return clean(parse_inline(text))

        if promoted:
            action = remove_promoted(action)
        prompts = [remove_promoted(prompt) if promoted else prompt for prompt in context["pair_prompts"]]
        prompts = [prompt for prompt in prompts if prompt.strip()]
        if action.strip() and action.strip().casefold() not in {p.casefold() for p in prompts}:
            prompts.append(action)
        result = ", ".join(prompts)
        if rendered["scene_prompts"]:
            sentences = " ".join(rendered["scene_prompts"])
            scene = scene.rstrip().rstrip(".") + ". " + sentences if scene.strip() else sentences
        unresolved = list(dict.fromkeys(re.findall(r"\$[^$]+\$", result)))
        if unresolved:
            context["diagnostics"].append("Unresolved action template tokens retained: " + ", ".join(unresolved))
        for diagnostic in context["diagnostics"]:
            logging.getLogger(__name__).warning("MistEra Scene Builder: %s", diagnostic)
        return result, scene
