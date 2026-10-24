"""Unexpanded prompt templates and source-scoped action context, without game state."""

from copy import deepcopy
import re

from .roles import TEXT_SELECTORS, resolve_choice, role_dependent, validate_roles


MAIN_ROLES = ("auto", "doer", "receiver", "self", "solo")
TOKEN = re.compile(r"\$([^$]*)\$")


def require(condition, message):
    if not condition:
        raise ValueError(message)


def add_unique(values, value):
    if value.casefold() not in {item.casefold() for item in values}:
        values.append(value)


def parse_inline(text):
    """Validate balanced inline choices without selecting or changing any branch."""
    def parse(index, depth, nested):
        require(depth <= 64, "Inline prompt nesting exceeds 64 levels")
        branches = [[]]
        literal = []

        def flush():
            if literal:
                branches[-1].append("".join(literal))
                literal.clear()

        while index < len(text):
            char = text[index]
            if char == "{":
                flush()
                child, index = parse(index + 1, depth + 1, True)
                branches[-1].append(child)
                continue
            if char == "}":
                require(nested, "Unmatched closing brace in inline prompt")
                flush()
                return ("choice" if len(branches) > 1 else "braces", branches), index + 1
            if char == "|" and nested:
                flush()
                branches.append([])
            else:
                literal.append(char)
            index += 1
        require(not nested, "Unclosed brace in inline prompt")
        flush()
        return branches[0], index

    return parse(0, 0, False)[0]


def select_options(rule, options):
    selected = {}
    for group, choices in rule.get("options", {}).items():
        preference = options.get(group, "auto")
        if preference == "auto":
            choice = next((choice for choice in choices
                           if not any(choice.get(key) for key in
                                       ("all", "any", "main", "support", "differs", "pairKind", "anyOf"))
                           and choice.get("view") != "pov"), None)
        else:
            choice = next(choice for choice in choices if choice["id"] == preference)
        if choice is not None:
            selected[group] = choice
    return selected


def scoped_terms(expression, slot, slots):
    terms = []
    for term in expression.split("|"):
        term = term.strip()
        prefix, separator, suffix = term.partition(":")
        if separator and prefix in slots:
            if prefix == slot:
                terms.append(suffix)
        else:
            terms.append(term)
    return terms


def source_evidence(records, slot, slots):
    """Choose only one witness for each OR; never union alternative action flags."""
    clauses = []
    blocked = set()
    for record in records:
        require(isinstance(record, dict), f"{slot}: action conditions must be objects")
        for expression in record.get("none", []):
            blocked.update(term.casefold() for term in scoped_terms(expression, slot, slots))
        for expression in record.get("all", []):
            terms = scoped_terms(expression, slot, slots)
            if terms:
                clauses.append(terms)
        any_terms = [term for expression in record.get("any", [])
                     for term in scoped_terms(expression, slot, slots)]
        if any_terms:
            clauses.append(any_terms)
    tags = []
    for clause in clauses:
        witness = next((term for term in clause if term.casefold() not in blocked), None)
        if witness is None:
            return None
        # Side predicates describe character properties, not source action flags.
        if ":" not in witness:
            add_unique(tags, witness)
    return tags


def role_bindings(role, support):
    if role == "doer":
        return {"doer": "main", "receiver": support}
    if role == "receiver":
        return {"doer": support, "receiver": "main"}
    if role == "self":
        return {"doer": "main", "receiver": "main"}
    if role == "solo":
        return {"doer": "main"}
    return {}


def source_branches(source):
    if "anyOf" not in source:
        yield []
    else:
        for branch in source["anyOf"]:
            for path in source_branches(branch):
                yield [branch, *path]


def preview_action_template(data):
    """Show manual/default option text without resolving actor tokens or rolling inline alternatives."""
    text = data.get("prompt_template", data["resolved_prompt"])
    groups = {group: metadata["choices"] for group, metadata in data.get("role_options", {}).items()}
    chosen = select_options({"options": groups}, data.get("selected_options", {}))
    for group in groups:
        choice = chosen.get(group, {})
        if "anyOf" in choice:
            if not data.get("actor_values") and data.get("scene_context") is None:
                continue
            valid, effective = resolve_choice(choice, data)
            if valid is None:
                continue
            choice = effective if valid else {}
        text = text.replace(f"$option.{group}$", ", ".join(choice.get("prompts", [])))
    return text


