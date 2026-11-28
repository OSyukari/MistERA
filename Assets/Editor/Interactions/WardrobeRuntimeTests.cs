using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

/// <summary>Run after loading a campaign. Uses detached characters, inventories and slot tables; never changes campaign objects.</summary>
public static class WardrobeRuntimeTests
{
    const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;
    static void Set(object target, string field, object value) => target.GetType().GetField(field, Fields).SetValue(target, value);
    static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    [MenuItem("Tools/Wardrobe/Check Selection And Inventory Rules")]
    public static void Run()
    {
        Require(scr_System_CampaignManager.current != null && scr_System_Serializer.current != null, "Load a campaign before running wardrobe checks.");
        var c = ScriptableObject.CreateInstance<Character_Trainable>();
        try
        {
            c.hideFlags = HideFlags.HideAndDontSave;
            Set(c, "referenceID", 900093);
            var f = new Character_Factions();
            Set(f, "ownerPointer", c); Set(c, "factionManager", f);
            var home = Faction("wardrobe_test_home", true);
            var trip = Faction("wardrobe_test_trip", false);
            var venue = Faction("wardrobe_test_gym", false);
            Set(f, "Faction_Home_Cache", home);
            c.Appearance = Humanoid_GenderAppearance.Female;
            Set(c, "_actorKeywords", new List<string> { "humanrace" });
            c.Relationships = new RelationshipManager();
            var personality = new Character_Personality { dressCodeID = "" };
            Set(c.Relationships, "_personality", personality);
            c.Body = new Character_Body();
            var torso = Part(BodyPartEquipSlot.Torso);
            var lower = Part(BodyPartEquipSlot.Lower);
            Set(c.Body, "body", new List<BodyPart_Instance> { torso, lower });
            c.Inventory = new CharacterInventory();
            var wardrobe = new Character_Wardrobe();
            Set(wardrobe, "owner", c); Set(c, "wardrobe", wardrobe);

            var swimming = new RecreationActivity
            {
                ID = "swimming",
                outfit = new OutfitSet { pieces = new List<OutfitPiece> { new OutfitPiece("clothing_jp_dkn_swim_trunks"), new OutfitPiece("clothing_jp_dkn_rash_guard") },
                    overrideSkin = true, overrideInner = true, overrideOuter = true },
                wardrobeRequirements = new OutfitRequirements { slotTags = new Dictionary<BodyPartEquipSlot, List<string>>
                    { [BodyPartEquipSlot.Lower] = new List<string> { "swimwear" } }, personalitySlotTags = new List<string> { "swimwear" } }
            };
            const int day = 100;
            Require(!wardrobe.TryActivityOutfit(venue, swimming, null, day, out _, out _), "Player home received automatic activity clothes.");
            Set(f, "Faction_Home_Temporary_Cache", trip);
            Require(wardrobe.TryActivityOutfit(venue, swimming, null, day, out var supplied, out _) && supplied != null,
                "NPC-managed temporary home did not enable supplied clothes.");
            trip.ManagerRefs.Add(0);
            Require(!wardrobe.TryActivityOutfit(venue, swimming, null, day, out _, out _), "Player-managed temporary home enabled free clothes.");
            Set(f, "Faction_Home_Temporary_Cache", null);

            var stock = new[] { new Item_Instance("clothing_jp_dkn_swim_trunks", ""), new Item_Instance("clothing_jp_dkn_rash_guard", "") };
            for (int i = 0; i < stock.Length; i++)
            {
                stock[i].RegisterItem(900094 + i); // Assigns the detached object's ID only; no campaign registration.
                home.Inventory.Contents.Add(stock[i]);
            }
            string poolKey = wardrobe.NewPoolKey();
            var draft = wardrobe.BeginEdit(poolKey, out _);
            draft.pieces.AddRange(stock.Select(item => new OutfitPiece(item.BaseID)));
            Require(wardrobe.ValidateDraft(draft, out var validity), "Valid swimwear rejected: " + validity);
            var trunks = draft.Clone();
            trunks.pieces = new List<OutfitPiece> { new OutfitPiece("clothing_jp_dkn_swim_trunks") };
            Require(!wardrobe.ValidateDraft(trunks, out _), "An uncovered torso satisfied the female appearance default.");
            c.Appearance = Humanoid_GenderAppearance.Male;
            Require(wardrobe.ValidateDraft(trunks, out _), "The male appearance lower-only default rejected swim trunks.");
            c.Appearance = Humanoid_GenderAppearance.Inhuman;
            var naked = draft.Clone();
            naked.pieces.Clear();
            Require(wardrobe.ValidateDraft(naked, out _), "Inhuman/animal appearance default rejected uncovered outfit.");
            c.Appearance = Humanoid_GenderAppearance.Female;
            var conflict = draft.Clone(); conflict.pieces.Add(conflict.pieces[0].Clone());
            Require(!wardrobe.ValidateDraft(conflict, out _), "Conflicting pieces were silently accepted.");
            c.Body.Body.Add(Part(BodyPartEquipSlot.Lower));
            Require(!wardrobe.ValidateDraft(draft, out _), "Coverage of one lower slot hid another uncovered lower slot.");
            c.Body.Body.RemoveAt(c.Body.Body.Count - 1);
            var before = SlotSnapshot(torso);
            Require(wardrobe.SavePlayerOutfit(draft, out _), "Could not save an inventory-backed outfit.");
            Require(stock.All(item => item.Count == 1) && home.Inventory.Contents.Count == 2, "Saving consumed inventory stock.");
            Require(wardrobe.TryActivityOutfit(venue, swimming, null, day, out var chosen, out _) && chosen.key == poolKey,
                "Valid pool fallback was not selected.");
            Require(wardrobe.TryActivityOutfit(venue, swimming, null, day, out var repeated, out _) && repeated.key == chosen.key,
                "Repeated selection rerolled the outfit.");
            Require(before == SlotSnapshot(torso), "Validation mutated live equipment slots.");

            var visit = new RecreationBooking { day = day, startHour = 23, hours = 3, factionID = venue.ID };
            Set(visit, "_faction", venue);
            Set(visit, "_activity", swimming);
            var prepare = typeof(Character_Wardrobe).GetMethod("PrepareVisitOutfit", Fields);
            Require((bool)prepare.Invoke(wardrobe, new object[] { visit }), "Departure failed to choose a usable pool outfit.");
            var visitOutfitField = typeof(RecreationBooking).GetField("visitOutfit", Fields);
            var visitOutfit = (OutfitSet)visitOutfitField.GetValue(visit);
            Require(visitOutfit?.key == poolKey && visit.Covers(day, 23) && visit.Covers(day + 1, 1) && !visit.Covers(day + 1, 2),
                "The booking did not carry the chosen outfit across midnight.");

            var direct = draft.Clone();
            direct.key = OutfitUtility.ActivityKey(venue.ID, swimming);
            Require(wardrobe.SavePlayerOutfit(direct, out _), "Could not save a direct activity assignment.");
            Require(wardrobe.TryActivityOutfit(venue, swimming, null, day, out chosen, out _) && chosen.key == direct.key,
                "A valid direct assignment did not take priority over the pool.");
            Set(f, "Faction_Home_Temporary_Cache", trip);
            trip.ManagerRefs.Clear();
            Require(wardrobe.TryActivityOutfit(venue, swimming, null, day, out chosen, out _) && chosen.key == direct.key && chosen.playerEdited,
                "Temporary-home supplied clothes replaced a valid player assignment.");
            Set(f, "Faction_Home_Temporary_Cache", null);

            // An invalid explicit assignment must fall back to the pool, then block once no usable pool remains.
            var assigned = new OutfitSet { key = OutfitUtility.ActivityKey(venue.ID, swimming), overrideSkin = true, overrideInner = true, overrideOuter = true };
            Require(wardrobe.SavePlayerOutfit(assigned, out _), "Incomplete drafts should be savable.");
            Require(wardrobe.TryActivityOutfit(venue, swimming, null, day, out chosen, out _) && chosen.key == poolKey,
                "Invalid direct assignment prevented a valid pool fallback.");
            home.Inventory.Contents.Clear();
            Require(wardrobe.TryActivityOutfit(venue, swimming, null, day, out _, out _), "Selling originals invalidated saved clothing.");
            var pruned = wardrobe.BeginEdit(poolKey, out int removed);
            Require(removed == 2 && pruned.pieces.Count == 0, "Reopening an edit retained unavailable clothing.");
            Require(wardrobe.GetPlayerOutfit(poolKey).pieces.Count == 2, "Beginning/canceling an edit changed the saved outfit.");
            Require(wardrobe.SavePlayerOutfit(pruned, out _), "Could not save a pruned draft.");
            Require(!wardrobe.TryActivityOutfit(venue, swimming, null, day, out _, out _), "Activity remained available without a valid outfit.");
            Require((bool)prepare.Invoke(wardrobe, new object[] { visit }) && ReferenceEquals(visitOutfitField.GetValue(visit), visitOutfit),
                "An edit rerolled the outfit for a visit already prepared.");
            var restoredVisit = JsonConvert.DeserializeObject<RecreationBooking>(JsonConvert.SerializeObject(visit));
            var restoredOutfit = (OutfitSet)visitOutfitField.GetValue(restoredVisit);
            Require(restoredOutfit?.key == poolKey && restoredOutfit.pieces.Count == 2 && (bool)prepare.Invoke(wardrobe, new object[] { restoredVisit }),
                "Saving and loading lost the visit's chosen outfit.");

            string json = JsonConvert.SerializeObject(wardrobe);
            var loaded = JsonConvert.DeserializeObject<Character_Wardrobe>(json);
            Set(loaded, "owner", c);
            Require(loaded.PlayerOutfits.Count == 2 && loaded.PlayerOutfits.All(o => o.playerEdited), "Player wardrobe provenance did not survive saving.");
            Require(!loaded.TryActivityOutfit(venue, swimming, null, day, out _, out _), "Loading changed invalid outfit eligibility.");

            // Personality dressCodeID inheritance & fallback: empty inherits Fallback, then auto-resolves defaultDressCodes; cycles terminate.
            var child = new Character_Personality();
            Set(child, "fallbackRef", personality);
            personality.dressCodeID = "dresscode_human_male";
            Require(child.EffectiveDressCodeID == "dresscode_human_male" && child.ExplicitDressCode?.ID == "dresscode_human_male",
                "Empty dressCodeID did not inherit from Fallback.");
            child.dressCodeID = "dresscode_none";
            Require(child.EffectiveDressCodeID == "dresscode_none" && child.ExplicitDressCode?.ID == "dresscode_none",
                "Explicit dressCodeID did not override Fallback.");
            child.dressCodeID = ""; personality.dressCodeID = "";
            Set(personality, "fallbackRef", child);
            Require(child.EffectiveDressCodeID == "" && child.ExplicitDressCode == null, "Cyclic fallback did not terminate.");

            // Activity conditionalRules: replaceBaseDressCode=true can lower female base decency, while false adds on top.
            var relaxSwimming = new RecreationActivity
            {
                ID = "relax_swim",
                wardrobeRequirements = new OutfitRequirements
                {
                    conditionalRules = new List<DressCodeRule>
                    {
                        new DressCodeRule
                        {
                            ID = "relax_female",
                            priority = 10,
                            requireTags_all = new List<string> { "appearance_female" },
                            dressCodeID = "dresscode_swimwear_male",
                            replaceBaseDressCode = true
                        }
                    }
                }
            };
            Set(c.Relationships, "_personality", new Character_Personality());
            c.Appearance = Humanoid_GenderAppearance.Female;
            var trunksOnly = new OutfitSet { key = OutfitUtility.ActivityKey(venue.ID, relaxSwimming), playerEdited = true,
                overrideSkin = true, overrideInner = true, overrideOuter = true,
                pieces = new List<OutfitPiece> { new OutfitPiece("clothing_jp_dkn_swim_trunks") } };
            home.Inventory.Contents.Add(stock[0]);
            Require(wardrobe.SavePlayerOutfit(trunksOnly, out _), "Could not save trunks-only test outfit.");
            Require(wardrobe.TryActivityOutfit(venue, relaxSwimming, null, day, out _, out _),
                "replaceBaseDressCode=true did not allow lowered coverage.");
            relaxSwimming.wardrobeRequirements.conditionalRules[0].replaceBaseDressCode = false;
            Require(!wardrobe.TryActivityOutfit(venue, relaxSwimming, null, day, out _, out _),
                "replaceBaseDressCode=false bypassed base female dress code.");

            // Clothing intendedFor: advisory audience metadata (shown in tooltip, does not forbid wearing).
            var onePieceItem = new Item_Instance("clothing_jp_dkn_swimsuit_onepiece", "");
            Require(onePieceItem.Comp_Equippable != null && onePieceItem.Comp_Equippable.intendedFor != null
                && onePieceItem.Comp_Equippable.intendedFor.IsSpecified, "One-piece swimsuit is missing intendedFor metadata.");
            Require(!string.IsNullOrEmpty(onePieceItem.Comp_Equippable.Tooltip), "Equip tooltip was empty.");
            c.Appearance = Humanoid_GenderAppearance.Female;
            Require(onePieceItem.Comp_Equippable.IsIntendedFor(c) && !onePieceItem.Comp_Equippable.IsUnusualFor(c),
                "Female appearance should match female-intended swimsuit.");
            c.Appearance = Humanoid_GenderAppearance.Male;
            Require(!onePieceItem.Comp_Equippable.IsIntendedFor(c) && onePieceItem.Comp_Equippable.IsUnusualFor(c),
                "Male appearance should be flagged as unusual for female-intended swimsuit.");
            var onePieceDraft = new OutfitSet
            {
                key = wardrobe.NewPoolKey(), playerEdited = true,
                overrideSkin = true, overrideInner = true, overrideOuter = true,
                pieces = new List<OutfitPiece> { new OutfitPiece("clothing_jp_dkn_swimsuit_onepiece") }
            };
            Require(wardrobe.ValidateDraft(onePieceDraft, out var opReason),
                "intendedFor should not forbid wearing a swimsuit: " + opReason);

            // Humanoid clothing should reject animals via requireActorTags_any.
            Set(c, "_actorKeywords", new List<string> { "animal", "dog" });
            c.Appearance = Humanoid_GenderAppearance.Inhuman;
            Require(!wardrobe.ValidateDraft(onePieceDraft, out _), "Animal was allowed to wear humanoid swimsuit.");

            Debug.Log("Wardrobe selection, temporary-home ownership, inventory, save/load, dress code and intendedFor checks passed.");
        }
        finally { UnityEngine.Object.DestroyImmediate(c); }
    }

    static Manageable Faction(string id, bool player)
    {
        var faction = new Manageable { ID = id };
        Set(faction, "_managerRefs", player ? new List<int> { 0 } : new List<int>());
        Set(faction, "_inventory", new FactionInventory());
        return faction;
    }
    static BodyPart_Instance Part(BodyPartEquipSlot slot)
    {
        var part = new BodyPart_Instance();
        Set(part, "basePointer", new BodyPart_Base { AvailableSlots = new List<BodyPartEquipSlot> { slot } });
        return part;
    }
    static string SlotSnapshot(BodyPart_Instance part) => JsonConvert.SerializeObject(new[]
    {
        typeof(BodyPart_Instance).GetField("contentsIndex", Fields).GetValue(part),
        typeof(BodyPart_Instance).GetField("coversIndex", Fields).GetValue(part)
    });
}
