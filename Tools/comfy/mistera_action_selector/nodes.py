"""Action preferences and selected prompt templates; game predicates stay game-owned."""

import asyncio
import json
import re
from pathlib import Path

from aiohttp import web
from server import PromptServer

from .action_data import preview_action_template, resolve_action_data, select_options
from .roles import validate_roles


DEFAULT_CONFIG = r"E:\UnityProjects\MistEra\Mods\Assetpacks\Image_Generation_Config.json"
MAX_CONFIG_BYTES = 16 * 1024 * 1024
ID_PATTERN = re.compile(r"[A-Za-z0-9_.-]+\Z")


def require(condition, message):
    if not condition:
        raise ValueError(message)


def unique_object(pairs):
    result = {}
    for key, value in pairs:
        require(key not in result, f"Duplicate JSON key: {key!r}")
        result[key] = value
    return result


def parse_json(text):
    def invalid_constant(value):
        raise ValueError(f"Invalid JSON constant: {value}")

    return json.loads(text, object_pairs_hook=unique_object, parse_constant=invalid_constant)


def identifier(value, context):
    require(isinstance(value, str) and ID_PATTERN.fullmatch(value),
            f"{context}: expected a nonempty identifier (letters, digits, underscore, hyphen, dot)")
    return value


def unique_ids(values, context):
    require(isinstance(values, list) and values, f"{context}: expected a nonempty ID array")
    seen = set()
    for value in values:
        identifier(value, context)
        require(value.casefold() not in seen, f"{context}: duplicate ID ignoring case: {value!r}")
        seen.add(value.casefold())


def validate_registry(registry):
    require(isinstance(registry, dict) and registry, "prompt_action_option_rules must be a nonempty object")
    unique_ids(list(registry), "registry groups")
    for group, choices in registry.items():
        unique_ids(choices, f"registry group {group!r}")
        require(all(choice.casefold() != "auto" for choice in choices),
                f"registry group {group!r}: 'auto' is reserved for automatic selection")


def validate_record(record, context, option=False, branch=False):
    require(isinstance(record, dict), f"{context}: expected an object")
    if branch:
        allowed = {"all", "any", "none", "main", "support", "differs", "pairKind",
                   "conditions", "prompts", "scenePrompts"}
        require(all(key in allowed or key.startswith("_comment") for key in record),
                f"{context}: option branches allow only conditions, prompts, scenePrompts and comments")
    else:
        identifier(record.get("id"), context)
    for field in ("prompts", "scenePrompts"):
        prompts = record.get(field, [])
        require(isinstance(prompts, list) and all(isinstance(p, str) for p in prompts),
                f"{context}: {field} must be an array of strings")
    for key in ("all", "any", "none", "differs"):
        if key in record:
            require(isinstance(record[key], list) and all(isinstance(p, str) and p.strip() for p in record[key]),
                      f"{context}: {key} must be an array of nonempty strings")
    for key in ("main", "support"):
        if key in record:
            block = record[key]
            require(isinstance(block, dict) and set(block) <= {"all", "any", "none"},
                    f"{context}: {key} must be a condition clause object")
            for field, clauses in block.items():
                require(isinstance(clauses, list)
                        and all(isinstance(clause, str) and clause.strip() for clause in clauses),
                        f"{context}: {key}.{field} must be an array of nonempty strings")
    if "pairKind" in record:
        require(record["pairKind"] in ("solo", "self", "distinct"),
                f"{context}: pairKind must be solo, self or distinct")
    if "actionTags" in record:
        require(isinstance(record["actionTags"], list)
                and all(isinstance(tag, str) and tag.strip() for tag in record["actionTags"]),
                f"{context}: actionTags must be an array of nonempty strings")
    if "conditions" in record:
        require(isinstance(record["conditions"], dict), f"{context}: conditions must be an object")
    if "view" in record:
        require(record["view"] in ("pov", "external"), f"{context}: view must be pov or external")
    if option and "anyOf" in record:
        branches = record["anyOf"]
        require(isinstance(branches, list) and branches, f"{context}: option anyOf must be a nonempty array")
        for index, child in enumerate(branches):
            validate_record(child, f"{context}/anyOf[{index}]", branch=True)


def resolve_path(config_path):
    require(isinstance(config_path, str) and config_path.strip(), "config_path must be a nonempty string")
    # Relative paths follow the ComfyUI process working directory, not this package.
    return Path(config_path).expanduser().resolve()


