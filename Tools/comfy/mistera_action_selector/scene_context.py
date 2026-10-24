"""Pure, registration-identity scene facts and independent composition/pair rules."""

from copy import deepcopy
import random
import re

from .roles import actor_identity


MANUAL_ROLES = {"doer", "receiver", "actDoer", "actReceiver"}


def _unique(values):
    result, seen = [], set()
    for value in values:
        if isinstance(value, str) and value.strip() and value.casefold() not in seen:
            result.append(value)
            seen.add(value.casefold())
    return result


def _contains(values, tag):
    return tag.casefold() in {value.casefold() for value in values}


def _clause(clause, has):
    return not clause or any(has(term.strip()) for term in clause.split("|"))


def _block(block, has):
    return (all(_clause(c, has) for c in block.get("all", []) or [])
            and (not block.get("any") or any(_clause(c, has) for c in block["any"]))
            and not any(c and _clause(c, has) for c in block.get("none", []) or []))


def _property(actor, tag):
    if actor is None:
        return False
    tags = actor.get("actor_tags", actor.get("tags", []))
    return all(bool(term.strip()) and term.strip() != "!" and
               (not _contains(tags, term.strip()[1:]) if term.strip().startswith("!")
                else _contains(tags, term.strip())) for term in tag.split("&"))


def pair_conditions_match(rule, pair):
    """Only pairKind/main/support/differs; None means a required pair is unavailable."""
    guarded = any(rule.get(key) is not None and rule.get(key) != "" and rule.get(key) != []
                  for key in ("pairKind", "main", "support", "differs"))
    if pair is None:
        return None if guarded else True
    kind = str(pair.get("kind", pair.get("pairKind", ""))).casefold()
    if rule.get("pairKind") and str(rule["pairKind"]).casefold() != kind:
        return False
    main, support = pair.get("main"), pair.get("support")
    for key, actor in (("main", main), ("support", support)):
        if rule.get(key) is not None:
            if actor is None or not _block(rule[key], lambda tag: _property(actor, tag)):
                return False
    if rule.get("differs"):
        if main is None or support is None:
            return False
        if not all(_clause(c, lambda tag: _property(main, tag)) !=
                   _clause(c, lambda tag: _property(support, tag)) for c in rule["differs"]):
            return False
    return True


def has_scene_tag(context, tag):
    """Count predicates take precedence; side properties use the receiver-wins partition."""
    tag = tag.strip()
    name, colon, value = tag.partition(":")
    name = name.casefold()
    counts = {key.casefold(): count for key, count in context.get("counts", {}).items()}
    if colon and name in counts:
        match = re.fullmatch(r"([+-]?\d+)(\+)?", value.strip())
        if not match:
            diagnostic = f"Bad count predicate {tag!r}; expected name:N or name:N+."
            if diagnostic not in context.get("diagnostics", []):
                context.setdefault("diagnostics", []).append(diagnostic)
            return False
        count = int(match[1])
        return counts[name] >= count if match[2] else counts[name] == count
    if colon and name in ("doer", "receiver"):
        return any(_property(actor, value) for actor in context.get("actors", {}).values()
                   if actor.get("is_receiver", False) == (name == "receiver"))
    return (_contains(context.get("tags", []), tag)
            or any(_property(actor, tag) for actor in context.get("actors", {}).values()))


def _pair_has(pair, tag):
    name, colon, value = tag.partition(":")
    if colon and name.casefold() in ("main", "support"):
        return _property(pair.get(name.casefold()), value)
    return (_contains(pair["tags"], tag) or _property(pair["main"], tag)
            or _property(pair["support"], tag))


def _prompts(value):
    if isinstance(value, str):
        return [value] if value.strip() else []
    return [text for text in value or [] if isinstance(text, str) and text.strip()]


def _apply(node, has, rng, extra=lambda rule: True, prefix=""):
    if not isinstance(node, dict) or not _block(node, has) or extra(node) is not True:
        return False, [], []
    chance = node.get("chance", 1)
    if chance < 1 and rng.random() >= chance:
        return False, [], []
    texts = _prompts(node.get("prompts"))
    own = bool(texts)
    picks = [pick for pick in node.get("pick", []) or []
             if pick and pick.get("weight", 1) > 0 and _block(pick, has)
             and extra(pick) is True]
    if picks:
        roll = rng.random() * sum(pick.get("weight", 1) for pick in picks)
        chosen = picks[-1]
        for pick in picks:
            roll -= pick.get("weight", 1)
            if roll < 0:
                chosen = pick
                break
        # A selected empty string still applies, as it does in the game.
        if chosen.get("prompts") is not None:
            texts.extend(_prompts(chosen["prompts"]))
            own = True
    applied, any_child = [], False
    children = node.get("children", []) or []
    for child in children:
        ok, child_texts, child_ids = _apply(child, has, rng, extra, prefix)
        if ok:
            any_child = True
            texts.extend(child_texts)
            applied.extend(child_ids)
            if str(node.get("mode", "all")).casefold() == "first":
                break
    if not own and not any_child and (children or node.get("pick")):
        return False, [], []
    return True, texts, ([prefix + node["id"]] if node.get("id") else []) + applied


