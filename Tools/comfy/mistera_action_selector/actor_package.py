"""Portable actor snapshots and pure rendering of canonical action packages."""

from copy import deepcopy
import json
import re

from .roles import TEXT_SELECTORS, TOKEN, actor_identity, actor_text, replace_spans, resolve_roles


MANUAL_ROLES = ("doer", "receiver", "actDoer", "actReceiver")


def registration_key(character_id, actor_binding, is_receiver, is_doer,
                     registration_id=None):
    if registration_id is not None:
        return str(registration_id)
    return "direct:" + json.dumps([character_id, actor_binding,
                                   [is_doer, is_receiver]], separators=(",", ":"))


def template_text(data):
    """Return canonical text, accepting older packages with only resolved_prompt."""
    text = data.get("prompt_template", data.get("resolved_prompt", ""))
    if not isinstance(text, str):
        raise ValueError("action_data prompt template must be a string")
    return text


def set_template(data, text):
    """Explicitly edit both canonical fields in the caller's package."""
    if not isinstance(text, str):
        raise ValueError("action_data prompt template must be a string")
    data["prompt_template"] = data["resolved_prompt"] = text
    return data


def _aliases(data, actor):
    selected = set(actor.get("roles", []))
    if actor.get("actor_binding"):
        selected.add(actor["actor_binding"])
    bindings = data.get("bindings", {})
    if data.get("node_role_mode") == "logical":
        # Only explicit advanced bindings may opt into main/source-slot aliases.
        binding = actor.get("actor_binding")
        if binding and binding not in MANUAL_ROLES:
            target = bindings.get(binding, binding)
            selected.update(alias for alias, value in bindings.items()
                            if alias not in MANUAL_ROLES and value == target)
        return selected
    targets = {bindings.get(alias, alias) for alias in selected}
    # Legacy sides are independent checkboxes, even for self/main aliases.
    return selected | {alias for alias, target in bindings.items()
                       if alias not in ("doer", "receiver") and target in targets}


def _rebuild(data):
    names = dict.fromkeys([*data.get("source_role_groups", {}),
                          *data.get("role_groups", {}), *MANUAL_ROLES])
    groups = {name: [] for name in names}
    values = {}
    for registration_id, actor in data.get("actor_registrations", {}).items():
        record = actor["record"]
        values[registration_id] = {
            "id": actor["character_id"],
            **actor_identity(record),
            "actor_tags": list(record.get("actor_tags", [])),
        }
        if values[registration_id]["short_tag"] not in values[registration_id]["actor_tags"]:
            values[registration_id]["actor_tags"].append(values[registration_id]["short_tag"])
        for alias in _aliases(data, actor):
            if alias in groups or alias == actor.get("actor_binding"):
                groups.setdefault(alias, []).append(registration_id)
    data["role_groups"], data["actor_values"] = groups, values
    data["actor_short_tags"] = {identity: actor["short_tag"] for identity, actor in values.items()}
    data["actor_short_names"] = {identity: actor["short_name"] for identity, actor in values.items()}


def register_character(data, character_id, record, actor_binding="", is_receiver=False,
                       is_doer=False, registration_id=None):
    """Copy a package and replace this node's registration, never its template."""
    if not isinstance(data, dict) or type(data.get("version")) is not int or data["version"] != 1:
        raise ValueError("action_data must be a version 1 action package")
    flags = (is_doer, is_receiver)
    if any(type(flag) is not bool for flag in flags):
        raise ValueError("Actor role switches must be booleans")
    if not isinstance(actor_binding, str):
        raise ValueError("actor_binding must be a string")
    actor_binding = actor_binding.strip()
    known = (set(data.get("bindings", {})) | set(data.get("actor_action_tags", {}))
             | set(data.get("source_role_groups", {})) | set(data.get("role_groups", {}))
             | set(MANUAL_ROLES))
    if actor_binding and actor_binding not in known:
        raise ValueError(f"Unknown actor_binding {actor_binding!r}")
    roles = (["doer", "actDoer"] if is_doer else []) + (["receiver", "actReceiver"] if is_receiver else [])
    registration_id = registration_key(character_id, actor_binding, is_receiver, is_doer,
                                       registration_id)
    edited = deepcopy(data)
    set_template(edited, template_text(data))
    registry = edited.setdefault("actor_registrations", {})
    if roles or actor_binding:
        registry[registration_id] = {"character_id": character_id, "record": deepcopy(record),
                                     "roles": roles, "actor_binding": actor_binding}
    else:
        registry.pop(registration_id, None)
    _rebuild(edited)
    return edited


