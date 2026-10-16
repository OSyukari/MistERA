using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using System;
using UnityEngine.UI;
using Cysharp.Threading.Tasks.Triggers;



public class scr_Canvas_CharacterEditor : scr_Menu
{

    private Character_Trainable c;
    public Character_Trainable Character { get { return c; } }
    protected int currentStartingOptionIndex = 0;

    Action<string> refresh = null;

    public void InitializeWithArgument(Character_Trainable c, Action<string> refresh = null)
    {
        if (!initialized) Initialize();
        //this.refresh = refresh;
        foreach (scr_SelectableText button in GetComponentsInChildren<scr_SelectableText>(true))
        {
            //Debug.Log("Button "+button + " " + button.optionID);
            switch (button.optionID)
            {
                case 111: button.Initialize(this, new ButtonValidator_SelectOrigin(this, true));  break;
                case 112: button.Initialize(this, new ButtonValidator_SelectOrigin(this, false));  break;
                case 113: button.Initialize(this, new ButtonValidator_SelectRace(this, true)); break;
                case 114: button.Initialize(this, new ButtonValidator_SelectRace(this, false)); break;
                case 115: button.Initialize(this, new ButtonValidator_SelectraceTemplate(this, true)); break;
                case 116: button.Initialize(this, new ButtonValidator_SelectraceTemplate(this, false)); break;
                case 117: button.Initialize(this, new ButtonValidator_SelectStartingOption(this, true)); break;
                case 118: button.Initialize(this, new ButtonValidator_SelectStartingOption(this, false)); break;
                case 121:
                case 122:
                case 123:
                    button.Initialize(this, button_alwaysValid);
                    break;
                case 1300: case 1301: case 1302: case 1303:
                case 1310: case 1311: case 1312: case 1313:
                case 1320: case 1321: case 1322: case 1323:
                case 1330: case 1331: case 1332: case 1333:
                    button.Initialize(this, button_alwaysValid); break;

                case 132://month left
                case 133://month right
                case 134://day left
                case 135://day right
                    button.Initialize(this, button_alwaysInvalid); break;
                case 140: case 141:
                    button.Initialize(this, new ButtonValidator_SelectGender(this)); break;
                case 142: case 143: // personality
                case 144: case 145: // bodytype
                case 130: case 131: //age selection
                    button.Initialize(this, button_alwaysInvalid); break;
                case 146:   //select trait
                    button.Initialize(this, new ButtonValidator_selectTraitPanel(this, button));

                    button.SetText("char_editor_selectTraitBTN"); 
                    break;
                //case 149:   //cancel select trait
                //    button.Initialize(this, button_alwaysValid); break;
                case 150:
                    button_confirmTraits = new ButtonValidator_selectTrait_Confirm(this, 150);
                    button.Initialize(this, button_confirmTraits); break;
                case 200:   //sensitivity B left
                    button.Initialize(this, new ButtonValidator_selectSensitivity_B(this, true)); break;
                case 201:   //sensitivity B right
                    button.Initialize(this, new ButtonValidator_selectSensitivity_B(this, false)); break;
                case 202:   //size B left
                    button.Initialize(this, new ButtonValidator_selectSize_B(this, true));  break;
                case 203:   //size b right
                    button.Initialize(this, new ButtonValidator_selectSize_B(this, false)); break;
                case 210:   //sensitivity M left
                    button.Initialize(this, new ButtonValidator_selectSensitivity_M(this, true)); break;
                case 211:   //sensitivity M right
                    button.Initialize(this, new ButtonValidator_selectSensitivity_M(this, false)); break;
                case 220:   //sensitivity C left
                    button.Initialize(this, new ButtonValidator_selectSensitivity_C(this, true)); break;
                case 221:   //sensitivity C right
                    button.Initialize(this, new ButtonValidator_selectSensitivity_C(this, false)); break;
                case 222:   //size C left
                    button.Initialize(this, new ButtonValidator_selectSize_P(this, true)); break;
                case 223:   //size C right
                    button.Initialize(this, new ButtonValidator_selectSize_P(this, false)); break;
                case 230:   //sensitivity V left
                    button.Initialize(this, new ButtonValidator_selectSensitivity_V(this, true)); break;
                case 231:   //sensitivity V right
                    button.Initialize(this, new ButtonValidator_selectSensitivity_V(this, false)); break;
                case 232:   //size V left
                    button.Initialize(this, new ButtonValidator_selectSize_V(this, true)); break;
                case 233:   //size V right
                    button.Initialize(this, new ButtonValidator_selectSize_V(this, false)); break;
                case 240:   //sensitivity A left
                    button.Initialize(this, new ButtonValidator_selectSensitivity_A(this, true)); break;
                case 241:   //sensitivity A right
                    button.Initialize(this, new ButtonValidator_selectSensitivity_A(this, false)); break;
                case 242:   //size A left
                    button.Initialize(this, new ButtonValidator_selectSize_A(this, true)); break;
                case 243:   //size A right
                    button.Initialize(this, new ButtonValidator_selectSize_A(this, false)); break;

                case 9998:  //confirm and save character
                    button_confirmCharacter = new ButtonValidator_confirmCharacter(this, 9998);
                    button.Initialize(this, button_confirmCharacter);
                    break;
                case 9999:  //exit without saving
                    button.Initialize(this, button_alwaysValid); break;
                case -1: 
                    break;
                default:
                    button.Initialize(this, button_alwaysValid); break;
            }

            if (button.optionID != -1)
            {
                buttonsByID.Add(button.optionID, button);
                validatorsByID.Add(button.optionID, button.Validator);
            }

        }
        //this.c = 
        //Debug.LogError("REMOVED CODE CHARA INSTANTIATION");
        //this.c = null;
        this.c = c;

        c.OnAfterDeserialize();
        // preset json has no appearance (InitializeWithRefID copies it in game); trait requirements check the character's
        if (c.Template != null) c.Appearance = c.Template.Appearance;

        UpdatePanel();
        UpdateAllTexts();
        ValidateAll();

        RebuildTraitsLeft();
        //ValidateLate();

    }

    public scr_HoverableText origin, race, raceTemplate, startingGift;

    public TextMeshProUGUI stat_strength_value, stat_constitution_value, stat_psyche_value, stat_willpower_value;
    public TextMeshProUGUI stat_strength_race, stat_constitution_race, stat_psyche_race, stat_willpower_race;
    public TextMeshProUGUI stat_strength_final, stat_constitution_final, stat_psyche_final, stat_willpower_final;

    public TextMeshProUGUI Traits_Prefab;

    //public TextMeshProUGUI skill_shooting, skill_stealth, skill_survival;
    //public TextMeshProUGUI skill_athletics, skill_melee;
    //public TextMeshProUGUI skill_spellcraft, skill_cooking, skill_art, skill_science, skill_engineering, skill_chemistry, skill_biology;
    //public TextMeshProUGUI skill_animal, skill_perception, skill_social;

    public TextMeshProUGUI age, birth_month, birth_day;

    public TextMeshProUGUI gender, personality, bodyType;

    public scr_HoverableText sensitivity_b, sensitivity_m, sensitivity_c, sensitivity_v, sensitivity_a;

    public scr_HoverableText size_b, size_p, size_v, size_a;

    public RectTransform TraitsSexBlock;
    protected ArrayList Traits_Sex;
    public TextMeshProUGUI TraitsSex_Prefab;

    private void LoadOrigin()
    {
        this.origin.SetText(Character.Origin == null ? " - " : Character.Origin.DisplayName);// = "<link=" + id + "><u>" + Character.Origin.DisplayName + "</u></link>";
        currentStartingOptionIndex = 0;
       // LayoutRebuilder.ForceRebuildLayoutImmediate(this.origin.rectTransform);

        //Debug.Log("Set Origin [" + this.Character.Origin.ID + "] with availableOptions ["+this.Character.Origin.availableOptionsID.Length+ "]");
    }
    private void LoadRace()
    {
        this.race.SetText(Character.Race == null ? " - " : Character.Race.DisplayName);// = "<link=" + id + "><u>" + Character.Race.DisplayName + "</u></link>";
        //LayoutRebuilder.ForceRebuildLayoutImmediate(this.race.rectTransform);
        //Debug.Log("SetRace on [" + this.Character.Race.ID + "] as ["+id+"]");
    }

    private void LoadRaceTemplate()
    {
        this.raceTemplate.SetText(Character.RaceTemplate == null ? " - " : Character.RaceTemplate.DisplayName);// = "<link=" + id + "><u>" + Character.RaceTemplate.DisplayName + "</u></link>";
        //LayoutRebuilder.ForceRebuildLayoutImmediate(this.raceTemplate.rectTransform);
        //Debug.Log("SetRace on [" + this.Character.RaceTemplate.ID + "] as [" + id + "]");
    }

    private void LoadStartingGift()
    {
        this.startingGift.SetText(Character.StartingGift == null ? " - " : Character.StartingGift.DisplayName);// = "<link=" + id + "><u>" + Character.StartingGift.DisplayName + "</u></link>";
       // LayoutRebuilder.ForceRebuildLayoutImmediate(this.startingGift.rectTransform);
    }

    public TextMeshProUGUI stat1, stat2, stat3, stat4;
    public TextMeshProUGUI mod_str, mod_con, mod_psy, mod_will;

    protected void UpdateNames()
    {
        if (c == null)
        {
            input_firstname.text = "";
            input_middlename.text = "";
            input_lastname.text = "";
        }
        else
        {
            input_firstname.text = c.FirstName;
            input_middlename.text = c.MiddleName;
            input_lastname.text = c.LastName;
        }
    }

