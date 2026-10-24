"""Action-local actor groups and deterministic, three-state deferred options."""

from copy import deepcopy
import re


TOKEN = re.compile(r"\$([^$]*)\$")
ID = re.compile(r"[A-Za-z0-9_.-]+\Z")
RESERVED = {"main", "support", "doer", "receiver", "self", "solo", "auto", "option", "composite", "view"}
TEXT_SELECTORS = {"shortTag", "shortName", "shortPrompt", "be"}


def actor_identity(record):
    """Keep exported classifiers separate from names, including legacy snapshots."""
    prompt = record.get("short_prompt", "").strip()
    first, _, body = prompt.partition(",")
    short_tag = record.get("short_tag", "").strip() or first.strip() or "other"
    short_name = record.get("short_name", "").strip() or short_tag
    # A DB name may be updated independently of an older short_prompt export.
    # Only inspect its identity prefix; do not split or rewrite body-feature expressions.
    body = body.lstrip() if first.strip().casefold() == short_tag.casefold() else prompt
    if short_name.casefold() != short_tag.casefold() and body.partition(",")[0].strip().casefold() != short_name.casefold():
        body = short_name + (", " + body if body else "")
    description = short_tag + (", " + body if body else "")
    return {"short_tag": short_tag, "short_name": short_name, "short_prompt": description}


def actor_text(actors, selector):
    """Actors must already be deduplicated by registration identity, never by label."""
    if selector == "be":
        return "is" if len(actors) == 1 else "are"
    if selector == "shortPrompt":
        return " and ".join(f"({actor['short_prompt']})" for actor in actors)
    if selector == "shortName":
        return " and ".join(actor["short_name"] for actor in actors)
    return ", ".join(actor["short_tag"] for actor in actors)


def validate_roles(rule):
    sources = rule.get("actions", [])
    if not isinstance(sources, list):
        raise ValueError("actions must be an array")
    aliases = RESERVED | {f"{source.get('id')}.support".casefold()
                          for source in sources if isinstance(source, dict)}
    aliases.update(str(source.get("id")).casefold() for source in sources if isinstance(source, dict))
    groups = {}

    def visit(record, depth=0):
        if depth > 8:
            raise ValueError("source anyOf nesting exceeds 8 levels")
        if not isinstance(record, dict):
            raise ValueError("action source/anyOf branch must be an object")
        mapping = record.get("roles", {})
        if not isinstance(mapping, dict):
            raise ValueError("roles must map doer/receiver sides to role IDs")
        for side, role in mapping.items():
            if side not in ("doer", "receiver"):
                raise ValueError(f"Invalid roles side {side!r}; expected doer or receiver")
            if not isinstance(role, str) or not ID.fullmatch(role) or role.casefold() in aliases:
                raise ValueError(f"Invalid or reserved role ID {role!r}")
            if role.casefold() in groups and groups[role.casefold()] != role:
                raise ValueError(f"Role IDs differ only by case: {role!r}")
            groups[role.casefold()] = role
        if "anyOf" in record:
            branches = record["anyOf"]
            if not isinstance(branches, list) or not branches:
                raise ValueError("source anyOf must be a nonempty array")
            for branch in branches:
                visit(branch, depth + 1)

    for source in sources:
        visit(source)
    return list(groups.values())


def role_dependent(choice, groups):
    return (any(term.strip().partition(":")[0] in groups
               for key in ("all", "any", "none") for clause in choice.get(key, [])
               for term in clause.split("|"))
            or any(role_dependent(branch, groups) for branch in choice.get("anyOf", [])))


def has_property(actor, tag):
    tags = {value.casefold() for value in actor["actor_tags"]}
    for term in tag.split("&"):
        term = term.strip().casefold()
        if not term or term == "!":
            return False
        if term.startswith("!"):
            if term[1:] in tags:
                return False
        elif term not in tags:
            return False
    return True


def combine(values, every):
    values = list(values)
    decisive = False if every else True
    if any(value is decisive for value in values):
        return decisive
    return None if None in values else every


def predicate(term, data):
    parts = term.strip().split(":")
    if len(parts) == 2 and parts[0] == "view":
        selected = data.get("selected_view")
        return None if selected is None else selected == parts[1]
    if parts[0] in data.get("role_groups", {}):
        if len(parts) == 2:
            role, tag = parts
            quantifier = "any"
        elif len(parts) == 3 and parts[1] in ("any", "all"):
            role, quantifier, tag = parts
        else:
            raise ValueError(f"Invalid role predicate {term!r}")
        members = data["role_groups"][role]
        if not members:
            return False
        actors = data.get("actor_values", {})
        return combine((has_property(actors[member], tag) if member in actors else None
                        for member in members), quantifier == "all")
    # Registered scene facts supersede synthetic relation flags from the initial manual preview.
    context = data.get("scene_context")
    if context is not None and (len(parts) == 1 or parts[0] in context.get("counts", {})):
        from .scene_context import has_scene_tag
        if term.startswith("!"):
            return not has_scene_tag(context, term[1:])
        return has_scene_tag(context, term)
    # Source-slot facts remain local to the bound source.
    if len(parts) == 1:
        tags = data.get("action_tags", [])
    elif len(parts) == 2:
        binding = data.get("bindings", {}).get(parts[0] + ".support")
        if binding is None:
            return None
        tags = data.get("actor_action_tags", {}).get(binding, [])
    else:
        return None
    tag = parts[-1]
    return (tag[1:].casefold() not in {t.casefold() for t in tags} if tag.startswith("!")
            else tag.casefold() in {t.casefold() for t in tags})