def load_catalog(config_path):
    path = resolve_path(config_path)
    with path.open("rb") as stream:
        require(stream.seek(0, 2) <= MAX_CONFIG_BYTES, "Config exceeds the 16 MiB limit")
        stream.seek(0)
        content = stream.read(MAX_CONFIG_BYTES + 1)
    require(len(content) <= MAX_CONFIG_BYTES, "Config exceeds the 16 MiB limit")
    data = parse_json(content.decode("utf-8-sig"))
    require(isinstance(data, dict), "Config must be a JSON object")
    registry = data.get("prompt_action_option_rules")
    validate_registry(registry)
    actor_groups = data.get("prompt_actor_groups", [])
    require(isinstance(actor_groups, list), "prompt_actor_groups must be an ID array")
    if actor_groups:
        unique_ids(actor_groups, "prompt_actor_groups")
    rules = data.get("prompt_composite_action_rules")
    require(isinstance(rules, list) and rules, "prompt_composite_action_rules must be a nonempty array")
    ids = []
    groups = {}
    for index, rule in enumerate(rules):
        context = f"action[{index}]"
        validate_record(rule, context)
        validate_roles(rule)
        action_id = rule["id"]
        ids.append(action_id)
        options = rule.get("options", {})
        require(isinstance(options, dict), f"{action_id}: options must be an object (empty is allowed)")
        for group, records in options.items():
            require(group in registry, f"{action_id}: unknown/noncanonical group {group!r}")
            require(isinstance(records, list), f"{action_id}/{group}: expected a choice array")
            for record in records:
                validate_record(record, f"{action_id}/{group}", option=True)
            choice_ids = [record["id"] for record in records]
            unique_ids(choice_ids, f"{action_id}/{group}")
            require(set(choice_ids) == set(registry[group]),
                    f"{action_id}/{group}: choices must exactly match {registry[group]!r}; got {choice_ids!r}")
        groups[action_id] = {group: [record["id"] for record in records] for group, records in options.items()}
    unique_ids(ids, "action IDs")
    scene_rules = {key: data.get(key) for key in ("prompt_composition_rules", "prompt_pair_rules",
                    "prompt_derived_tags", "prompt_pair_derived_tags")}
    for key, value in scene_rules.items():
        expected = list if key.endswith("derived_tags") else dict
        require(value is None or isinstance(value, expected), f"{key} has an invalid rule shape")
    pair_prompts = data.get("scene_pair_prompts", {})
    require(isinstance(pair_prompts, dict), "scene_pair_prompts must be an object")
    for flag, prompts in pair_prompts.items():
        require(isinstance(flag, str) and flag.strip() and isinstance(prompts, list)
                and all(isinstance(prompt, str) for prompt in prompts),
                "scene_pair_prompts must map nonempty flags to arrays of strings")
    scene_rules["scene_pair_prompts"] = pair_prompts
    return {"version": 1, "config_path": str(path), "registry": registry,
             "action_ids": ids, "action_groups": groups, "prompt_actor_groups": actor_groups,
            "action_rules": {rule["id"]: rule for rule in rules}, "scene_rules": scene_rules}


def selected_prompt(rule, options):
    prompts = list(rule.get("prompts", []))
    for choice in select_options(rule, options).values():
        prompts.extend(choice.get("prompts", []))
    return ", ".join(prompt.strip() for prompt in prompts if prompt.strip())


def validate_choices(options, groups, context):
    require(isinstance(options, dict), f"{context}: expected a group-to-choice object")
    for group, choice in options.items():
        require(group in groups, f"{context}: unknown or inapplicable group {group!r}")
        require(isinstance(choice, str) and (choice == "auto" or choice in groups[group]),
                f"{context}/{group}: invalid choice {choice!r}; expected auto or {groups[group]!r}")
    return options


def file_stamp(config_path):
    path = resolve_path(config_path)
    try:
        stat = path.stat()
        return f"{path}|{stat.st_mtime_ns}|{stat.st_size}"
    except OSError:
        return f"{path}|missing"


@PromptServer.instance.routes.post("/mistera/actions/config")
async def action_config(request):
    try:
        # Bound the POST body as well as the selected file. No paths are read from query parameters.
        body = bytearray()
        async for chunk in request.content.iter_chunked(8192):
            body.extend(chunk)
            require(len(body) <= 65536, "Request body exceeds 64 KiB")
        payload = parse_json(body.decode("utf-8-sig"))
        require(isinstance(payload, dict), "Request body must be an object")
        require(set(payload) == {"config_path"}, "Request requires only config_path")
        catalog = await asyncio.to_thread(load_catalog, payload["config_path"])
        return web.json_response(catalog)
    except (ValueError, OSError, RuntimeError) as exc:
        return web.json_response({"error": str(exc)}, status=400)


