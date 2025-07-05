using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using System;
using Unity.Jobs;
using UnityEngine.UI;
using System.Linq;

public class scr_panel_TargetInfo : scr_Menu
{
    public RectTransform parentBox1, parentBox2, parentBox3;

    public scr_HoverableText hp, mp, st, en;
    public TextMeshProUGUI fullname;

    public scr_HoverableText prideBox, corruptBox, despairBox;
    public scr_HoverableText attitudeBox, obedienceBox;
    public scr_HoverableText moodBox, stressBox, lustBox;
    public RectTransform StatusBox;

    public TMP_Text currentJobDesc;
    public TMP_Text nextHourJobDesc;

    public CanvasGroup self_canvasGroup;
    //private Image image_bg;

    protected override void Start()
    {
        //update = 0.0f;        
        //image_bg = this.GetComponent<Image>();
        //parentBox1.gameObject.SetActive(false);
        //parentBox2.gameObject.SetActive(false);
        //parentBox3.gameObject.SetActive(false);
        //self_canvasGroup.alpha = 0;


        if (!initialized) Initialize();
    }

    protected override void Awake()
    {
        base.Awake();

        self_canvasGroup.gameObject.SetActive(false);

        scr_System_CampaignManager.current.Observer_CurrentTarget += ReadCurrentChar;
        scr_System_CampaignManager.current.Observer_UpdateNotice += OnNotifyUpdate;
        scr_System_CampaignManager.current.Observer_CurrentViewMode += OnViewChange;
    }

    private void OnViewChange(ViewMode v, bool b) { Refresh(); }

    private void ReadCurrentChar(int id)
    {
        Refresh();
    }

    private void OnNotifyUpdate(bool value)
    {
        Refresh();
    }

    Character_Trainable chara = null;

    //private float update;

    public RectTransform Grid_UnequipAll;
    public RectTransform Grid_UnequipSingles;

    List<int> managedEquipRefs = new List<int>();

    public RectTransform prefab_equippedItem;
    private void Refresh()
    {

        chara = scr_System_CampaignManager.current.CurrentTarget;

        var turnOff = false;
        if (chara != null && chara.RefID > 0)
        {
            //image_bg.raycastTarget = true;
            //parentBox1.gameObject.SetActive(true);
            //parentBox2.gameObject.SetActive(true);
            //parentBox3.gameObject.SetActive(true);
            //self_canvasGroup.alpha = 1;
            self_canvasGroup.gameObject.SetActive(true);

            fullname.text = chara.FullName+(chara.FactionManager.CurrentlyActiveFaction == null ? "" : ", "+chara.FactionManager.CurrentlyActiveFactionStatus);

            if(chara.Stats.HP != null) chara.Stats.HP.Draw(hp);
            else this.hp.SetText(" - ");

            if (chara.Stats.MP != null) chara.Stats.MP.Draw(mp);
            else this.mp.SetText(" - ");

            if (chara.Stats.Stamina != null) chara.Stats.Stamina.Draw(st);
        else this.st.SetText(" - ");

            if (chara.Stats.Energy != null) chara.Stats.Energy.Draw(en);
        else this.en.SetText(" - ");

            //if (chara.FactionManager.CurrentlyActiveFaction != null) socialStatusBox.SetText(chara.FactionManager.CurrentlyActiveFactionStatus);

            RelationshipManager.Character_Relationship rel = chara.Relationships.FindRelationshipWith(0);
            if (rel != null)
            {
                UI_Utility.Draw_Attitude(rel, attitudeBox);// rel.DrawAttitude(attitudeBox);
                UI_Utility.Draw_Obedience(rel, obedienceBox);// rel.DrawObedience(obedienceBox);
            }

            if (chara.Stats.Mood != null) chara.Stats.Mood.Draw(moodBox);
        else this.moodBox.SetText(" - ");
            if (chara.Stats.Stress != null) chara.Stats.Stress.Draw(stressBox);
        else this.stressBox.SetText(" - ");
            if (chara.Stats.Lust != null) chara.Stats.Lust.Draw(lustBox);
        else this.lustBox.SetText(" - ");

            RefreshStatusBox();

            currentJobDesc.text = chara.GetJobDescription();

            int nextHour = scr_System_Time.current.getCurrentTime().Hour + 1;
            if (nextHour >= 24) nextHour -= 24;
            var nextHourJob = chara.FactionManager.CurrentJobPost(nextHour);
            Manageable faction = chara.FactionManager.CurrentJobScheduleFaction(nextHour);
            nextHourJobDesc.text = (nextHourJob == null || nextHourJob.Name == "") ? LocalizeDictionary.QueryThenParse("chara_currentjob_free") : nextHourJob.Name + (faction != null ? "(" + chara.FactionManager.CurrentJobScheduleFaction(nextHour).FactionDisplayName + ")" : "") ;

            /*
            foreach(var i in managedEquipRefs) DestroyCOMButton(i);
            managedEquipRefs.Clear();

            managedEquipRefs.AddRange(chara.EquippedItemRefs);
            managedEquipRefs.AddRange(chara.inventory_ref);

            managedEquipRefs = managedEquipRefs.Distinct().ToList();
            managedEquipRefs.Sort();

            foreach(var i in managedEquipRefs) MakeCOMButton(Grid_UnequipSingles, prefab_equippedItem, chara.RefID, i);
            */

            if (scr_System_CentralControl.current.isSafeMode)
            {
                lustBox.gameObject.SetActive(false);
            }
        }
        else
        {
            /*
            foreach (var i in managedEquipRefs) DestroyCOMButton(i);
            managedEquipRefs.Clear();
            */
            turnOff = true;
            self_canvasGroup.gameObject.SetActive(false);
        }
        ValidateAll();

        if(turnOff) self_canvasGroup.gameObject.SetActive(false);
    }