def _choice_paths(text, offsets):
    stack, choices, paths = [], set(), {}
    for index, char in enumerate(text):
        if index in offsets:
            paths[index] = dict(stack)
        if char == "{":
            if len(stack) >= 64:
                raise ValueError("Inline prompt nesting exceeds 64 levels")
            stack.append([index, 0])
        elif char == "}":
            if not stack:
                raise ValueError("Unmatched closing brace in action prompt")
            stack.pop()
        elif char == "|" and stack:
            choices.add(stack[-1][0])
            stack[-1][1] += 1
    if stack:
        raise ValueError("Unclosed brace in action prompt")
    return {offset: {scope: branch for scope, branch in path.items() if scope in choices}
            for offset, path in paths.items()}


def _body_context(data, actor, text):
    owned = _aliases(data, actor)
    role_groups = data.get("role_groups", {})
    body_groups = data.get("role_body_groups", [])
    tokens = []
    for match in TOKEN.finditer(text):
        alias, separator, group = match.group(1).rpartition(".")
        if (separator and alias in owned and alias != "option" and group not in TEXT_SELECTORS
                and (alias not in role_groups or group in body_groups)):
            tokens.append((match, group))
    paths = _choice_paths(text, {match.start() for match, _ in tokens})

    def cooccurring(path):
        return list(dict.fromkeys(group for match, group in tokens
                    if all(path.get(scope) == branch for scope, branch in paths[match.start()].items())))

    return tokens, paths, cooccurring


def _tags(data, actor):
    tags = set(actor["record"].get("actor_tags", []))
    tags.update(data.get("root_action_tags", []))
    bindings = data.get("bindings", {})
    action_tags = data.get("actor_action_tags", {})
    for alias in _aliases(data, actor):
        binding = alias if data.get("node_role_mode") == "logical" and alias in MANUAL_ROLES else bindings.get(alias, alias)
        tags.update(action_tags.get(binding, []))
    return tags


def _details(record, state, requested, tags, context_groups, group_token=False):
    entries = record.get("optional_prompts", {}).get(state, {})
    rules = list(entries.get("__base__", []))
    if group_token:
        rules = [rule for rule in rules if any(
            tag.lstrip("!") == requested[0] for guard in
            ("requireActorTags", "requireAnyActorTags", "excludeActorTags")
            for tag in rule.get(guard, []))]
    for group in requested:
        rules.extend(entries.get(group, []))
    context = tags | set(context_groups)

    def has(tag):
        return (len(tag) > 1 and tag[1:] not in context) if tag.startswith("!") else tag in context

    values = []
    for rule in rules:
        if (not all(has(tag) for tag in rule.get("requireActorTags", []))
                or (rule.get("requireAnyActorTags")
                    and not any(has(tag) for tag in rule["requireAnyActorTags"]))
                or any(has(tag) for tag in rule.get("excludeActorTags", []))):
            continue
        for value in rule.get("prompts", []):
            for tag in value.split(","):
                tag = tag.strip()
                if tag and tag not in values:
                    values.append(tag)
    return values


