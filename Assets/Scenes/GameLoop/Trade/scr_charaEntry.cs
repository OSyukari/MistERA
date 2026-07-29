using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;


public class scr_charaEntry : MonoBehaviour
{
    public scr_HoverableText charaName;
    public scr_SelectableText btn_capture, btn_transfer, btn_nothing, btn_inspectChara;
    public RectTransform rect_charaTreatment, rect_charaBody, rect_charaInventory;
    public RectTransform selfRect;
    public Image selfImage;

    public bool isTeamA;
    public Character_Trainable innerChara = null;
    public menu_Trade parent;
    public Color32 neutral, toggled;

    public scr_itemEntry prefab_item;

    Treatment _treatment = Treatment.none;
    public Treatment TreatmentResult
    {
        get
        {
            return _treatment;
        }
        set
        {
            _treatment= value;
            UpdateUI();
        }
    }

    /// <summary>
    /// The MemberType a Can* check already resolved for the current TreatmentResult, computed exactly
    /// once at button-validation time (Button_SetTreatment's constructor, or InitChara for the default
    /// treatment) and stored here. Resolve() must read this directly rather than re-running Can* checks,
    /// so the outcome acted on always matches what the player actually saw/selected.
    /// </summary>
    MemberType resolvedNewStatus = null;

    public void SetTreatment(Treatment target, MemberType newStatus)
    {
        this.resolvedNewStatus = newStatus;
        this.TreatmentResult = target;
    }
    public enum Treatment
    {
        none,
        transfer,
        capture,
        rescue,
        liberate
    }

    public bool allowDismember = false;
    public bool allowInventory = false;

    List<scr_itemEntry> managedLists = new List<scr_itemEntry>();

    /// <summary>
    /// No Can* calls here by design - eligibility and the resulting MemberType are decided exactly once,
    /// at button-validation time (Button_SetTreatment's constructor, propagated via SetTreatment), and
    /// stored in resolvedNewStatus. Re-checking here risked disagreeing with what the player actually saw.
    /// </summary>
    public void Resolve()
    {
        if (this.TreatmentResult == Treatment.none)
        {
            foreach (var i in managedLists) i.Resolve();
        }
        else
        {
            var targetFaction = this.isTeamA ? this.parent.b : this.parent.a;
            var newStats = resolvedNewStatus;

            if (TreatmentResult == Treatment.liberate && parent.liberateEventID != "")
            {
                if (newStats != null)
                {
                    var ev = new EventInstance(innerChara, parent.liberateEventID, "");
                    ev.Targets.Add("party", isTeamA ? this.parent.b.ManagedChara : this.parent.a.ManagedChara);
                    ev.Targets.Add("liberate", new List<Character_Trainable>() { innerChara });
                    ev.displayOverride = innerChara.DisplayCharaEvent || isTeamA ? parent.b.isPlayerFaction : parent.a.isPlayerFaction;// (innerChara.DisplayCharaEvent || (isTeamA ? parent.a.isPlayerFaction || parent.b.isPlayerFaction));
                    //Debug.Log($"displayOverride? {ev.displayOverride}");
                    scr_UpdateHandler.current.EventHandler.StartEvent(ev, false);
                }
                else
                {
                    Debug.LogError($"Liberating character {innerChara.CallName} failed, no resolved status stored");
                }
                return;
            }
            else if (newStats == null)
            {
                Debug.LogError($"Chara Resolve Trade failed, no resolved status stored for {innerChara.CallName} treatment {TreatmentResult}");
                return;
            }
            else if (targetFaction != null)
            {
                scr_System_CampaignManager.current.MoveCharacterTo(innerChara, targetFaction.MainExit);
                this.innerChara.FactionManager.RemoveFromParty(null, true, true);

                if (TreatmentResult == Treatment.rescue)
                {
                    if (parent.liberateEventID != "")
                    {
                        // kidnapped time
                        var kidnappedtime = targetFaction.FactionOwnerRoot.GetKidnappedDays(innerChara);
                        Debug.Log($"starting rescue event on [{parent.liberateEventID}], kidnapped time {kidnappedtime}");

                        var ev = new EventInstance(innerChara, parent.liberateEventID, "");
                        ev.Targets.Add("party", isTeamA ? this.parent.b.ManagedChara : this.parent.a.ManagedChara);
                        ev.Targets.Add("rescue", new List<Character_Trainable>() { innerChara });
                        ev.displayOverride = innerChara.DisplayCharaEvent || parent.b.isPlayerFaction || parent.a.isPlayerFaction;// (innerChara.DisplayCharaEvent || (isTeamA ? parent.a.isPlayerFaction || parent.b.isPlayerFaction));
                                                                                                                                           //Debug.Log($"displayOverride? {ev.displayOverride}");
                        ev.Parameters.Add("kidnappedDays", kidnappedtime);
                        ev.AppendStrings.Add("MIADays", new List<string>() { $"{(int)Math.Ceiling(kidnappedtime)}" });

                        scr_UpdateHandler.current.EventHandler.StartEventAuto(ev);

                        foreach(var f in innerChara.FactionManager.Factions)
                        {
                            f.FactionOwnerRoot.NotifyCharaRescued(innerChara);
                        }
                    }

                }

                if (targetFaction is Manageable_Party)
                {
                    // party membership itself stays temporary/non-combat (Prisoner if that's what CanRescue/etc.
                    // resolved to, Visitor otherwise) even when newStats represents a fuller status at the
                    // faction-root level - matches CanRescue's documented "temporary rescued status" behavior.
                    var tempStatus = newStats.isPrisoner ? newStats : FactionUtility.MemberType_Rescued;
                    if (this.innerChara.FactionManager.AddToPartyAsTemp(targetFaction, tempStatus, newStats))
                    {
                        // do nothing

                    }
                    else
                    {
                        Debug.LogError($"Chara Resolve Trade failed, add to party unsuccessful");
                    }
                }
                else if (targetFaction is Manageable)
                {
                    if (this.innerChara.FactionManager.Faction_Home == null) this.innerChara.FactionManager.SetHomeFaction((targetFaction as Manageable).ID, newStats);
                    else this.innerChara.FactionManager.SetTempHomeFaction((targetFaction as Manageable).ID, newStats);
                }
                else
                {
                    Debug.LogError($"Chara Resolve Trade failed, undefined faction type");
                }
            }

            this.innerChara.ChangeCurrentJob();
        }
    }

