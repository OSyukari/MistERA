"""Data-driven character emotions, independent of the action catalogue."""

from copy import deepcopy

from .nodes import MAX_CONFIG_BYTES, parse_json, require, resolve_path, unique_ids


def load_emotions(config_path):
    path = resolve_path(config_path)
    try:
        with path.open("rb") as stream:
            require(stream.seek(0, 2) <= MAX_CONFIG_BYTES, "Config exceeds the 16 MiB limit")
            stream.seek(0)
            content = stream.read(MAX_CONFIG_BYTES + 1)
        require(len(content) <= MAX_CONFIG_BYTES, "Config exceeds the 16 MiB limit")
        data = parse_json(content.decode("utf-8-sig"))
        require(isinstance(data, dict), "Config must be a JSON object")
        registry = data.get("character_emotions")
        require(isinstance(registry, dict) and registry, "character_emotions must be a nonempty object")
        unique_ids(list(registry), "character_emotions IDs")
        for emotion_id, entry in registry.items():
            context = f"character_emotions/{emotion_id}"
            require(isinstance(entry, dict), f"{context}: expected an object")
            require("prompts" in entry, f"{context}: prompts is required")
            for field in ("prompts", "requireActorTags", "requireAnyActorTags", "excludeActorTags",
                          "actorTags", "removeActorTags"):
                values = entry.get(field, [])
                require(isinstance(values, list)
                        and all(isinstance(value, str) and value.strip() for value in values),
                        f"{context}/{field}: expected an array of nonempty strings")
    except (ValueError, OSError) as exc:
        raise ValueError(f"Emotion config {str(path)!r}: {exc}") from exc
    return str(path), registry


def valid_emotions(registry, actor_tags):
    """Match case-sensitive actor-profile predicates against the original character only."""
    tags = set(actor_tags)

    def has(tag):
        return (len(tag) > 1 and tag[1:] not in tags) if tag.startswith("!") else tag in tags

    return [emotion_id for emotion_id, entry in registry.items()
            if all(has(tag) for tag in entry.get("requireActorTags", []))
            and (not entry.get("requireAnyActorTags")
                 or any(has(tag) for tag in entry["requireAnyActorTags"]))
            and not any(has(tag) for tag in entry.get("excludeActorTags", []))]


def overlay_emotion(record, entry):
    record = deepcopy(record)
    if entry and (entry.get("actorTags") or entry.get("removeActorTags")):
        removed = set(entry.get("removeActorTags", []))
        tags = [tag for tag in record.get("actor_tags", []) if tag not in removed]
        for tag in entry.get("actorTags", []):
            if tag not in tags:
                tags.append(tag)
        record["actor_tags"] = tags
    return record


def append_emotion(prompts, entry):
    if not entry or not entry["prompts"]:
        return prompts
    result = list(prompts)
    # Support is an original snapshot; composition additions belong only to main outputs.
    for index in (1, 2):
        base = prompts[index]
        seen = {tag.strip() for tag in base.split(",") if tag.strip()}
        extras = []
        for value in entry["prompts"]:
            for tag in value.split(","):
                tag = tag.strip()
                if tag and tag not in seen:
                    seen.add(tag)
                    extras.append(tag)
        result[index] = ", ".join(([base] if base.strip() else []) + extras) if extras else base
    return tuple(result)


def append_main_custom(prompts, custom_prompt):
    custom = custom_prompt.strip()
    if not custom:
        return prompts
    result = list(prompts)
    # Keep commas, nested braces and randomizer syntax verbatim in temporary corrections.
    for index in (1, 2):
        base = prompts[index]
        result[index] = f"{base}, {custom}" if base.strip() else custom
    return tuple(result)