def _tokens(texts):
    # Commas inside unexpanded inline choices belong to the choice, not the scene.
    for text in texts:
        depth, parens, brackets, start = 0, 0, 0, 0
        for index, char in enumerate(text):
            if char == "{":
                depth += 1
            elif char == "}":
                depth = max(0, depth - 1)
            elif char == "(":
                parens += 1
            elif char == ")":
                parens = max(0, parens - 1)
            elif char == "[":
                brackets += 1
            elif char == "]":
                brackets = max(0, brackets - 1)
            elif char == "," and depth == parens == brackets == 0:
                if text[start:index].strip():
                    yield text[start:index].strip()
                start = index + 1
        if text[start:].strip():
            yield text[start:].strip()


def _count_tags(context):
    counts = context["counts"]

    def number(n, noun):
        return ("1" + noun if n == 1 else ("6+" if n >= 6 else str(n)) + noun + "s")

    result = []
    for noun, n in (("girl", counts["girl"] + counts["futa"]), ("boy", counts["boy"])):
        if n:
            result.append(number(n, noun))
    other = 0
    for actor in context["actors"].values():
        tag = actor["short_tag"]
        if tag.casefold() in ("girl", "boy", "futa"):
            continue
        if tag.casefold() == "other":
            other += 1
        elif not _contains(result, tag):
            result.append(tag)
    if other:
        result.append(number(other, "other"))
    return result


def render_composition(data, context):
    """Render only prompt_composition_rules, retaining all inline alternatives."""
    ok, texts, applied = _apply(data["scene_rules"].get("prompt_composition_rules"),
                               lambda tag: has_scene_tag(context, tag),
                               random.Random(data.get("seed", 0)), prefix="composition:")
    context["applied_rules"] = _unique(context.get("applied_rules", []) + applied)
    counts = _count_tags(context)
    if not ok:
        return ", ".join(counts)
    result = []
    for token in _tokens(texts):
        if token.casefold() == "{count}":
            result.extend(counts)
        elif re.fullmatch(r"\{[^{}|]+\}", token):
            message = f"Composition placeholder {token!r} is unsupported; only {{count}} is used."
            if message not in context["diagnostics"]:
                context["diagnostics"].append(message)
        else:
            result.append(token)
    return ", ".join(_unique(result))


def _aliases(data, registration):
    aliases = set(registration.get("roles", []))
    binding = registration.get("actor_binding", "")
    if binding:
        aliases.add(binding)
        if binding not in MANUAL_ROLES:
            bindings = data.get("source_bindings", data.get("bindings", {}))
            target = bindings.get(binding, binding)
            aliases.update(alias for alias, value in bindings.items()
                           if alias not in MANUAL_ROLES and value == target)
    return aliases