    private void UpdatePanel()
    {
        if (c == null)
        {
            Debug.LogError("chara null");
        }

        input_firstname.DeactivateInputField();
        input_middlename.DeactivateInputField();
        input_lastname.DeactivateInputField();

        UpdateNames();

        LoadOrigin();
        LoadRace();
        LoadRaceTemplate();
        LoadStartingGift();

        stat = new int[4] { c.Stats.Strength.BaseValue, c.Stats.Constitution.BaseValue, c.Stats.Psyche.BaseValue, c.Stats.Willpower.BaseValue };
        statGrid = new int[4] { 0, 1, 2, 3 };

        //age.text = c.getAge().ToString();
        age.text = "-";

        SetBirthday(c.Birthday);

        personality.text = "Personality - unimplemented";//c.Personality.DisplayName;
                                                         // bodyType.text = c.Template.BodyType.ToString();

        //Debug.Log("character sensitivity a [" + c.getSensitivity_A().ID + "] b [" + c.getSensitivity_B().ID + "] c [" + c.getSensitivity_C().ID + "] m [" + c.getSensitivity_M().ID + "] v [" + c.getSensitivity_V().ID + "]");

        gender.text = $"<link=tooltip_GenderAppearance_{c.Template.Appearance.ToString()}>{LocalizeDictionary.QueryThenParse($"GenderAppearance_{c.Template.Appearance.ToString()}")}</link>";

        PopulateTraits_Spectrum_GroupListByType( TraitBox_middle, scr_System_Serializer.current.index_TraitsAll.traits_STR, Character.Stats.Strength,  "char_editor_selectTrait_STR", scr_System_Serializer.current.index_TraitsAll.traits_STR_SEX);
        PopulateTraits_Spectrum_GroupListByType( TraitBox_middle,  scr_System_Serializer.current.index_TraitsAll.traits_CON, Character.Stats.Constitution,  "char_editor_selectTrait_CON", scr_System_Serializer.current.index_TraitsAll.traits_CON_SEX);
        PopulateTraits_Spectrum_GroupListByType( TraitBox_middle,  scr_System_Serializer.current.index_TraitsAll.traits_PSY, Character.Stats.Psyche,  "char_editor_selectTrait_PSY", scr_System_Serializer.current.index_TraitsAll.traits_PSY_SEX);
        PopulateTraits_Spectrum_GroupListByType( TraitBox_middle,  scr_System_Serializer.current.index_TraitsAll.traits_WIL, Character.Stats.Willpower,  "char_editor_selectTrait_WIL", scr_System_Serializer.current.index_TraitsAll.traits_WIL_SEX);

        // 3 link to rect left, rect middle, rect right
        // assign dynamic ID

        PopulateTraits_Single(TraitBox_Single);

        PopulateHairstylePanel();
        PopulateBodyTraits();

        PopulateAllSkills();
        RefreshAllSkills();

        RebuildTraitsLeft();

        PopulateAllDerivedStats();

        //order by trait group, Str, strsex, con, consex, psy, psysex, wil, wilsex
        //

        panel_TraitSelect.gameObject.SetActive(false);  //setactive is disruptive

    }

    public void RefreshAllDerivedStats()
    {
        if (c != null) c.Stats.RefreshAllStats(true);
        foreach (var s in DerivedStatValues.Keys)
        {
            var drv = c == null ? null : c.Stats.GetDerivedStat(s);
            if (drv != null) UI_Utility.Draw(drv, DerivedStatValues[s]);//.text = drv.FinalValue(null);
            else DerivedStatValues[s].SetText(" - ");
        }
    }


    public RectTransform Panel_DerivedStats;
    Dictionary<string, scr_HoverableText> DerivedStatValues = new Dictionary<string, scr_HoverableText>();
    private void PopulateAllDerivedStats()
    {
        DerivedStatValues.Clear();

        foreach(var statderived in scr_System_Serializer.current.index_StatsDerived.list)
        {
            RectTransform box = Instantiate(prefab_text_link);
            box.SetParent(Panel_DerivedStats, false);

            var hov = box.GetComponent<scr_HoverableText>();
            DerivedStatValues.Add(statderived.ID, hov);
        }


        RefreshAllDerivedStats();
    }

    protected void SetBirthday(DateTime date)
    {
        c.Birthday = date;
        birth_month.text = date.Month.ToString();
        birth_day.text = date.Day.ToString();
    }

    protected void SetGenderAppearance(Humanoid_GenderAppearance app)
    {
        c.Template.SetGender(app);
        c.Appearance = app; // trait requirements check the character's appearance
    }

    /// <summary>
    /// Traits block
    /// </summary>

    public RectTransform TraitBox_middle;
    public RectTransform prefab_TraitText;
    public RectTransform prefab_TraitLinkText;
    public RectTransform prefab_TraitButtonBox;

    public RectTransform TraitBox_Single;

    Dictionary<int, Traits> Traits_getTraitbyID;
    /// <summary>
    /// trait groups this editor displayed; on save, the template's entries of these groups are replaced by the selection
    /// </summary>
    HashSet<scr_Traits_Group> Traits_editedGroups = new HashSet<scr_Traits_Group>();
    private int TraitID = 2000; // initialize at
    protected int GetTraitID
    {
        get {
            TraitID += 1;
            return TraitID - 1;
        }
    }

    public void Traits_AddTraitsbyID(int id, Traits trait)
    {
        int i = id - (id % 2);
        if (! Traits_getTraitbyID.ContainsKey(i)) Traits_getTraitbyID.Add(i, trait);
    }
    public Traits Traits_GetTraitByID(int id)
    {
        int newID = id - (id % 2);
        if (Traits_getTraitbyID.ContainsKey(newID)) return Traits_getTraitbyID[newID];
        else return null;
    }
    public void Traits_SetTraitByID(int id, Traits trait)
    {
        int newID = id - (id % 2);
        if (Traits_getTraitbyID.ContainsKey(newID))
        {
            Traits_getTraitbyID[newID] = trait;
        }
    }

    public void Traits_ToggleTraitByID(int id, Traits trait)
    {
        if (Traits_getTraitbyID.ContainsKey(id)) Traits_getTraitbyID.Remove(id);
        else Traits_getTraitbyID.Add(id, trait);
    }

    public scr_SelectableText prefab_trait_single;

    private void PopulateTraits_Single(RectTransform parent)
    {
        foreach (List<scr_Traits_Group> list in scr_System_Serializer.current.index_TraitsAll.traits_All)
        {
            foreach(scr_Traits_Group group in list)
            {
                if (group.SortType == Trait_Group_Type.Singular)
                {
                    Traits_editedGroups.Add(group);
                    foreach(Traits entry in group.entries)
                    {

                        scr_SelectableText comp = Instantiate(prefab_trait_single);
                        comp.SelfRect.SetParent(parent, false);
                        comp.optionID = GetTraitID;
                        ButtonValidator_selectTrait_Single validator = new ButtonValidator_selectTrait_Single(this, comp.optionID, entry);
                        comp.linkText = entry.TooltipID;
                        comp.isButtonToggle = true;

                        comp.Initialize(this, validator);
                        comp.SetText(entry.displaynameFull);

                        if (Character.Stats.Traits.Contains(entry))
                        {
                            comp.Toggle(true, true);
                            Traits_ToggleTraitByID(comp.optionID, entry);
                        }

                        buttonsByID.Add(comp.optionID, comp);
                        validatorsByID.Add(comp.optionID, validator);
                        comp.Validate();

                    }
                }
            }
        }
    }


    private void PopulateTraits_Spectrum_GroupListByType(RectTransform boxMiddle,  List<scr_Traits_Group> typeGroup,  Stats_Base basestat, string displayText,List<scr_Traits_Group> typeGroup2 = null)
    {
        // text prefab
        // text link prefab
        // text button prefab

        var groupBox = Instantiate(prefab_traitclass);
        groupBox.selfRect.SetParent(boxMiddle, false);
        groupBox.title.SetText(LocalizeDictionary.QueryThenParse( displayText), false);

        groupBox.basestat = basestat;
        trackedBoxes.Add(groupBox);

        foreach (scr_Traits_Group group in typeGroup)
        {
            //Debug.Log("Trait group ["+group.groupName+ "] listtype [" + group.sortTypeString + "]");
            if (group.SortType == Trait_Group_Type.SortedList || group.SortType == Trait_Group_Type.UnsortedList)
            {
                PopulateTraits_Spectrum_GroupList(groupBox.body, group);
            }
        }

        if (typeGroup2 != null)
        {
            foreach (scr_Traits_Group group in typeGroup2)
            {
                //Debug.Log("Trait group ["+group.groupName+ "] listtype [" + group.sortTypeString + "]");
                if (group.SortType == Trait_Group_Type.SortedList || group.SortType == Trait_Group_Type.UnsortedList)
                {
                    PopulateTraits_Spectrum_GroupList(groupBox.body, group);
                }
            }
        }
    }

    public scr_trait_spectrum prefab_trait_spectrum;
    public scr_traitclass prefab_traitclass;

    private void PopulateTraits_Spectrum_GroupList(RectTransform boxMiddle, scr_Traits_Group group)
    {
        Traits_editedGroups.Add(group);
        Traits trait = null;
        foreach (Traits t in group.entries)
        {
            if (Character.Stats.HasTrait(t))
            {
                trait = t; break;
            }
        }
        if (trait == null)
        {

            if (group.SortType != Trait_Group_Type.SortedList && group.SortType != Trait_Group_Type.UnsortedList) return;
            //if (tr.Type == Trait_Type.Body) continue;
            if (group.tags.Contains("do_not_use")) return;
            if (!group.allowPopulate) return;
            // invalid groups are still shown (arrows disabled), with their plain neutral entry
            var raceKeywords = new List<string>();
            Trait_Requirement.GetRaceKeywords(Character, raceKeywords);
            var neutral = group.IsValidFor(raceKeywords, Character.Appearance) ? group.GetNeutralValid(raceKeywords, Character.Appearance) : group.getNeutralinGroup();
            if (neutral == null) return;

            Character.Stats.AddTrait(neutral);
            trait = neutral;
        }

        var box = Instantiate(prefab_trait_spectrum);
        box.selfRect.SetParent(boxMiddle, false);

        box.traitMod.text = trait.trait_score.ToString("+0;-#");
        box.centerText.SetText(trait.displaynameFull, false, trait.TooltipID);

        var button_left = box.leftBTN;
        button_left.optionID = GetTraitID;
        ButtonValidator_selectTrait_ordered validator_left = new ButtonValidator_selectTrait_ordered(this, button_left.optionID, box, true);
        button_left.Initialize(this, validator_left);
        button_left.SetText("<");

        buttonsByID.Add(button_left.optionID, button_left);
        validatorsByID.Add(button_left.optionID, validator_left);
        Traits_AddTraitsbyID(button_left.optionID, trait);
        button_left.Validate();


        var button_right = box.rightBTN;
        button_right.optionID = GetTraitID;
        ButtonValidator_selectTrait_ordered validator_right = new ButtonValidator_selectTrait_ordered(this, button_right.optionID, box, false);
        button_right.Initialize(this, validator_right);
        button_right.SetText(">");

        buttonsByID.Add(button_right.optionID, button_right);
        validatorsByID.Add(button_right.optionID, validator_right);
        Traits_AddTraitsbyID(button_right.optionID, trait);
        button_right.Validate();
    }

