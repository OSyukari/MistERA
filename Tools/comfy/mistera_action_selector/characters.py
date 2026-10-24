"""Character prompt selection from the Unity-exported portrait template database."""

import asyncio

from aiohttp import web
from server import PromptServer

from .nodes import DEFAULT_CONFIG, MAX_CONFIG_BYTES, file_stamp, parse_json, require, resolve_path
from .newemotions import append_emotion, append_main_custom, load_emotions, overlay_emotion, valid_emotions
from .actor_package import (character_prompts, finalize_action_package, register_character,
                            registration_key)


DEFAULT_DB_PATH = r"E:\UnityProjects\MistEra\Mods\Assetpacks\characterDBs.json"
CHARACTER_FIELDS = (
    "name", "short_prompt", "support_prompt", "main_naked_prompt", "main_clothed_prompt",
)


def string_list(value, context):
    require(isinstance(value, list) and all(isinstance(item, str) and item.strip() for item in value),
            f"{context}: expected an array of nonempty strings")
    return value


def validate_optional(record, context):
    if "short_tag" in record:
        require(isinstance(record["short_tag"], str) and record["short_tag"].strip(),
                f"{context}/short_tag must be a nonempty string")
    if "short_name" in record:
        require(isinstance(record["short_name"], str), f"{context}/short_name must be a string")
    if "actor_tags" in record:
        string_list(record["actor_tags"], f"{context}/actor_tags")
    if "optional_prompts" not in record:
        return
    states = record["optional_prompts"]
    require(isinstance(states, dict), f"{context}/optional_prompts must be an object")
    for state in ("naked", "clothed"):
        groups = states.get(state, {})
        require(isinstance(groups, dict), f"{context}/{state} must be an object")
        for group, rules in groups.items():
            require(isinstance(group, str) and group.strip() and isinstance(rules, list),
                    f"{context}/{state}: groups must contain rule arrays")
            for rule in rules:
                require(isinstance(rule, dict), f"{context}/{state}/{group}: expected a rule object")
                string_list(rule.get("prompts", []), f"{context}/{state}/{group}/prompts")
                for guard in ("requireActorTags", "requireAnyActorTags", "excludeActorTags"):
                    string_list(rule.get(guard, []), f"{context}/{state}/{group}/{guard}")


def apply_action(record, action_data, actor_binding):
    require(isinstance(action_data, dict) and type(action_data.get("version")) is int
            and action_data["version"] == 1, "action_data must be a version 1 resolved action")
    require(isinstance(actor_binding, str) and actor_binding.strip(), "actor_binding must be a nonempty string")
    bindings = action_data.get("bindings")
    requests = action_data.get("actor_requests")
    actor_tags = action_data.get("actor_action_tags")
    require(isinstance(bindings, dict) and isinstance(requests, dict) and isinstance(actor_tags, dict),
            "action_data needs bindings, actor_requests and actor_action_tags")
    for alias, target in bindings.items():
        require(isinstance(alias, str) and alias and isinstance(target, str) and target,
                "action_data bindings must map nonempty aliases to binding names")
    for table, name in ((requests, "actor_requests"), (actor_tags, "actor_action_tags")):
        for binding, values in table.items():
            require(isinstance(binding, str) and binding, f"action_data {name}: invalid binding")
            string_list(values, f"action_data {name}/{binding}")
    binding = bindings.get(actor_binding, actor_binding)
    require(binding in actor_tags, f"Unknown actor_binding {actor_binding!r}; available bindings: {list(bindings)!r}")
    unresolved = string_list(action_data.get("unresolved_bindings", []), "action_data unresolved_bindings")
    require(not unresolved or binding not in ("main", "support"),
            "This legacy package has unbound doer/receiver requests. "
            "Explicitly use actor_binding doer/receiver on Character Selector.")
    # Never use the scene-wide action_tags union: another support's action must not enable this actor's rules.
    groups = requests.get(binding, [])
    tags = set(record.get("actor_tags", []) + actor_tags[binding] + groups)
    states = record.get("optional_prompts", {})

    def has(predicate):
        return (len(predicate) > 1 and predicate[1:] not in tags) if predicate.startswith("!") else predicate in tags

    def prompt(field, state):
        base = record[field]
        seen = {tag.strip() for tag in base.split(",") if tag.strip()}
        extras = []
        for group in ["__base__", *groups]:
            for rule in states.get(state, {}).get(group, []):
                if not all(has(tag) for tag in rule.get("requireActorTags", [])):
                    continue
                any_tags = rule.get("requireAnyActorTags", [])
                if any_tags and not any(has(tag) for tag in any_tags):
                    continue
                if any(has(tag) for tag in rule.get("excludeActorTags", [])):
                    continue
                for value in rule["prompts"]:
                    for tag in value.split(","):
                        tag = tag.strip()
                        if tag and tag not in seen:
                            seen.add(tag)
                            extras.append(tag)
        return ", ".join(([base] if base.strip() else []) + extras) if extras else base

    # Unity already selected the compact/full support representation at export. Do not expand it back into a main actor.
    return (record["support_prompt"], prompt("main_naked_prompt", "naked"),
            prompt("main_clothed_prompt", "clothed"))