def choice_matches(choice, data):
    checks = []
    for key in ("all", "any", "none"):
        clauses = [combine((predicate(term, data) for term in clause.split("|")), False)
                   for clause in choice.get(key, [])]
        if clauses:
            value = combine(clauses, key == "all")
            checks.append(None if value is None else not value if key == "none" else value)
    if data.get("scene_context") is not None:
        from .scene_context import pair_conditions_match
        checks.append(pair_conditions_match(choice, data["scene_context"].get("primary_pair")))
    elif any(choice.get(key) for key in ("main", "support", "differs", "pairKind")):
        checks.append(None)
    if choice.get("conditions"):
        checks.append(None)
    return combine(checks, True)


def resolve_choice(choice, data):
    """Resolve an ordered option OR, preserving the canonical root and its controls."""
    branches = choice.get("anyOf")
    if "anyOf" in choice:
        if not isinstance(branches, list) or not branches:
            raise ValueError("option anyOf must be a nonempty array")
        if any(not isinstance(branch, dict) or "anyOf" in branch for branch in branches):
            raise ValueError("option anyOf supports only one level of object branches")
    valid = choice_matches(choice, data)
    if valid is not True:
        return valid, None
    if branches is None:
        return True, choice
    for branch in branches:
        valid = choice_matches(branch, data)
        if valid is None:
            return None, None
        if valid:
            effective = choice.copy()
            for field in ("prompts", "scenePrompts"):
                if field in choice or field in branch:
                    effective[field] = [*choice.get(field, []), *branch.get(field, [])]
            return True, effective
    return False, None


def replace_spans(text, replacements):
    for start, end, value in sorted(replacements, reverse=True):
        if not value:
            left = max(text.rfind(delimiter, 0, start) for delimiter in ",|{}")
            right = min((index for delimiter in ",|{}"
                         if (index := text.find(delimiter, end)) >= 0), default=len(text))
            if not text[left + 1:start].strip() and not text[end:right].strip():
                if right < len(text) and text[right] == ",":
                    end = right + 1
                    while end < len(text) and text[end].isspace():
                        end += 1
                elif left >= 0 and text[left] == ",":
                    start = left
        text = text[:start] + value + text[end:]
    return text


def resolve_roles(data, preserve_empty_roles=False):
    """Mutate only the caller's private copy; unknown earlier choices block fallback."""
    text = data["resolved_prompt"]
    replacements = []
    pending = data.get("role_options", {})
    data["resolved_options"] = []
    resolved_groups = set()

    def choose(metadata):
        choices, preference = metadata["choices"], metadata["preference"]
        ordered = ([choice for choice in choices if choice["id"] == preference]
                   + [choice for choice in choices if choice["id"] != preference])
        for choice in ordered:
            valid, effective = resolve_choice(choice, data)
            if valid is None:
                return False, None
            if valid:
                return True, effective
        return True, None

    # Match Unity's view-first ordering even when the camera group is authored last.
    view_choices = {}
    for group, metadata in pending.items():
        metadata.pop("resolved_choice", None)
        if any(choice.get("view") for choice in metadata["choices"]):
            view_choices[group] = choose(metadata)
            known, choice = view_choices[group]
            if known and choice is not None:
                data["selected_view"] = choice.get("view")
    for match in TOKEN.finditer(text):
        alias, _, group = match.group(1).rpartition(".")
        if alias != "option" or group not in pending:
            continue
        metadata = pending[group]
        known, choice = view_choices[group] if group in view_choices else choose(metadata)
        if not known:
            continue
        if choice is not None:
            replacements.append((match.start(), match.end(), ", ".join(choice.get("prompts", []))))
            data["selected_options"][group] = choice["id"]
            metadata["resolved"] = choice["id"]
            metadata["resolved_choice"] = deepcopy(choice)
            if group not in resolved_groups:
                data["resolved_options"].append(dict(deepcopy(choice), group=group))
                resolved_groups.add(group)
        else:
            replacements.append((match.start(), match.end(), ""))
            data["selected_options"].pop(group, None)
            metadata["resolved"] = None
    text = replace_spans(text, replacements)
    replacements = []
    actors = data.get("actor_values", {})
    for match in TOKEN.finditer(text):
        role, _, tag = match.group(1).rpartition(".")
        if role not in data.get("role_groups", {}):
            continue
        members = list(dict.fromkeys(data["role_groups"][role]))
        if preserve_empty_roles and not members:
            continue
        # Multi-member body snapshots are deliberately not synthesized from partial selections.
        if tag not in TEXT_SELECTORS and tag in data.get("role_body_groups", []):
            continue
        if all(member in actors for member in members):
            value = (actor_text([actors[member] for member in members], tag) if tag in TEXT_SELECTORS
                     else ", ".join(actors[member]["short_tag"] for member in members
                                    if has_property(actors[member], tag)))
            replacements.append((match.start(), match.end(), value if members else ""))
    data["resolved_prompt"] = replace_spans(text, replacements)


def register_actor(data, targets, character_id, record):
    edited = deepcopy(data)
    actors = edited.setdefault("actor_values", {})
    value = {"id": character_id, **actor_identity(record),
             "actor_tags": list(record.get("actor_tags", []))}
    if value["short_tag"] not in value["actor_tags"]:
        value["actor_tags"].append(value["short_tag"])
    for target in targets:
        if target in actors and actors[target] != value:
            raise ValueError(f"Actor binding {target!r} is already assigned to {actors[target]['id']!r}; "
                             "cannot change its character/properties after rendering. Restart from Action Selector.")
        actors[target] = deepcopy(value)
    resolve_roles(edited)
    return edited
