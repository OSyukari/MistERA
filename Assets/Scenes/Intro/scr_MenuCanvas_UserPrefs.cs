using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using System;
using System.Reflection;
using static UnityEngine.GraphicsBuffer;

public class scr_MenuCanvas_UserPrefs : scr_Menu
{
    List<scr_Gender_demoChara> demoCharaList;

    protected override void Awake()
    {
        base.Awake();
        button_alwaysValid = new ButtonValidator_AlwaysTrue(this);

        demoCharaList = new List<scr_Gender_demoChara>();
    }

    public override void Initialize()
    {
        base.Initialize();
        

        foreach (scr_Gender_demoChara c in GetComponentsInChildren<scr_Gender_demoChara>(true))
        {
            demoCharaList.Add(c);
        }

        foreach (scr_SelectableText button in GetComponentsInChildren<scr_SelectableText>(true))
        {
            Debug.Log("Button " + button + " " + button.optionID);
            switch (button.optionID)
            {
                
                case 1200: // gender priority left
                    button.Initialize(this, new ButtonValidator_toggleEnum(this, button, text_gender_priority, scr_System_CentralControl.current.pref.gender_priority, true)); break;
                case 1201: // gender priority right
                    button.Initialize(this, new ButtonValidator_toggleEnum(this, button, text_gender_priority, scr_System_CentralControl.current.pref.gender_priority)); break;
                case 1205: // show gender demo
                    button.Initialize(this, new ButtonValidator_DisplayGenderDemo(this, button)); break;
                case 1210: // male app left
                    button.Initialize(this, new ButtonValidator_toggleEnum(this, button, text_male_app, scr_System_CentralControl.current.pref.male_appearance, true)); break;
                case 1211: // male app right
                    button.Initialize(this, new ButtonValidator_toggleEnum(this, button, text_male_app, scr_System_CentralControl.current.pref.male_appearance)); break;
                case 1220: // male penis left
                    button.Initialize(this, new ButtonValidator_toggleEnum(this, button, text_male_penis, scr_System_CentralControl.current.pref.male_penis, true)); break;
                case 1221: // male penis right
                    button.Initialize(this, new ButtonValidator_toggleEnum(this, button, text_male_penis, scr_System_CentralControl.current.pref.male_penis)); break;
                case 1230: // male vagina left
                    button.Initialize(this, new ButtonValidator_toggleEnum(this, button, text_male_vagina, scr_System_CentralControl.current.pref.male_vagina, true)); break;
                case 1231: // male vagina right
                    button.Initialize(this, new ButtonValidator_toggleEnum(this, button, text_male_vagina, scr_System_CentralControl.current.pref.male_vagina)); break;
                case 1240: // female app left
                    button.Initialize(this, new ButtonValidator_toggleEnum(this, button, text_female_app, scr_System_CentralControl.current.pref.female_appearance, true)); break;
                case 1241: // female app right
                    button.Initialize(this, new ButtonValidator_toggleEnum(this, button, text_female_app, scr_System_CentralControl.current.pref.female_appearance)); break;
                case 1250: // female penis left
                    button.Initialize(this, new ButtonValidator_toggleEnum(this, button, text_female_penis, scr_System_CentralControl.current.pref.female_penis, true)); break;
                case 1251: // female penis right
                    button.Initialize(this, new ButtonValidator_toggleEnum(this, button, text_female_penis, scr_System_CentralControl.current.pref.female_penis)); break;
                case 1260: // female vagina left
                    button.Initialize(this, new ButtonValidator_toggleEnum(this, button, text_female_vagina, scr_System_CentralControl.current.pref.female_vagina, true)); break;
                case 1261: // female vagina right
                    button.Initialize(this, new ButtonValidator_toggleEnum(this, button, text_female_vagina, scr_System_CentralControl.current.pref.female_vagina)); break;

                case 1300:  // dismember left
                    button.Initialize(this, new ButtonValidator_toggleEnum(this, button, text_dismember, scr_System_CentralControl.current.pref._dismember_mode, true)); break;
                case 1301:  // dismember right
                    button.Initialize(this, new ButtonValidator_toggleEnum(this, button, text_dismember, scr_System_CentralControl.current.pref._dismember_mode)); break;
                case 1310:  // cannibal left
                    button.Initialize(this, new ButtonValidator_toggleEnum(this, button, text_cannibal, scr_System_CentralControl.current.pref._cannibal_mode, true)); break;
                case 1311:  // cannibal right
                    button.Initialize(this, new ButtonValidator_toggleEnum(this, button, text_cannibal, scr_System_CentralControl.current.pref._cannibal_mode)); break;
                case 1320:  // sex prev
                    button.Initialize(this, new ButtonValidator_toggleEnum(this, button,  text_sex, scr_System_CentralControl.current.pref.sex_mode, true)); break;
                case 1321:  // sex next
                    button.Initialize(this, new ButtonValidator_toggleEnum(this, button,  text_sex, scr_System_CentralControl.current.pref.sex_mode)); break;


                case 1401:
                    button.Initialize(this, new ButtonValidator_toggleBoolean_SexFilter(this, button, scr_System_CentralControl.current.pref.male_on_male)); break;
                case 1402:
                    button.Initialize(this, new ButtonValidator_toggleBoolean_SexFilter(this, button, scr_System_CentralControl.current.pref.male_on_male, true)); break;
                case 1403:
                    button.Initialize(this, new ButtonValidator_toggleBoolean_SexFilterCenter(this, button, scr_System_CentralControl.current.pref.male_on_male, scr_System_CentralControl.current.pref.male_on_male)); break;
 
                case 1411:
                    button.Initialize(this, new ButtonValidator_toggleBoolean_SexFilter(this, button, scr_System_CentralControl.current.pref.male_on_female)); break;
                case 1412:
                    button.Initialize(this, new ButtonValidator_toggleBoolean_SexFilter(this, button, scr_System_CentralControl.current.pref.female_on_male, true)); break;
                case 1413:
                    button.Initialize(this, new ButtonValidator_toggleBoolean_SexFilterCenter(this, button, scr_System_CentralControl.current.pref.male_on_female, scr_System_CentralControl.current.pref.female_on_male)); break;


                case 1421:
                    button.Initialize(this, new ButtonValidator_toggleBoolean_SexFilter(this, button, scr_System_CentralControl.current.pref.male_on_ambi)); break;
                case 1422:
                    button.Initialize(this, new ButtonValidator_toggleBoolean_SexFilter(this, button, scr_System_CentralControl.current.pref.ambi_on_male,  true)); break;
                case 1423:
                    button.Initialize(this, new ButtonValidator_toggleBoolean_SexFilterCenter(this, button, scr_System_CentralControl.current.pref.male_on_ambi, scr_System_CentralControl.current.pref.ambi_on_male)); break;

                case 1431:
                    button.Initialize(this, new ButtonValidator_toggleBoolean_SexFilter(this, button, scr_System_CentralControl.current.pref.female_on_female)); break;
                case 1432:
                    button.Initialize(this, new ButtonValidator_toggleBoolean_SexFilter(this, button, scr_System_CentralControl.current.pref.female_on_female, true)); break;
                case 1433:
                    button.Initialize(this, new ButtonValidator_toggleBoolean_SexFilterCenter(this, button, scr_System_CentralControl.current.pref.female_on_female, scr_System_CentralControl.current.pref.female_on_female)); break;

                case 1441:
                    button.Initialize(this, new ButtonValidator_toggleBoolean_SexFilter(this, button, scr_System_CentralControl.current.pref.female_on_ambi)); break;
                case 1442:
                    button.Initialize(this, new ButtonValidator_toggleBoolean_SexFilter(this, button, scr_System_CentralControl.current.pref.ambi_on_female, true)); break;
                case 1443:
                    button.Initialize(this, new ButtonValidator_toggleBoolean_SexFilterCenter(this, button, scr_System_CentralControl.current.pref.female_on_ambi, scr_System_CentralControl.current.pref.ambi_on_female)); break;

                case 1451:
                    button.Initialize(this, new ButtonValidator_toggleBoolean_SexFilter(this, button, scr_System_CentralControl.current.pref.ambi_on_ambi)); break;
                case 1452:
                    button.Initialize(this, new ButtonValidator_toggleBoolean_SexFilter(this, button, scr_System_CentralControl.current.pref.ambi_on_ambi, true)); break;
                case 1453:
                    button.Initialize(this, new ButtonValidator_toggleBoolean_SexFilterCenter(this, button, scr_System_CentralControl.current.pref.ambi_on_ambi, scr_System_CentralControl.current.pref.ambi_on_ambi)); break;


                case 1460:  // sex presence left
                    button.Initialize(this, new ButtonValidator_toggleEnum(this, button, text_sex_presence, scr_System_CentralControl.current.pref.sex_presence_mode, true)); break;
                case 1461:  // sex presence right
                    button.Initialize(this, new ButtonValidator_toggleEnum(this, button, text_sex_presence, scr_System_CentralControl.current.pref.sex_presence_mode)); break;
                
                case 1501:
                    button.Initialize(this, new ButtonValidator_toggleBoolean_SexFilter(this, button, scr_System_CentralControl.current.pref.creature_on_ambi)); break;
                case 1502:
                    button.Initialize(this, new ButtonValidator_toggleBoolean_SexFilter(this, button, scr_System_CentralControl.current.pref.ambi_on_creature, true)); break;
                case 1503:
                    button.Initialize(this, new ButtonValidator_toggleBoolean_SexFilterCenter(this, button, scr_System_CentralControl.current.pref.creature_on_ambi, scr_System_CentralControl.current.pref.ambi_on_creature)); break;

                case 1511:
                    button.Initialize(this, new ButtonValidator_toggleBoolean_SexFilter(this, button, scr_System_CentralControl.current.pref.creature_on_female )); break;
                case 1512:
                    button.Initialize(this, new ButtonValidator_toggleBoolean_SexFilter(this, button, scr_System_CentralControl.current.pref.female_on_creature,  true)); break;
                case 1513:
                    button.Initialize(this, new ButtonValidator_toggleBoolean_SexFilterCenter(this, button, scr_System_CentralControl.current.pref.creature_on_female, scr_System_CentralControl.current.pref.female_on_creature)); break;

                case 1521:
                    button.Initialize(this, new ButtonValidator_toggleBoolean_SexFilter(this, button, scr_System_CentralControl.current.pref.creature_on_male)); break;
                case 1522:
                    button.Initialize(this, new ButtonValidator_toggleBoolean_SexFilter(this, button, scr_System_CentralControl.current.pref.male_on_creature,   true)); break;
                case 1523:
                    button.Initialize(this, new ButtonValidator_toggleBoolean_SexFilterCenter(this, button, scr_System_CentralControl.current.pref.creature_on_male, scr_System_CentralControl.current.pref.male_on_creature)); break;

                case 1531:
                    button.Initialize(this, new ButtonValidator_toggleBoolean_SexFilter(this, button, scr_System_CentralControl.current.pref.creature_on_creature)); break;
                case 1532:
                    button.Initialize(this, new ButtonValidator_toggleBoolean_SexFilter(this, button, scr_System_CentralControl.current.pref.creature_on_creature,   true)); break;
                case 1533:
                    button.Initialize(this, new ButtonValidator_toggleBoolean_SexFilterCenter(this, button, scr_System_CentralControl.current.pref.creature_on_creature,  scr_System_CentralControl.current.pref.creature_on_creature)); break;
                
                case 1550:  // creature setting left
                    button.Initialize(this, new ButtonValidator_toggleEnum(this, button, text_creature,  scr_System_CentralControl.current.pref._creature_mode, true)); break;
                case 1551:  // creature setting right
                    button.Initialize(this, new ButtonValidator_toggleEnum(this, button, text_creature, scr_System_CentralControl.current.pref._creature_mode)); break;
                    
                case 1601:
                    button.Initialize(this, new ButtonValidator_toggleBoolean_SexFilter(this, button, scr_System_CentralControl.current.pref.ambi_on_necro )); break;
                case 1603:
                    button.Initialize(this, new ButtonValidator_toggleBoolean_SexFilterCenter(this, button, scr_System_CentralControl.current.pref.ambi_on_necro,  scr_System_CentralControl.current.pref.ambi_on_necro)); break;

                case 1611:
                    button.Initialize(this, new ButtonValidator_toggleBoolean_SexFilter(this, button, scr_System_CentralControl.current.pref.female_on_necro )); break;
                case 1613:
                    button.Initialize(this, new ButtonValidator_toggleBoolean_SexFilterCenter(this, button, scr_System_CentralControl.current.pref.female_on_necro,  scr_System_CentralControl.current.pref.female_on_necro)); break;

                case 1621:
                    button.Initialize(this, new ButtonValidator_toggleBoolean_SexFilter(this, button, scr_System_CentralControl.current.pref.male_on_necro )); break;
                case 1623:
                    button.Initialize(this, new ButtonValidator_toggleBoolean_SexFilterCenter(this, button, scr_System_CentralControl.current.pref.male_on_necro,  scr_System_CentralControl.current.pref.male_on_necro)); break;

                case 1631:
                    button.Initialize(this, new ButtonValidator_toggleBoolean_SexFilter(this, button, scr_System_CentralControl.current.pref.creature_on_necro)); break;
                case 1633:
                    button.Initialize(this, new ButtonValidator_toggleBoolean_SexFilterCenter(this, button, scr_System_CentralControl.current.pref.creature_on_necro,  scr_System_CentralControl.current.pref.creature_on_necro)); break;
                
                case 1640:  // necro setting left
                    button.Initialize(this, new ButtonValidator_toggleEnum(this, button, text_necro, scr_System_CentralControl.current.pref._necro_mode, true)); break;
                case 1641:  // necro setting right
                    button.Initialize(this, new ButtonValidator_toggleEnum(this, button, text_necro, scr_System_CentralControl.current.pref._necro_mode)); break;
                
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
        box_gender_demo.gameObject.SetActive(false);
        ValidateAll();

       // 

    }

    public override void ValidateAll()
    {
        base.ValidateAll();

        if (scr_System_CentralControl.current.pref.SexMode >= Sex_Mode.enabled)
        {
            box_gender_on.gameObject.SetActive(true);
            box_gender_off.gameObject.SetActive(false);
            box_train_on.gameObject.SetActive(true);
            box_train_off.gameObject.SetActive(false);
        }
        else
        {
            box_gender_on.gameObject.SetActive(false);
            box_gender_off.gameObject.SetActive(true);
            box_train_on.gameObject.SetActive(false);
            box_train_off.gameObject.SetActive(true);
        }


        if (true || scr_System_CentralControl.current.pref.SexMode != Sex_Mode.hardcore)
        {
            box_hardcore.gameObject.SetActive(false);
        }
        else
        {
            box_hardcore.gameObject.SetActive(true);
        }
    }


    protected override void Start()
    {
        base.Start();
    }

    public RectTransform gender_priority_left, gender_priority_right;
    public TMP_Text text_gender_priority;

    public TMP_Text text_male_app, text_male_penis, text_male_vagina, text_female_app, text_female_penis, text_female_vagina;

    public TMP_Text text_dismember, text_cannibal, text_sex, text_sex_presence, text_creature, text_necro;

    public RectTransform box_sex;
    public RectTransform box_hardcore,box_bestiality,box_necrophilia;

    public RectTransform box_gender_on, box_gender_off;
    public RectTransform box_gender_male, box_gender_female, box_gender_ambiguous, box_gender_demo;

    public RectTransform box_train_on, box_train_off;

    public override void Notify(int optionID)
    {
        // reset conflict validators
        Debug.Log("Parent Notified ! [" + optionID + "]");
        //
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
                /*
                case 1205:
                    GetButtonByID(1205).Toggle();
                    if (box_gender_demo.gameObject.activeInHierarchy) box_gender_demo.gameObject.SetActive(false);
                    else
                    {
                        box_gender_demo.gameObject.SetActive(true);
                        //ValidateGenderDemo();
                    }
                    break;*/
                case 9999:
                    scr_System_CentralControl.current.SaveUserPref();
                    scr_System_SceneManager.current.UnloadLastCanvasFromScene();
                    break;
                default:
                    break;
            }
        }
        //ValidateGenderDemo();
        ValidateAll();
    }


