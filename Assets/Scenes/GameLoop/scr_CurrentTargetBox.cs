using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using TMPro;
using UnityEngine.UI;
using System.IO;

/*
public class scr_CurrentTargetBox : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
{

    public Image picture;

    public RectTransform box_Xray;
    public RectTransform box_lust;
    public RectTransform box_ovum;


    // Start is called before the first frame update
    private void Awake()
    {
        update = 0.0f;
        box_lust.gameObject.SetActive(false);
        box_Xray.gameObject.SetActive(false);
        box_ovum.gameObject.SetActive(false);


        scr_System_CampaignManager.current.Observer_CurrentTarget += ReadCurrentChar;
        // How to observe
        // private void UpdateFunction(TimeSpan elapsedTime) { }
        // 
        // on instantiate
    }

    private void ReadCurrentChar(int id)
    {
        if (id == -1) return;

    }

    Character_Trainable chara = null;
    int chara_refID = -1;
    Canvas canvas;
    public bool InitializeWithArgument(int refID, Canvas canvas)
    {
        this.canvas = canvas;
        chara_refID = refID;
        chara = scr_System_CampaignManager.current.FindInstanceByID(chara_refID) as Character_Trainable;
        if (chara != null)
        {
            if (scr_System_CentralControl.current.pref.icon_display == Icon_Display_Mode.Picture)
            {
                if (chara.portraitPath != "" && chara.defaultIcon != "") Initialize("", chara.portraitPath + chara.defaultIcon);
                else Initialize("", DataPath.icon_default);
            }
            else
            {
                if (chara.portraitPath != "" && chara.defaultAAIcon != "") Initialize(chara.portraitPath + chara.defaultAAIcon);
                else Initialize(DataPath.iconAA_default);
            }
            updateImage();
            return true;
        }
        else
        {
            return false;
        }

    }

    private Texture2D LoadTexture(string FilePath)
    {

        // Load a PNG or JPG file from disk to a Texture2D
        // Returns null if load fails

        Texture2D Tex2D;
        byte[] FileData;

        if (File.Exists(FilePath))
        {
            FileData = File.ReadAllBytes(FilePath);
            Tex2D = new Texture2D(2, 2);           // Create new "empty" texture
            if (Tex2D.LoadImage(FileData))           // Load the imagedata into the texture (size is set automatically)
                return Tex2D;                 // If data = readable -> return texture
        }
        return null;                     // Return null if load failed

    }


    private void loadSprite(string path, Image image)
    {
        Texture2D SpriteTexture = LoadTexture(Application.dataPath + "/" + path);
        Sprite NewSprite = Sprite.Create(SpriteTexture, new Rect(0, 0, SpriteTexture.width, SpriteTexture.height), new Vector2(0, 0), 100.0f);
        image.sprite = NewSprite;
    }


    private void Initialize(string target, string targetAlt = "")
    {
        loadSprite(targetAlt, picture);
        mouseOver = false;
    }

    public RectTransform prefab_Canvas_charaDetail;
    scr_Menu_CharaDetail detail;

    void IPointerClickHandler.OnPointerClick(PointerEventData eventData)
    {

        if (this.chara != null)
        {
            Debug.Log("Mouse Click on [" + this.chara.baseID + "] refID [" + chara.RefID + "]");

            detail = scr_System_SceneManager.current.LoadCanvasIntoScene(prefab_Canvas_charaDetail, canvas.GetComponent<RectTransform>()).GetComponent<scr_Menu_CharaDetail>();
            detail.InitializeWithArgument(chara_refID);
        }
    }

    bool mouseOver;
    public void OnPointerEnter(PointerEventData eventData)
    {
        if (mouseOver != true)
        {
            //picture.gameObject.SetActive(true);
            //picture_AA.gameObject.SetActive(false);
            mouseOver = true;
            updateImage();

        }

    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (mouseOver != false)
        {
            //picture.gameObject.SetActive(false);
            //picture_AA.gameObject.SetActive(true);
            mouseOver = false;

            updateImage();
        }

    }

    private void updateImage()
    {
        if (chara == null) return;

        if (scr_System_CentralControl.current.adult)
        {
            box_lust.gameObject.SetActive(true);
        }

        if (scr_System_CentralControl.current.xray_mode > 0)
        {
            box_Xray.gameObject.SetActive(true);
            switch (scr_System_CentralControl.current.xray_mode)
            {
                case (XRay_Mode.widget_first):
                    if (mouseOver) xray_eratw();
                    else xray_widget();
                    break;
                default:
                    xray_none();
                    break;
            }
        }
        else
        {
            box_lust.gameObject.SetActive(false);
            box_Xray.gameObject.SetActive(false);
            box_ovum.gameObject.SetActive(false);
        }

        ScaleStatBar(hp, chara.Stats.HP);
        ScaleStatBar(mp, chara.Stats.MP);
        ScaleStatBar(st, chara.Stats.Stamina);
        ScaleStatBar(en, chara.Stats.Energy);

        //Debug.Log("refresh character " + chara.FirstName + " hp " + chara.Stats.HP.Value + "/" + chara.Stats.HP.MaxValue + " mp "+chara.Stats.MP.Value+"/"+ chara.Stats.MP.MaxValue+" st "+ chara.Stats.Stamina.Value+"/"+ chara.Stats.Stamina.MaxValue+" en "+ chara.Stats.Energy.Value+"/"+ chara.Stats.Energy.MaxValue);
    }

    public RectTransform hp, mp, st, en;

    private void ScaleStatBar(RectTransform bar, Stats_Derived_InstanceBase_Extend stat)
    {
        var scale = bar.localScale;
        if (stat.MaxValue > 0.2f) scale.x = stat.Value / stat.MaxValue;
        else scale.x = 0f;
        //Debug.Log("scale stat bar : " + stat.DisplayName + " " + stat.Value + "/" + stat.MaxValue);
        bar.localScale = scale;
    }


    private void xray_widget()
    {
        xray_B.GetComponent<Image>().sprite = null;
        xray_W.GetComponent<Image>().sprite = null;

        if (chara.isMale && !scr_System_CentralControl.current.gay)
        {
            xray_A.GetComponent<Image>().sprite = null;
            xray_M.GetComponent<Image>().sprite = null;
        }
        else
        {
            Utility.LoadSprite(XraySprite.widget_ass0, xray_A.GetComponent<Image>());
            Utility.LoadSprite(XraySprite.widget_oral0, xray_M.GetComponent<Image>());
        }

        if (chara.isFemale)
        {
            box_ovum.gameObject.SetActive(true);
            Utility.LoadSprite(XraySprite.rjw_egg1, box_ovum.GetComponent<Image>());
            Utility.LoadSprite(XraySprite.widget_vag1, xray_V.GetComponent<Image>());
        }
        else
        {
            box_ovum.gameObject.SetActive(false);
            xray_V.GetComponent<Image>().sprite = null;
        }

    }

    private void xray_eratw()
    {
        xray_B.GetComponent<Image>().sprite = null;
        xray_M.GetComponent<Image>().sprite = null;

        if (chara.isMale && !scr_System_CentralControl.current.gay) xray_A.GetComponent<Image>().sprite = null;
        else Utility.LoadSprite(XraySprite.eratw_a1, xray_A.GetComponent<Image>());

        if (chara.isFemale)
        {
            box_ovum.gameObject.SetActive(true);
            Utility.LoadSprite(XraySprite.rjw_egg1, box_ovum.GetComponent<Image>());
            Utility.LoadSprite(XraySprite.eratw_w1, xray_W.GetComponent<Image>());
            Utility.LoadSprite(XraySprite.eratw_v1, xray_V.GetComponent<Image>());
        }
        else
        {
            box_ovum.gameObject.SetActive(false);
            xray_W.GetComponent<Image>().sprite = null;
            xray_V.GetComponent<Image>().sprite = null;
        }
    }


    private void xray_none()
    {
        box_ovum.gameObject.SetActive(false);
        xray_A.GetComponent<Image>().sprite = null;
        xray_B.GetComponent<Image>().sprite = null;
        xray_M.GetComponent<Image>().sprite = null;
        xray_W.GetComponent<Image>().sprite = null;
        xray_V.GetComponent<Image>().sprite = null;
    }

    public RectTransform xray_B, xray_M, xray_W, xray_V, xray_A;


    private float update;

    public void Update()
    {
        update += Time.deltaTime;
        if (update > 1.0f)
        {
            update = 0.0f;
            updateImage();
        }

    }

}

*/