using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Newtonsoft.Json;
using System.Linq;
using System;
using UnityEngine.UI;
using JetBrains.Annotations;

[System.Serializable]
public class PortraitManager
{
    public List<CharaPortrait> portraitPriorityList = null;

    public PortraitManager()
    {
        //Debug.Log("PortraitManager instantiate on null call");
    }
    public PortraitManager(Character_Trainable c)
    {
        this._owner = c;
        if (portraitPriorityList != null) foreach(var i in portraitPriorityList) i.Owner = this;
    }
    protected Character_Trainable _owner = null;
    [JsonIgnore] public Character_Trainable Owner { get { return _owner; } }


    public void RebuildInternal(Character_Trainable c)
    {
        _owner = c;
        if (portraitPriorityList != null) foreach (var i in portraitPriorityList) i.Owner = this;
    }

    public void Prepend(CharaPortrait cm)
    {
        if (this.portraitPriorityList == null || this.portraitPriorityList.Count < 1) this.portraitPriorityList = new List<CharaPortrait>();
        this.portraitPriorityList.Insert(0,cm);
        cm.Owner = this;

        Debug.Log("new portrait, owner ["+Owner.FirstName+"] cmOwner["+cm.Owner.Owner.FirstName+"] isImage[" + (cm is PortraitManager.CharaPortrait_Image) + "] listCount["+portraitPriorityList.Count+"] variantsCount [" + cm.Variants.Count + "] isvalid[" + (cm.isValid()) + "] portraitPath[" + (cm as PortraitManager.CharaPortrait_Image).PortraitPath + "]");
    }

    public CharaPortrait GetValidPortrait()
    {

        if (portraitPriorityList == null || portraitPriorityList.Count == 0)
        {
            Debug.Log("GetValidPortrait return new");
            return new CharaPortrait_Image("", "");
        }

        foreach (var i in portraitPriorityList)
        {
            if (i.isValid())
            {
                //Debug.Log("GetValidPortrait list valid fo ownr "+Owner.FirstName);
                return i;
            }
        }
        //Debug.Log("GetValidPortrait return last");
        return portraitPriorityList[portraitPriorityList.Count - 1];
    }

    public void Click()
    {
        this.GetValidPortrait().Click();
    }

    [System.Serializable]
    public class PortraitVariant
    {
        public List<PortraitValidators> Conditions;
        public string PortraitVariantData;
        public string IconVariantData;
        public bool Validate(Character_Trainable c)
        {
            if (Conditions == null || Conditions.Count < 1) return true;
            foreach(var i in Conditions) if (!i.Validate(c)) return false;

            return true;
        }
    }

    [System.Serializable]
    public class PortraitValidators
    {
        public string requireNaked = "";
        public bool Validate(Character_Trainable c)
        {
            bool returnVal = true;
            returnVal = (requireNaked == "" || c.Body.GetRevealingScoreByTag(requireNaked, BodyEquipLayer.None) <= 0);

            return returnVal;
        }
    }

    [System.Serializable]
    public class CharaPortrait
    {
        [JsonIgnore] public PortraitManager Owner;

        public float portrait_offset_x;
        public float portrait_offset_y;
        public float portrait_offset_size;

        public float icon_offset_x;
        public float icon_offset_y;
        public float icon_offset_size;

        public bool Disable = false;

        public List<PortraitVariant> Variants;

        public virtual bool isValid() { return false; }
        public virtual void DrawIcon(scr_CharIconBox iconBox)
        {
            iconBox.picture.sprite = SpriteAsset.transparent;
        }
        public virtual void DrawPortrait(scr_CharPortraitBox portraitBox)
        {
            portraitBox.picture.gameObject.SetActive(true);
            //portraitBox.spineLoader.gameObject.SetActive(false);
            portraitBox.picture.sprite = SpriteAsset.transparent;
        }
        public virtual void DrawPortrait(scr_Menu_CharaDetail movablePortraitBox)
        {
            //Debug.LogError("BaseDrawportrait claled");
            movablePortraitBox.picture.gameObject.SetActive(true);
            movablePortraitBox.spineLoader.gameObject.SetActive(false);

            movablePortraitBox.picture.sprite = SpriteAsset.transparent;
        }