def replace_actor_tokens(record, character_id, action_data, actor_binding, is_receiver, is_doer, state):
    """Direct-call adapter; action body tokens always use naked snapshots."""
    key = registration_key(character_id, actor_binding, is_receiver, is_doer)
    edited = register_character(action_data, character_id, record, actor_binding,
                                is_receiver, is_doer, registration_id=key)
    return character_prompts(edited, record, key), edited, finalize_action_package(edited)


def load_characters(db_path):
    require(isinstance(db_path, str) and db_path.strip(), "db_path must be a nonempty string")
    path = resolve_path(db_path)
    try:
        with path.open("rb") as stream:
            require(stream.seek(0, 2) <= MAX_CONFIG_BYTES, "Character database exceeds the 16 MiB limit")
            stream.seek(0)
            content = stream.read(MAX_CONFIG_BYTES + 1)
        require(len(content) <= MAX_CONFIG_BYTES, "Character database exceeds the 16 MiB limit")
        data = parse_json(content.decode("utf-8-sig"))
        require(isinstance(data, dict), "Character database root must be an object keyed by portrait template ID")
        require(data, "Character database is empty; export the character database from Unity first")
        for base_id, record in data.items():
            require(isinstance(base_id, str) and base_id != "", "Portrait template IDs must be nonempty strings")
            require(isinstance(record, dict), f"Character {base_id!r}: expected an object")
            for field in CHARACTER_FIELDS:
                require(field in record and isinstance(record[field], str),
                        f"Character {base_id!r}: {field} is required and must be a string")
            validate_optional(record, f"Character {base_id!r}")
    except (ValueError, OSError) as exc:
        raise ValueError(f"Character database {str(path)!r}: {exc}") from exc
    return str(path), data


@PromptServer.instance.routes.post("/mistera/characters/config")
async def character_config(request):
    try:
        body = bytearray()
        async for chunk in request.content.iter_chunked(8192):
            body.extend(chunk)
            require(len(body) <= 65536, "Request body exceeds 64 KiB")
        payload = parse_json(body.decode("utf-8-sig"))
        require(isinstance(payload, dict), "Request body must be an object")
        require("db_path" in payload and set(payload) <= {"db_path", "config_path"},
                "Request requires db_path and optionally config_path")
        path, characters = await asyncio.to_thread(load_characters, payload["db_path"])
        config_path, emotions = await asyncio.to_thread(load_emotions, payload.get("config_path", DEFAULT_CONFIG))
        return web.json_response({"db_path": path, "config_path": config_path, "emotions": list(emotions), "characters": [
            {"id": base_id, "name": record["name"],
             "valid_emotions": valid_emotions(emotions, record.get("actor_tags", []))}
            for base_id, record in characters.items()
        ]})
    except (ValueError, OSError, RuntimeError) as exc:
        return web.json_response({"error": str(exc)}, status=400)


