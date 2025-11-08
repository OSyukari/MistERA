using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using System.IO;
using TMPro;
using UnityEngine.EventSystems;
using System;

public class scr_Menu_CharaDetail : scr_Menu, IPointerClickHandler
{
    public RectTransform panel_basicInfo, panel_equip, panel_relation, panel_records;
    public List<RectTransform> panel_basicInfo_extras;
    public scr_SpineLoader spineLoader;
    int chara_refID = -1;
    Character_Trainable _chara;
    Character_Trainable chara { get
        {
            if (chara_refID < 0) return null;
            else if (_chara == null )
            {
                _chara = scr_System_CampaignManager.current.FindInstanceByID(chara_refID);
            }
            return _chara;
        } set
        {
            _chara = value;
            chara_refID = _chara.RefID;
        }
    }
    public Image picture;
#pragma warning disable CS0436 // Type conflicts with imported type
    public ScrollRect pictureRect;
    public Scrollbar scrollbar_horizontal, scrollbar_vertical;
#pragma warning restore CS0436 // Type conflicts with imported type

    public ptDownTracker portraitTracker;
    public void InitializeWithArgument(int refID)
    {
        chara_refID = refID;
        scr_System_CampaignManager.current.CurrentTargetEX = chara;

        if (!initialized) Initialize();
        //pictureRect.onValueChanged.AddListener(OnPictureRectChange);

        portraitTracker.SetRectPosition(scr_System_CampaignManager.current.CurrentTargetEXPortrait);
        
        currentTab = panel_basicInfo;
        InitializeBasicInfo();

        ValidateAll();
    }

    protected override void Awake()
    {
        base.Awake();
        this.m_Canvas.overrideSorting = true;
        button_alwaysValid = new ButtonValidator_AlwaysTrue(this);
        selfRect = GetComponent<RectTransform>();
        this.currentTab = panel_basicInfo;

    }