    /// <summary>
    /// Skills block
    /// </summary>
    public RectTransform SkillBox_STR_left;
    public RectTransform SkillBox_STR_right;
    public RectTransform SkillBox_CON_left;
    public RectTransform SkillBox_CON_right;
    public RectTransform SkillBox_PSY_left;
    public RectTransform SkillBox_PSY_right;
    public RectTransform SkillBox_WIL_left;
    public RectTransform SkillBox_WIL_right;

    Dictionary<Skills, TMP_Text> SkillValues = new Dictionary<Skills, TMP_Text>();

    RectTransform prefab_SkillText, prefab_SkillNumber;
    private void PopulateAllSkills()
    {
        if (c == null || c.Template == null || c.Template.Skills == null) return;
        SkillValues.Clear();
        RectTransform boxLeft = null, boxRight = null;
        foreach(Skills s in c.Template.Skills)
        {
            switch (s.KeyAttribute)
            {
                case Skill_KeyAttribute.Strength:
                    boxLeft = SkillBox_STR_left;
                    boxRight = SkillBox_STR_right;
                    break;
                case Skill_KeyAttribute.Constitution:
                    boxLeft = SkillBox_CON_left;
                    boxRight = SkillBox_CON_right;
                    break;
                case Skill_KeyAttribute.Psyche:
                    boxLeft = SkillBox_PSY_left;
                    boxRight = SkillBox_PSY_right;
                    break;
                case Skill_KeyAttribute.Willpower:
                    boxLeft = SkillBox_WIL_left;
                    boxRight = SkillBox_WIL_right;
                    break;
            }

            if (boxLeft == null || boxRight == null) return;
            if (!boxLeft.gameObject.activeInHierarchy) return;
            if (!boxRight.gameObject.activeInHierarchy) return;

            PopulateSkills(boxLeft, boxRight, s);

        }
    }

    private void RefreshAllSkills()
    {
        foreach(Skills s in SkillValues.Keys)
        {
            SkillValues[s].text = s.GetSkillLevel().ToString("+0;-#");
        }
    }

    private void PopulateSkills(RectTransform boxLeft, RectTransform boxRight, Skills s)
    {
        RectTransform left = Instantiate(prefab_TraitLinkText);
        left.SetParent(boxLeft, false);
        left.GetComponent<scr_HoverableText>().SetText(s.DisplayName, false, s.ID);

        RectTransform right = Instantiate(prefab_TraitText);
        right.SetParent(boxRight, false);

        SkillValues.Add(s, right.GetComponent<TMP_Text>());
    }


    /// <summary>
    /// //////////////////////////////////
    /// </summary>


    public RectTransform prefab_inputField;
    private RectTransform inputField;
    private scr_inputFieldLink inputFieldLink;
    int[] stat;
    int[] statGrid; int[,] statGridLayout;

    public RectTransform panel_Default;
    public RectTransform panel_TraitSelect;

    public override void Notify(int optionID)
    {
        // reset conflict validators
        GetButton_TraitsConfirm.Reset();
        GetButton_ConfirmCharacter.Reset();

        //
        ButtonValidator validator = validatorsByID[optionID];
        I_ButtonClickable button = validator as I_ButtonClickable;
        if (button != null)
        {
            button.OnClickButton();
        }
        else
        {
            int i;
            switch (optionID)
            {

                case 0101:  //change first name
                   // InstantiateInputField(inputField, new Vector3(Screen.width / 2, Screen.height / 2), firstName);
                    //DisplaySingle(m_Camera, inputField, ContentSizeFitter.FitMode.PreferredSize, ContentSizeFitter.FitMode.PreferredSize, new Vector2(Screen.width, box2.sizeDelta.y), new Vector3(Screen.width / 2, Screen.height * 1 / 10), TextAnchor.UpperCenter);
                    break;
                case 0102:  //random first name
                    break;
                case 0103:  //change middle name
                    //InstantiateInputField(inputField, new Vector3(Screen.width / 2, Screen.height / 2), middleName);
                    break;
                case 0104:  //random middle name
                    break;
                case 0105:  //change last name
                   // InstantiateInputField(inputField, new Vector3(Screen.width / 2, Screen.height / 2), lastName);
                    break;
                case 0106:  //random last name
                    break;
                case 121:   //reroll once
                    stat = UtilityEX.RollStat();
                    break;
                case 122:   //reroll 10
                    stat = UtilityEX.RollStatRepeat(10);
                    break;
                case 123:   //maxvalues
                    stat = new int[4] { 18, 18, 18, 18 };
                    break;
                case 132://month left
                    SetBirthday(c.Birthday.AddMonths(-1)); break;
                case 133://month right
                    SetBirthday(c.Birthday.AddMonths(1)); break;
                case 134://day left
                    SetBirthday(c.Birthday.AddDays(-1)); break;
                case 135://day right
                    SetBirthday(c.Birthday.AddDays(1)); break;
                case 140:   //gender left
                    i = ((int)this.Character.Template.Appearance);
                    if ((i - 1) < 0)
                    {

                        SetGenderAppearance((Humanoid_GenderAppearance)(3));
                    }
                    else
                    {
                        SetGenderAppearance((Humanoid_GenderAppearance)(i - 1));
                    }
                    gender.text = $"<link=tooltip_GenderAppearance_{c.Template.Appearance.ToString()}>{LocalizeDictionary.QueryThenParse($"GenderAppearance_{c.Template.Appearance.ToString()}")}</link>";

                    break;
                    
                case 141:   //gender right
                    i = ((int)this.Character.Template.Appearance);
                    if ((i + 1) > 3)
                    {
                        SetGenderAppearance((Humanoid_GenderAppearance)(0));
                    }
                    else
                    {
                        SetGenderAppearance((Humanoid_GenderAppearance)(i+1));
                    }
                    gender.text = $"<link=tooltip_GenderAppearance_{c.Template.Appearance.ToString()}>{LocalizeDictionary.QueryThenParse($"GenderAppearance_{c.Template.Appearance.ToString()}")}</link>";

                    break;

                case 1300:
                    if (statGrid[0] != 0) SwapStatGrid(0, 0); break;
                case 1301:
                    if (statGrid[0] != 1) SwapStatGrid(0, 1); break;
                case 1302:
                    if (statGrid[0] != 2) SwapStatGrid(0, 2); break;
                case 1303:
                    if (statGrid[0] != 3) SwapStatGrid(0, 3); break;
                case 1310:
                    if (statGrid[1] != 0) SwapStatGrid(1, 0); break;
                case 1311:
                    if (statGrid[1] != 1) SwapStatGrid(1, 1); break;
                case 1312:
                    if (statGrid[1] != 2) SwapStatGrid(1, 2); break;
                case 1313:
                    if (statGrid[1] != 3) SwapStatGrid(1, 3); break;
                case 1320:
                    if (statGrid[2] != 0) SwapStatGrid(2, 0); break;
                case 1321:
                    if (statGrid[2] != 1) SwapStatGrid(2, 1); break;
                case 1322:
                    if (statGrid[2] != 2) SwapStatGrid(2, 2); break;
                case 1323:
                    if (statGrid[2] != 3) SwapStatGrid(2, 3); break;
                case 1330:
                    if (statGrid[3] != 0) SwapStatGrid(3, 0); break;
                case 1331:
                    if (statGrid[3] != 1) SwapStatGrid(3, 1); break;
                case 1332:
                    if (statGrid[3] != 2) SwapStatGrid(3, 2); break;
                case 1333:
                    if (statGrid[3] != 3) SwapStatGrid(3, 3); break;
                case 149:   //cancel select sex trait
                    panel_TraitSelect.gameObject.SetActive(false);
                    break;
                case 150:  // check trait confirm button
                    RebuildTraitsLeft();
                    panel_TraitSelect.gameObject.SetActive(false);
                    break;
                case 9998:
                    SaveCharacter();
                    scr_System_Serializer.current.SavePresetJSON(c);
                    if (refresh != null) refresh("something");
                    scr_System_SceneManager.current.UnloadLastCanvasFromScene();
                    break;
                case 9999:  //exit without saving
                    scr_System_SceneManager.current.UnloadLastCanvasFromScene();
                    break;
                default:
                    break;
            }
        }

        // gender / race changes can invalidate a selected body trait
        RevalidateBodyTraitSelections();

        UpdateStatsBase();
        UpdateAllTexts();
        RefreshAllDerivedStats();
        ValidateAll();
        //
        //ValidateLate();
    }


    protected void SaveCharacter()
    {
        c.FirstName = input_firstname.text;
        c.MiddleName = input_middlename.text;
        c.LastName = input_lastname.text;

        c.Template.stat_STR = c.Stats.Strength.BaseValue;
        c.Template.stat_CON = c.Stats.Constitution.BaseValue;
        c.Template.stat_PSY = c.Stats.Psyche.BaseValue;
        c.Template.stat_WIL = c.Stats.Willpower.BaseValue;

        c.Stats.ResetTrait();

        // replace (not append) the template's entries of the groups this editor shows; entries of other groups
        // (body traits, unresolvable ids such as sexual traits in safe mode) are kept. also cleans duplicates
        // that older saves accumulated by appending on every save.
        c.Template.traits.RemoveAll(id =>
        {
            var t = scr_System_Serializer.current.GetByNameOrID_Traits(id);
            return t != null && t.Parent != null && Traits_editedGroups.Contains(t.Parent);
        });
        foreach (var trait in Traits_getTraitbyID.Values)
        {
            c.Template.traits.Add(trait.ID);
        }
        Utility.DistinctInPlace(c.Template.traits);

        // sensitivity/size rows edited the template; the body holds the per-character copy
        if (c.Body != null) c.Body.ResetBodyTraitsFromTemplate();
        c.Stats.RefreshTraits();
    }

    protected void ValidateLate()
    {
        GetButtonByID(150).Validate();
        GetButtonByID(9998).Validate();
    }

