using UnityEngine;
using System;
using UnityEngine.EventSystems;
using Cysharp.Threading.Tasks.Triggers;

public class scr_menu_AddProductionOrder : scr_Menu, IPointerClickHandler
{

    public Manageable faction;
    public RectTransform recipeList;
    public scr_addPO_recipe prefab_recipe;

    public void InitializeWithArgument(Manageable targetFaction, Action onExit)
    {
        this.onSelfExit = onExit;
        if (!initialized) Initialize();

        this.faction = targetFaction;
        Utility.DestroyAllChildrenFrom( recipeList);

        foreach(var recipe in Masterlist_Items.Instance.CraftingRecipe.Values)
        {
            // TODO INSTANTIATE BUTTON
            MakeRecipeButton(recipe);

        }
        ValidateAll();
    }

    public void NotifyAddRecipe(ItemComponentTemplate_Craftable_Recipe recipe)
    {
        faction.AddProductionOrder(recipe, 0);
        scr_System_SceneManager.current.UnloadLastCanvasFromScene();
    }

    private void MakeRecipeButton(ItemComponentTemplate_Craftable_Recipe recipe)
    {
        int recipeHash = AssertUniqueHash(recipe.RecipeUID.GetHashCode());
        scr_addPO_recipe box = Instantiate(prefab_recipe);
        box.LoadRecipe(faction, recipe);
        RegisterButton(recipeHash, box.Button, new Button_SelectRecipe(this, recipe, box.Button));
        box.GetComponent<RectTransform>().SetParent(recipeList, false);
    }

    private void RegisterButton(int optionID, scr_SelectableText button, ButtonValidator validator)
    {
        if (!buttonsByID.ContainsKey(optionID))
        {
            button.Initialize(this, validator);
            button.optionID = optionID;
            buttonsByID.Add(button.optionID, button);
            validatorsByID.Add(button.optionID, button.Validator);
           // button.Validate();
           // return true;
        }
       // else return false;
    }


    protected override void Awake()
    {
        base.Awake();
        this.m_Canvas.overrideSorting = true;
        button_alwaysValid = new ButtonValidator_AlwaysTrue(this);
        faction = null;
    }

    public override void Initialize()
    {
        base.Initialize();

        foreach (scr_SelectableText button in GetComponentsInChildren<scr_SelectableText>(true))
        {
            // Debug.Log("Button " + button + " " + button.optionID);
            switch (button.optionID)
            {
                case 9999: // exit
                    button.Initialize(this, button_alwaysValid); break;
                default:  break;
            }
            if (button.optionID != -1)
            {
                buttonsByID.Add(button.optionID, button);
                validatorsByID.Add(button.optionID, button.Validator);
            }

        }
        // build all presetLis

    }

    public override void ValidateAll()
    {
        base.ValidateAll();
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
                case 9999: scr_System_SceneManager.current.UnloadLastCanvasFromScene(); break;
                default: break;
            }
        }
        ValidateAll();
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        /*
        while (list_Jobs.transform.childCount > 0)
        {
            DestroyImmediate(list_Jobs.transform.GetChild(0).gameObject);
        }*/
        //Debug.LogError("CANVAS MANAGEMENT ONDESTROY");
        scr_System_CampaignManager.current.NotifyUpdate();
         
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        // if click outside box
        if ((eventData.rawPointerPress.GetComponent<scr_Canvas_Management>() != null) || (eventData.button == PointerEventData.InputButton.Right && UtilityEX.isClickBelowDragThreshold(eventData)))
        {
            scr_System_SceneManager.current.UnloadLastCanvasFromScene();
        }
    }

    public class Button_SelectRecipe : ButtonValidator, I_ButtonClickable
    {
        new scr_menu_AddProductionOrder parent;
        ItemComponentTemplate_Craftable_Recipe recipe;
        scr_SelectableText button;
        public Button_SelectRecipe(scr_menu_AddProductionOrder parent, ItemComponentTemplate_Craftable_Recipe recipe, scr_SelectableText button) :base(parent)
        {
            this.parent = parent;
            this.recipe = recipe;
            this.button = button;
            this.tooltip = recipe.Tooltip;
        }

        public override bool IsButtonValid()
        {
            return parent.faction != null;
        }

        public void OnClickButton()
        {
            parent.NotifyAddRecipe(recipe);
        }
    }

}