    public override void Initialize()
    {
        base.Initialize();

        bool safe = scr_System_CentralControl.current.isSafeMode;

        foreach (scr_SelectableText button in GetComponentsInChildren<scr_SelectableText>(true))
        {
           // Debug.Log("Button " + button + " " + button.optionID);
            switch (button.optionID)
            {
                case 1:  // basic info tab
                    button.Initialize(this, new button_ChangeTab(this, button, panel_basicInfo, InitializeBasicInfo, UnInitializeBasicInfo, panel_basicInfo_extras)); break;
                //case 2:   // health status tab
                    //button.Initialize(this, new button_ChangeTab(this, button, panel_health, InitializeHealth)); break;
                case 3:   // equipment tab
                    if (safe) button.gameObject.SetActive(false);
                    else button.Initialize(this, new button_ChangeTab(this, button, panel_equip, InitializeEquipment)); 
                    break;
                case 4:   // relationship tab
                    button.Initialize(this, new button_ChangeTab(this, button, panel_relation, InitializeRelationship)); break;
               // case 5:   // manage tab
                    //button.Initialize(this, new button_ChangeTab(this, button, panel_manage, InitializeJobs)); break;
                case 6:  // sex records tab
                    if (scr_System_CentralControl.current.isSafeMode)
                    {
                        panel_records.gameObject.SetActive(false);
                        button.gameObject.SetActive(false);
                    }
                    else button.Initialize(this, new button_ChangeTab(this, button, panel_records, InitializeSexRecords)); 
                    break;
                //case 7:
                //    button.Initialize(this, new button_ChangeTab(this, button, panel_memories, InitializeMemories));break;
                case 9999: // exit
                    button.Initialize(this, button_alwaysValid);break;
                case -1: break;

                //case 211: // Health tab contents
                 //   button.Initialize(this, new button_ChangeHealthTab(this, button, healthTab_contents)); break;
                //case 213: // health tab pregnancy
                //    button.Initialize(this, new button_ChangeHealthTab(this, button, healthTab_pregnancy)); break;
                default:
                    button.Initialize(this, button_alwaysValid); break;
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

    public override void Notify(int optionID)
    {
        //Debug.Log("Parent Notified ! [" + optionID + "]");
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
                case 9999:
                    SaveImageTransform();
                    scr_System_SceneManager.current.UnloadLastCanvasFromScene();
                    break;
                default: break;
            }
        }
        ValidateAll();
    }


   // public TextMeshProUGUI picture_AA;
    RectTransform selfRect;


    public RectTransform tab_basicInfo_statGrid;
    public initScript_basicInfo initScript_BasicInfo;
    bool initialized_basicInfo = false;
    private void InitializeBasicInfo()
    {
        if (chara == null) return;

        pictureRect.horizontal = true;
        pictureRect.vertical = true;
        scrollbar_horizontal.interactable = true;
       // scrollbar_horizontal.GetComponent<Image>().color = scr_System_CentralControl.current.DisplaySetting.TextColor_neutral;
        scrollbar_vertical.interactable = true;
        //scrollbar_vertical.GetComponent<Image>().color = scr_System_CentralControl.current.DisplaySetting.TextColor_neutral;

        if (initialized_basicInfo) return;
        else initialized_basicInfo = true;

        initScript_BasicInfo.InitData(chara);
    }

    private void UnInitializeBasicInfo()
    {
        pictureRect.horizontal = false;
        pictureRect.vertical = false;
        scrollbar_horizontal.interactable = false;
        //scrollbar_horizontal.GetComponent<Image>().color = scr_System_CentralControl.current.DisplaySetting.TextColor_transparent;
        scrollbar_vertical.interactable = false;
        //scrollbar_vertical.GetComponent<Image>().color = scr_System_CentralControl.current.DisplaySetting.TextColor_transparent;
    }



    public TextMeshProUGUI textBox, linkBox, buttonBox;
    public RectTransform tab_equip_bodyPart, tab_equip_equipmentsList, tab_equip_prefab_equipment;
    public RectTransform prefab_BodyInstanceGear;
    public initScript_Equip initScript_Equip;
    public delegate void Initializer();

    private bool initialized_equip = false;
    private void InitializeEquipment()
    {
        if (initialized_equip) return;
        else initialized_equip = true;

        initScript_Equip.InitializeData(chara);

        bool safeMode = scr_System_CentralControl.current.isSafeMode;

        foreach (BodyPart_Instance b in chara.Body.Body)
        {
            if (b.availableSlots.Count < 1) continue;
            RectTransform rect = Instantiate(prefab_BodyInstanceGear);
            rect.SetParent(tab_equip_equipmentsList, false);

            RectTransform childRect = rect.GetComponent< scr_BodyInstanceGears >().Instantiate(b.DisplayName);

            foreach (BodyPartEquipSlot slot in b.availableSlots)
            {
                RectTransform box = Instantiate(tab_equip_prefab_equipment);
                box.SetParent(childRect, false);

                if (slot != BodyPartEquipSlot.None) AddBox(textBox, box, LocalizeDictionary.QueryThenParse("equip_slot_"+Utility.GetEnumString(typeof(BodyPartEquipSlot), slot)));

                int score = b.GetRevealingScore(BodyEquipLayer.Skin);

                Item_Instance skin, inner, outer;

                if (!safeMode)
                {
                    if (score > 1 
                        && !scr_System_CampaignManager.current.XrayMode 
                        && !scr_System_CampaignManager.current.DebugMode) AddBox(textBox, box, "(" + score + ")");
                    else if (b.TryGetEquip(out skin, BodyEquipLayer.Skin, slot)) AddBox(buttonBox, box, skin.DisplayName);
                    else if (b.TryGetCover(out skin, BodyEquipLayer.Skin, slot)) AddBox(buttonBox, box, skin.DisplayName, true);
                    else AddBox(textBox, box, " - ");
                    
                }

                if (        b.TryGetEquip(out inner, BodyEquipLayer.Inner, slot)) AddBox(buttonBox, box, inner.DisplayName);
                else if (   b.TryGetCover(out inner, BodyEquipLayer.Inner, slot)) AddBox(buttonBox, box, inner.DisplayName, true);
                else AddBox(textBox, box, " - ");

                if (        b.TryGetEquip(out outer, BodyEquipLayer.Outer, slot)) AddBox(buttonBox, box, outer.DisplayName);
                else if (   b.TryGetCover(out outer, BodyEquipLayer.Outer, slot)) AddBox(buttonBox, box, outer.DisplayName, true);
                else AddBox(textBox, box, " - ");


                /*int l = b.GetEquip(BodyEquipLayer.Shell, slot);
                if (l > 0) AddBox(buttonBox, box, scr_System_CampaignManager.current.FindItemInstanceByID(l).DisplayName);
                else AddBox(textBox, box, " - ");*/

            }

            if (safeMode) continue;
            
            foreach (BodyInternal_Instance ins in b.internals)
            {
                foreach (BodyPartEquipSlot slot in ins.availableSlots)
                {
                    bool hasEquip = false;
                    RectTransform box = Instantiate(tab_equip_prefab_equipment);
                    box.SetParent(childRect, false);

                    if (slot != BodyPartEquipSlot.None) AddBox(textBox, box, LocalizeDictionary.QueryThenParse("equip_slot_" + Utility.GetEnumString(typeof(BodyPartEquipSlot), slot)));

                    int score = b.GetRevealingScore(BodyEquipLayer.Skin);
                    if (score > 1 && !scr_System_CampaignManager.current.XrayMode)
                    {
                        AddBox(textBox, box, " " + score + " ");
                        AddBox(textBox, box, " " + score + " ");
                        //AddBox(textBox, box, " ??? ");
                    }
                    else
                    {
                        if (ins.equipLayers.Contains(BodyEquipLayer.Skin))
                        {
                            int i = ins.GetEquip(BodyEquipLayer.Skin, slot);
                            //if (i > 0) AddBox(buttonBox, box, scr_System_CampaignManager.current.FindItemInstanceByID(i).DisplayName + "[" + score + "]");
                            if (i > 0)
                            {
                                hasEquip = true;
                                AddBox(buttonBox, box, scr_System_CampaignManager.current.FindItemInstanceByID(i).DisplayName);
                            }
                            else
                            {
                                AddBox(textBox, box, " - ");
                            }
                        }
                        else AddBox(textBox, box, " ");

                        if (ins.equipLayers.Contains(BodyEquipLayer.Inner))
                        {
                            int i = ins.GetEquip(BodyEquipLayer.Inner, slot);
                            //if (i > 0) AddBox(buttonBox, box, scr_System_CampaignManager.current.FindItemInstanceByID(i).DisplayName + "[" + score + "]");
                            if (i > 0)
                            {
                                hasEquip = true;
                                AddBox(buttonBox, box, scr_System_CampaignManager.current.FindItemInstanceByID(i).DisplayName);
                            }
                            else
                            {
                                AddBox(textBox, box, " - ");
                            }
                        }
                        else AddBox(textBox, box, " ");

                        AddBox(textBox, box, " ");
                    }


                    if (!hasEquip) box.gameObject.SetActive(false);
                }
            }
        }
    }

    public initScript_Relations initScript_relations;

    public RectTransform boxMemoriesList;

    public RectTransform boxRelationshipList, prefab_boxRelationship;
    

    private bool initialized_relation = false;
    private void InitializeRelationship()
    {
        if (initialized_relation) return;
        else initialized_relation = true;

        initScript_relations.InitializeData(this.chara, this);
    }

    public RectTransform prefab_panel_BodyDetail;
    Dictionary<BodyInternal_Instance, scr_Panel_BodyDetail> internalDictionary = new Dictionary<BodyInternal_Instance, scr_Panel_BodyDetail>();
    Dictionary<int, BodyInternal_Instance> internalIndex = new Dictionary<int, BodyInternal_Instance>();
    List<BodyInternal_Instance> listInternal = new List<BodyInternal_Instance>();
    protected RectTransform currentHealthTab;
    protected RectTransform currentTab;
    

    public RectTransform sexRecord_internals_list;
    public initScript_Records initSexRecords;
    bool initialized_sexRecords = false;
    private void InitializeSexRecords()
    {
        if (initialized_sexRecords) return;
        else initialized_sexRecords = true;

        //listInternal = new List<BodyInternal_Instance>();
        //internalIndex = new Dictionary<int, BodyInternal_Instance>();

        //each panel need to know what part from who its monitoring
        //internalDictionary = new Dictionary<BodyInternal_Instance, scr_Panel_BodyDetail>();


        foreach (BodyPart_Instance b in chara.Body.Body)
        {
            foreach (BodyInternal_Instance i in b.internals)
            {
                //if (i != null && i.canBeFucked || i.canFuck)
                //{
                    listInternal.Add(i);
                //}
            }
        }

        listInternal.Sort((x, y) => x.sortOrder.CompareTo(y.sortOrder));
        int j = 0;
        foreach (BodyInternal_Instance i in listInternal)
        {
            internalIndex.Add(j, i);

            RectTransform box = Instantiate(prefab_panel_BodyDetail);
            box.SetParent(sexRecord_internals_list, false);
            scr_Panel_BodyDetail scr = box.GetComponent<scr_Panel_BodyDetail>();
            scr.InitializeWithArgument(this, j);

            //internalDictionary.Add(i, scr);
            j++;
        }

        if (initSexRecords != null) initSexRecords.Initialize(chara);

        foreach(SkillInstance si in chara.Skills.Skills)
        {
            int hash = AssertUniqueHash(si.GetHashCode())  ;
            //Debug.LogError(si.BaseRef.ID);
            
            if (!ButtonsByID.ContainsKey(hash))
            {
                scr_SelectableText box = Instantiate(prefab_Button);
                box.transform.SetParent(initSexRecords.SkillsGrid, false);
                box.Initialize(this, new ButtonValidator_UpgradeSkill(this, box, chara, si));
                box.optionID = hash;
                box.showBrackets = false;

                buttonsByID.Add(box.optionID, box);
                validatorsByID.Add(box.optionID, box.Validator);

                box.Validate();
            }
        }

    }

    protected void SaveImageTransform()
    {
        //Debug.LogError("fail saving image transform");
        /*
        if (currentPortrait != null)
        {
            if (currentPortrait is PortraitManager.CharaPortrait_Spine && spineRect != null)
            {
                //Debug.Log(spineRect.anchoredPosition.x + "|" + spineRect.anchoredPosition.y + "|" + spineRect.localScale.x);
                currentPortrait.SetPortraitOffsets((float)Math.Round(spineRect.anchoredPosition.x, 3),
                (float)Math.Round(spineRect.anchoredPosition.y, 3),
                (float)Math.Round(spineRect.localScale.x, 3));
            }
            else if (currentPortrait is PortraitManager.CharaPortrait_Image)
            {
                //Debug.Log(picture.rectTransform.anchoredPosition.x + "|" + picture.rectTransform.anchoredPosition.y + "|" + picture.rectTransform.localScale.x);
                currentPortrait.SetPortraitOffsets((float)Math.Round(picture.rectTransform.anchoredPosition.x , 3),
                (float)Math.Round(picture.rectTransform.anchoredPosition.y , 3),
                (float)Math.Round(picture.rectTransform.localScale.x, 3));
            }
        }*/
    }

    public scr_SelectableText prefab_Button;
    //public scr_HoverableText consciousness, climax;
    public RectTransform prefab_BodyInstanceContent1, prefab_BodyInstanceContent2;

    public BodyInternal_Instance GetInternalwithIndex(int i)
    {
        if (internalIndex.ContainsKey(i)) return internalIndex[i];
        else return null;
    }

    public void HealthTab_SetTab(RectTransform rect)
    {
        this.currentHealthTab = rect;
    }


    private void AddBox(TextMeshProUGUI box_prefab, RectTransform parent, string content, bool dimColor = false)
    {
        TextMeshProUGUI text = Instantiate(box_prefab);
        text.text = content;
        text.GetComponent<RectTransform>().SetParent(parent, false);
        if (dimColor)
        {
            text.color = scr_System_CentralControl.current.DisplaySetting.TextColor_disabled.Color;
        }
    }

    protected override void OnDestroy()
    {
        //scr_System_CampaignManager.current.CurrentTargetEX = null;
        base.OnDestroy();
        /*
        Debug.Log("scr_Menu_CharaDetail: OnDestroy Called!" + "\n"+
            "offsetX ["+ Math.Round(picture.rectTransform.anchoredPosition.x / picture.rectTransform.rect.width, 3) + "]" + 
            "offsetY ["+ Math.Round(picture.rectTransform.anchoredPosition.y / picture.rectTransform.rect.height, 3) +"]" + 
            "offsetSize ["+ Math.Round(picture.rectTransform.localScale.x, 3) + "]\n" +
            "anchoredposition " + picture.rectTransform.anchoredPosition.x + " " + picture.rectTransform.anchoredPosition.y + "\n" +
           "portrait offset " + chara.portrait_offset_x + " " + chara.portrait_offset_y + "\n" +
           "rectTransform SizeDelta " + picture.rectTransform.sizeDelta.x + " " + picture.rectTransform.sizeDelta.y + "\n" +
           "rect width height " + picture.rectTransform.rect.width + " " + picture.rectTransform.rect.height + "\n" +
           "Preferred Width Height " + LayoutUtility.GetPreferredWidth(picture.rectTransform) + " " + LayoutUtility.GetPreferredHeight(picture.rectTransform));
          */        

        // trying to reinvoke target to provoke image refresh in scr_CharPortraitBox
        //if (scr_System_CampaignManager.current.CurrentTargetRef == chara_refID) scr_System_CampaignManager.current.ChangeCurrentTarget(chara_refID);

        listInternal.Clear();
        internalIndex.Clear();
        internalDictionary.Clear();
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        bool unload = false;
        // if click outside box
        if (eventData.rawPointerPress.GetComponent<scr_Menu_CharaDetail>() != null) unload = true;
        // inside box
        else if (eventData.button == PointerEventData.InputButton.Right && UtilityEX.isClickBelowDragThreshold(eventData)) unload = true;
        //Debug.Log("scr_Menu_CharaDetail: OnPointerClick! Data["+eventData.pointerPress+"] rawData["+ eventData.rawPointerPress + "]");

        if (unload)
        {
            SaveImageTransform();
            scr_System_SceneManager.current.UnloadLastCanvasFromScene();
        }
    }

    public class button_ChangeTab : ButtonValidator, I_ButtonClickable
    {
        RectTransform target;
        new scr_Menu_CharaDetail parent;
        scr_SelectableText text;
        Initializer init;
        Initializer uninit;
        List<RectTransform> extras;
        public button_ChangeTab(scr_Menu_CharaDetail parent, scr_SelectableText text, RectTransform target, Initializer init = null, Initializer uninit = null, List<RectTransform> extras = null) : base(parent)
        {
            this.parent = parent;
            this.text = text;
            this.target = target;
            this.init = init;
            this.uninit = uninit;
            this.extras = extras;
            this.text.GetComponent<scr_PointerEnterNotifier>().Initialize(parent, text.optionID);
        }


        public override bool IsButtonValid()
        {

            if (parent.currentTab != target)
            {
                text.Toggle(true, false);
                target.gameObject.SetActive(false);
                if (extras != null) foreach (var i in extras) i.gameObject.SetActive(false);
                //foreach (scr_SelectableText s in filters) if (!parent.currentTab_Filters.Contains(s)) s.gameObject.SetActive(false);
                if (uninit != null) uninit();

            }
            else
            {
                text.Toggle(true, true);
                target.gameObject.SetActive(true);
                if (extras != null) foreach (var i in extras) i.gameObject.SetActive(true);
                //foreach (scr_SelectableText s in filters) s.gameObject.SetActive(true);
                //if (init != null) init();

            }
            return true;
        }

        public void OnClickButton()
        {
            parent.currentTab = target;
           if (init != null) init();
        }
    }

    public class button_ChangeHealthTab : ButtonValidator, I_ButtonClickable
    {
        RectTransform target;
        new scr_Menu_CharaDetail parent;
        scr_SelectableText text;
        public button_ChangeHealthTab(scr_Menu_CharaDetail parent, scr_SelectableText text, RectTransform target) : base(parent)
        {
            this.parent = parent;
            this.text = text;
            this.target = target;
        }

        public override bool IsButtonValid()
        {

            if (parent.currentHealthTab != target)
            {
                text.Toggle(true, false);
                target.gameObject.SetActive(false);
                //foreach (scr_SelectableText s in filters) if (!parent.currentTab_Filters.Contains(s)) s.gameObject.SetActive(false);

            }
            else
            {
                        text.Toggle(true, true);
                        target.gameObject.SetActive(true);
                //foreach (scr_SelectableText s in filters) s.gameObject.SetActive(true);
            }
            return true;
        }

        public void OnClickButton()
        {
            //parent.currentHealthTab = target;
            parent.HealthTab_SetTab(target);
        }
    }

    public class ButtonValidator_UpgradeSkill : ButtonValidator, I_ButtonClickable
    {
        new scr_Menu_CharaDetail parent;
        scr_SelectableText text;
        Character_Trainable c;
        SkillInstance sk;
        public ButtonValidator_UpgradeSkill(scr_Menu_CharaDetail parent, scr_SelectableText text, Character_Trainable c, SkillInstance sk) : base(parent)
        {
            this.parent = parent;
            this.text = text;
            this.c = c;
            this.sk = sk;
            this.text.showBrackets = false;
            innerText = this.sk.DisplayName;
            text.disableColor = text.baseColor;
            skillTooltip = LocalizeDictionary.QueryThenParse(sk.BaseRef.ID + "_tooltip");
            
        }
        string innerText, skillTooltip;
        public override bool IsButtonValid()
        {
            if (sk == null) return false;
            if (sk.CanUpgrade)
            {
                text.SetText("[" + innerText +": "+sk.GetSkillLevel+ "] +");
                this.tooltip = skillTooltip + "\n\n" + String.Join("\n", sk.TooltipCache+"\n\nThis skill has enough experience to level up, will be auto-updated on next day.");
                return false;
            }
            else
            {
                text.SetText("[" + innerText + ": " + sk.GetSkillLevel + "]");
                this.tooltip = skillTooltip + "\n\n" + String.Join("\n", sk.TooltipCache);
                return false;
            }
        }

        public void OnClickButton()
        {
            sk.Upgrade(null);
        }
    }

    public override void ValidateAll()
    {
        base.ValidateAll();
    }
}