def resolve_action_data(rule, options, actor_groups, seed=0, main_role="auto", scene_rules=None):
    require(type(seed) is int and 0 <= seed <= 2**63 - 1,
            "seed must be an integer in the range 0..2^63-1")
    require(isinstance(main_role, str) and main_role in MAIN_ROLES,
            f"main_role must be one of {MAIN_ROLES!r}")
    registered_roles = list(dict.fromkeys([*validate_roles(rule), "actDoer", "actReceiver"]))
    role_options = {group: {"preference": options.get(group, "auto"), "choices": deepcopy(choices)}
                    for group, choices in rule.get("options", {}).items()}
    # Evidence selects a source branch, not final actor-dependent prompts. Preserve all options for final evaluation.
    immediate_rule = dict(rule, options={group: choices for group, choices in rule.get("options", {}).items()
                                         if not any(role_dependent(choice, registered_roles) for choice in choices)})
    chosen = select_options(immediate_rule, options)
    prompts = list(rule.get("prompts", []))
    for group in rule.get("options", {}):
        if group in role_options:
            prompts.append(f"$option.{group}$")
        elif group in chosen:
            prompts.extend(chosen[group].get("prompts", []))
    option_records = [record for metadata in role_options.values() for choice in metadata["choices"]
                      for record in [choice, *choice.get("anyOf", [])]]
    deferred_prompts = [prompt for record in option_records for prompt in record.get("prompts", [])]
    for prompt in [*prompts, *deferred_prompts]:
        parse_inline(prompt)

    sources = rule.get("actions", [])
    require(isinstance(sources, list) and sources, f"{rule['id']}: actions must be a nonempty array")
    slots = [source.get("id") for source in sources]
    require(all(isinstance(slot, str) and slot for slot in slots) and len(set(slots)) == len(slots),
            f"{rule['id']}: action slot IDs must be nonempty and unique")
    composite = len(sources) > 1
    bindings = {"main": "main"}
    actor_tags = {"main": []}
    slot_roles = []
    source_scopes = {}
    role_groups = {role: [] for role in registered_roles}
    global_tags = []
    root_tags = rule.get("actionTags", [])
    require(isinstance(root_tags, list) and all(isinstance(tag, str) and tag.strip() for tag in root_tags),
            f"{rule['id']}: actionTags must be an array of nonempty strings")
    for source_index, source in enumerate(sources):
        slot = source["id"]
        support = f"{slot}.support" if composite else "support"
        selected_source = None
        for path in source_branches(source):
            role = source.get("mainRole", "auto")
            mapping = dict(source.get("roles", {}))
            for branch in path:
                role = branch.get("mainRole", role)
                mapping.update(branch.get("roles", {}))
            require(role in MAIN_ROLES, f"{slot}: invalid mainRole {role!r}")
            if not composite and main_role != "auto":
                if role != "auto" and role != main_role:
                    continue
                role = main_role
            tags = source_evidence([rule, source, *path, *chosen.values()], slot, slots)
            if tags is not None:
                selected_source = role, tags, mapping
                break
        require(selected_source is not None,
                f"{slot}: selected options/main_role contradict every source branch")
        role, tags, mapping = selected_source
        source_scopes[slot] = mapping
        for tag in root_tags:
            add_unique(tags, tag)
        for tag in tags:
            add_unique(global_tags, tag)
            add_unique(actor_tags["main"], tag)
        if role not in ("self", "solo"):
            bindings[support] = support
            actor_tags[support] = list(tags)
        elif role == "self":
            bindings[support] = "main"
        if role != "solo":
            bindings[f"{slot}.support"] = bindings[support]
        if composite:
            if source_index == 0 and role != "solo":
                bindings["support"] = bindings[support]
        else:
            # Keep an explicit support alias only when a distinct support exists.
            bindings.update(role_bindings(role, support))
            if role == "auto":
                bindings.update({"doer": "doer", "receiver": "receiver"})
                actor_tags["doer"] = list(tags)
                actor_tags["receiver"] = list(tags)
            else:
                for alias in ("doer", "receiver"):
                    if alias in bindings:
                        actor_tags[alias] = list(actor_tags[bindings[alias]])
        actual_roles = role_bindings(role, support)
        if role == "auto":
            actual_roles = ({"doer": "main", "receiver": support} if composite
                            else {"doer": "doer", "receiver": "receiver"})
        slot_roles.append(actual_roles)
        for side, group in mapping.items():
            if side in actual_roles and actual_roles[side] not in role_groups[group]:
                role_groups[group].append(actual_roles[side])
    if composite:
        # Like Unity's action renderer, unqualified roles belong to the first bound source.
        bindings.update(slot_roles[0])

    source_bindings = dict(bindings)
    source_template = ", ".join(text.strip() for text in prompts if text.strip())
    # Unqualified game sides refer to the first source, just as in the game renderer.
    # Explicit main/support/source-slot aliases retain their narrower advanced scope.
    scope = source_scopes[slots[0]]

    def compile_prompt(text):
        def replace(match):
            side = match.group(1) or match.group(3)
            suffix = match.group(2) if match.group(1) else "shortTag"
            return f"${scope.get(side, side)}.{suffix}$"

        return re.sub(r"\$(doer|receiver)\.([^$]*)\$|\{(doer|receiver)\}", replace, text)

    prompts = [compile_prompt(text) for text in prompts]
    for record in option_records:
        for field in ("prompts", "scenePrompts"):
            if field in record:
                record[field] = [compile_prompt(text) for text in record[field]]
    scene_prompts = [compile_prompt(text) for text in rule.get("scenePrompts", [])]
    for text in [*scene_prompts, *(text for record in option_records for text in record.get("scenePrompts", []))]:
        parse_inline(text)
        require("$" not in TOKEN.sub("", text), "Unclosed actor token in scene prompt")
    deferred_prompts = [prompt for record in option_records for prompt in record.get("prompts", [])]
    bindings.update({"doer": "doer", "receiver": "receiver"})

    requests = {}
    unresolved = []

    def consume_token(match):
        token = match.group(1)
        alias, separator, group = token.rpartition(".")
        if alias == "option":
            require(group in role_options, f"Undeclared deferred option ${token}$")
            return match.group(0)
        if alias in role_groups:
            require(group and not any(char.isspace() or char in ",{}" for char in group),
                    f"Invalid role token ${token}$")
            if group not in TEXT_SELECTORS and group in actor_groups:
                for binding in role_groups[alias]:
                    add_unique(requests.setdefault(binding, []), group)
            return match.group(0)
        require(separator and alias in bindings,
                f"Unknown or ambiguous actor alias in ${token}$; use main or a source-slot support alias")
        if group in TEXT_SELECTORS:
            return match.group(0)
        require(group in actor_groups, f"Unknown actor prompt group {group!r} in ${token}$")
        binding = bindings[alias]
        add_unique(requests.setdefault(binding, []), group)
        if binding in ("doer", "receiver"):
            add_unique(unresolved, binding)
        return match.group(0)

    for text in [*prompts, *deferred_prompts]:
        require("$" not in TOKEN.sub("", text), "Unclosed deferred actor token in inline prompt")
        TOKEN.sub(consume_token, text)
    # Do not split or deduplicate inside expressions: that can remove tags from sibling alternatives.
    prompt = ", ".join(text.strip() for text in prompts if text.strip())
    data = {"version": 1, "token_format": "actor_placeholders_v1", "action_id": rule["id"], "seed": seed, "main_role": main_role,
            "node_role_mode": "logical", "source_template": source_template,
            "source_bindings": source_bindings, "source_role_scopes": source_scopes,
            "template_unexpanded": True,
            "selected_options": {group: options.get(group, "auto") for group in rule.get("options", {})},
            "source_role_groups": role_groups,
            "role_groups": {role: [] for role in dict.fromkeys([*registered_roles, "actDoer", "actReceiver"])},
             "role_options": role_options, "actor_values": {}, "actor_registrations": {}, "actor_short_tags": {},
              "scene_rules": deepcopy(scene_rules or {}),
             "scene_prompts": scene_prompts,
            "role_body_groups": list(actor_groups),
            "prompt_template": prompt, "resolved_prompt": prompt, "action_tags": global_tags,
            "root_action_tags": deepcopy(root_tags), "source_rule": deepcopy(rule),
            "actor_requests": requests, "actor_action_tags": actor_tags,
             "bindings": bindings, "unresolved_bindings": unresolved}
    return data