    public void InitChara(menu_Trade canvas, Character_Trainable c, bool isTeamA)
    {
        managedLists.Clear();

        this.parent = canvas;
        this.isTeamA = isTeamA;
        this.innerChara = c;

        var targetInv = isTeamA ? canvas.b.Inventory : canvas.a.Inventory;

        bool displayFullName = c.CallName != c.FirstName;
        if (displayFullName) charaName.SetText($"{c.CallName} {c.FirstName}");
        else charaName.SetText(c.CallName);

        if (isTeamA)
        {
            rect_charaTreatment.gameObject.SetActive(false);
            rect_charaBody.gameObject.SetActive(false);
            rect_charaInventory.gameObject.SetActive(false);
            return;
        }

        this.allowDismember = canvas.allowDismember(c, isTeamA);
        this.allowInventory = canvas.allowInventory(isTeamA);

        if (parent.a.FactionOwnerRoot.isManagedChara(innerChara.RefID))
        {
            var defaultBtn = new Button_SetTreatment(canvas, this.btn_nothing, this, Treatment.rescue);
            canvas.RegisterBtn(this.btn_nothing, defaultBtn);
            // this is the default treatment, not a click - store its already-resolved status directly,
            // same rule as everywhere else: the Can* check happens once, here, not again in Resolve().
            resolvedNewStatus = defaultBtn.ResolvedNewStatus;
            _treatment = Treatment.rescue;
        }
        else
        {
            canvas.RegisterBtn(this.btn_nothing, new Button_SetTreatment(canvas, this.btn_nothing, this, Treatment.none));
            _treatment = Treatment.none;
        }

        canvas.RegisterBtn(this.btn_capture, new Button_SetTreatment(canvas, this.btn_capture, this, Treatment.capture));
        canvas.RegisterBtn(this.btn_transfer, new Button_SetTreatment(canvas, this.btn_transfer, this, Treatment.transfer));
        canvas.RegisterBtn(this.btn_inspectChara, new Button_InspectChara(canvas, this.btn_inspectChara, c));


        foreach(var itemInst in c.Inventory.Contents)
        {
            var entry = Instantiate(prefab_item);
            entry.selfRect.SetParent(this.rect_charaInventory, false);
            entry.InitItem(canvas, itemInst, isTeamA, targetInv, c.Inventory);
            managedLists.Add(entry);
        }

        foreach(var item in c.EquippedItemRefs)
        {
            var itemInst = scr_System_CampaignManager.current.FindItemInstanceByID(item);
            if (itemInst == null) continue;
            var entry = Instantiate(prefab_item);
            entry.selfRect.SetParent(this.rect_charaInventory, false);
            entry.InitItem(canvas, itemInst, isTeamA, targetInv, null, c);
            managedLists.Add(entry);
        }

        neutral = scr_System_CentralControl.current.DisplaySetting.TextColor_neutral.Color;
        toggled = scr_System_CentralControl.current.DisplaySetting.TextColor_toggle.Color;
        UpdateUI();
    }
    protected void UpdateUI()
    {
        bool showInv = TreatmentResult == Treatment.none;
        rect_charaInventory.gameObject.SetActive(showInv);
        rect_charaBody.gameObject.SetActive(showInv);

        charaName.SetColor(showInv ? neutral : toggled);
    }

    public class Button_InspectChara : ButtonValidator, I_ButtonClickable
    {
        new menu_Trade parent;
        scr_SelectableText text;
        Character_Trainable c;
        public Button_InspectChara(menu_Trade parent, scr_SelectableText text, Character_Trainable c) : base(parent)
        {
            this.parent = parent;
            this.text = text;
            this.c = c;
        }
        public override bool IsButtonValid()
        {
            if (!text.gameObject.activeInHierarchy) return false;
            if (c == null) return false;
            return true;
        }
        public void OnClickButton()
        {
            scr_Menu_CharaDetail detail = scr_System_SceneManager.current.LoadCanvasIntoScene(parent, parent.prefab_Canvas_CharaDetail).GetComponent<scr_Menu_CharaDetail>();
            detail.InitializeWithArgument(c.RefID);
        }
    }