    public RectTransform panel_TraitsLeft;
    public RectTransform panel_TraitsLeft_first;
    public void RebuildTraitsLeft()
    {
        // https://stackoverflow.com/questions/46358717/how-to-loop-through-and-destroy-all-children-of-a-game-object-in-unity

        Utility.DestroyAllChildrenFrom(panel_TraitsLeft, 1);

        bool hastrait = false;

        foreach(Traits t in Traits_getTraitbyID.Values)
        {
            if (t.isDisplayable)
            {
                hastrait = true;
                var a = Instantiate(prefab_text_link).GetComponent<scr_HoverableText>();
                a.SetText(t.displaynameFull, false, t.TooltipID);
                a.transform.SetParent(panel_TraitsLeft.transform, false);
            }
        }

        panel_TraitsLeft_first.gameObject.SetActive(!hastrait);

    }

    protected void SwapStatGrid(int index, int value)
    {
        Debug.Log("before swap " + statGrid[0] + " " + statGrid[1] + " " + statGrid[2] + " " + statGrid[3] + " ");
        int placeholder = statGrid[index];
        for(int i = 0; i < 4; i++)
        {
            if (statGrid[i] == value) 
            {
                statGrid[i] = placeholder;
                break;
            }
        }
        statGrid[index] = value;
        Debug.Log("after swap " + statGrid[0] + " " + statGrid[1] + " " + statGrid[2] + " " + statGrid[3] + " ");
    }

    protected void UpdateStatsBase()
    {
        for (int i = 0; i < 4; i++)
        {
            switch (statGrid[i])
            {
                case 0:
                    c.Stats.Strength.SetValue(stat[i]); break;
                case 1:
                    c.Stats.Constitution.SetValue(stat[i]); break;
                case 2:
                    c.Stats.Psyche.SetValue(stat[i]); break;
                case 3:
                    c.Stats.Willpower.SetValue(stat[i]); break;
            }
        }
    }



    /// <summary>
    /// Update Grid display related to skills
    /// </summary>
    protected void UpdateAllTexts()
    {
        for (int i = 0; i < 4; i++)
        {
            for (int j = 0; j < 4; j++)
            {
                if (statGrid[i] == j)
                {
                    GetButtonByID(statGridLayout[i, j]).SetText("O");
                }
                else
                {
                    GetButtonByID(statGridLayout[i, j]).SetText("-");
                }
            }
        }

        stat1.text = stat[0].ToString();
        stat2.text = stat[1].ToString();
        stat3.text = stat[2].ToString();
        stat4.text = stat[3].ToString();

        stat_strength_final.text = c.Stats.Strength.FinalValue().ToString();
        stat_constitution_final.text = c.Stats.Constitution.FinalValue().ToString();
        stat_psyche_final.text = c.Stats.Psyche.FinalValue().ToString();
        stat_willpower_final.text = c.Stats.Willpower.FinalValue().ToString();

        stat_strength_value.text = c.Stats.Strength.BaseValue.ToString();
        stat_constitution_value.text = c.Stats.Constitution.BaseValue.ToString();
        stat_psyche_value.text = c.Stats.Psyche.BaseValue.ToString();
        stat_willpower_value.text = c.Stats.Willpower.BaseValue.ToString();

        stat_strength_race.text = (c.Stats.Strength.FinalValue() - c.Stats.Strength.BaseValue).ToString("+0;-#");
        stat_constitution_race.text = (c.Stats.Constitution.FinalValue() - c.Stats.Constitution.BaseValue).ToString("+0;-#");
        stat_psyche_race.text = (c.Stats.Psyche.FinalValue() - c.Stats.Psyche.BaseValue).ToString("+0;-#");
        stat_willpower_race.text = (c.Stats.Willpower.FinalValue() - c.Stats.Willpower.BaseValue).ToString("+0;-#");



        //  https://stackoverflow.com/questions/348201/custom-numeric-format-string-to-always-display-the-sign
        mod_str.text = (Character.Stats.Strength.GetStatMod()).ToString("+0;-#");
        mod_con.text = (Character.Stats.Constitution.GetStatMod()).ToString("+0;-#");
        mod_psy.text = (Character.Stats.Psyche.GetStatMod()).ToString("+0;-#");
        mod_will.text = (Character.Stats.Willpower.GetStatMod()).ToString("+0;-#");

        foreach (var i in trackedBoxes) i.UpdateScore();

        RefreshTraitText(sensitivity_a, Character.Template.Sensitivity_A);
        RefreshTraitText(sensitivity_b, Character.Template.Sensitivity_B);
        RefreshTraitText(sensitivity_c, Character.Template.Sensitivity_C);
        RefreshTraitText(sensitivity_m, Character.Template.Sensitivity_M);
        RefreshTraitText(sensitivity_v, Character.Template.Sensitivity_V);

        RefreshTraitText(size_a, Character.Template.Size_A);

        RefreshRightPanelTexts();
        RefreshTraitText(size_b, Character.Template.Size_B);
        RefreshTraitText(size_p, Character.Template.Size_P);
        RefreshTraitText(size_v, Character.Template.Size_V);
        
        RefreshAllSkills();
    }

    List<scr_traitclass> trackedBoxes = new List<scr_traitclass>();

    protected void RefreshTraitText(scr_HoverableText box, Traits data)
    {
        if (data == null) box.SetText(" - ");
        else box.SetText(data.ID, false, $"{data.ID}_tooltip", true);
       // LayoutRebuilder.ForceRebuildLayoutImmediate(box.rectTransform);
    }

    protected void InstantiateInputField(RectTransform pointer, Vector3 Position, TextMeshProUGUI linkTarget)
    {
        pointer = Instantiate(prefab_inputField) as RectTransform;
        pointer.SetParent(this.transform, false);
        pointer.position = Position;
        pointer.GetComponent<scr_inputFieldLink>().Initialize(linkTarget, linkTarget.text);
        pointer.GetComponent<TMP_InputField>().ActivateInputField();
    }

    protected override void Start()
    {
        //buttons[b2_btn2].SetValidator(new ButtonValidator_HasSaveFiletoLoad());
        
    }

    ButtonValidator_AlwaysFalse button_alwaysInvalid;




    ButtonValidator_selectTrait_Confirm button_confirmTraits;
    ButtonValidator_selectTrait_Confirm GetButton_TraitsConfirm { get { return button_confirmTraits; } }

    ButtonValidator_confirmCharacter button_confirmCharacter;
    ButtonValidator_confirmCharacter GetButton_ConfirmCharacter { get { return button_confirmCharacter;} }

    public override void Initialize()
    {
        base.Initialize();
        buttonsByID = new Dictionary<int, scr_SelectableText>();
        validatorsByID = new Dictionary<int, ButtonValidator>();

        Traits_getTraitbyID = new Dictionary<int, Traits>();
        Traits_editedGroups.Clear();

        button_alwaysValid = new ButtonValidator_AlwaysTrue(this);

        button_alwaysInvalid = new ButtonValidator_AlwaysFalse(this);

        // attach validators
        statGrid = new int[4] { 0, 1, 2, 3 };
        statGridLayout = new int[4,4]{ 
            { 1300, 1301, 1302 , 1303 },{ 1310, 1311, 1312, 1313 },{ 1320, 1321, 1322, 1323 },{ 1330, 1331, 1332, 1333 } };
    }
   
    ContentSizeFitter fitter;
    Vector3 worldPointInRectangle;
    public void DisplaySingle(Camera m_camera, RectTransform box, ContentSizeFitter.FitMode horizontalFit, ContentSizeFitter.FitMode verticalFit, Vector2 sizeDelta
                , Vector3 ScreenPointTo, TextAnchor childAlignment)
    {
        fitter = box.GetComponent<ContentSizeFitter>();
        fitter.horizontalFit = horizontalFit;
        fitter.verticalFit = verticalFit;
        box.sizeDelta = sizeDelta;
        RectTransformUtility.ScreenPointToWorldPointInRectangle(box, ScreenPointTo, m_camera, out worldPointInRectangle);
        box.position = worldPointInRectangle;
        box.GetComponent<VerticalLayoutGroup>().childAlignment = childAlignment;
        box.gameObject.SetActive(true);

        //Debug.Log("Display box at x ["+ ScreenPointTo.x+ "] y [" + ScreenPointTo.y + "]");
    }

    class ButtonValidator_SelectOrigin : ButtonValidator, I_ButtonClickable
    {
        protected new scr_Canvas_CharacterEditor parent;
        bool isleft;
        public ButtonValidator_SelectOrigin(scr_Menu parent, bool isLeft) : base(parent)
        {
            this.parent = parent as scr_Canvas_CharacterEditor;
            this.isleft = isLeft;
            this.tooltip = LocalizeDictionary.QueryThenParse("wip_disabled");
        }

        public override bool IsButtonValid()
        {
            return false;
            this.state = ButtonValidator_States.Valid;
            tooltip = "";
            foreach (string s in this.parent.Character.Origin.disallowRace_ID)
            {
                if (this.parent.Character.Race.ID == s)
                {
                    this.state = ButtonValidator_States.Conflict;
                    string s2 = "Origin [" + parent.Character.Origin.DisplayName + "] conflict with Race [" + parent.Character.Race.DisplayName + "]\n";
                    tooltip += s2;
                    parent.GetButton_ConfirmCharacter.NotifyConflict(s2);
                }
            }

            foreach (string s in this.parent.Character.Origin.disallowRaceTemplate_ID)
            {
                if (this.parent.Character.RaceTemplate.ID == s)
                {
                    this.state = ButtonValidator_States.Conflict;
                    string s2 = "Origin [" + parent.Character.Origin.DisplayName + "] conflict with Race Modifier [" + parent.Character.RaceTemplate.DisplayName + "]\n";
                    tooltip += s2;
                    parent.GetButton_ConfirmCharacter.NotifyConflict(s2);
                }
            }

            return true;
        }

        public void OnClickButton()
        {
            var next = isleft ? scr_System_Serializer.current.MasterList.Character_Origins.GetItemBefore(parent.Character.Origin) : scr_System_Serializer.current.MasterList.Character_Origins.GetItemAfter(parent.Character.Origin);
            if (next != null)
            {
                parent.Character.Origin = next;
                parent.Character.StartingGift = scr_System_Serializer.current.MasterList.Character_Origin_StartingOptions.GetByID( next.availableOptionsID[0]);
            }
            parent.LoadOrigin();
            parent.LoadStartingGift();
        }
    }
    class ButtonValidator_SelectRace : ButtonValidator, I_ButtonClickable
    {
        protected new scr_Canvas_CharacterEditor parent;
        bool isleft;
        public ButtonValidator_SelectRace(scr_Menu parent, bool isLeft) : base(parent)
        {
            this.parent = parent as scr_Canvas_CharacterEditor;
            this.isleft = isLeft;
        }

