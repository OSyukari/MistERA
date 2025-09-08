using UnityEngine;
using System.Collections.Generic;
using System;
using TMPro;

public class initScript_Expeditions : MonoBehaviour
{
    public enum PartyEditUI
    {
        Neutral,
        MembersEdit
    }
    
    PartyEditUI _currentMode = PartyEditUI.Neutral;

    public PartyEditUI CurrentMode
    { get
        {
            return _currentMode;
        }
        set 
        { 
            _currentMode = value;
            bool _neutral = _currentMode == PartyEditUI.Neutral;
            bool _editM = _currentMode == PartyEditUI.MembersEdit;
            if (canvas != null && canvas.Tab_Expeditions.gameObject.activeInHierarchy)
            {
                foreach (var i in ui_neutral) i.gameObject.SetActive(_neutral);
                foreach (var i in ui_editMembers) i.gameObject.SetActive(_editM);
            }
        }
    }
    public List<RectTransform> ui_neutral;
    public List<RectTransform> ui_editMembers;

    public RectTransform teamList;

    public scr_partyBTN prefab_partyButton;
    public scr_SelectableText prefab_memberButton;
    Manageable_Party previous = null;
    public void Initialize(scr_Canvas_Management canvas, Manageable m, bool loadPrev = false)
    {
        this.canvas = canvas;

        canvas.UnloadButton(this.temporaryTeamIDs);
        Utility.DestroyAllChildrenFrom(teamList, 1);

        Manageable_Party first = loadPrev ? previous : null;
        foreach (var party in m.SubFactions)
        {
            if (first == null) first = party;
            var script = Instantiate(prefab_partyButton);
            script.SelfRect.SetParent(teamList, false);
            var names = new List<string>();
            foreach (var i in party.ManagedChara) names.Add(i.FirstName);
            script.partyMembers.SetText(String.Join(" ", names));
            canvas.MakeButton_Party(party, script.PartyButton);
            temporaryTeamIDs.Add(script.PartyButton.optionID);
        }
        if (previousFaction != m)
        {
            canvas.UnloadButton(this.temporaryCharaIDs);
            Utility.DestroyAllChildrenFrom(list_EditCharaInParty, 1);

            previousFaction = m;

            foreach (var chara in m.ManagedChara)
            {
                var script = Instantiate(prefab_memberButton);
                script.SelfRect.SetParent(list_EditCharaInParty, false);
                canvas.MakeButton_PartyMembers(chara, script);
                temporaryCharaIDs.Add(script.optionID);
            }

        }
        if (first == null) CurrentMode = PartyEditUI.Neutral;
        else if (first != previous)
        {
            previous = first;
            CurrentMode = PartyEditUI.Neutral;
        }

        canvas.LoadParty(first);
    }

    List<int> temporaryTeamIDs = new List<int>();
    List<int> temporaryCharaIDs = new List<int>();

    public TMP_InputField teamNameButton;
    public TMP_Text teammates;
    public scr_HoverableText teamStatus;
    public scr_inputFieldLink prefab_inputfield;
    public RectTransform list_EditCharaInParty;

    Manageable previousFaction = null;
    scr_Canvas_Management canvas = null;
    Manageable_Party party;

    public scr_HoverableText grid_gear;
    public RectTransform grid_inventory, grid_inv_temp, text_inv_empty;

    public void Draw(Manageable_Party p)
    {
        if (party != p) teamNameButton.DeactivateInputField();
        party = p;
        previous = p;

        Utility.DestroyAllChildrenFrom(grid_inventory);
        Utility.DestroyAllChildrenFrom(grid_inv_temp);

        if (party == null)
        {
            teamNameButton.text = "-";
            teammates.SetText("-");
            teamNameButton.interactable = false;
            teamStatus.SetText("-");

            text_inv_empty.gameObject.SetActive(true); 
            grid_gear.gameObject.SetActive(false);
        }
        else
        {
            teamNameButton.interactable = true;
            teamNameButton.text = $"{p.FactionDisplayName}";// (p.FactionDisplayName);
            var names = new List<string>();
            foreach (var i in p.ManagedChara) names.Add(i.FirstName);
            teammates.SetText($"{String.Join(", ", names)}");
            string status_tooltip;
            teamStatus.SetText($"{p.GetAvailability(out status_tooltip)}");
            teamStatus.SetExternalTooltip(status_tooltip);

            bool hasInv = false;
            if (p.Inventory != null)
            {
                foreach (var i in p.Inventory.Contents)
                {
                    hasInv = hasInv || true;
                    var v = Instantiate(canvas.prefab_text_link);
                    v.SetParent(grid_inventory);
                    var v2 = v.GetComponent<scr_HoverableText>();
                    v2.SetText(i.DisplayName);
                }
            }
            if (p.Room != null && p.Room.DisplayableFurnitures.Count > 0)
            {
                hasInv = true;
                grid_gear.gameObject.SetActive(true);
                grid_gear.SetText(p.Room.DisplayableFurnitureNames_withLink);
            }
            else grid_gear.gameObject.SetActive(false);
            
            if (p.TempInventory != null)
            {
                foreach (var i in p.TempInventory.Contents)
                {
                    hasInv = hasInv || true;
                    var v = Instantiate(canvas.prefab_text_link);
                    v.SetParent(grid_inv_temp);
                    var v2 = v.GetComponent<scr_HoverableText>();
                    v2.SetText(i.DisplayName);
                }
            }
            text_inv_empty.gameObject.SetActive(!hasInv);

            
        }


    }
    public void OnValueChanged(string s)
    {
        Debug.Log("OnValueChanged");
        if (party == null) return;
        
        party.FactionDisplayName = teamNameButton.text;
        if (canvas != null) canvas.UpdatePartyNames();
    }

    
}