    public class Button_SetTreatment : ButtonValidator, I_ButtonClickable
    {
        new menu_Trade parent;
        scr_SelectableText text;
        scr_charaEntry entry;
        Treatment target;
        bool deactivateSelf = false;

        string innerText = "";

        /// <summary>
        /// The Can* outcome for whichever treatment this button ends up representing, computed exactly
        /// once here and never recomputed - entry.Resolve() reads it via SetTreatment, it doesn't
        /// re-check. If the Can* call itself fails, the button deactivates rather than firing on click.
        /// </summary>
        MemberType resolvedNewStatus = null;
        public MemberType ResolvedNewStatus { get { return resolvedNewStatus; } }

        public Button_SetTreatment(menu_Trade parent, scr_SelectableText text, scr_charaEntry entry, Treatment target) : base(parent)
        {
            this.parent = parent;
            this.text = text;
            this.entry = entry;
            this.target = target;

            bool forceRescue = false;
            bool canbeLiberated = false;

            var status = parent.b.FactionOwnerRoot.GetMemberType(entry.innerChara);
            MemberType newstatus = null;

            if (entry.isTeamA || !parent.allowTransfer) deactivateSelf = true;
            else if (parent.a.FactionOwnerRoot.isManagedChara(entry.innerChara.RefID))
            {// if chara in A and not in B -> rescue
                forceRescue = true;
            }
            else if (parent.b.FactionOwnerRoot.CanRescue(entry.innerChara, status, out newstatus) && parent.liberateEventID != "")
            {// not in faction, can be liberate
                canbeLiberated = true;
            }
            // if chara not in A, capture or rescue(transfer)

            if (deactivateSelf)
            {
                // do nothing
            }
            else if (!parent.allowTransfer) deactivateSelf = true;
            else if (target == Treatment.transfer)
            {
                if (forceRescue || !canbeLiberated) deactivateSelf = true;
                else if (!parent.isHostile)
                {
                    // non hostile faction allow transfer -> direct transfer
                    innerText = $"trade_chara_Treatment_{target}";
                }
                else if (canbeLiberated)
                {
                    // hostile faction allow transfer and is prisoner -> rescue
                    this.target = Treatment.liberate;
                    innerText = $"trade_chara_Treatment_{target}";
                }
            }
            else if (target == Treatment.capture)
            {
                if (!parent.allowHostile || forceRescue) deactivateSelf = true;
                else if (!entry.innerChara.isHumanoid) deactivateSelf = true;
                else
                {
                    innerText = $"trade_chara_Treatment_{target}";
                    // variants: hostile capture, non-hostile kidnap/enslave
                }
            }
            else
            {
                innerText = $"trade_chara_Treatment_{target}";
            }

            // Resolve the real Can* outcome for whichever treatment this button ended up as (this.target,
            // which transfer may have just switched to liberate above), exactly once. A failed Can* check
            // deactivates the button just like the other eligibility checks above. Treatment.none needs no
            // Can* check at all - it's always valid (Resolve() short-circuits on it before ever touching
            // resolvedNewStatus) - so it's excluded from this gate rather than falling into a "default:
            // fail" case.
            if (!deactivateSelf && this.target != Treatment.none)
            {
                var targetFaction = entry.isTeamA ? parent.b : parent.a;
                var prevStatus = entry.innerChara.FactionManager.CurrentActiveParty == null
                    ? FactionUtility.MemberType_Prisoner
                    : entry.innerChara.FactionManager.CurrentActiveParty.GetMemberType(entry.innerChara);

                bool success;
                switch (this.target)
                {
                    case Treatment.rescue: success = targetFaction.FactionOwnerRoot.CanRescue(entry.innerChara, prevStatus, out resolvedNewStatus); break;
                    case Treatment.transfer: success = targetFaction.FactionOwnerRoot.CanTransfer(entry.innerChara, prevStatus, out resolvedNewStatus); break;
                    case Treatment.capture: success = targetFaction.FactionOwnerRoot.CanCapture(entry.innerChara, prevStatus, out resolvedNewStatus); break;
                    case Treatment.liberate: success = targetFaction.FactionOwnerRoot.CanLiberate(entry.innerChara, prevStatus, out resolvedNewStatus); break;
                    default: success = false; resolvedNewStatus = null; break;
                }
                if (!success) deactivateSelf = true;
            }

            if (deactivateSelf) this.text.gameObject.SetActive (false);
        }
        public override bool IsButtonValid()
        {
            if (deactivateSelf) return false;
            if (!text.gameObject.activeInHierarchy) return false;
            if (entry == null) return false;
            text.SetText(innerText);
            text.Toggle(true, target == entry.TreatmentResult);
            return true;
        }
        public void OnClickButton()
        {
            entry.SetTreatment(target, resolvedNewStatus);
        }
    }
}