        public override bool IsButtonValid()
        {
            if (this.parent.Character.Origin.forceRace_ID != "")
            {
                this.state = ButtonValidator_States.Invalid;
                tooltip = "Race enforced by Origin Selection";
                return false;
            }
            else
            {
                this.state = ButtonValidator_States.Valid;
                tooltip = "";
                return true;
            }
        }
        public void OnClickButton()
        {
            var nextval = isleft ? scr_System_Serializer.current.MasterList.humanoid_Races.GetItemBefore(parent.Character.Race) : scr_System_Serializer.current.MasterList.humanoid_Races.GetItemAfter(parent.Character.Race);

            if (nextval != null) parent.Character.Race = nextval;
            parent.LoadRace();
        }
    }
    class ButtonValidator_SelectraceTemplate : ButtonValidator, I_ButtonClickable
    {
        protected new scr_Canvas_CharacterEditor parent;
        bool isleft;
        public ButtonValidator_SelectraceTemplate(scr_Menu parent, bool isleft) : base(parent)
        {
            this.parent = parent as scr_Canvas_CharacterEditor;
            this.isleft = isleft;
        }

        public override bool IsButtonValid()
        {
            if (this.parent.Character.Origin.forceRaceTemplate_ID != "")
            {
                this.state = ButtonValidator_States.Invalid;
                tooltip = "Race Modifier enforced by Origin Selection";
                return false;
            }
            else
            {
                this.state = ButtonValidator_States.Valid;
                tooltip = "";
                return true;
            }
        }
        public void OnClickButton()
        {
            var c = parent.Character;
            var nextval = isleft ? scr_System_Serializer.current.MasterList.humanoid_RaceTemplates.GetItemBefore(c.RaceTemplate, c.Origin, c.StartingGift, c.Race) : 
                                    scr_System_Serializer.current.MasterList.humanoid_RaceTemplates.GetItemAfter(parent.Character.RaceTemplate, c.Origin, c.StartingGift, c.Race);
            if (nextval != null) parent.Character.RaceTemplate = nextval;
            parent.LoadRaceTemplate();
        }
    }
    class ButtonValidator_SelectStartingOption : ButtonValidator, I_ButtonClickable
    {
        protected new scr_Canvas_CharacterEditor parent;
        bool isleft;
        public ButtonValidator_SelectStartingOption(scr_Menu parent, bool isleft) : base(parent)
        {
            this.parent = parent as scr_Canvas_CharacterEditor;
            this.isleft = isleft;
            this.tooltip = LocalizeDictionary.QueryThenParse("wip_disabled");
        }

        public override bool IsButtonValid()
        {
            return false;
            this.state = ButtonValidator_States.Valid;
            tooltip = "";
            return true;
        }
        public void OnClickButton()
        {
            string nextgiftID = "";
            Character_Origin_startingOption nextgift = null;
            if (isleft) {
                if (parent.currentStartingOptionIndex - 1 >= 0) parent.currentStartingOptionIndex -= 1;
                else parent.currentStartingOptionIndex = parent.Character.Origin.availableOptionsID.Length - 1;
                nextgiftID = parent.Character.Origin.availableOptionsID[parent.currentStartingOptionIndex];
            } 
            else {
                if (parent.currentStartingOptionIndex + 1 < parent.Character.Origin.availableOptionsID.Length) parent.currentStartingOptionIndex += 1;
                else parent.currentStartingOptionIndex = 0;
                nextgiftID = parent.Character.Origin.availableOptionsID[parent.currentStartingOptionIndex];
            }
            nextgift = scr_System_Serializer.current.MasterList.Character_Origin_StartingOptions.GetByID(nextgiftID);
            if (nextgift != null) parent.Character.StartingGift = nextgift;
            parent.LoadStartingGift();

        }
    }
    class ButtonValidator_SelectGender : ButtonValidator
    {
        protected new scr_Canvas_CharacterEditor parent;
        public ButtonValidator_SelectGender(scr_Menu parent) : base(parent)
        {
            this.parent = parent as scr_Canvas_CharacterEditor;
        }

        public override bool IsButtonValid()
        {
            this.state = ButtonValidator_States.Valid;
            tooltip = "";
            return true;
        }
    }

    class ButtonValidator_selectTraitPanel : ButtonValidator, I_ButtonClickable
    {
        protected new scr_Canvas_CharacterEditor parent;
        scr_SelectableText btn;
        public ButtonValidator_selectTraitPanel(scr_Menu parent, scr_SelectableText btn) : base(parent)
        {
            this.parent = parent as scr_Canvas_CharacterEditor;
            this.btn = btn;

        }

        public override bool IsButtonValid()    // always valid
        {
            if (this.parent.panel_TraitSelect.gameObject.activeSelf == true)
            {
                btn.Toggle(true, true);
                tooltip = "A Trait selection panel is already active!";
            }
            else
            {
                btn.Toggle(true, false);
                tooltip = "";
            }
            this.state = ButtonValidator_States.Valid;
            return true;
        }

        public void OnClickButton()
        {
            //        case 146:   //select sex trait
            parent.panel_TraitSelect.gameObject.SetActive(true);
        }
    }



    /// <summary>
    /// ////////////////
    /// </summary>
    class ButtonValidator_selectSensitivity_B : ButtonValidator, I_ButtonClickable
    {
        protected new scr_Canvas_CharacterEditor parent;
        bool isleft;
        public ButtonValidator_selectSensitivity_B(scr_Menu parent, bool isleft) : base(parent)
        {
            this.parent = parent as scr_Canvas_CharacterEditor;
            this.isleft = isleft;
        }

        public override bool IsButtonValid()
        {
            if (parent.Character.Template.Sensitivity_B == null) return false;
            if (isleft) return parent.Character.Template.Sensitivity_B.GetPreviousValidInGroup(parent.Character) != null;
            else return parent.Character.Template.Sensitivity_B.GetNextValidInGroup(parent.Character) != null;
        }
        public void OnClickButton()
        {
            //parent.Character.Template.Sensitivity_B = parent.Character.Template.Sensitivity_B.GetNextValidInGroup(parent.Character);
            if (parent.Character.Template.Sensitivity_B == null) return;
            if (isleft) parent.Character.Template.Sensitivity_B = parent.Character.Template.Sensitivity_B.GetPreviousValidInGroup(parent.Character);
            else parent.Character.Template.Sensitivity_B = parent.Character.Template.Sensitivity_B.GetNextValidInGroup(parent.Character);
        }
    }

    class ButtonValidator_selectSensitivity_M : ButtonValidator, I_ButtonClickable
    {
        protected new scr_Canvas_CharacterEditor parent;
        bool isleft;
        public ButtonValidator_selectSensitivity_M(scr_Menu parent, bool isleft) : base(parent)
        {
            this.parent = parent as scr_Canvas_CharacterEditor;
            this.isleft = isleft;
        }

        public override bool IsButtonValid()
        {
            if (parent.Character.Template.Sensitivity_M == null) return false;
            if (isleft) return parent.Character.Template.Sensitivity_M.GetPreviousValidInGroup(parent.Character) != null;
            else return parent.Character.Template.Sensitivity_M.GetNextValidInGroup(parent.Character) != null;
        }
        public void OnClickButton()
        {
            if (parent.Character.Template.Sensitivity_M == null) return;
            if (isleft) parent.Character.Template.Sensitivity_M = parent.Character.Template.Sensitivity_M.GetPreviousValidInGroup(parent.Character);
            else parent.Character.Template.Sensitivity_M = parent.Character.Template.Sensitivity_M.GetNextValidInGroup(parent.Character);
        }
    }
    class ButtonValidator_selectSensitivity_C : ButtonValidator, I_ButtonClickable
    {
        protected new scr_Canvas_CharacterEditor parent;
        bool isleft;
        public ButtonValidator_selectSensitivity_C(scr_Menu parent, bool isleft) : base(parent)
        {
            this.parent = parent as scr_Canvas_CharacterEditor;
            this.isleft = isleft;
        }

        public override bool IsButtonValid()
        {
            if (parent.Character.Template.Sensitivity_C == null) return false;
            if (isleft) return (parent.Character.Template.Sensitivity_C.GetPreviousValidInGroup(parent.Character) != null);
            else return (parent.Character.Template.Sensitivity_C.GetNextValidInGroup(parent.Character) != null);
        }
        public void OnClickButton()
        {
            //parent.Character.Template.Sensitivity_C = parent.Character.Template.Sensitivity_C.GetPreviousValidInGroup(parent.Character);
            if (parent.Character.Template.Sensitivity_C == null) return;
            if (isleft) parent.Character.Template.Sensitivity_C = parent.Character.Template.Sensitivity_C.GetPreviousValidInGroup(parent.Character);
            else parent.Character.Template.Sensitivity_C = parent.Character.Template.Sensitivity_C.GetNextValidInGroup(parent.Character);
        }
    }

    class ButtonValidator_selectSensitivity_V : ButtonValidator, I_ButtonClickable
    {
        protected new scr_Canvas_CharacterEditor parent;
        bool isleft;
        public ButtonValidator_selectSensitivity_V(scr_Menu parent, bool isleft) : base(parent)
        {
            this.parent = parent as scr_Canvas_CharacterEditor;
            this.isleft = isleft;
        }

        public override bool IsButtonValid()
        {
            if (parent.Character.Template.Sensitivity_V == null) return false;
            if (isleft) return (parent.Character.Template.Sensitivity_V.GetPreviousValidInGroup(parent.Character) != null);
            else return (parent.Character.Template.Sensitivity_V.GetNextValidInGroup(parent.Character) != null);
        }
        public void OnClickButton()
        {
            if (parent.Character.Template.Sensitivity_V == null) return;
            if (isleft) parent.Character.Template.Sensitivity_V = parent.Character.Template.Sensitivity_V.GetPreviousValidInGroup(parent.Character);
            else parent.Character.Template.Sensitivity_V = parent.Character.Template.Sensitivity_V.GetNextValidInGroup(parent.Character);
        }
    }