    private void ValidateGenderDemo()
    {
        //if (scr_System_CentralControl.current.pref.SexMode == Sex_Mode.disabled) return;
        //if (!box_gender_demo.gameObject.activeInHierarchy) return;
        box_gender_demo.gameObject.SetActive(true);
        box_gender_ambiguous.gameObject.SetActive(true);
        box_gender_female.gameObject.SetActive(true);
        box_gender_male.gameObject.SetActive(true);

        foreach (scr_Gender_demoChara c in demoCharaList)
        {
            var symbol = scr_System_CentralControl.current.GetGenderSimple(c.Template);
            if (symbol == InteractionGenderType.female) c.transform.SetParent(box_gender_female, false);
            else if (symbol == InteractionGenderType.male) c.transform.SetParent(box_gender_male, false);
            else c.transform.SetParent(box_gender_ambiguous, false);
        }

    }

    
    class ButtonValidator_toggleBoolean_SexFilter : ButtonValidator, I_ButtonClickable
    {
        new scr_MenuCanvas_UserPrefs parent;
        bool isLeft;
        scr_SelectableText button;
        scr_HoverableText centerText;
        BoolSetting targetBool;
        public ButtonValidator_toggleBoolean_SexFilter(scr_Menu parent, scr_SelectableText selfbox, BoolSetting targetBool, bool isLeft = false) : base(parent)
        {

            this.parent = parent as scr_MenuCanvas_UserPrefs;
            this.isLeft = isLeft;
            this.button = selfbox;
            this.targetBool = targetBool;
        }

