using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using UnityEngine.UI;
using System;

public class scr_Panel_BodyDetail : MonoBehaviour
{
    int ownerRefID;
    string bodyBaseID;
    string internalBaseID;


    BodyInternal_Instance instance;
    public Image selfImage;
    scr_Menu_CharaDetail parent;
    int referenceIndex;

    protected void Awake()
    {
        selfImage.color = scr_System_CentralControl.current.DisplaySetting.BackgroundColor_Transparent.Color;
    }
    public void InitializeWithArgument(scr_Menu_CharaDetail parent, int referenceIndex)
    {
        this.parent = parent;
        this.referenceIndex = referenceIndex;

        this.instance = parent.GetInternalwithIndex(referenceIndex);

        //boxName.text = "Body Part: "+instance.DisplayName;
        boxName.text = instance.DisplayName;

        //image.gameObject.SetActive(false);

        // INCREASE SCROLL SPEED

        
        if (instance.canFuck)
        {
            boxSize.gameObject.SetActive(true);
            boxDepth.gameObject.SetActive(true);
            boxSensitivity.gameObject.SetActive(true);

            boxSize.text = "Size: [" + instance.Size.ToString("N1") + "]";
            boxDepth.text = "Length [" + instance.Depth.ToString("N1") + "]";
            boxSensitivity.text = instance.Sensitivity;
        }
        else if (instance.canBeFucked)
        {
            boxSize.gameObject.SetActive(true);
            boxDepth.gameObject.SetActive(true);
            boxSensitivity.gameObject.SetActive(true);

            boxSize.text = "Size: "+instance.Rank_Size+"[" + (instance.Size != 0 ? instance.Size.ToString("N1")+"/"+ instance.MaxSize.ToString("N1") : " - ") + "]";
            boxDepth.text = "Depth " + instance.Rank_Depth + "[" + (instance.Depth != 0 ? instance.Depth.ToString("N1")+"/"+instance.MaxDepth.ToString("N1") : " - ") + "]";
            boxSensitivity.text = instance.Sensitivity;
        }
        else
        {
            boxSize.gameObject.SetActive(false);
            boxDepth.gameObject.SetActive(false);
            boxSensitivity.gameObject.SetActive(false);
        }

        instance.Draw_FirstExperience(firstExp);
        instance.Draw_LastExperience(lastExp);
        

        if (instance.canContain && instance.volume_capacity > 0.01f)
        {
            float totalVolume = 0;
            List<string> volumeTooltip = new List<string>();

            foreach (var i in instance.Contains)
            {
                volumeTooltip.Add("name [" + i.DisplayName + "] amount [" + i.GetComp_Ingestible().amount + "]");
                totalVolume += i.GetComp_Ingestible().amount;
            }

            
            boxVolume.SetText($"Content: {totalVolume}ml / {instance.VolumeCapacity.ToString("N0")}ml|{instance.VisiblyExpandedCapacity.ToString("N0")}ml|{instance.MaxCapacity.ToString("N0")}ml");
            if (volumeTooltip.Count > 0) boxVolume.SetExternalTooltip(String.Join("\n", volumeTooltip));
        }

        //this.mostExp.gameObject.SetActive(false);
    }
    /*
    private void LoadImage()
    {
        if (instance.hasTag("vagina")) LoadSprite(XraySprite.widget_vag1);
        else if (instance.hasTag("anus")) LoadSprite(XraySprite.widget_ass1);
        else if (instance.hasTag("mouth")) LoadSprite(XraySprite.widget_oral1);
    }*/

    public Image image;
    public TMP_Text boxName, boxSize, boxDepth, boxSensitivity;
    public scr_HoverableText boxVolume;
    public RectTransform box_Fuckable;
    public RectTransform description;

    public scr_HoverableText firstExp, lastExp;
}