   // public TMP_Text prefab_statusDescription;

    private void RefreshStatusBox()
    {
        while (StatusBox.transform.childCount > 0)
        {
            DestroyImmediate(StatusBox.transform.GetChild(0).gameObject);
        }

        foreach (var si in chara.Stats.statusInstancesEx_Displayable)
        {

            RectTransform box = Instantiate(prefab_text_link);
            box.SetParent(StatusBox, false);

            si.Draw(box.GetComponent<scr_HoverableText>());// UI_Utility.Draw(si, 

            //text.text = si.BaseRef.displayName + ":" + si.SeverityDisplayName;
        }

        foreach ( var si in chara.Stats.StatusInstances_Displayable)
        {

            RectTransform box = Instantiate(prefab_text_link);
            box.SetParent(StatusBox, false);

            UI_Utility.Draw(si, box.GetComponent<scr_HoverableText>());

            //text.text = si.BaseRef.displayName + ":" + si.SeverityDisplayName;
        }
    }

    public override void Notify(int optionID)
    {
        ButtonValidator validator = validatorsByID[optionID];
        I_ButtonClickable button = validator as I_ButtonClickable;
        if (button != null)
        {
            button.OnClickButton();
        }
        else
        {
            switch (optionID)
            {
                default: break;
            }
        }
        ValidateAll();
    }

    private void MakeCOMButton(RectTransform parent, RectTransform prefab, int charaRef, int equipRef)
    {
        RectTransform r = Instantiate(prefab);
        r.SetParent(parent, false);
        scr_SelectableText comp = r.GetComponent<scr_SelectableText>();

        comp.Initialize(this, new ButtonValidator_equipSingle(this, charaRef, equipRef, comp));
        comp.optionID = equipRef;

        buttonsByID.Add(comp.optionID, comp);
        validatorsByID.Add(comp.optionID, comp.Validator);

        comp.Validate();
    }

    private void DestroyCOMButton(int buttonRef)
    {

        scr_SelectableText text = buttonsByID[buttonRef];
        buttonsByID.Remove(buttonRef);
        ButtonValidator validator = validatorsByID[buttonRef];
        validatorsByID.Remove(buttonRef);

        validator.Destroy();
        text.gameObject.SetActive(false);
        Destroy(text.gameObject);
        
    }

    public override void Initialize()
    {
        base.Initialize();


        foreach (scr_SelectableText button in GetComponentsInChildren<scr_SelectableText>(true))
        {

            switch (button.optionID)
            {
                case -1: break;
                //case -2: button.Initialize(this, new ButtonValidator_FixClothes(this, button)); break;
                //case -3: button.Initialize(this, new ButtonValidator_RedressLayers(this, button,1)); break;
                //case -4: button.Initialize(this, new ButtonValidator_UndressLayers(this, button,1)); break;
                //case -5: button.Initialize(this, new ButtonValidator_UndressAll(this, button, 1)); break;

               // case -7: button.Initialize(this, new ButtonValidator_FixClothes(this, button)); break;
                default:
                    button.Initialize(this, button_alwaysValid);
                    break;
            }
            if (button.optionID != -1)
            {
                buttonsByID.Add(button.optionID, button);
                validatorsByID.Add(button.optionID, button.Validator);
            }

        }

        // build all presetList
        ValidateAll();
    }

    public class ButtonValidator_equipSingle : ButtonValidator, I_ButtonClickable
    {

        int charaRef = -1;
        int equipRef = -1;
        new scr_panel_TargetInfo parent;
        scr_SelectableText text;
        Character_Trainable chara;
        Item_Instance item = null;
        ItemComponent_Equippable eq = null;
        public ButtonValidator_equipSingle(scr_Menu parent, int charaRef, int equipRef, scr_SelectableText text) : base(parent)
        {
            this.parent = parent as scr_panel_TargetInfo;
            this.charaRef = charaRef;
            this.chara = scr_System_CampaignManager.current.FindInstanceByID(charaRef);
            this.equipRef = equipRef;
            this.text = text;
            item = scr_System_CampaignManager.current.FindItemInstanceByID(equipRef);
            if (item != null) eq = item.GetComp_Equippable();
        }

        public override bool IsButtonValid()
        {

            if (eq == null || item == null) return false;
            //Debug.LogError("Chara " + chara.FullName + " inventory " + chara.inventory_ref.ToArray().ToString());
            //Debug.LogError("EquipRef " + equipRef +" displayName "+item.DisplayName);

            text.SetText(item.DisplayName);


            var equippedPart = chara.GetPartByEquipRef(equipRef);
            if (chara.Inventory.Contains(item) || equippedPart == null) return false;

            if (equippedPart.GetRevealingScore(eq.equipLayer) > 1) text.gameObject.SetActive(false);
            else text.gameObject.SetActive(true);

            return true;
        }




        public void OnClickButton()
        {
            //if (chara.inventory_ref.Contains(equipRef)) chara.EquipItem(equipRef, true);
            chara.UnequipItem(equipRef, -1, true, true);
            scr_System_CampaignManager.current.NotifyUpdate();
        }
    }

}