def build_scene_context(data):
    """Freeze actual actors, build unambiguous edges, then derive pair/source flags in order."""
    rules = data.get("scene_rules", {})
    derived = rules.get("prompt_derived_tags", []) or []
    pair_derived = rules.get("prompt_pair_derived_tags", []) or []
    synthetic = {tag.casefold() for rule in [*derived, *pair_derived]
                 for tag in rule.get("add", [])}

    def clean(tags):
        return _unique(tag for tag in tags if tag.casefold() not in synthetic)

    context = {"actors": {}, "counts": {}, "tags": [], "pairs": [], "pair_prompts": [],
               "diagnostics": [], "primary_pair": None, "applied_rules": []}
    actors, counts = context["actors"], context["counts"]
    aliases_by_id = {}
    for registration_id, registration in data.get("actor_registrations", {}).items():
        identity = str(registration_id)
        record = deepcopy(registration["record"])
        identity_fields = actor_identity(record)
        short = identity_fields["short_tag"]
        aliases = _aliases(data, registration)
        aliases_by_id[identity] = aliases
        manual = set(registration.get("roles", []))
        if registration.get("actor_binding") in MANUAL_ROLES:
            manual.add(registration["actor_binding"])
        receiver = bool(manual & {"receiver", "actReceiver"})
        doer = bool(manual & {"doer", "actDoer"})
        props = _unique([*record.get("actor_tags", []), short])
        actors[identity] = {"id": identity, "identity": identity,
                            "character_id": registration.get("character_id"), "record": record,
                            **identity_fields, "tags": props[:], "actor_tags": props,
                            "roles": list(registration.get("roles", [])),
                            "actor_binding": registration.get("actor_binding", ""),
                            "is_receiver": receiver, "is_doer": doer}
        key = short.casefold()
        counts[key] = counts.get(key, 0) + 1
    for gender in ("girl", "boy", "futa"):
        counts.setdefault(gender, 0)
    counts.update(actors=len(actors), receivers=sum(a["is_receiver"] for a in actors.values()))
    counts["doers"] = len(actors) - counts["receivers"]
    counts["other"] = len(actors) - sum(counts[key] for key in ("girl", "boy", "futa"))
    base = clean([*data.get("root_action_tags", []), *data.get("action_tags", [])])
    context["tags"] = base[:]
    actor_actions = data.get("actor_action_tags", {})
    for identity, actor in actors.items():
        extra = []
        for alias in aliases_by_id[identity]:
            target = data.get("bindings", {}).get(alias, alias)
            extra.extend(actor_actions.get(target, []))
            extra.extend(actor_actions.get(alias, []))
        actor["context_tags"] = clean(extra)
        context["tags"] = _unique(context["tags"] + actor["context_tags"])

    def members(alias):
        return [a for identity, a in actors.items() if alias in aliases_by_id[identity]]

    explicit_main = members("main")
    main = explicit_main[0] if len(explicit_main) == 1 else None
    seen = {}

    def add_pair(anchor, support, doer, receiver, tags, slot=None):
        kind = "solo" if support is None else "self" if support["id"] == anchor["id"] else "distinct"
        key = (kind, tuple(sorted([anchor["id"], support["id"]] if support else [anchor["id"]])))
        pair = seen.get(key)
        if pair is None:
            pair = {"kind": kind, "pairKind": kind, "main": anchor, "support": support,
                    "tags": [], "action_tags": [], "actions": [], "rules": []}
            context["pairs"].append(pair)
            seen[key] = pair
        tags = clean(tags + anchor["context_tags"] + (support["context_tags"] if support else []))
        pair["tags"] = _unique(pair["tags"] + tags)
        pair["action_tags"] = _unique(pair["action_tags"] + tags)
        # Explicit self has no source receiver in the game's side-local predicates.
        action = {"doer": doer, "receiver": None if kind == "self" else receiver,
                  "self_action": kind == "self", "tags": tags[:], "slot": slot}
        if action not in pair["actions"]:
            pair["actions"].append(action)

    advanced = False
    source_bindings = data.get("source_bindings", data.get("bindings", {}))
    sources = data.get("source_rule", {}).get("actions", [])

    def target_members(target):
        if target is None:
            return []
        return [actor for identity, actor in actors.items() if any(
            alias not in MANUAL_ROLES and source_bindings.get(alias, alias) == target
            for alias in aliases_by_id[identity])]

    if main is not None:
        for index, source in enumerate(sources or [{"id": "support"}]):
            slot = source["id"]
            support_alias = slot + ".support" if sources else "support"
            supports = members(support_alias)
            if not supports and len(sources) <= 1:
                supports = members("support")
            role = source.get("mainRole", data.get("main_role", "auto"))
            if role == "auto" and len(sources) == 1:
                role = data.get("main_role", "auto")
            if source_bindings.get(support_alias) == source_bindings.get("main", "main"):
                supports = [main]
                role = "self"
            if role == "self" and not supports:
                supports = [main]
            if len(supports) > 1:
                context["diagnostics"].append(f"Ambiguous advanced support {support_alias!r}; no guessed edge.")
                continue
            if not supports and role != "solo":
                continue
            support = supports[0] if supports else None
            doer, receiver = (support, main) if role == "receiver" else (main, support)
            scope = data.get("source_role_scopes", {}).get(slot, {})
            mapped = [members(scope.get(side, "")) for side in ("doer", "receiver")]
            if index == 0:
                # Unqualified raw source bindings belong only to the first source.
                raw = [target_members(source_bindings.get(side)) for side in ("doer", "receiver")]
                if all(len(side) == 1 for side in raw):
                    mapped = raw
            if (support is not None and all(len(side) == 1 for side in mapped)
                    and {mapped[0][0]["id"], mapped[1][0]["id"]} == {main["id"], support["id"]}):
                doer, receiver = mapped[0][0], mapped[1][0]
            elif role not in ("receiver", "doer", "solo", "self"):
                doer = receiver = None
                context["diagnostics"].append(f"Source {slot!r} has unknown direction; source derivation skipped.")
            target = source_bindings.get(support_alias, support_alias)
            tags = clean([*data.get("root_action_tags", []), *actor_actions.get(target, [])])
            if len(sources) <= 1:
                tags = _unique(tags + base)
            add_pair(main, support, doer, receiver, tags, slot)
            advanced = True
    if not advanced:
        doers = [a for a in actors.values() if a["is_doer"]]
        receivers = [a for a in actors.values() if a["is_receiver"]]
        if len(doers) == len(receivers) == 1 and doers[0]["id"] == receivers[0]["id"]:
            add_pair(receivers[0], receivers[0], doers[0], receivers[0], base)
        elif len(receivers) == 1 or len(doers) == 1:
            anchor = receivers[0] if len(receivers) == 1 else doers[0]
            others = doers if len(receivers) == 1 else receivers
            for other in others:
                if other["id"] == anchor["id"]:
                    continue
                primary = main if main and main["id"] in (anchor["id"], other["id"]) else anchor
                support = other if primary["id"] == anchor["id"] else anchor
                doer, receiver = (other, anchor) if len(receivers) == 1 else (anchor, other)
                add_pair(primary, support, doer, receiver, base)
        elif len(doers) > 1 and len(receivers) > 1:
            context["diagnostics"].append("Ambiguous many-to-many manual roles; original source endpoints are unavailable. No Cartesian pairs or relative flags were inferred.")
        if not context["pairs"] and len(actors) == 1:
            actor = next(iter(actors.values()))
            if actor["is_doer"] or actor["is_receiver"] or actor["actor_binding"]:
                add_pair(actor, None, actor, None, base)
    if not context["pairs"] and actors and not context["diagnostics"]:
        context["diagnostics"].append("No unambiguous pair: register both manual sides or unique advanced main/support aliases.")

    def derive(entries, target, has, left, right, pair=None):
        for rule in entries:
            if not rule.get("add") or not _block(rule, has):
                continue
            if (left is None or right is None or left["id"] == right["id"]) and any(
                    tag.casefold() in {"yuri", "interracial", "bestiality", "futa_with_female"}
                    for tag in rule["add"]):
                continue
            if pair_conditions_match({k: v for k, v in rule.items() if k != "differs"}, pair) is not True:
                continue
            if pair is not None and pair["kind"] != "distinct" and rule.get("differs"):
                continue
            if any(rule.get(key) is not None and
                   (actor is None or not _block(rule[key], lambda tag: _property(actor, tag)))
                   for key, actor in (("doer", left), ("receiver", right))):
                continue
            if rule.get("differs") and (left is None or right is None or not all(
                    _clause(c, lambda tag: _property(left, tag)) !=
                    _clause(c, lambda tag: _property(right, tag)) for c in rule["differs"])):
                continue
            target[:] = _unique(target + rule["add"])

    # Pair contexts must never see later source-derived flags.
    for pair in context["pairs"]:
        derive(pair_derived, pair["tags"], lambda tag: _pair_has(pair, tag),
               pair["main"], pair["support"], pair)
    for pair in context["pairs"]:
        for action in pair["actions"]:
            left, right = action["doer"], action["receiver"]
            if left is not None:
                def source_has(tag):
                    name, colon, value = tag.partition(":")
                    if colon and name.casefold() in ("doer", "receiver"):
                        return _property(left if name.casefold() == "doer" else right, value)
                    return _contains(action["tags"], tag) or _property(left, tag) or _property(right, tag)

                derive(derived, action["tags"], source_has, left, right)
            context["tags"] = _unique(context["tags"] + action["tags"])
        ok, texts, ids = _apply(rules.get("prompt_pair_rules"), lambda tag: _pair_has(pair, tag),
                                random.Random(f"{data.get('seed', 0)}:{pair['main']['id']}:{pair['support']['id'] if pair['support'] else ''}"),
                                lambda rule: pair_conditions_match(rule, pair), "pair:")
        pair["rules"] = ids
        context["applied_rules"] = _unique(context["applied_rules"] + ids)
        if ok:
            for text in texts:
                text = re.sub(r"\$(main|support)\.shortTag\$", lambda m: pair[m[1].casefold()]["short_tag"] if pair[m[1].casefold()] else "", text, flags=re.I)
                context["pair_prompts"].extend(_tokens([text]))
    context["pair_prompts"] = _unique(context["pair_prompts"])
    context["primary_pair"] = context["pairs"][0] if context["pairs"] else None
    return context
