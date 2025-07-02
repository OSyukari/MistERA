using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using System;
using UnityEngine.UI;



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
                case 111: case 112:
                    button.Initialize(this, new ButtonValidator_SelectOrigin(this));
                    break;
                case 113: case 114:
                    button.Initialize(this, new ButtonValidator_SelectRace(this));
                    break;
                case 115: case 116:
                    button.Initialize(this, new ButtonValidator_SelectraceTemplate(this));
                    break;
                case 117: case 118:
                    button.Initialize(this, new ButtonValidator_SelectStartingOption(this));
                    break;
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
                    button.Initialize(this, new ButtonValidator_selectTraitPanel(this, button.optionID)); break;
                //case 149:   //cancel select trait
                //    button.Initialize(this, button_alwaysValid); break;
                case 150:
                    button_confirmTraits = new ButtonValidator_selectTrait_Confirm(this, 150);
                    button.Initialize(this, button_confirmTraits); break;
                case 200:   //sensitivity B left
                    button.Initialize(this, new ButtonValidator_selectSensitivity_B_left(this)); break;
                case 201:   //sensitivity B right
                    button.Initialize(this, new ButtonValidator_selectSensitivity_B_right(this)); break;
                case 202:   //size B left
                    button.Initialize(this, new ButtonValidator_selectSize_B_left(this));  break;
                case 203:   //size b right
                    button.Initialize(this, new ButtonValidator_selectSize_B_right(this)); break;
                case 210:   //sensitivity M left
                    button.Initialize(this, new ButtonValidator_selectSensitivity_M_left(this)); break;
                case 211:   //sensitivity M right
                    button.Initialize(this, new ButtonValidator_selectSensitivity_M_right(this)); break;
                case 220:   //sensitivity C left
                    button.Initialize(this, new ButtonValidator_selectSensitivity_C_left(this)); break;
                case 221:   //sensitivity C right
                    button.Initialize(this, new ButtonValidator_selectSensitivity_C_right(this)); break;
                case 222:   //size C left
                    button.Initialize(this, new ButtonValidator_selectSize_P_left(this)); break;
                case 223:   //size C right
                    button.Initialize(this, new ButtonValidator_selectSize_P_right(this)); break;
                case 230:   //sensitivity V left
                    button.Initialize(this, new ButtonValidator_selectSensitivity_V_left(this)); break;
                case 231:   //sensitivity V right
                    button.Initialize(this, new ButtonValidator_selectSensitivity_V_right(this)); break;
                case 232:   //size V left
                    button.Initialize(this, new ButtonValidator_selectSize_V_left(this)); break;
                case 233:   //size V right
                    button.Initialize(this, new ButtonValidator_selectSize_V_right(this)); break;
                case 240:   //sensitivity A left
                    button.Initialize(this, new ButtonValidator_selectSensitivity_A_left(this)); break;
                case 241:   //sensitivity A right
                    button.Initialize(this, new ButtonValidator_selectSensitivity_A_right(this)); break;
                case 242:   //size A left
                    button.Initialize(this, new ButtonValidator_selectSize_A_left(this)); break;
                case 243:   //size A right
                    button.Initialize(this, new ButtonValidator_selectSize_A_right(this)); break;

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
        //this.c = Instantiate(c);
        Debug.LogError("REMOVED CODE CHARA INSTANTIATION");
        this.c = null;
        UpdatePanel();
        UpdateAllTexts();
        ValidateAll();

        RebuildTraitsLeft();
        //ValidateLate();

    }

    public TextMeshProUGUI firstName, middleName;
    public TextMeshProUGUI lastName;
    public TextMeshProUGUI origin, race, raceTemplate, startingGift;

    public TextMeshProUGUI stat_strength_value, stat_constitution_value, stat_psyche_value, stat_willpower_value;
    public TextMeshProUGUI stat_strength_race, stat_constitution_race, stat_psyche_race, stat_willpower_race;
    public TextMeshProUGUI stat_strength_final, stat_constitution_final, stat_psyche_final, stat_willpower_final;

    public RectTransform TraitsBlock;
    public TextMeshProUGUI Traits_Prefab;

    //public TextMeshProUGUI skill_shooting, skill_stealth, skill_survival;
    //public TextMeshProUGUI skill_athletics, skill_melee;
    //public TextMeshProUGUI skill_spellcraft, skill_cooking, skill_art, skill_science, skill_engineering, skill_chemistry, skill_biology;
    //public TextMeshProUGUI skill_animal, skill_perception, skill_social;

    public TextMeshProUGUI age, birth_month, birth_day;

    public TextMeshProUGUI gender, personality, bodyType;

    public TextMeshProUGUI sensitivity_b, sensitivity_m, sensitivity_c, sensitivity_v, sensitivity_a;

    public TextMeshProUGUI size_b, size_p, size_v, size_a;

    public RectTransform TraitsSexBlock;
    protected ArrayList Traits_Sex;
    public TextMeshProUGUI TraitsSex_Prefab;

    private void SetOrigin(string id, bool noForceGift = false)
    {
        this.Character.Origin = scr_System_Serializer.current.MasterList.Character_Origins.GetByID(id);
        this.origin.text = "<link=" + id + "><u>" + Character.Origin.displayname + "</u></link>";
        currentStartingOptionIndex = 0;
        if (!noForceGift) SetStartingGift(this.Character.Origin.availableOptionsID[0]);
        if (Character.Origin.forceRace_ID != "")
        {
            SetRace(Character.Origin.forceRace_ID);
        }
        if (Character.Origin.forceRaceTemplate_ID != "")
        {
            SetRaceTemplate(Character.Origin.forceRaceTemplate_ID);
        }
        LayoutRebuilder.ForceRebuildLayoutImmediate(this.origin.rectTransform);

        Debug.Log("Set Origin [" + this.Character.Origin.ID + "] with availableOptions ["+this.Character.Origin.availableOptionsID.Length+ "]");
    }
    private void SetRace(string id)
    {
        this.Character.Race = scr_System_Serializer.current.MasterList.humanoid_Races.GetByID(id);
        this.race.text = "<link=" + id + "><u>" + Character.Race.DisplayName + "</u></link>";
        LayoutRebuilder.ForceRebuildLayoutImmediate(this.race.rectTransform);
        Debug.Log("SetRace on [" + this.Character.Race.ID + "] as ["+id+"]");
    }

    private void SetRaceTemplate(string id)
    {
        this.Character.RaceTemplate = scr_System_Serializer.current.MasterList.humanoid_RaceTemplates.GetByID(id);
        this.raceTemplate.text = "<link=" + id + "><u>" + Character.RaceTemplate.DisplayName + "</u></link>";
        LayoutRebuilder.ForceRebuildLayoutImmediate(this.raceTemplate.rectTransform);
        Debug.Log("SetRace on [" + this.Character.RaceTemplate.ID + "] as [" + id + "]");
    }

    private void SetStartingGift(string id)
    {
        this.Character.StartingGift = scr_System_Serializer.current.MasterList.Character_Origin_StartingOptions.GetByID(id);
        this.startingGift.text = "<link=" + id + "><u>" + Character.StartingGift.DisplayName + "</u></link>";
        LayoutRebuilder.ForceRebuildLayoutImmediate(this.startingGift.rectTransform);
    }

    public TextMeshProUGUI stat1, stat2, stat3, stat4;
    public TextMeshProUGUI mod_str, mod_con, mod_psy, mod_will;
    private void UpdatePanel()
    {

        firstName.text = c.FirstName;
        middleName.text = c.MiddleName;
        lastName.text = c.LastName;

        SetOrigin(c.Origin.ID, true);
        SetRace(c.Race.ID);
        SetRaceTemplate(c.RaceTemplate.ID);
        SetStartingGift(c.StartingGift.ID);

        stat = new int[4] { c.Stats.Strength.BaseValue, c.Stats.Constitution.BaseValue, c.Stats.Psyche.BaseValue, c.Stats.Willpower.BaseValue };
        statGrid = new int[4] { 0, 1, 2, 3 };

        //age.text = c.getAge().ToString();
        age.text = "-";

        SetBirthday(c.Birthday);

        personality.text = "Personality - unimplemented";//c.Personality.DisplayName;
       // bodyType.text = c.Template.BodyType.ToString();

        //Debug.Log("character sensitivity a [" + c.getSensitivity_A().ID + "] b [" + c.getSensitivity_B().ID + "] c [" + c.getSensitivity_C().ID + "] m [" + c.getSensitivity_M().ID + "] v [" + c.getSensitivity_V().ID + "]");

        //SetGenderAppearance(c.GenderAppearance);
        gender.text = "<link=tooltip_GenderAppearance_" + c.Template.Appearance.ToString() + "><u>" + c.Template.Appearance.ToString() + "</u></link>";

        PopulateTraits_Spectrum_GroupListByType( TraitBox_left, TraitBox_middle, TraitBox_right, scr_System_Serializer.current.index_TraitsAll.traits_STR, true, "STR Traits", "stats_base_Strength_tooltip");
        PopulateTraits_Spectrum_GroupListByType( TraitBox_left, TraitBox_middle, TraitBox_right, scr_System_Serializer.current.index_TraitsAll.traits_STR_SEX, false);
        PopulateTraits_Spectrum_GroupListByType( TraitBox_left, TraitBox_middle, TraitBox_right, scr_System_Serializer.current.index_TraitsAll.traits_CON, true, "CON Traits", "stats_base_Constitution_tooltip");
        PopulateTraits_Spectrum_GroupListByType( TraitBox_left, TraitBox_middle, TraitBox_right, scr_System_Serializer.current.index_TraitsAll.traits_CON_SEX, false);
        PopulateTraits_Spectrum_GroupListByType( TraitBox_left, TraitBox_middle, TraitBox_right, scr_System_Serializer.current.index_TraitsAll.traits_PSY, true, "PSY Traits", "stats_base_Psyche_tooltip");
        PopulateTraits_Spectrum_GroupListByType( TraitBox_left, TraitBox_middle, TraitBox_right, scr_System_Serializer.current.index_TraitsAll.traits_PSY_SEX, false);
        PopulateTraits_Spectrum_GroupListByType( TraitBox_left, TraitBox_middle, TraitBox_right, scr_System_Serializer.current.index_TraitsAll.traits_WIL, true, "WIL Traits", "stats_base_Willpower_tooltip");
        PopulateTraits_Spectrum_GroupListByType( TraitBox_left, TraitBox_middle, TraitBox_right, scr_System_Serializer.current.index_TraitsAll.traits_WIL_SEX, false);
        // 3 link to rect left, rect middle, rect right
        // assign dynamic ID

        PopulateTraits_Single(TraitBox_Single);

        PopulateAllSkills();
        RefreshAllSkills();

        RebuildTraitsLeft();

        PopulateAllDerivedStats();
        RefreshAllDerivedStats();

        //order by trait group, Str, strsex, con, consex, psy, psysex, wil, wilsex
        //

        panel_TraitSelect.gameObject.SetActive(false);  //setactive is disruptive

    }

    public void RefreshAllDerivedStats()
    {
        foreach (var s in DerivedStatValues.Keys)
        {

            /*
            var x = s as Stats_Derived_InstanceBase_Extend;
            var y = s as Stats_Derived_InstanceBase;

            if (x != null)
            {
                DerivedStatValues[s].text = x.Value.ToString() + " / " + x.MaxValue.ToString();
            }
            else if (y != null)
            {
                DerivedStatValues[s].text = y.Value.ToString();
            }
            else
            {
                DerivedStatValues[s].text = "No value";
            }
            */
            if (s.Contains("stats_derived_extended_"))
            {
                var ex = c.Stats.GetStatEx(s);
                DerivedStatValues[s].text = ex.Value.ToString() + " / " + ex.MaxValue.ToString();
            }
            else if (s.Contains("stats_derived_"))
            {
                var drv = c.Stats.GetStatValue(s);
                DerivedStatValues[s].text = drv.ToString();

            }
            else
            {
                DerivedStatValues[s].text = "No value";
            }
        }
    }


    public RectTransform Panel_DerivedStats;
    public RectTransform prefab_DerivedStatBox;
    Dictionary<string, TMP_Text> DerivedStatValues;
    private void PopulateAllDerivedStats()
    {
        DerivedStatValues = new Dictionary<string, TMP_Text>();

        foreach (var s in c.Stats.StatsExtended)
        {
            if (s != null)
            {
                RectTransform box = Instantiate(prefab_DerivedStatBox);
                box.SetParent(Panel_DerivedStats, false);
                PopulateDerivedStats(box, s);
            }
        }

        foreach(var statderived in scr_System_Serializer.current.index_StatsDerived.list)
        {
            if (!c.hasStatKeyword(statderived.StatKeyword)) continue;

            RectTransform box = Instantiate(prefab_DerivedStatBox);
            box.SetParent(Panel_DerivedStats, false);
            PopulateDerivedStats(box, statderived);

        }
    }
    private void PopulateDerivedStats(RectTransform box, Stats_Derived_Extended_Instance s)
    {

        RectTransform left = Instantiate(prefab_TraitLinkText);
        left.SetParent(box, false);
        left.GetComponent<scr_HoverableText>().SetText(s.DisplayName, false, s.ID);

        RectTransform right = Instantiate(prefab_TraitText);
        right.SetParent(box, false);

        DerivedStatValues.Add(s.ID, right.GetComponent<TMP_Text>());
    }

    private void PopulateDerivedStats(RectTransform box, Stats_Derived_Base s)
    {

        RectTransform left = Instantiate(prefab_TraitLinkText);
        left.SetParent(box, false);
        left.GetComponent<scr_HoverableText>().SetText(s.DisplayName, false, s.ID);

        RectTransform right = Instantiate(prefab_TraitText);
        right.SetParent(box, false);

        DerivedStatValues.Add(s.ID, right.GetComponent<TMP_Text>());
    }

    protected void SetBirthday(DateTime date)
    {
        c.Birthday = date;
        birth_month.text = date.Month.ToString();
        birth_day.text = date.Day.ToString();
    }

    protected void SetGenderAppearance(Humanoid_GenderAppearance app)
    {
       // c.Template.GenderAppearance_Set(app,true,false);
        gender.text = "<link=tooltip_GenderAppearance_" + c.Template.Appearance.ToString() + "><u>" + c.Template.Appearance.ToString() + "</u></link>";
    }

    /// <summary>
    /// Traits block
    /// </summary>

    public RectTransform TraitBox_left;
    public RectTransform TraitBox_middle;
    public RectTransform TraitBox_right;
    public RectTransform prefab_TraitboxHeader;
    public RectTransform prefab_TraitboxBody;
    public RectTransform prefab_TraitText;
    public RectTransform prefab_TraitLinkText;
    public RectTransform prefab_TraitButtonLeftRight;
    public RectTransform prefab_TraitButtonLinkText;
    public RectTransform prefab_TraitButtonBox;

    public RectTransform TraitBox_Single;

    Dictionary<int, Traits> Traits_getTraitbyID;
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

    private void PopulateTraits_Single(RectTransform parent)
    {
        foreach (List<scr_Traits_Group> list in scr_System_Serializer.current.index_TraitsAll.traits_All)
        {
            foreach(scr_Traits_Group group in list)
            {
                if (group.SortType == Trait_Group_Type.Singular)
                {
                    foreach(Traits entry in group.entries)
                    {

                        RectTransform buttonBox = Instantiate(prefab_TraitButtonLeftRight);
                        buttonBox.SetParent(parent, false);

                        scr_SelectableText comp = buttonBox.GetComponent<scr_SelectableText>();
                        comp.optionID = GetTraitID;
                        ButtonValidator_selectTrait_Single validator = new ButtonValidator_selectTrait_Single(this, comp.optionID, entry);
                        comp.linkText = entry.ID;
                        comp.isButtonToggle = true;



                        comp.Initialize(this, validator);
                        comp.SetText(entry.displayname);

                        if (Character.Traits.Contains(entry))
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
    private void PopulateTraits_Spectrum_GroupListByType(RectTransform boxLeft, RectTransform boxMiddle, RectTransform boxRight, List<scr_Traits_Group> typeGroup, bool newHeader, string displayText = "", string hoverID = "")
    {
        // text prefab
        // text link prefab
        // text button prefab


        if (newHeader)
        {
            RectTransform header = Instantiate(prefab_TraitboxHeader);
            header.SetParent(boxLeft, false);
            header.GetComponent<scr_HoverableText>().SetText(displayText, false, hoverID);

            RectTransform empty1 = Instantiate(prefab_TraitText);
            empty1.SetParent(boxMiddle, false);
            empty1.GetComponent<TMP_Text>().text = " ";

            RectTransform empty2 = Instantiate(prefab_TraitText);
            empty2.SetParent(boxRight, false);
            empty2.GetComponent<TMP_Text>().text = " ";
        }

        RectTransform groupBox = Instantiate(prefab_TraitboxBody);
        groupBox.SetParent(boxLeft, false);

        foreach (scr_Traits_Group group in typeGroup)
        {
            //Debug.Log("Trait group ["+group.groupName+ "] listtype [" + group.sortTypeString + "]");
            if (group.SortType == Trait_Group_Type.SortedList || group.SortType == Trait_Group_Type.UnsortedList)
            {
                PopulateTraits_Spectrum_GroupList(groupBox, boxMiddle, boxRight, group);
            }
        }
    }

    private void PopulateTraits_Spectrum_GroupList(RectTransform boxLeft, RectTransform boxMiddle, RectTransform boxRight, scr_Traits_Group group)
    {
        RectTransform name = Instantiate(prefab_TraitLinkText);
        name.SetParent(boxLeft, false);
        name.GetComponent<scr_HoverableText>().SetText(group.displayName, false, group.ID);

        RectTransform number = Instantiate(prefab_TraitText);
        number.SetParent(boxRight, false);
        number.GetComponent<TMP_Text>().text = "+0";

        RectTransform buttonBox = Instantiate(prefab_TraitButtonBox);
        buttonBox.SetParent(boxMiddle, false);

        RectTransform left = Instantiate(prefab_TraitButtonLeftRight);
        left.SetParent(buttonBox, false);

        RectTransform middle = Instantiate(prefab_TraitButtonLinkText);
        middle.SetParent(buttonBox, false);

        RectTransform right = Instantiate(prefab_TraitButtonLeftRight);
        right.SetParent(buttonBox, false);

        Traits trait = null;
        foreach (Traits t in group.entries)
        {
            if (Character.HasTrait(t))
            {
                trait = t; break;
            }
        }
        if (trait == null) Debug.Log("Critical Error in PopulateTraits_Spectrum_GroupList, character contains trait null");

        middle.GetComponent<scr_HoverableText>().SetText(trait.displayname, false, trait.ID);

        scr_SelectableText button_left = left.GetComponent<scr_SelectableText>();
        button_left.optionID = GetTraitID;
        ButtonValidator_selectTrait_left_ordered validator_left = new ButtonValidator_selectTrait_left_ordered(this, button_left.optionID, ref number, ref middle);
        button_left.Initialize(this, validator_left);
        button_left.SetText("<");
        button_left.GetComponent<RectTransform>().SetParent(buttonBox, false);

        buttonsByID.Add(button_left.optionID, button_left);
        validatorsByID.Add(button_left.optionID, validator_left);
        Traits_AddTraitsbyID(button_left.optionID, trait);
        button_left.Validate();


        scr_SelectableText button_right = right.GetComponent<scr_SelectableText>();
        button_right.optionID = GetTraitID;
        ButtonValidator_selectTrait_right_ordered validator_right = new ButtonValidator_selectTrait_right_ordered(this, button_right.optionID, ref number, ref middle);
        button_right.Initialize(this, validator_right);
        button_right.SetText(">");
        button_right.GetComponent<RectTransform>().SetParent(buttonBox, false);

        buttonsByID.Add(button_right.optionID, button_right);
        validatorsByID.Add(button_right.optionID, validator_right);
        Traits_AddTraitsbyID(button_right.optionID, trait);
        button_right.Validate();



        
        

        /*
        scr_SelectableText button_right = Instantiate(prefab_TraitButtonLeftRight).GetComponent<scr_SelectableText>();
        button_right.optionID = IDList + 0;
        button_right.Initialize(this, validator_right);
        button_right.SetText(">");
        button_right.GetComponent<RectTransform>().SetParent(buttonBox);
        IDList += 1;

        buttonsByID.Add(button_right.optionID, button_right);

        validatorsByID.Add(button_right.optionID, validator_right);
        */
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

    Dictionary<Skills, TMP_Text> SkillValues;

    RectTransform prefab_SkillText, prefab_SkillNumber;
    private void PopulateAllSkills()
    {
        SkillValues = new Dictionary<Skills, TMP_Text>();
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
                    InstantiateInputField(inputField, new Vector3(Screen.width / 2, Screen.height / 2), firstName);
                    //DisplaySingle(m_Camera, inputField, ContentSizeFitter.FitMode.PreferredSize, ContentSizeFitter.FitMode.PreferredSize, new Vector2(Screen.width, box2.sizeDelta.y), new Vector3(Screen.width / 2, Screen.height * 1 / 10), TextAnchor.UpperCenter);
                    break;
                case 0102:  //random first name
                    break;
                case 0103:  //change middle name
                    InstantiateInputField(inputField, new Vector3(Screen.width / 2, Screen.height / 2), middleName);
                    break;
                case 0104:  //random middle name
                    break;
                case 0105:  //change last name
                    InstantiateInputField(inputField, new Vector3(Screen.width / 2, Screen.height / 2), lastName);
                    break;
                case 0106:  //random last name
                    break;
                case 0111:  //Origin left
                    //Debug.Log("Getitembefore " + o.displayname);
                    SetOrigin(scr_System_Serializer.current.MasterList.Character_Origins.GetItemBefore(this.Character.Origin).ID); break;
                case 0112:  //Origin right
                    SetOrigin(scr_System_Serializer.current.MasterList.Character_Origins.GetItemAfter(this.Character.Origin).ID); break;
                case 0113:  //Race left
                    SetRace(scr_System_Serializer.current.MasterList.humanoid_Races.GetItemBefore(this.Character.Race).ID); break;
                case 0114:  //Race right
                    SetRace(scr_System_Serializer.current.MasterList.humanoid_Races.GetItemAfter(this.Character.Race).ID); break;
                case 0115:  //Addon left
                    SetRaceTemplate(scr_System_Serializer.current.MasterList.humanoid_RaceTemplates.GetItemBefore(this.Character.RaceTemplate).ID); break;
                case 0116:  //Addon right
                    SetRaceTemplate(scr_System_Serializer.current.MasterList.humanoid_RaceTemplates.GetItemAfter(this.Character.RaceTemplate).ID); break;
                case 0117:  //Gift left
                    if (currentStartingOptionIndex - 1 >= 0) currentStartingOptionIndex -= 1;
                    else currentStartingOptionIndex = Character.Origin.availableOptionsID.Length - 1;
                    SetStartingGift(this.Character.Origin.availableOptionsID[currentStartingOptionIndex]);
                    break;
                case 0118:  //Gift right
                    if (currentStartingOptionIndex + 1 < Character.Origin.availableOptionsID.Length) currentStartingOptionIndex += 1;
                    else currentStartingOptionIndex = 0;
                    SetStartingGift(this.Character.Origin.availableOptionsID[currentStartingOptionIndex]);
                    break;
                case 121:   //reroll once
                    stat = Utility.RollStat();
                    break;
                case 122:   //reroll 10
                    stat = Utility.RollStatRepeat(10);
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
                    //scr_System_Serializer.current.SaveDataContract(typeof(Character_Trainable), c as Character_Trainable);
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

        UpdateStatsBase();
        UpdateAllTexts();
        ValidateAll();
        //
        //ValidateLate();
    }


    protected void SaveCharacter()
    {
        c.FirstName = firstName.text;
        c.MiddleName = middleName.text;
        c.LastName = lastName.text;

        c.ResetTrait();
        // save traitlist into Character.traits
        foreach (Traits t in Traits_getTraitbyID.Values)
        {
            c.AddTrait(t);
        }
    }

    protected void ValidateLate()
    {
        GetButtonByID(150).Validate();
        GetButtonByID(9998).Validate();
    }

    public RectTransform panel_TraitsLeft;
    public TMP_Text prefab_panel_TraitsLeft_Single;
    public void RebuildTraitsLeft()
    {
        // https://stackoverflow.com/questions/46358717/how-to-loop-through-and-destroy-all-children-of-a-game-object-in-unity
        while (panel_TraitsLeft.transform.childCount > 0)
        {
            DestroyImmediate(panel_TraitsLeft.transform.GetChild(0).gameObject);
        }
        foreach(Traits t in Traits_getTraitbyID.Values)
        {
            if (t.isDisplayable)
            {
                TMP_Text a = Instantiate(prefab_panel_TraitsLeft_Single);
                a.text = "<link=" + t.ID + "><u>" + t.displayname + "</u></link>";
                a.transform.SetParent(panel_TraitsLeft.transform);
            }
        }

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
                    GetButtonByID(statGridLayout[i, j]).SetText("X");
                }
            }
        }

        stat1.text = stat[0].ToString();
        stat2.text = stat[1].ToString();
        stat3.text = stat[2].ToString();
        stat4.text = stat[3].ToString();

        stat_strength_final.text = c.Stats.Strength.FinalValue().ToString();
        stat_constitution_final.text = c.Stats.Constitution.GetStatMod().ToString();
        stat_psyche_final.text = c.Stats.Psyche.GetStatMod().ToString();
        stat_willpower_final.text = c.Stats.Willpower.GetStatMod().ToString();

        stat_strength_value.text = c.Stats.Strength.BaseValue.ToString();
        stat_constitution_value.text = c.Stats.Constitution.BaseValue.ToString();
        stat_psyche_value.text = c.Stats.Psyche.BaseValue.ToString();
        stat_willpower_value.text = c.Stats.Willpower.BaseValue.ToString();

        stat_strength_race.text = (c.Stats.Strength.FinalValue() - c.Stats.Strength.BaseValue).ToString();
        stat_constitution_race.text = (c.Stats.Constitution.FinalValue() - c.Stats.Constitution.BaseValue).ToString();
        stat_psyche_race.text = (c.Stats.Psyche.FinalValue() - c.Stats.Psyche.BaseValue).ToString();
        stat_willpower_race.text = (c.Stats.Willpower.FinalValue() - c.Stats.Willpower.BaseValue).ToString();



        //  https://stackoverflow.com/questions/348201/custom-numeric-format-string-to-always-display-the-sign
        mod_str.text = (Character.Stats.Strength.GetStatMod()).ToString("+0;-#");
        mod_con.text = (Character.Stats.Constitution.GetStatMod()).ToString("+0;-#");
        mod_psy.text = (Character.Stats.Psyche.GetStatMod()).ToString("+0;-#");
        mod_will.text = (Character.Stats.Willpower.GetStatMod()).ToString("+0;-#");

        /*

        RefreshTraitText(ref sensitivity_a, Character.Template.Sensitivity_A);
        RefreshTraitText(ref sensitivity_b, Character.Template.Sensitivity_B);
        RefreshTraitText(ref sensitivity_c, Character.Template.Sensitivity_C);
        RefreshTraitText(ref sensitivity_m, Character.Template.Sensitivity_M);
        RefreshTraitText(ref sensitivity_v, Character.Template.Sensitivity_V);

        RefreshTraitText(ref size_a, Character.Template.Size_A);
        RefreshTraitText(ref size_b, Character.Template.Size_B);
        RefreshTraitText(ref size_p, Character.Template.Size_P);
        RefreshTraitText(ref size_v, Character.Template.Size_V);
        */
        RefreshAllSkills();
        RefreshAllDerivedStats();
    }

    protected void RefreshTraitText(ref TextMeshProUGUI box, Traits data)
    {
        box.text = "<link=" + data.ID + "> " + ((data.tooltip == "") ? (data.displayname) : ("<u>" + data.displayname + "</u>")) + " </link>";
        LayoutRebuilder.ForceRebuildLayoutImmediate(box.rectTransform);
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

    class ButtonValidator_SelectOrigin : ButtonValidator
    {
        protected new scr_Canvas_CharacterEditor parent;
        public ButtonValidator_SelectOrigin(scr_Menu parent) : base(parent)
        {
            this.parent = parent as scr_Canvas_CharacterEditor;
        }

        public override bool IsButtonValid()
        {
            this.state = ButtonValidator_States.Valid;
            tooltip = "";
            foreach (string s in this.parent.Character.Origin.disallowRace_ID)
            {
                if (this.parent.Character.Race.ID == s)
                {
                    this.state = ButtonValidator_States.Conflict;
                    string s2 = "Origin [" + parent.Character.Origin.displayname + "] conflict with Race [" + parent.Character.Race.DisplayName + "]\n";
                    tooltip += s2;
                    parent.GetButton_ConfirmCharacter.NotifyConflict(s2);
                }
            }

            foreach (string s in this.parent.Character.Origin.disallowRaceTemplate_ID)
            {
                if (this.parent.Character.RaceTemplate.ID == s)
                {
                    this.state = ButtonValidator_States.Conflict;
                    string s2 = "Origin [" + parent.Character.Origin.displayname + "] conflict with Race Modifier [" + parent.Character.RaceTemplate.DisplayName + "]\n";
                    tooltip += s2;
                    parent.GetButton_ConfirmCharacter.NotifyConflict(s2);
                }
            }

            return true;
        }
    }
    class ButtonValidator_SelectRace : ButtonValidator
    {
        protected new scr_Canvas_CharacterEditor parent;
        public ButtonValidator_SelectRace(scr_Menu parent) : base(parent)
        {
            this.parent = parent as scr_Canvas_CharacterEditor;
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
    }
    class ButtonValidator_SelectraceTemplate : ButtonValidator
    {
        protected new scr_Canvas_CharacterEditor parent;
        public ButtonValidator_SelectraceTemplate(scr_Menu parent) : base(parent)
        {
            this.parent = parent as scr_Canvas_CharacterEditor;
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
    }
    class ButtonValidator_SelectStartingOption : ButtonValidator
    {
        protected new scr_Canvas_CharacterEditor parent;
        public ButtonValidator_SelectStartingOption(scr_Menu parent) : base(parent)
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
        private int ButtonID;
        public ButtonValidator_selectTraitPanel(scr_Menu parent, int ButtonID) : base(parent)
        {
            this.parent = parent as scr_Canvas_CharacterEditor;
            this.ButtonID = ButtonID;
        }

        public override bool IsButtonValid()    // always valid
        {
            if (this.parent.panel_TraitSelect.gameObject.activeSelf == true)
            {
                parent.GetButtonByID(this.ButtonID).Toggle(true, true);
                tooltip = "A Trait selection panel is already active!";
            }
            else
            {
                parent.GetButtonByID(this.ButtonID).Toggle(true, false);
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

    class ButtonValidator_selectSensitivity_B_left : ButtonValidator, I_ButtonClickable
    {
        protected new scr_Canvas_CharacterEditor parent;
        public ButtonValidator_selectSensitivity_B_left(scr_Menu parent) : base(parent)
        {
            this.parent = parent as scr_Canvas_CharacterEditor;
        }

        public override bool IsButtonValid()
        {
            return false;
            //return (parent.Character.Template.Sensitivity_B.GetPreviousInGroup() != null);
        }
        public void OnClickButton()
        {
            //parent.Character.Template.Sensitivity_B = parent.Character.Template.Sensitivity_B.GetPreviousInGroup();
        }
    }
    class ButtonValidator_selectSensitivity_B_right : ButtonValidator, I_ButtonClickable
    {
        protected new scr_Canvas_CharacterEditor parent;
        public ButtonValidator_selectSensitivity_B_right(scr_Menu parent) : base(parent)
        {
            this.parent = parent as scr_Canvas_CharacterEditor;
        }

        public override bool IsButtonValid()
        {
            return false;
            //return (parent.Character.Template.Sensitivity_B.GetNextInGroup() != null);
        }
        public void OnClickButton()
        {
            //parent.Character.Template.Sensitivity_B = parent.Character.Template.Sensitivity_B.GetNextInGroup();
        }
    }
    class ButtonValidator_selectSensitivity_M_left : ButtonValidator, I_ButtonClickable
    {
        protected new scr_Canvas_CharacterEditor parent;
        public ButtonValidator_selectSensitivity_M_left(scr_Menu parent) : base(parent)
        {
            this.parent = parent as scr_Canvas_CharacterEditor;
        }

        public override bool IsButtonValid()
        {
            return false;
            // return (parent.Character.Template.Sensitivity_M.GetPreviousInGroup() != null);
        }
        public void OnClickButton()
        {
            //parent.Character.Template.Sensitivity_M = parent.Character.Template.Sensitivity_M.GetPreviousInGroup();
        }
    }
    class ButtonValidator_selectSensitivity_M_right : ButtonValidator, I_ButtonClickable
    {
        protected new scr_Canvas_CharacterEditor parent;
        public ButtonValidator_selectSensitivity_M_right(scr_Menu parent) : base(parent)
        {
            this.parent = parent as scr_Canvas_CharacterEditor;
        }

        public override bool IsButtonValid()
        {
            return false;
            //return (parent.Character.Template.Sensitivity_M.GetNextInGroup() != null);
        }
        public void OnClickButton()
        {
            //parent.Character.Template.Sensitivity_M = parent.Character.Template.Sensitivity_M.GetNextInGroup();
        }
    }
    class ButtonValidator_selectSensitivity_C_left : ButtonValidator, I_ButtonClickable
    {
        protected new scr_Canvas_CharacterEditor parent;
        public ButtonValidator_selectSensitivity_C_left(scr_Menu parent) : base(parent)
        {
            this.parent = parent as scr_Canvas_CharacterEditor;
        }

        public override bool IsButtonValid()
        {
            return false;
            //return (parent.Character.Template.Sensitivity_C.GetPreviousInGroup() != null);
        }
        public void OnClickButton()
        {
            //parent.Character.Template.Sensitivity_C = parent.Character.Template.Sensitivity_C.GetPreviousInGroup();
        }
    }
    class ButtonValidator_selectSensitivity_C_right : ButtonValidator, I_ButtonClickable
    {
        protected new scr_Canvas_CharacterEditor parent;
        public ButtonValidator_selectSensitivity_C_right(scr_Menu parent) : base(parent)
        {
            this.parent = parent as scr_Canvas_CharacterEditor;
        }

        public override bool IsButtonValid()
        {
            return false;
            //return (parent.Character.Template.Sensitivity_C.GetNextInGroup() != null);
        }
        public void OnClickButton()
        {
            //parent.Character.Template.Sensitivity_C = parent.Character.Template.Sensitivity_C.GetNextInGroup();
        }
    }
    class ButtonValidator_selectSensitivity_V_left : ButtonValidator, I_ButtonClickable
    {
        protected new scr_Canvas_CharacterEditor parent;
        public ButtonValidator_selectSensitivity_V_left(scr_Menu parent) : base(parent)
        {
            this.parent = parent as scr_Canvas_CharacterEditor;
        }

        public override bool IsButtonValid()
        {
            return false;
            //return (parent.Character.Template.Sensitivity_V.GetPreviousInGroup() != null);
        }
        public void OnClickButton()
        {
            //parent.Character.Template.Sensitivity_V = parent.Character.Template.Sensitivity_V.GetPreviousInGroup();
        }
    }
    class ButtonValidator_selectSensitivity_V_right : ButtonValidator, I_ButtonClickable
    {
        protected new scr_Canvas_CharacterEditor parent;
        public ButtonValidator_selectSensitivity_V_right(scr_Menu parent) : base(parent)
        {
            this.parent = parent as scr_Canvas_CharacterEditor;
        }

        public override bool IsButtonValid()
        {
            return false;
            //return (parent.Character.Template.Sensitivity_V.GetNextInGroup() != null);
        }
        public void OnClickButton()
        {
            //parent.Character.Template.Sensitivity_V = parent.Character.Template.Sensitivity_V.GetNextInGroup();
        }
    }
    class ButtonValidator_selectSensitivity_A_left : ButtonValidator, I_ButtonClickable
    {
        protected new scr_Canvas_CharacterEditor parent;
        public ButtonValidator_selectSensitivity_A_left(scr_Menu parent) : base(parent)
        {
            this.parent = parent as scr_Canvas_CharacterEditor;
        }

        public override bool IsButtonValid()
        {
            return false;
            //return (parent.Character.Template.Sensitivity_A.GetPreviousInGroup() != null);
        }
        public void OnClickButton()
        {
            //parent.Character.Template.Sensitivity_A = parent.Character.Template.Sensitivity_A.GetPreviousInGroup();
        }
    }
    class ButtonValidator_selectSensitivity_A_right : ButtonValidator, I_ButtonClickable
    {
        protected new scr_Canvas_CharacterEditor parent;
        public ButtonValidator_selectSensitivity_A_right(scr_Menu parent) : base(parent)
        {
            this.parent = parent as scr_Canvas_CharacterEditor;
        }

        public override bool IsButtonValid()
        {
            return false;
            //return (parent.Character.Template.Sensitivity_A.GetNextInGroup() != null);
        }

        public void OnClickButton()
        {
            //parent.Character.Template.Sensitivity_A = parent.Character.Template.Sensitivity_A.GetNextInGroup();
        }
    }



    class ButtonValidator_selectSize_B_left : ButtonValidator, I_ButtonClickable
    {
        protected new scr_Canvas_CharacterEditor parent;
        public ButtonValidator_selectSize_B_left(scr_Menu parent) : base(parent)
        {
            this.parent = parent as scr_Canvas_CharacterEditor;
        }

        public override bool IsButtonValid()
        {
            return false;
            //return (parent.Character.Template.Size_B.GetPreviousInGroup() != null);
        }
        public void OnClickButton()
        {
            //parent.Character.Template.Size_B = parent.Character.Template.Size_B.GetPreviousInGroup();
        }
    }
    class ButtonValidator_selectSize_B_right : ButtonValidator, I_ButtonClickable
    {
        protected new scr_Canvas_CharacterEditor parent;
        public ButtonValidator_selectSize_B_right(scr_Menu parent) : base(parent)
        {
            this.parent = parent as scr_Canvas_CharacterEditor;
        }

        public override bool IsButtonValid()
        {
            return false;
            //return (parent.Character.Template.Size_B.GetNextInGroup() != null);
        }

        public void OnClickButton()
        {
            //parent.Character.Template.Size_B = parent.Character.Template.Size_B.GetNextInGroup();
        }
    }
    class ButtonValidator_selectSize_P_left : ButtonValidator, I_ButtonClickable
    {
        protected new scr_Canvas_CharacterEditor parent;
        public ButtonValidator_selectSize_P_left(scr_Menu parent) : base(parent)
        {
            this.parent = parent as scr_Canvas_CharacterEditor;
        }

        public override bool IsButtonValid()
        {
            return false;
            //return (parent.Character.Template.Size_P.GetPreviousInGroup() != null);
        }
        public void OnClickButton()
        {
            //parent.Character.Template.Size_P = parent.Character.Template.Size_P.GetPreviousInGroup();
        }
    }
    class ButtonValidator_selectSize_P_right : ButtonValidator, I_ButtonClickable
    {
        protected new scr_Canvas_CharacterEditor parent;
        public ButtonValidator_selectSize_P_right(scr_Menu parent) : base(parent)
        {
            this.parent = parent as scr_Canvas_CharacterEditor;
        }

        public override bool IsButtonValid()
        {
            return false;
            //return (parent.Character.Template.Size_P.GetNextInGroup() != null);
        }

        public void OnClickButton()
        {
            //parent.Character.Template.Size_P = parent.Character.Template.Size_P.GetNextInGroup();
        }
    }
    class ButtonValidator_selectSize_V_left : ButtonValidator, I_ButtonClickable
    {
        protected new scr_Canvas_CharacterEditor parent;
        public ButtonValidator_selectSize_V_left(scr_Menu parent) : base(parent)
        {
            this.parent = parent as scr_Canvas_CharacterEditor;
        }

        public override bool IsButtonValid()
        {
            return false;
            //return (parent.Character.Template.Size_V.GetPreviousInGroup() != null);
        }
        public void OnClickButton()
        {
            //parent.Character.Template.Size_V = parent.Character.Template.Size_V.GetPreviousInGroup();
        }
    }
    class ButtonValidator_selectSize_V_right : ButtonValidator, I_ButtonClickable
    {
        protected new scr_Canvas_CharacterEditor parent;
        public ButtonValidator_selectSize_V_right(scr_Menu parent) : base(parent)
        {
            this.parent = parent as scr_Canvas_CharacterEditor;
        }

        public override bool IsButtonValid()
        {
            return false;
            //return (parent.Character.Template.Size_V.GetNextInGroup() != null);
        }

        public void OnClickButton()
        {
            //parent.Character.Template.Size_V = parent.Character.Template.Size_V.GetNextInGroup();
        }
    }
    class ButtonValidator_selectSize_A_left : ButtonValidator, I_ButtonClickable
    {
        protected new scr_Canvas_CharacterEditor parent;
        public ButtonValidator_selectSize_A_left(scr_Menu parent) : base(parent)
        {
            this.parent = parent as scr_Canvas_CharacterEditor;
        }

        public override bool IsButtonValid()
        {
            return false;
            //return (parent.Character.Template.Size_A.GetPreviousInGroup() != null);
        }
        public void OnClickButton()
        {
            //parent.Character.Template.Size_A = parent.Character.Template.Size_A.GetPreviousInGroup();
        }
    }
    class ButtonValidator_selectSize_A_right : ButtonValidator, I_ButtonClickable
    {
        protected new scr_Canvas_CharacterEditor parent;
        public ButtonValidator_selectSize_A_right(scr_Menu parent) : base(parent)
        {
            this.parent = parent as scr_Canvas_CharacterEditor;
        }

        public override bool IsButtonValid()
        {
            return false;
            //return (parent.Character.Template.Size_A.GetNextInGroup() != null);
        }

        public void OnClickButton()
        {
            //parent.Character.Template.Size_A = parent.Character.Template.Size_A.GetNextInGroup();
        }
    }



    class ButtonValidator_selectTrait_left_ordered : ButtonValidator, I_ButtonClickable
    {
        protected new scr_Canvas_CharacterEditor parent;
        private Traits trait;
        private int buttonID;
        protected TMP_Text number;
        protected scr_HoverableText name;
        public ButtonValidator_selectTrait_left_ordered(scr_Menu parent, int buttonID, ref RectTransform number, ref RectTransform name) : base(parent)
        {
            this.parent = parent as scr_Canvas_CharacterEditor;
            this.buttonID = buttonID;
            this.number = number.GetComponent<TMP_Text>();
            this.name = name.GetComponent<scr_HoverableText>();
        }

        Trait_Type A, B;
        int statMod, totalScore;
        public override bool IsButtonValid()
        {
            bool result = (parent.Traits_GetTraitByID(buttonID).GetPreviousInGroup() != null);
            //if (result == false) return result;

            if (result != false)
            {
                this.state = ButtonValidator_States.Valid;
                this.tooltip = "";
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
                tooltip += "conflict message buttonID ["+this.buttonID+ "] currentTrait [" + parent.Traits_GetTraitByID(buttonID).displayname + "] current [" + current.ToString() + "] A.type [" + A.ToString() + "] B.type [" + B.ToString() + "] statscore [" + statMod + "] totalscore [" + totalScore + "]";
            }
            return result;
        }

        public void OnClickButton()
        {
            trait = parent.Traits_GetTraitByID(buttonID).GetPreviousInGroup();
            parent.Traits_SetTraitByID(buttonID, trait);
            number.text = trait.trait_score.ToString("+0;-#");
            name.SetText(trait.displayname, false, trait.ID);
        }
    }
    class ButtonValidator_selectTrait_right_ordered : ButtonValidator, I_ButtonClickable
    {
        protected new scr_Canvas_CharacterEditor parent;
        private Traits trait;
        private int buttonID;
        protected TMP_Text number;
        protected scr_HoverableText name;
        public ButtonValidator_selectTrait_right_ordered(scr_Menu parent, int buttonID, ref RectTransform number, ref RectTransform name) : base(parent)
        {
            this.parent = parent as scr_Canvas_CharacterEditor;
            this.buttonID = buttonID;
            this.number = number.GetComponent<TMP_Text>();
            this.name = name.GetComponent<scr_HoverableText>();
        }

        Trait_Type A, B;
        int statMod, totalScore;
        public override bool IsButtonValid()
        {
            bool result = (parent.Traits_GetTraitByID(buttonID).GetNextInGroup() != null);
            //if (result == false) return result;

            if (result != false)
            {
                this.state = ButtonValidator_States.Valid;
                this.tooltip = "";
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
                tooltip += "trait score in [" + A.ToString() + "] must be equal or smaller than corresponding stat modifier " + statMod;
                parent.GetButton_TraitsConfirm.NotifyConflict(tooltip);
            }
            return result;
        }

        public void OnClickButton()
        {
            trait = parent.Traits_GetTraitByID(buttonID).GetNextInGroup();
            parent.Traits_SetTraitByID(buttonID, trait);
            number.text = trait.trait_score.ToString("+0;-#");
            name.SetText(trait.displayname, false, trait.ID);
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
}