def character_prompts(data, record, registration_id=None):
    """Current actor's three prompts using only guaranteed original-template groups."""
    actor = data.get("actor_registrations", {}).get(str(registration_id))
    if actor is None:
        actor = {"record": record, "roles": [], "actor_binding": ""}
        tags, groups = set(record.get("actor_tags", [])), []
    else:
        _, _, cooccurring = _body_context(data, actor, template_text(data))
        tags, groups = _tags(data, actor), cooccurring({})
    prompts = [record["support_prompt"]]
    for field, state in (("main_naked_prompt", "naked"), ("main_clothed_prompt", "clothed")):
        base = record[field]
        seen = {tag.strip() for tag in base.split(",") if tag.strip()}
        extras = [tag for tag in _details(record, state, groups, tags, groups) if tag not in seen]
        prompts.append(", ".join(([base] if base.strip() else []) + extras) if extras else base)
    return tuple(prompts)


def render_action_package(data):
    """Render tags and complete scene sentences from one private, final actor context."""
    rendered = deepcopy(data)
    rendered["resolved_prompt"] = template_text(data)
    _rebuild(rendered)
    from .scene_context import build_scene_context
    rendered["scene_context"] = build_scene_context(rendered)
    resolve_roles(rendered, preserve_empty_roles=True)
    text = rendered["resolved_prompt"]
    replacements = {}
    aliases = {}
    for identity, actor in rendered.get("actor_registrations", {}).items():
        for alias in _aliases(rendered, actor):
            aliases.setdefault(alias, []).append(identity)
    for actor in rendered.get("actor_registrations", {}).values():
        record = actor["record"]
        short_tag = actor_identity(record)["short_tag"]
        owned = _aliases(rendered, actor)
        tokens, paths, cooccurring = _body_context(rendered, actor, text)
        tags = _tags(rendered, actor)
        for match, group in tokens:
            values = _details(record, "naked", [group], tags, cooccurring(paths[match.start()]), True)
            replacements.setdefault((match.start(), match.end()), []).extend(values)
        for match in re.finditer(r"\{(doer|receiver)\}", text):
            if match.group(1) in owned:
                replacements.setdefault((match.start(), match.end()), []).append(short_tag)
    for match in TOKEN.finditer(text):
        alias, _, selector = match.group(1).rpartition(".")
        members = list(dict.fromkeys(aliases.get(alias, [])))
        if selector in TEXT_SELECTORS and members:
            replacements[(match.start(), match.end())] = [actor_text(
                [rendered["actor_values"][identity] for identity in members], selector)]
    action_prompt = replace_spans(text, [(start, end, ", ".join(values))
                                        for (start, end), values in replacements.items()])
    sentences = list(rendered.get("scene_prompts", []))
    selected = rendered.get("resolved_options", [])
    for choice in selected:
        effective = rendered.get("role_options", {}).get(choice["group"], {}).get("resolved_choice", choice)
        sentences.extend(effective.get("scenePrompts", []))
    context = rendered["scene_context"]
    scene_prompts = []
    from .action_data import parse_inline

    for sentence in sentences:
        parse_inline(sentence)
        invalid = []

        def semantic(match):
            alias, separator, selector = match.group(1).rpartition(".")
            members = list(dict.fromkeys(rendered["role_groups"].get(alias, aliases.get(alias, []))))
            if not separator or selector not in TEXT_SELECTORS or not members:
                invalid.append(match.group(0))
                return match.group(0)
            return actor_text([rendered["actor_values"][identity] for identity in members], selector)

        # Accept raw legacy placeholders in manually edited packages as well.
        sentence = re.sub(r"\{(doer|receiver)\}",
                          lambda match: f"${match.group(1)}.shortTag$", sentence)
        sentence = TOKEN.sub(semantic, sentence)
        if "$" in sentence or invalid:
            diagnostic = "Scene sentence omitted: missing or unsupported actor tokens: " + ", ".join(invalid or [sentence])
            if diagnostic not in context["diagnostics"]:
                context["diagnostics"].append(diagnostic)
            continue
        sentence = sentence.strip()
        if sentence and sentence not in scene_prompts:
            scene_prompts.append(sentence)
    return {"action_prompt": action_prompt, "scene_prompts": scene_prompts,
            "selected_options": selected, "scene_context": context}


def finalize_action_package(data):
    """Legacy text-only wrapper around the pure package renderer."""
    return render_action_package(data)["action_prompt"]