        public override bool IsButtonValid()
        {
           // if (!button.gameObject.activeInHierarchy) return false;
            if (targetBool.value)
            {
                button.Toggle(true, true);
                if (isLeft) button.SetText(" < ");
                else button.SetText(" > ");
            }
            else
            {
                button.Toggle(true, false);
                button.SetText(" - ");
            }

            return true;
        }

        void I_ButtonClickable.OnClickButton()
        {
            targetBool.Toggle();
        }
    }

    class ButtonValidator_toggleBoolean_SexFilterCenter : ButtonValidator, I_ButtonClickable
    {
        new scr_MenuCanvas_UserPrefs parent;
        BoolSetting target1, target2;

        scr_SelectableText button;

        public ButtonValidator_toggleBoolean_SexFilterCenter(scr_Menu parent, scr_SelectableText selfbutton, BoolSetting target1, BoolSetting target2) : base(parent)
        {
            this.parent = parent as scr_MenuCanvas_UserPrefs;
            this.target1 = target1;
            this.target2 = target2;
            this.button = selfbutton;
        }

        public override bool IsButtonValid()
        {
            //if (!button.gameObject.activeInHierarchy) return false;
            if (target1.value == false && target2.value == false)
            {
                button.Toggle(true, false);
                button.SetText(" X ");
            }
            else
            {
                button.Toggle(true, true);
                button.SetText(" - ");
            }

            return true;
        }