        public virtual void SetPortraitOffsets(float offsetX, float offsetY, float offsetSize)
        {
            Debug.LogError("Base Portrait setoffset called");
        }

        public virtual void Click()
        {
            //Debug.Log("Portrait Clicked on "+Owner.Owner.FirstName);
        }

        // More internal condition validation with shared portrait position data
    }

    [System.Serializable]
    public class CharaPortrait_Image : CharaPortrait
    {   // do not allow multiple transparent image layering -> should use spine instead.
        public string portrait_path = "";
        public string icon_path="";

        public CharaPortrait_Image()
        {

        }
        public CharaPortrait_Image(string portrait_path, string icon_path)
        {
            this.portrait_path = portrait_path;
            this.icon_path = icon_path;

            portrait_offset_x = 0;
            portrait_offset_y = 0;
            portrait_offset_size = 1;

            icon_offset_x = 0;
            icon_offset_y = 0;
            icon_offset_size = 1;
        }

        public override bool isValid() { return !Disable; }
        public override void DrawIcon(scr_CharIconBox iconBox)
        {
            var drawImg = IconPath;
            if (drawImg == "") drawImg = PortraitPath;  // if no icon but have portrait then draw portrait using icon offset

            if (drawImg == "") base.DrawIcon(iconBox);  // no fallback, draw transparent
            else
            {
                iconBox.picture.sprite = scr_System_CentralControl.current.LoadCachedSprite(drawImg);

                iconBox.picture.SetNativeSize();
                iconBox.picture.rectTransform.anchoredPosition = new Vector2(icon_offset_x, icon_offset_y);
                iconBox.picture.rectTransform.localScale = new Vector3(icon_offset_size, icon_offset_size, icon_offset_size);
                /*
                // RESIZE IMAGE TO FIT MINIMUM
                float resize = 0.0f;
                bool resiz = false;
                var targetSizeX = pictureBox.sizeDelta.x;
                var targetSizeY = pictureBox.sizeDelta.y;

                image.SetNativeSize();
                //Debug.Log("image resize targetX [" + targetSizeX + "] targetY[" + targetSizeY + "] imageX ["+image.rectTransform.sizeDelta.x+"] imageY ["+ image.rectTransform.sizeDelta.y+"]");
                float x = image.rectTransform.sizeDelta.x * image.transform.localScale.x;
                float y = image.rectTransform.sizeDelta.y * image.transform.localScale.y;

                if (x + 0.01f < targetSizeX || y + 0.01f < targetSizeY)
                {
                    resize = Mathf.Max(targetSizeX / (x + 0.01f), targetSizeY / (y + 0.01f));
                    resiz = true;
                }
                else if ((x - 0.01f) > targetSizeX || (y - 0.01f) > targetSizeY)
                {
                    resize = Mathf.Max(targetSizeX / (x - 0.01f), targetSizeY / (y - 0.01f));
                    resiz = true;
                }

                if (resiz)
                {
                    //Debug.Log("UpdateImage: resize localX " + aaBox.localScale.x + "*" + resize + " localY " + aaBox.localScale.y + "*" + resize + " localZ " + aaBox.localScale.z + "*" + resize);
                    image.transform.localScale = new Vector3(resize, resize, resize);
                }*/
                /*
                var resize = (iconBox.picture.rectTransform.sizeDelta.x > 145 || iconBox.picture.rectTransform.sizeDelta.y > 150);
                if (resize)
                {
                    var targScale = Math.Max(145 / iconBox.picture.rectTransform.sizeDelta.x, 145 / iconBox.picture.rectTransform.sizeDelta.y);
                    iconBox.picture.rectTransform.localScale = new Vector3(targScale, targScale, targScale);
                }*/
            }

        }

        [JsonIgnore] public string IconPath
        {
            get
            {
                if (this.Variants != null)
                {
                    foreach (var i in this.Variants) if (i.Validate(Owner.Owner) && i.IconVariantData != null && i.IconVariantData.Length > 0) return i.IconVariantData;
                }
                return icon_path;
            }
        }