class MistEraActionRuleDefinitions:
    CATEGORY = "MistEra/Actions"
    FUNCTION = "define"
    RETURN_TYPES = ("MISTERA_ACTION_RULE_CONFIG",)
    RETURN_NAMES = ("rule_config",)

    @classmethod
    def INPUT_TYPES(cls):
        return {"required": {
            "config_path": ("STRING", {"default": DEFAULT_CONFIG}),
            "choices_json": ("STRING", {"default": "{}"}),
        }}

    @classmethod
    def IS_CHANGED(cls, config_path, **kwargs):
        return file_stamp(config_path)

    def define(self, config_path, choices_json="{}"):
        catalog = load_catalog(config_path)
        choices = validate_choices(parse_json(choices_json), catalog["registry"], "global defaults")
        return ({"version": 1, "config_path": catalog["config_path"],
                 "registry": catalog["registry"],
                 "options": {group: choices.get(group, "auto") for group in catalog["registry"]}},)


class MistEraActionSelector:
    CATEGORY = "MistEra/Actions"
    FUNCTION = "select"
    RETURN_TYPES = ("STRING", "MISTERA_ACTION_SELECTIONS", "STRING", "MISTERA_ACTION_DATA")
    RETURN_NAMES = ("action_ids", "selections", "prompt", "action_data")
    OUTPUT_IS_LIST = (True, False, False, False)

    @classmethod
    def INPUT_TYPES(cls):
        return {"required": {
            "config_path": ("STRING", {"default": DEFAULT_CONFIG}),
            "action_id": ("STRING", {"default": ""}),
            "selections_json": ("STRING", {"default": "{}"}),
        }, "optional": {"rule_config": ("MISTERA_ACTION_RULE_CONFIG",),
                          "seed": ("INT", {"default": 0, "min": 0, "max": 2**63 - 1,
                                           "tooltip": "Reserved metadata; this node preserves inline choices without randomizing them."})}}

    @classmethod
    def IS_CHANGED(cls, config_path, **kwargs):
        return file_stamp(config_path)

    def select(self, config_path, action_id="", selections_json="{}", rule_config=None, seed=0, main_role="auto"):
        catalog = load_catalog(config_path)
        registry = catalog["registry"]
        ids = catalog["action_ids"]
        require(isinstance(action_id, str), "action_id must be a string")
        selected = action_id or ids[0]
        require(selected in ids, f"Unknown action_id {selected!r}; expected one of {ids!r}")
        overrides = {}
        if rule_config is None:
            overrides = parse_json(selections_json)
            require(isinstance(overrides, dict), "selections_json must be an action-to-options object")
            for key, options in overrides.items():
                require(key in catalog["action_groups"], f"Stale/unknown action override {key!r}")
                validate_choices(options, catalog["action_groups"][key], f"action override {key!r}")
        defaults = {}
        if rule_config is not None:
            require(isinstance(rule_config, dict) and type(rule_config.get("version")) is int
                    and rule_config["version"] == 1, "rule_config must be a version 1 rule configuration")
            supplied_registry = rule_config.get("registry")
            validate_registry(supplied_registry)
            # Compare canonical sets, not priority order; even the same path must not hide schema drift.
            require({g: set(v) for g, v in supplied_registry.items()} == {g: set(v) for g, v in registry.items()},
                    "rule_config registry differs from this config; reload both nodes")
            resolve_path(rule_config.get("config_path"))
            defaults = validate_choices(rule_config.get("options"), registry, "rule_config options")
        actions = []
        for key in ids:
            local = overrides.get(key, {})
            options = {}
            for group in catalog["action_groups"][key]:
                choice = local.get(group, "auto")
                options[group] = defaults.get(group, "auto") if choice == "auto" else choice
            actions.append({"id": key, "options": options})
        selections = {"version": 1, "config_path": catalog["config_path"],
                       "selected_action": selected, "actions": actions}
        selected_options = next(action["options"] for action in actions if action["id"] == selected)
        action_data = resolve_action_data(catalog["action_rules"][selected], selected_options,
                                          catalog["prompt_actor_groups"], seed, main_role, catalog["scene_rules"])
        prompt = preview_action_template(action_data)
        return {"ui": {"action_selector": [{"selected_action": selected, "action_ids": ids,
                                              "option_selections": {action["id"]: action["options"] for action in actions},
                                              "config_path": catalog["config_path"]}]},
                "result": (ids, selections, prompt, action_data)}