    class ButtonValidator_selectSensitivity_A : ButtonValidator, I_ButtonClickable
    {
        protected new scr_Canvas_CharacterEditor parent;
        bool isleft;
        public ButtonValidator_selectSensitivity_A(scr_Menu parent, bool isleft) : base(parent)
        {
            this.parent = parent as scr_Canvas_CharacterEditor;
            this.isleft = isleft;
        }

        public override bool IsButtonValid()
        {
            if (parent.Character.Template.Sensitivity_A == null) return false;
            //
            if (isleft) return (parent.Character.Template.Sensitivity_A.GetPreviousValidInGroup(parent.Character) != null);
            else return (parent.Character.Template.Sensitivity_A.GetNextValidInGroup(parent.Character) != null);
        }
        public void OnClickButton()
        {
            if (parent.Character.Template.Sensitivity_A == null) return ;
            if (isleft) parent.Character.Template.Sensitivity_A = parent.Character.Template.Sensitivity_A.GetPreviousValidInGroup(parent.Character);
            else parent.Character.Template.Sensitivity_A = parent.Character.Template.Sensitivity_A.GetNextValidInGroup(parent.Character);
        }
    }

    class ButtonValidator_selectSize_B : ButtonValidator, I_ButtonClickable
    {
        protected new scr_Canvas_CharacterEditor parent;
        bool isleft;
        public ButtonValidator_selectSize_B(scr_Menu parent, bool isleft) : base(parent)
        {
            this.parent = parent as scr_Canvas_CharacterEditor;
            this.tooltip = LocalizeDictionary.QueryThenParse("ui_tooltip_trait_size");
            this.isleft = isleft;
        }

        public override bool IsButtonValid()
        {
            if (parent.Character.Template.Size_B == null) return false;
            if (isleft) return (parent.Character.Template.Size_B.GetPreviousValidInGroup(parent.Character) != null);
            else return (parent.Character.Template.Size_B.GetNextValidInGroup(parent.Character) != null);
        }
        public void OnClickButton()
        {
            if (parent.Character.Template.Size_B == null) return;
            if (isleft) parent.Character.Template.Size_B = parent.Character.Template.Size_B.GetPreviousValidInGroup(parent.Character);
            else parent.Character.Template.Size_B = parent.Character.Template.Size_B.GetNextValidInGroup(parent.Character);
        }
    }

    class ButtonValidator_selectSize_P : ButtonValidator, I_ButtonClickable
    {
        protected new scr_Canvas_CharacterEditor parent;
        bool isleft;
        public ButtonValidator_selectSize_P(scr_Menu parent, bool isleft) : base(parent)
        {
            this.parent = parent as scr_Canvas_CharacterEditor;
            this.tooltip = LocalizeDictionary.QueryThenParse("ui_tooltip_trait_size");
            this.isleft = isleft;
        }

        public override bool IsButtonValid()
        {
            if (parent.Character.Template.Size_P == null) return false;
            if (isleft) return (parent.Character.Template.Size_P.GetPreviousValidInGroup(parent.Character) != null);
            else return (parent.Character.Template.Size_P.GetNextValidInGroup(parent.Character) != null);
        }
        public void OnClickButton()
        {
            if (parent.Character.Template.Size_P == null) return;
            if (isleft) parent.Character.Template.Size_P = parent.Character.Template.Size_P.GetPreviousValidInGroup(parent.Character);
            else parent.Character.Template.Size_P = parent.Character.Template.Size_P.GetNextValidInGroup(parent.Character);
        }
    }

    class ButtonValidator_selectSize_V : ButtonValidator, I_ButtonClickable
    {
        protected new scr_Canvas_CharacterEditor parent;
        bool isleft;
        public ButtonValidator_selectSize_V(scr_Menu parent, bool isleft) : base(parent)
        {
            this.parent = parent as scr_Canvas_CharacterEditor;
            this.tooltip = LocalizeDictionary.QueryThenParse("ui_tooltip_trait_size");
            this.isleft = isleft;
        }

        public override bool IsButtonValid()
        {
            if (parent.Character.Template.Size_V == null) return false;
            if (isleft) return (parent.Character.Template.Size_V.GetPreviousValidInGroup(parent.Character) != null);
            else return (parent.Character.Template.Size_V.GetNextValidInGroup(parent.Character) != null);
        }
        public void OnClickButton()
        {
            if (parent.Character.Template.Size_V == null) return;
            if (isleft) parent.Character.Template.Size_V = parent.Character.Template.Size_V.GetPreviousValidInGroup(parent.Character);
            else parent.Character.Template.Size_V = parent.Character.Template.Size_V.GetNextValidInGroup(parent.Character);
        }
    }

    class ButtonValidator_selectSize_A : ButtonValidator, I_ButtonClickable
    {
        protected new scr_Canvas_CharacterEditor parent;
        bool isleft;
        public ButtonValidator_selectSize_A(scr_Menu parent, bool isleft) : base(parent)
        {
            this.parent = parent as scr_Canvas_CharacterEditor;
            this.tooltip = LocalizeDictionary.QueryThenParse("ui_tooltip_trait_size");
            this.isleft = isleft;
        }

        public override bool IsButtonValid()
        {
            if (parent.Character.Template.Size_A == null) return false;
            if (isleft) return (parent.Character.Template.Size_A.GetPreviousValidInGroup(parent.Character) != null);
            else return (parent.Character.Template.Size_A.GetNextValidInGroup(parent.Character) != null);
        }
        public void OnClickButton()
        {
            if (parent.Character.Template.Size_A == null) return;
            if (isleft) parent.Character.Template.Size_A = parent.Character.Template.Size_A.GetPreviousValidInGroup(parent.Character);
            else parent.Character.Template.Size_A = parent.Character.Template.Size_A.GetNextValidInGroup(parent.Character);
        }
    }

    class ButtonValidator_selectTrait_ordered : ButtonValidator, I_ButtonClickable
    {
        protected new scr_Canvas_CharacterEditor parent;
        private Traits trait;
        private int buttonID;
        scr_trait_spectrum box;
        bool isLeft = false;
        public ButtonValidator_selectTrait_ordered(scr_Menu parent, int buttonID, scr_trait_spectrum box, bool isLeft) : base(parent)
        {
            this.parent = parent as scr_Canvas_CharacterEditor;
            this.buttonID = buttonID;
            this.box = box;
            this.isLeft = isLeft;
        }

        Trait_Type A, B;
        int statMod, totalScore;
        public override bool IsButtonValid()
        {

            var current_trait = parent.Traits_GetTraitByID(buttonID);
            bool result = isLeft ? current_trait.GetPreviousValidInGroup(parent.Character) != null : current_trait.GetNextValidInGroup(parent.Character) != null;
            //if (result == false) return result;

            if (result != false)
            {
                this.state = ButtonValidator_States.Valid;
                this.tooltip = "";
            }
            else if (current_trait.Parent != null && !current_trait.Parent.IsValidFor(parent.Character))
            {
                // group shown but not selectable for this character
                this.state = ButtonValidator_States.Invalid;
                this.tooltip = LocalizeDictionary.QueryThenParse("char_editor_selectTrait_requirementNotMet");
                return false;
            }

            Trait_Type current = parent.Traits_GetTraitByID(buttonID).type;
            switch (current)
            {
                case Trait_Type.Strength:
                case Trait_Type.Sexual_Strength:
                    statMod = parent.Character.Stats.Strength.GetStatMod();
                    A = Trait_Type.Strength;
                    B = Trait_Type.Sexual_Strength;
                    break;
                case Trait_Type.Constitution:
                case Trait_Type.Sexual_Constitution:
                    statMod = parent.Character.Stats.Constitution.GetStatMod();
                    A = Trait_Type.Constitution;
                    B = Trait_Type.Sexual_Constitution;
                    break;
                case Trait_Type.Psyche:
                case Trait_Type.Sexual_Psyche:
                    statMod = parent.Character.Stats.Psyche.GetStatMod();
                    A = Trait_Type.Psyche;
                    B = Trait_Type.Sexual_Psyche;
                    break;
                case Trait_Type.Willpower:
                case Trait_Type.Sexual_Willpower:
                    statMod = parent.Character.Stats.Willpower.GetStatMod();
                    A = Trait_Type.Willpower;
                    B = Trait_Type.Sexual_Willpower;
                    break;
                default:
                    break;
            }
            totalScore = 0;
            foreach (Traits t in parent.Traits_getTraitbyID.Values) if (t.type == A || t.type == B) totalScore += t.trait_score;
            if (statMod < totalScore)
            {
                if (result != false) this.state = ButtonValidator_States.Conflict;
                //tooltip += "conflict message buttonID [" + this.buttonID + "] currentTrait [" + parent.Traits_GetTraitByID(buttonID).displayname + "] current [" + current.ToString() + "] A.type [" + A.ToString() + "] B.type [" + B.ToString() + "] statscore [" + statMod + "] totalscore [" + totalScore + "]";

                tooltip += LocalizeDictionary.QueryThenParse($"char_editor_selectTrait_{A}_scoreError").Replace("$score$", $"{statMod.ToString("+0;-#")}");
                
                tooltip += "trait score in [" + A.ToString() + "] must be equal or smaller than corresponding stat modifier " + statMod;
                parent.GetButton_TraitsConfirm.NotifyConflict(tooltip);
            }
            return result;
        }

        public void OnClickButton()
        {
            if (isLeft)
            {
                trait = parent.Traits_GetTraitByID(buttonID).GetPreviousValidInGroup(parent.Character);
            }
            else
            {
                trait = parent.Traits_GetTraitByID(buttonID).GetNextValidInGroup(parent.Character);
            }

            parent.Traits_SetTraitByID(buttonID, trait);
            box.traitMod.text = trait.trait_score.ToString("+0;-#");
            box.centerText.SetText(trait.displaynameFull, false, trait.TooltipID);
        }
    }


    class ButtonValidator_selectTrait_Confirm : ButtonValidator, I_ConflictCatcher
    {
        protected new scr_Canvas_CharacterEditor parent;
        int id;
        public ButtonValidator_selectTrait_Confirm(scr_Menu parent, int id) : base(parent)
        {
            this.parent = parent as scr_Canvas_CharacterEditor;
            this.id = id;
        }

        public override bool IsButtonValid()
        {
            if (this.tooltip == "")
            {
                this.state = ButtonValidator_States.Valid;
                return true;
            }
            else
            {
                this.state = ButtonValidator_States.Invalid;
                parent.GetButton_ConfirmCharacter.NotifyConflict("Trait Selection is invalid due to stat score change. Please re-select.");
                return false;
            }
        }