class MistEraCharacterSelector:
    CATEGORY = "MistEra/Characters"
    FUNCTION = "select"
    RETURN_TYPES = ("STRING", "STRING", "STRING", "STRING", "MISTERA_ACTION_DATA", "STRING", "STRING", "STRING")
    RETURN_NAMES = ("support_prompt", "main_naked_prompt", "main_clothed_prompt", "character_ids", "action_data", "prompt",
                    "emotion_ids", "valid_emotion_ids")
    OUTPUT_IS_LIST = (False, False, False, True, False, False, True, True)

    @classmethod
    def INPUT_TYPES(cls):
        return {"required": {
            "db_path": ("STRING", {"default": DEFAULT_DB_PATH}),
            "character_id": ("STRING", {"default": ""}),
        }, "optional": {
            "action_data": ("MISTERA_ACTION_DATA",),
            "actor_binding": ("STRING", {"default": "", "tooltip": "Advanced: choose an explicit main, support, or source slot (e.g. oral.support) only if needed. Output names do not bind an actor."}),
            "is_receiver": ("BOOLEAN", {"default": False}),
            "is_doer": ("BOOLEAN", {"default": False}),
            "config_path": ("STRING", {"default": DEFAULT_CONFIG}),
            "emotion_id": ("STRING", {"default": "none"}),
            "custom_prompt": ("STRING", {"default": "", "multiline": True,
                                         "tooltip": "Temporary correction appended only to MAIN naked/clothed outputs; not support or action composition."}),
        }, "hidden": {"registration_id": "UNIQUE_ID"}}

    @classmethod
    def IS_CHANGED(cls, db_path, config_path=DEFAULT_CONFIG, **kwargs):
        return file_stamp(db_path), file_stamp(config_path)

    def select(self, db_path, character_id="", action_data=None, actor_binding="",
               is_receiver=False, is_doer=False, config_path=DEFAULT_CONFIG, emotion_id="none",
               registration_id=None, custom_prompt=""):
        path, characters = load_characters(db_path)
        resolved_config_path, emotions = load_emotions(config_path)
        require(isinstance(character_id, str), "character_id must be a string")
        selected = character_id or next(iter(characters))
        require(selected in characters,
                f"Unknown character_id {selected!r} in {path!r}; use a canonical portrait template ID "
                "from this database (Load / Refresh to browse names)")
        require(isinstance(emotion_id, str), "emotion_id must be a string")
        require(isinstance(custom_prompt, str), "custom_prompt must be a string")
        valid_emotion_ids = valid_emotions(emotions, characters[selected].get("actor_tags", []))
        emotion_valid = emotion_id in valid_emotion_ids
        emotion = emotions[emotion_id] if emotion_valid else None
        record = overlay_emotion(characters[selected], emotion)
        # Carry correction metadata without changing base fields or action templates.
        record["custom_main_prompt"] = custom_prompt
        require(isinstance(actor_binding, str), "actor_binding must be a string")
        actor_binding = actor_binding.strip()
        require(all(type(flag) is bool for flag in (is_receiver, is_doer)),
                "Actor role switches must be booleans")
        edited, prompt = None, ""
        if action_data is not None:
            key = registration_key(selected, actor_binding, is_receiver, is_doer,
                                   registration_id)
            edited = register_character(action_data, selected, record, actor_binding,
                                        is_receiver, is_doer, key)
            prompts = character_prompts(edited, record, key)
            prompt = finalize_action_package(edited)
        elif any("__base__" in groups for groups in record.get("optional_prompts", {}).values()):
            context = {"version": 1, "bindings": {"main": "main"}, "actor_requests": {}, "actor_action_tags": {"main": []}}
            prompts = apply_action(record, context, "main")
        else:
            prompts = tuple(record[field] for field in self.RETURN_NAMES[:3])
        prompts = append_emotion(prompts, emotion)
        prompts = append_main_custom(prompts, custom_prompt)
        return {"ui": {"character_selector": [{
            "db_path": path, "input_db_path": db_path,
            "character_id": selected, "name": record["name"],
            "config_path": resolved_config_path, "input_config_path": config_path,
            "emotion_id": emotion_id, "emotion_valid": emotion_valid,
            "emotion_ids": list(emotions), "valid_emotion_ids": valid_emotion_ids,
        }]}, "result": (*prompts, list(characters), edited, prompt, list(emotions), valid_emotion_ids)}
