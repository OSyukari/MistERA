"""Copy this folder into ComfyUI/custom_nodes and restart ComfyUI to install."""

from .nodes import MistEraActionRuleDefinitions, MistEraActionSelector
from .characters import MistEraCharacterSelector
from .inspector import MistEraActionDataInspector
from .scene_builder import MistEraSceneBuilder

NODE_CLASS_MAPPINGS = {
    "MistEraActionRuleDefinitions": MistEraActionRuleDefinitions,
    "MistEraActionSelector": MistEraActionSelector,
    "MistEraCharacterSelector": MistEraCharacterSelector,
    "MistEraActionDataInspector": MistEraActionDataInspector,
    "MistEraSceneBuilder": MistEraSceneBuilder,
}
NODE_DISPLAY_NAME_MAPPINGS = {
    "MistEraActionRuleDefinitions": "MistEra Action Rule Definitions",
    "MistEraActionSelector": "MistEra Action Selector",
    "MistEraCharacterSelector": "MistEra Character Selector",
    "MistEraActionDataInspector": "MistEra Action Data Inspector",
    "MistEraSceneBuilder": "MistEra Scene Builder",
}
WEB_DIRECTORY = "./web"

__all__ = ["NODE_CLASS_MAPPINGS", "NODE_DISPLAY_NAME_MAPPINGS", "WEB_DIRECTORY"]