        [JsonIgnore] public string PortraitPath { get
            {
                if (this.Variants != null)
                {
                    foreach (var i in this.Variants) if (i.Validate(Owner.Owner) && i.PortraitVariantData != null && i.PortraitVariantData.Length > 0) return i.PortraitVariantData;
                }
                return portrait_path;
            } }

        public override void DrawPortrait(scr_CharPortraitBox portraitBox)
        {
            portraitBox.picture.gameObject.SetActive(true);
            //portraitBox.spineLoader.gameObject.SetActive(false);


            //Debug.Log("image drawportrait " + this.PortraitPath);
            if (this.PortraitPath == "")
            {
                base.DrawPortrait(portraitBox);
            }
            else
            {
                portraitBox.picture.sprite = scr_System_CentralControl.current.LoadCachedSprite(PortraitPath);
                portraitBox.picture.SetNativeSize();

                portraitBox.picture.rectTransform.anchoredPosition = new Vector2(portrait_offset_x , portrait_offset_y);
                portraitBox.picture.rectTransform.localScale = new Vector3(portrait_offset_size, portrait_offset_size, portrait_offset_size);
            }
        }

        public override void DrawPortrait(scr_Menu_CharaDetail movablePortraitBox)
        {
            movablePortraitBox.picture.gameObject.SetActive(true);
            //movablePortraitBox.spineLoader.gameObject.SetActive(false);

            if (this.PortraitPath == "")
            {
                movablePortraitBox.picture.sprite = SpriteAsset.transparent;
            }
            else
            {
                movablePortraitBox.picture.sprite = scr_System_CentralControl.current.LoadCachedSprite(PortraitPath);
                movablePortraitBox.picture.SetNativeSize();

                movablePortraitBox.picture.rectTransform.anchoredPosition = new Vector2(portrait_offset_x, portrait_offset_y );
                movablePortraitBox.picture.rectTransform.localScale = new Vector3(portrait_offset_size, portrait_offset_size, portrait_offset_size);

            }
        }

        public override void SetPortraitOffsets(float offsetX, float offsetY, float offsetSize)
        {
            this.portrait_offset_x = offsetX;
            this.portrait_offset_y = offsetY;
            this.portrait_offset_size = offsetSize;
        }
    }


    [System.Serializable]
    public class CharaPortrait_Spine : CharaPortrait
    {   // do not allow multiple transparent image layering -> should use spine instead.
        public List<string> materialTexturePaths;
        public string atlasJSON_path;
        public string skeletonJSON_path;
        public float skeletonScale;
        public string idleAnimName;

        public string icon_path;
        public CharaPortrait_Spine()
        {

        }
        /// <summary>
        /// 
        /// </summary>
        /// <param name="icon_path">Still use a static image icon display</param>
        /// <param name="materialTexturePath"></param>
        /// <param name="atlasJSON_path"></param>
        /// <param name="skeletonJSON_path"></param>
        /// <param name="skeletonScale"></param>
        /// <param name="idleAnimName"></param>
        public CharaPortrait_Spine(string icon_path, List<string> materialTexturePath, string atlasJSON_path, string skeletonJSON_path, float skeletonScale, string idleAnimName)
        {
            this.materialTexturePaths = materialTexturePath;
            this.atlasJSON_path = atlasJSON_path;
            this.skeletonJSON_path = skeletonJSON_path;
            this.skeletonScale = skeletonScale;
            this.idleAnimName = idleAnimName;


            portrait_offset_x = 0;
            portrait_offset_y = 0;
            portrait_offset_size = 1;

            icon_offset_x = 0;
            icon_offset_y = 0;
            icon_offset_size = 1;
        }

        public override bool isValid() { return !Disable; }
        public override void DrawIcon(scr_CharIconBox iconBox)
        {
            if (IconPath == "") base.DrawIcon(iconBox);
            else
            {
                iconBox.picture.sprite = scr_System_CentralControl.current.LoadCachedSprite(IconPath);
                /*
                var resize = (iconBox.picture.rectTransform.sizeDelta.x > 145 || iconBox.picture.rectTransform.sizeDelta.y > 150);
                if (resize)
                {
                    var targScale = Math.Max(145 / iconBox.picture.rectTransform.sizeDelta.x, 145 / iconBox.picture.rectTransform.sizeDelta.y);
                    iconBox.picture.rectTransform.localScale = new Vector3(targScale, targScale, targScale);
                }*/
                iconBox.picture.SetNativeSize();
                iconBox.picture.rectTransform.anchoredPosition = new Vector2(icon_offset_x, icon_offset_y);
                iconBox.picture.rectTransform.localScale = new Vector3(icon_offset_size, icon_offset_size, icon_offset_size);
            }
        }