        public void NotifyConflict(string tooltip)
        {
            if (!this.tooltip.Contains(tooltip)) {
                if (this.tooltip == "") this.tooltip += tooltip;
                else this.tooltip += "\n" + tooltip;
                parent.GetButtonByID(id).Validate();
            }
        }
        public void Reset(){
            this.state = ButtonValidator_States.Valid;
            tooltip = "";
        }
    }

    class ButtonValidator_selectTrait_Single : ButtonValidator, I_ButtonClickable
    {
        protected new scr_Canvas_CharacterEditor parent;
        private int buttonID;
        private Traits trait;
        public ButtonValidator_selectTrait_Single(scr_Menu parent, int buttonID, Traits trait) : base(parent)
        {
            this.parent = parent as scr_Canvas_CharacterEditor;
            this.buttonID = buttonID;
            this.trait = trait;
        }

        public override bool IsButtonValid()
        {
            // trait requirements (race keywords / appearance); an already selected trait stays clickable so it can be removed
            if (!trait.IsValidFor(parent.Character) && !parent.Traits_getTraitbyID.ContainsKey(buttonID))
            {
                this.state = ButtonValidator_States.Invalid;
                this.tooltip = LocalizeDictionary.QueryThenParse("char_editor_selectTrait_requirementNotMet");
                return false;
            }
            this.state = ButtonValidator_States.Valid;
            this.tooltip = "";
            return true;
        }

        public void OnClickButton()
        {
            parent.GetButtonByID(buttonID).GetComponent<scr_SelectableText>().Toggle();
            parent.Traits_ToggleTraitByID(buttonID, trait);

        }
    }

    class ButtonValidator_confirmCharacter : ButtonValidator, I_ConflictCatcher
    {
        protected new scr_Canvas_CharacterEditor parent;
        private int id;
        public ButtonValidator_confirmCharacter(scr_Menu parent, int id) : base(parent)
        {
            this.parent = parent as scr_Canvas_CharacterEditor;
            this.id = id;
        }

        public override bool IsButtonValid()
        {
            if (parent.c.Stats.Strength.FinalValue() < 1) { AddTooltip("Final STR stat must be at least 1"); }
            if (parent.c.Stats.Constitution.FinalValue() < 1) { AddTooltip("Final CON stat must be at least 1"); }
            if (parent.c.Stats.Psyche.FinalValue() < 1) { AddTooltip("Final PSY stat must be at least 1"); }
            if (parent.c.Stats.Willpower.FinalValue() < 1) { AddTooltip("Final WIL stat must be at least 1"); }

            if (this.tooltip == "" || this.tooltip.Length < 1)
            {
                this.state = ButtonValidator_States.Valid;
                return true;
            }
            else
            {
                this.state = ButtonValidator_States.Invalid;
                return false;
            }
        }

        private void AddTooltip(string tooltip)
        {
            if (!this.tooltip.Contains(tooltip))
            {
                if (this.tooltip == "") this.tooltip += tooltip;
                else this.tooltip += "\n" + tooltip;
            }
        }



        public void NotifyConflict(string tooltip)
        {
            if (!this.tooltip.Contains(tooltip))
            {
                if (this.tooltip == "") this.tooltip += tooltip;
                else this.tooltip += "\n" + tooltip;
                parent.GetButtonByID(id).Validate();
            }

        }

        public void Reset()
        {
            this.state = ButtonValidator_States.Valid;
            tooltip = "";
        }
    }

    /// <summary>
    /// Wrapping step through a list; index -1 (nothing found) steps to the first / last entry.
    /// </summary>
    static int StepIndex(int index, int count, bool isleft)
    {
        if (count < 1) return -1;
        if (index < 0) return isleft ? count - 1 : 0;
        return isleft ? (index - 1 + count) % count : (index + 1) % count;
    }

    /// <summary>
    /// Hairstyle rows edit the template's defaultHairstyle: the preset save only writes the template, and the
    /// editor's character has no body (Character_Body copies the template seed when the character is created).
    /// The first entry is "random" (null seed = random default hairstyle on creation).
    /// </summary>
    class ButtonValidator_SelectHairstyle : ButtonValidator, I_ButtonClickable
    {
        protected new scr_Canvas_CharacterEditor parent;
        bool isleft;
        public ButtonValidator_SelectHairstyle(scr_Menu parent, bool isleft) : base(parent)
        {
            this.parent = parent as scr_Canvas_CharacterEditor;
            this.isleft = isleft;
        }

        public override bool IsButtonValid()
        {
            var list = parent.Hairstyles;
            bool result = list != null && list.Count > 0;
            this.state = result ? ButtonValidator_States.Valid : ButtonValidator_States.Invalid;
            return result;
        }

        public void OnClickButton()
        {
            var list = parent.Hairstyles;
            var template = parent.Character.Template;
            if (list == null || list.Count < 1 || template == null) return;

            // option 0 = random, option i + 1 = list[i]
            var def = template.defaultHairstyle != null ? template.defaultHairstyle.GetBase() : null;
            int i = StepIndex(def != null ? list.IndexOf(def) + 1 : 0, list.Count + 1, isleft);

            if (i == 0) template.defaultHairstyle = null;
            else
            {
                // subcategory and slot options start at the new definition's defaults
                var instance = new Hairstyle_Instance();
                instance.InitializeFromDef(list[i - 1]);
                template.defaultHairstyle = instance;
            }
            parent.RebuildHairstyleRows();
        }
    }

    class ButtonValidator_SelectHairstyleSubtype : ButtonValidator, I_ButtonClickable
    {
        protected new scr_Canvas_CharacterEditor parent;
        bool isleft;
        public ButtonValidator_SelectHairstyleSubtype(scr_Menu parent, bool isleft) : base(parent)
        {
            this.parent = parent as scr_Canvas_CharacterEditor;
            this.isleft = isleft;
        }

        public override bool IsButtonValid()
        {
            var def = parent.TemplateHairstyleDef;
            bool result = def != null && def.subCategories != null && def.subCategories.Count > 1;
            this.state = result ? ButtonValidator_States.Valid : ButtonValidator_States.Invalid;
            return result;
        }

        public void OnClickButton()
        {
            var instance = parent.Character.Template.defaultHairstyle;
            var def = parent.TemplateHairstyleDef;
            if (def == null || def.subCategories == null || def.subCategories.Count < 1) return;

            int i = StepIndex(def.subCategories.FindIndex(x => x.ID == instance.SubCategoryID), def.subCategories.Count, isleft);
            instance.SetSubCategory(def.subCategories[i].ID);
        }
    }

    class ButtonValidator_SelectHairstyleSlot : ButtonValidator, I_ButtonClickable
    {
        protected new scr_Canvas_CharacterEditor parent;
        string keyword;
        bool isleft;
        public ButtonValidator_SelectHairstyleSlot(scr_Menu parent, string keyword, bool isleft) : base(parent)
        {
            this.parent = parent as scr_Canvas_CharacterEditor;
            this.keyword = keyword;
            this.isleft = isleft;
        }

        public override bool IsButtonValid()
        {
            var def = parent.TemplateHairstyleDef;
            var slot = def != null ? def.GetSlot(keyword) : null;
            bool result = slot != null && slot.GetOptions().Count > 1;
            this.state = result ? ButtonValidator_States.Valid : ButtonValidator_States.Invalid;
            return result;
        }

        public void OnClickButton()
        {
            var instance = parent.Character.Template.defaultHairstyle;
            var def = parent.TemplateHairstyleDef;
            var slot = def != null ? def.GetSlot(keyword) : null;
            if (slot == null) return;

            var options = slot.GetOptions();
            if (options.Count < 1) return;

            var current = instance.GetSelectedSlotOption(keyword);
            int i = StepIndex(current != null ? options.FindIndex(x => x.ID == current.ID) : -1, options.Count, isleft);
            instance.SetSlotOption(keyword, options[i].ID);
        }
    }

    /// <summary>
    /// traits_BODY rows edit the template's traits_body (copied onto the body by ResetBodyTraitsFromTemplate on save).
    /// The current entry is re-read every validation, so race / gender changes need no repopulation.
    /// </summary>
    class ButtonValidator_selectBodyTrait : ButtonValidator, I_ButtonClickable
    {
        protected new scr_Canvas_CharacterEditor parent;
        scr_Traits_Group group;
        bool isLeft;
        public ButtonValidator_selectBodyTrait(scr_Menu parent, scr_Traits_Group group, bool isLeft) : base(parent)
        {
            this.parent = parent as scr_Canvas_CharacterEditor;
            this.group = group;
            this.isLeft = isLeft;
        }

        public override bool IsButtonValid()
        {
            var c = parent.Character;
            if (!group.IsValidFor(c))
            {
                // group shown but not selectable for this character
                this.state = ButtonValidator_States.Invalid;
                this.tooltip = LocalizeDictionary.QueryThenParse("char_editor_selectTrait_requirementNotMet");
                return false;
            }

            this.state = ButtonValidator_States.Valid;
            this.tooltip = "";
            var current = parent.GetTemplateBodyTrait(group);
            if (current == null) return false;
            return isLeft ? current.GetPreviousValidInGroup(c) != null : current.GetNextValidInGroup(c) != null;
        }

        public void OnClickButton()
        {
            var c = parent.Character;
            var current = parent.GetTemplateBodyTrait(group);
            if (current == null) return;

            var next = isLeft ? current.GetPreviousValidInGroup(c) : current.GetNextValidInGroup(c);
            if (next != null) BodyTraitUtility.SetInList(c.Template.traits_body, next);
        }
    }
    public TMP_InputField input_firstname, input_middlename, input_lastname;

    public void OnValueChanged_FirstName(string s)
    {
        if (this.c == null) return;
        this.c.FirstName = input_firstname.text;
        UpdateNames();
    }
    public void OnValueChanged_MiddleName(string s)
    {
        if (this.c == null) return;
        this.c.MiddleName = input_middlename.text;
        UpdateNames();
    }
    public void OnValueChanged_LastName(string s)
    {
        if (this.c == null) return;
        this.c.LastName = input_lastname.text;
        UpdateNames();
    }

    public rightPanel RightPanel;

