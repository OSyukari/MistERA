using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class scr_panel_wombdata : MonoBehaviour
{

    public Image selfImage;
    scr_Menu_CharaDetail parent;
    public RectTransform selfRect;
    protected void Awake()
    {
        selfImage.color = scr_System_CentralControl.current.DisplaySetting.BackgroundColor_Transparent.Color;
    }
    BodyInternal_Womb womb;
    List<string> images = null;
    public void InitializeWithArgument(scr_Menu_CharaDetail parent, BodyInternal_Womb womb)
    {
        this.parent = parent;
        this.womb = womb;

        images = womb.GetImages;

        if (co1 != null)
        {
            StopCoroutine(co1);
            co1 = null;
        }
        if (co2 != null)
        {
            StopCoroutine(co2);
            co2 = null;
        }
        if (co3 != null)
        {
            StopCoroutine(co3);
            co3 = null;
        }


        if (images.Count >= 1 && images[0] != "") imagePath = images[0];
        else imagePath = "";


        if (images.Count >= 2 && images[1] != "") overlayPath = images[1];
        else overlayPath = "";

        var ovumimage = womb.ovumImage;
        if (ovumimage != "") ovumPath = ovumimage;
        else ovumPath = "";

        title.SetText(womb.DisplayName);
        title.SetExternalTooltip($"source name {womb.source.DisplayNameFull}");
        desc.SetText(womb.debugTooltip);


        // current active ovum count
        if (womb.source.canContain && womb.source.volume_capacity > 0.01f)
        {
            int ovum_ready = 0;
            int ovum_fertilized = 0;
            int ovum_implanted = 0;
            int ovum_foetus_count = 0;
            int ovum_foetus_object = 0;

            foreach (var i in womb.eggs)
            {
                // if ovum, compute fertility, lifespan, current status
                if (i.State == OvumState.Default) ovum_ready += 1;
                else if (i.State == OvumState.Fertilized) ovum_fertilized += 1;
                else if (i.State == OvumState.Implanted) ovum_implanted += 1;
                else if (i.State > OvumState.Implanted) ovum_foetus_count += 1;
            }


            float totalVolume = 0;
            foreach (var i in womb.source.Contains)
            {
                totalVolume += i.GetComp_Ingestible().amount;

                var box = Instantiate(prefab_itemInstance);
                box.SelfRect.SetParent(itemGrid, false);

                box.SetText(i.Print());
                if (i.BaseID == "item_foetus")
                {
                    // implanted ovum, growing
                    ovum_foetus_object += 1;
                    //box.SetText(i.DisplayName);
                    foreach(var egg in womb.eggs)
                    {
                        if (egg.foetusItem == i)
                        {
                            box.SetExternalTooltip(egg.Tooltip);
                        }
                    }
                    //box.SetExternalTooltip(i.Tooltip);
                }
                else if (i is Item_Instance_Cum)
                {
                    var cum = i as Item_Instance_Cum;
                    // if cum, comute cum lifespan and preg chance
                    //box.SetText($"{cum.DisplayName} {cum.CumAmount}ml");
                    var ttips = i.Tooltip;
                    float totalfert = 0;
                    string warning = "";
                    string formula = "";
                    foreach(var egg in womb.eggs)
                    {
                        if (egg.State != OvumState.Default) continue;
                        totalfert = womb.CalcFertility(cum, out var warn, out formula);
                        if (warn != "") warning = warn;
                        break;
                    }

                    var finalttips = $"{i.Tooltip}{(i.Tooltip.Length > 0 ? "\n\n" : "")}{LocalizeDictionary.QueryThenParse("Item_Instance_Cum_fertChance_perHour").Replace("$chance$", (totalfert).ToString("N1"))}{(warning == "" ? "" : $" {LocalizeDictionary.QueryThenParse(warning)}")}";
                    if (scr_System_CampaignManager.current.DebugMode)
                    {
                        finalttips += $"\n{formula}";
                    }

                    box.SetExternalTooltip(finalttips);

                }
                else
                {
                    //box.SetText(i.DisplayName);
                    box.SetExternalTooltip(i.Tooltip);
                }
                //box.SetExternalTooltip(i.Tooltip);
            }

            var ovtext = "";
            if (ovum_ready > 0) ovtext += (ovtext.Length > 0 ? " " : "") + LocalizeDictionary.QueryThenParse("charaDetail_panel_cycle_OvumState_Default").Replace("$count$", $"{ovum_ready}");
            if (ovum_fertilized > 0) ovtext += (ovtext.Length > 0 ? " " : "") + LocalizeDictionary.QueryThenParse("charaDetail_panel_cycle_OvumState_Fertilized").Replace("$count$", $"{ovum_fertilized}");
            if (ovum_implanted > 0) ovtext += (ovtext.Length > 0 ? " " : "") + LocalizeDictionary.QueryThenParse("charaDetail_panel_cycle_OvumState_Implanted").Replace("$count$", $"{ovum_implanted}");
            if (ovum_foetus_count > 0) ovtext += (ovtext.Length > 0 ? " " : "") + LocalizeDictionary.QueryThenParse("charaDetail_panel_cycle_OvumState_Foetus").Replace("$count$", $"{ovum_foetus_count}");
            ovums.SetText(LocalizeDictionary.QueryThenParse("charaDetail_panel_cycle_ovumLists")
                .Replace("$content$", ovtext.Length > 0 ? ovtext : LocalizeDictionary.QueryThenParse("none"))
                .Replace("$total$", $"{womb.eggs.Count}"));

            string debug = $"{womb.source.VolumeCapacity.ToString("N0")}ml|{womb.source.VisiblyExpandedCapacity.ToString("N0")}ml|{womb.source.MaxCapacity.ToString("N0")}ml";
            if (scr_System_CampaignManager.current.DebugMode && ovum_foetus_object != ovum_foetus_count)
            {
                debug += $"\nDiscrepancy between ovum count {ovum_foetus_count} and object count {ovum_foetus_object}";
            }

            desc.SetText(LocalizeDictionary.QueryThenParse("charaDetail_panel_womb_content").Replace("$volume$", totalVolume.ToString($"N1")));
            desc.SetExternalTooltip(debug);
            //ovums.SetText($"Ovums: ready {ovum_ready} fertilized {ovum_fertilized} foetus {ovum_foetus}");
        }
        else
        {
            ovums.gameObject.SetActive(false);
            desc.gameObject.SetActive(false);
        }

    }

    public scr_HoverableText title, desc, ovums;
    public RectTransform itemGrid;
    public scr_HoverableText prefab_itemInstance;

    protected void Start()
    {
        if (imagePath != "")
        {
            wombImage.gameObject.SetActive(true);
            co1 = StartCoroutine(loadImage(wombImage, imagePath));
        }
        else
        {
            wombImage.gameObject.SetActive(false);
        }

        if (overlayPath != "")
        {
            wombOverlay.gameObject.SetActive(true);
            co2 = StartCoroutine(loadImage(wombOverlay, overlayPath));
        }
        else
        {
            wombOverlay.gameObject.SetActive(false);
        }

        if (ovumPath != "")
        {
            ovumImage.gameObject.SetActive(true);
            co3 = StartCoroutine(loadImage(ovumImage, ovumPath));
        }
        else
        {
            ovumImage.gameObject.SetActive(false);
        }
    }

    string imagePath = "";
    string overlayPath = "";
    string ovumPath = "";

    public Image wombImage;
    public Image wombOverlay;
    public Image ovumImage;

    Coroutine co1 = null, co2 = null, co3 = null;

    public IEnumerator loadImage(Image i, string a)
    {
        if (scr_System_CentralControl.current.GetSprite(a, out var sprite))
        {
            i.sprite = sprite;
        }
        else
        {
            Texture2D loaded = null;
            yield return AssetsLoader.LoadTextureCoroutine(a, texture => loaded = texture);
            i.sprite = scr_System_CentralControl.current.MakeSprite(a, loaded);
        }
    }
}