        [JsonIgnore]
        public string IconPath
        {
            get
            {
                if (this.Variants != null)
                {
                    foreach (var i in this.Variants) if (i.Validate(Owner.Owner) && i.IconVariantData != null && i.IconVariantData.Length > 0) return i.IconVariantData;
                }
                return icon_path;
            }
        }

        [JsonIgnore]
        public string PortraitAnimName
        {
            get
            {
                if (this.Variants != null)
                {
                    foreach (var i in this.Variants) if (i.Validate(Owner.Owner) && i.PortraitVariantData != null && i.PortraitVariantData.Length > 0) return i.PortraitVariantData;
                }
                return idleAnimName;
            }
        }

        public override void DrawPortrait(scr_CharPortraitBox portraitBox)
        {
            if (this.PortraitAnimName == "")
            {
                //portraitBox.spineLoader.gameObject.SetActive(false);
                portraitBox.picture.gameObject.SetActive(true);
                base.DrawPortrait(portraitBox);

            }
            else
            {
                //if (portraitBox.pictureRect == null) Debug.LogError("pictureRect null");

                //portraitBox.pictureRect.content = portraitBox.spineRect;
                //portraitBox.spineLoader.gameObject.SetActive(true);

                portraitBox.spineLoader.SetBase(materialTexturePaths, atlasJSON_path, skeletonJSON_path, skeletonScale, PortraitAnimName);

                portraitBox.picture.gameObject.SetActive(false);
                if (portraitBox.spineRect == null) return;
                portraitBox.spineLoader.gameObject.SetActive(true);
                portraitBox.spineRect.gameObject.SetActive(true);

                portraitBox.spineRect.SetParent(portraitBox.spineLoader.transform, false);
                portraitBox.spineLoader.spineLoader.MatchWithBound();
                portraitBox.spineRect.anchoredPosition = new Vector2(portrait_offset_x, portrait_offset_y);
                portraitBox.spineRect.localScale = new Vector3(portrait_offset_size, portrait_offset_size, portrait_offset_size);
            }
        }

        public override void DrawPortrait(scr_Menu_CharaDetail movablePortraitBox)
        {
            if (this.PortraitAnimName == "")
            {
                //Debug.LogError("PortraitAnimatio name null disabling");
                movablePortraitBox.picture.gameObject.SetActive(true);
                //movablePortraitBox.spineLoader.gameObject.SetActive(false);

                movablePortraitBox.picture.sprite = SpriteAsset.transparent;
            }
            else
            {
                // Debug.LogError("PortraitAnimatio name setting");

                movablePortraitBox.spineLoader.SetBase(materialTexturePaths, atlasJSON_path, skeletonJSON_path, skeletonScale, PortraitAnimName);
                movablePortraitBox.pictureRect.content = movablePortraitBox.spineRect;
                movablePortraitBox.picture.gameObject.SetActive(false);

                movablePortraitBox.spineRect.SetParent(movablePortraitBox.spineLoader.transform, false);
                movablePortraitBox.spineLoader.spineLoader.MatchWithBound();
                movablePortraitBox.spineRect.anchoredPosition = new Vector2(portrait_offset_x , portrait_offset_y );
                movablePortraitBox.spineRect.localScale = new Vector3(portrait_offset_size, portrait_offset_size, portrait_offset_size);

            }

        }

        public override void SetPortraitOffsets(float offsetX, float offsetY, float offsetSize)
        {
            //Debug.Log("SetPortraitOffsets [" + offsetX + "] ["+offsetY+"] ["+offsetSize+"]");
            this.portrait_offset_x = offsetX;
            this.portrait_offset_y = offsetY;
            this.portrait_offset_size = offsetSize;
        }
    }
}