    /// <summary>
    /// Right panel : main hairstyle, hairstyle subtypes / slot addons, non-sex body traits
    /// </summary>
    List<int> hairstyleMainIDs = new List<int>();
    List<int> hairstyleDynamicIDs = new List<int>();
    List<int> bodyTraitPanelIDs = new List<int>();

    // boxes whose texts RefreshRightPanelTexts rewrites after every click
    scr_traitSelectBox hairstyleSubtypeBox = null;
    List<KeyValuePair<string, scr_traitSelectBox>> hairstyleSlotBoxes = new List<KeyValuePair<string, scr_traitSelectBox>>();
    List<KeyValuePair<scr_Traits_Group, scr_traitSelectBox>> bodyTraitBoxes = new List<KeyValuePair<scr_Traits_Group, scr_traitSelectBox>>();

    /// <summary>
    /// After a click (gender / race change): a row whose stored entry is no longer valid while its group still is
    /// gets the group default stored (e.g. curvy -> average when switching to male)
    /// </summary>
    void RevalidateBodyTraitSelections()
    {
        if (c == null || c.Template == null) return;
        foreach (var pair in bodyTraitBoxes)
        {
            var group = pair.Key;
            if (!group.IsValidFor(c)) continue;
            var stored = BodyTraitUtility.FindInGroup(c.Template.traits_body, group.ID);
            if (stored == null || stored.IsValidFor(c)) continue;
            var t = GetTemplateBodyTrait(group);
            if (t != null && t.IsValidFor(c)) BodyTraitUtility.SetInList(c.Template.traits_body, t);
        }
    }

    List<Hairstyle_Base> Hairstyles
    {
        get
        {
            var master = scr_System_Serializer.current != null ? scr_System_Serializer.current.MasterList : null;
            return master != null && master.BodyPartBases != null ? master.BodyPartBases.BodyPart_Hairstyles : null;
        }
    }

    /// <summary> definition of the template's seed hairstyle; null = random default hairstyle </summary>
    Hairstyle_Base TemplateHairstyleDef
    {
        get
        {
            var h = c != null && c.Template != null ? c.Template.defaultHairstyle : null;
            return h != null ? h.GetBase() : null;
        }
    }

    /// <summary>
    /// The template's traits_BODY entry as the body will get it (FillMissingBodyTraits rules): the stored entry if
    /// valid, else the group default. Invalid groups show their stored entry, else the neutral one.
    /// </summary>
    Traits GetTemplateBodyTrait(scr_Traits_Group group)
    {
        var stored = BodyTraitUtility.FindInGroup(c.Template.traits_body, group.ID);
        if (stored != null && stored.IsValidFor(c)) return stored;

        var raceKeywords = new List<string>();
        Trait_Requirement.GetRaceKeywords(c, raceKeywords);
        var def = BodyTraitUtility.GetDefaultValid(group, c.Appearance, raceKeywords);
        if (def != null) return def;
        return stored != null ? stored : group.getNeutralinGroup();
    }

    /// <summary>
    /// Same init order as the scene buttons in InitializeWithArgument / PopulateTraits_* :
    /// optionID, then Initialize (reads the serialized replaceText), only then SetText overwrites it.
    /// Validate runs the validator once so texts and colors are correct immediately.
    /// </summary>
    protected void RegisterPanelButton(scr_SelectableText button, ButtonValidator validator, List<int> trackedIDs, string text = null)
    {
        button.optionID = GetTraitID;
        button.Initialize(this, validator);
        if (text != null) button.SetText(text);
        buttonsByID.Add(button.optionID, button);
        validatorsByID.Add(button.optionID, validator);
        trackedIDs.Add(button.optionID);
        button.Validate();
    }

    protected void UnregisterButtons(List<int> trackedIDs)
    {
        foreach (int id in trackedIDs)
        {
            buttonsByID.Remove(id);
            validatorsByID.Remove(id);
        }
        trackedIDs.Clear();
    }

    protected void PopulateHairstylePanel()
    {
        var rp = RightPanel;
        if (rp == null || rp.MainHairstyle == null) return;

        UnregisterButtons(hairstyleMainIDs);
        rp.MainHairstyle.title.SetText(LocalizeDictionary.QueryThenParse("char_editor_hairstyle", "Hairstyle"));
        RegisterPanelButton(rp.MainHairstyle.left, new ButtonValidator_SelectHairstyle(this, true), hairstyleMainIDs, "<");
        RegisterPanelButton(rp.MainHairstyle.right, new ButtonValidator_SelectHairstyle(this, false), hairstyleMainIDs, ">");

        RebuildHairstyleRows();
    }

    /// <summary>
    /// Subtype and slot rows depend on the selected hairstyle: unregister their buttons, destroy the boxes,
    /// repopulate for the current definition
    /// </summary>
    public void RebuildHairstyleRows()
    {
        UnregisterButtons(hairstyleDynamicIDs);
        hairstyleSubtypeBox = null;
        hairstyleSlotBoxes.Clear();

        var rp = RightPanel;
        if (rp == null) return;
        if (rp.Hairstyle_Subtype_rect != null) Utility.DestroyAllChildrenFrom(rp.Hairstyle_Subtype_rect);
        if (rp.Hairstyle_Addon_rect != null) Utility.DestroyAllChildrenFrom(rp.Hairstyle_Addon_rect);
        if (rp.optionPrefab == null) return;

        var def = TemplateHairstyleDef;
        if (def == null) return;

        // the seed may predate a data change
        c.Template.defaultHairstyle.Revalidate();

        if (rp.Hairstyle_Subtype_rect != null && def.subCategories != null && def.subCategories.Count > 0)
        {
            var box = Instantiate(rp.optionPrefab);
            box.transform.SetParent(rp.Hairstyle_Subtype_rect, false);
            box.title.SetText(LocalizeDictionary.QueryThenParse("char_editor_hairstyle_subtype", "Subtype"));
            RegisterPanelButton(box.left, new ButtonValidator_SelectHairstyleSubtype(this, true), hairstyleDynamicIDs, "<");
            RegisterPanelButton(box.right, new ButtonValidator_SelectHairstyleSubtype(this, false), hairstyleDynamicIDs, ">");
            hairstyleSubtypeBox = box;
        }

        if (rp.Hairstyle_Addon_rect != null)
        {
            foreach (var slot in HairstyleUtility.GetOrderedSlots(def))
            {
                var box = Instantiate(rp.optionPrefab);
                box.transform.SetParent(rp.Hairstyle_Addon_rect, false);
                box.title.SetText(slot.DisplayName, false, slot.LocKey + "_tooltip");
                RegisterPanelButton(box.left, new ButtonValidator_SelectHairstyleSlot(this, slot.Keyword, true), hairstyleDynamicIDs, "<");
                RegisterPanelButton(box.right, new ButtonValidator_SelectHairstyleSlot(this, slot.Keyword, false), hairstyleDynamicIDs, ">");
                hairstyleSlotBoxes.Add(new KeyValuePair<string, scr_traitSelectBox>(slot.Keyword, box));
            }
        }
    }

    /// <summary>
    /// One row per exclusive traits_BODY group (skin, hair color...). The sexual body trait groups have
    /// their dedicated rows elsewhere in this canvas. Groups invalid for the character are shown with locked arrows.
    /// </summary>
    protected void PopulateBodyTraits()
    {
        UnregisterButtons(bodyTraitPanelIDs);
        bodyTraitBoxes.Clear();

        var rp = RightPanel;
        if (rp == null || rp.optionPrefab == null || rp.BodyTraitsRect == null) return;
        Utility.DestroyAllChildrenFrom(rp.BodyTraitsRect);

        var index = scr_System_Serializer.current.index_TraitsAll;
        if (index == null || index.traits_BODY == null) return;

        foreach (var group in index.traits_BODY)
        {
            if (!BodyTraitUtility.IsExclusiveGroup(group)) continue;
            if (group.tags.Contains("do_not_use")) continue;
            if (!group.allowPopulate) continue;
            if (!group.isDisplayable) continue;
            if (group.entries.Count < 1) continue;

            var box = Instantiate(rp.optionPrefab);
            box.transform.SetParent(rp.BodyTraitsRect, false);
            box.title.SetText(LocalizeDictionary.QueryThenParse(group.ID, group.displayName));
            RegisterPanelButton(box.left, new ButtonValidator_selectBodyTrait(this, group, true), bodyTraitPanelIDs, "<");
            RegisterPanelButton(box.right, new ButtonValidator_selectBodyTrait(this, group, false), bodyTraitPanelIDs, ">");
            bodyTraitBoxes.Add(new KeyValuePair<scr_Traits_Group, scr_traitSelectBox>(group, box));
        }
    }

    /// <summary>
    /// Selection texts of the right panel, rewritten from the template after every click (UpdateAllTexts)
    /// </summary>
    protected void RefreshRightPanelTexts()
    {
        var rp = RightPanel;
        if (rp == null || c == null || c.Template == null) return;

        var h = c.Template.defaultHairstyle;
        var def = TemplateHairstyleDef;
        if (rp.MainHairstyle != null)
        {
            if (def != null) rp.MainHairstyle.traitName.SetText(def.DisplayName, false, def.ID + "_tooltip");
            else rp.MainHairstyle.traitName.SetText(LocalizeDictionary.QueryThenParse("char_editor_hairstyle_random", "Random"));
        }

        if (hairstyleSubtypeBox != null && def != null)
        {
            var sub = h.GetSubCategory();
            if (sub == null) sub = def.GetDefaultSubCategory();
            hairstyleSubtypeBox.traitName.SetText(sub != null ? sub.DisplayName : " - ", false, sub != null ? sub.ID + "_tooltip" : "");
        }

        foreach (var pair in hairstyleSlotBoxes)
        {
            var opt = def != null ? h.GetSelectedSlotOption(pair.Key) : null;
            pair.Value.traitName.SetText(opt != null ? opt.DisplayName : " - ", false, opt != null ? opt.ID + "_tooltip" : "");
        }

        // a group / trait failing its requirements is not applied to the body: show " - " until it is valid again
        foreach (var pair in bodyTraitBoxes)
        {
            var t = pair.Key.IsValidFor(c) ? GetTemplateBodyTrait(pair.Key) : null;
            if (t != null && t.IsValidFor(c)) pair.Value.traitName.SetText(t.displaynameFull, false, t.TooltipID);
            else pair.Value.traitName.SetText(" - ");
        }    }
}