        void I_ButtonClickable.OnClickButton()
        {
            if (!target1.value && !target2.value)
            {
                target1.value = true;
                target2.value = true;
            }
            else
            {
                target1.value = false;
                target2.value = false;
            }
        }
    }

    class ButtonValidator_DisplayGenderDemo : ButtonValidator, I_ButtonClickable
    {
        new scr_MenuCanvas_UserPrefs parent;
        scr_SelectableText selfButton;
        public ButtonValidator_DisplayGenderDemo(scr_MenuCanvas_UserPrefs parent, scr_SelectableText selfButton) : base(parent)
        {
            this.parent = parent;
            this.selfButton = selfButton;
        }
        public override bool IsButtonValid()
        {
            if( scr_System_CentralControl.current.pref.SexMode >= Sex_Mode.enabled)
            {
                if (parent.box_gender_demo.gameObject.activeInHierarchy) parent.ValidateGenderDemo();
                return true;
            }
            else
            {
                parent.box_gender_demo.gameObject.SetActive(false);
                return false;
            }

        }

        void I_ButtonClickable.OnClickButton()
        {
            if (parent.box_gender_demo.gameObject.activeInHierarchy)
            {
                parent.box_gender_demo.gameObject.SetActive(false);
                selfButton.Toggle(true, false);
            }
            else
            {
                parent.box_gender_demo.gameObject.SetActive(true);
                selfButton.Toggle(true, true);
            }
        }

    }

    class ButtonValidator_toggleEnum : ButtonValidator, I_ButtonClickable
    {
        new scr_MenuCanvas_UserPrefs parent;
        EnumSetting targetObj;
        TMP_Text text;
        scr_SelectableText selfButton;
        bool gotoPrev;
        public ButtonValidator_toggleEnum(scr_Menu parent, scr_SelectableText selfButton, TMP_Text text, EnumSetting target, bool gotoPrev=false) : base(parent)
        {
            this.parent = parent as scr_MenuCanvas_UserPrefs;
            this.targetObj = target;
            this.text = text;
            this.selfButton = selfButton;
            this.gotoPrev = gotoPrev;
        }

        public override bool IsButtonValid()
        {
            //if (!selfButton.gameObject.activeInHierarchy) return false;
            text.text = scr_System_Serializer.current.Dictionary.QueryThenParse( targetObj.DisplayName());
            if (gotoPrev) return targetObj.HasPrev();
            else return targetObj.HasNext();
            
        }

        void I_ButtonClickable.OnClickButton()
        {
            if (gotoPrev) targetObj.Prev();
            else targetObj.Next();
        }
    }

}
