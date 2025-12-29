using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Spine_v40;
using Spine_v40.Unity;
using UnityEngine;

public class  SpineDataTiny_40 : SpineDataTiny
{
    public SpineAtlasAsset atlasAsset = null;
    public SkeletonBinary skeletonBinary = null;
    public AtlasAttachmentLoader attachmentLoader = null;
    public SkeletonData skeletonData = null;
    public AnimationStateData animationStateData = null;
    public SkeletonDataAsset skeletonDataAsset = null;
    public override string Version { get { return "4.0"; } }
    public override void Clear()
    {
        base.Clear();
        this.initialized = false;
        if (atlasAsset != null)
        {
            MonoBehaviour.Destroy(atlasAsset.atlasFile);
            MonoBehaviour.Destroy(atlasAsset);
            atlasAsset = null;
        }
        if (skeletonDataAsset != null)
        {
            skeletonDataAsset.Clear();
            MonoBehaviour.Destroy(skeletonDataAsset.skeletonJSON);
            MonoBehaviour.Destroy(skeletonDataAsset);
            skeletonDataAsset = null;
        }
    }
}

public class SpineLoaderTiny_40 : SpineLoaderTiny
{
    public SkeletonAnimation Animation = null;
    public override string Version { get { return "4.0"; } }
    public override void Clear()
    {
        if (Animation != null)
        {
            Animation.AnimationState.ClearTracks();
            Animation.ClearState();
            Animation.StopAllCoroutines();

            Animation.gameObject.SetActive(false); 

            if (Animation.TryGetComponent<MeshFilter>(out var filter) && filter.sharedMesh != null)
            {
                //Debug.Log("clear shared mesh");
                MonoBehaviour.DestroyImmediate(filter.sharedMesh);
            }
            Animation.skeletonDataAsset = null;
            MonoBehaviour.Destroy(Animation.gameObject);
            MonoBehaviour.Destroy(Animation);
            Animation = null;
        }
    }
    public override GameObject GameObject
    {
        get
        {
            if (Animation == null) return null;
            return Animation.gameObject;
        }
    }
}

/// <summary>
/// A single class of animator
/// </summary>
public class SpineAnimator40 : SpineAnimatorBase
{
    public override IEnumerator Initialize(PortraitManager.CharaPortrait_Spine manager, SpineLoaderTiny loader, List<string> texturePath, string atlasPath, string skeletonPath, bool straightAlpha, string idleAnimName = "idle", string addonAnimName = "action")
    {
        if (loader is SpineLoaderTiny_40) yield return Initialize(manager, loader as SpineLoaderTiny_40, texturePath, atlasPath, skeletonPath, straightAlpha, idleAnimName, addonAnimName);
        else
        {
            Debug.LogError("SpineAnimator40 Initialize called on wrong loader!");
            yield break;
        }
    }

    public IEnumerator Initialize(PortraitManager.CharaPortrait_Spine manager, SpineLoaderTiny_40 loader, List<string> texturePath, string atlasPath, string skeletonPath, bool straightAlpha, string idleAnimName = "idle", string addonAnimName = "action")
    {
        bool refresh = false;
        SpineDataTiny_40 dataloader = null;

        if (manager.dataHolder == null)
        {
            dataloader = new SpineDataTiny_40();
            manager.dataHolder = dataloader;
        }
        else
        {
            dataloader = manager.dataHolder as SpineDataTiny_40;
        }

        if (loader.dataPointer != dataloader)
        {
            loader.dataPointer = dataloader;
            refresh = true;
            loader.Clear();
        }

        if (!dataloader.initialized || dataloader.atlasPath != atlasPath || dataloader.skeletonPath != skeletonPath || dataloader.texturePath != texturePath)
        {
            // SGmaterial.SetInt("_StraightAlphaInput", straightAlpha ? 1 : 0);
            refresh = true;
            dataloader.Clear();
            loader.Clear();
            dataloader.atlasPath = atlasPath;
            dataloader.skeletonPath = skeletonPath;
            dataloader.texturePath = texturePath;

            yield return AssetsLoader.LoadTextCoroutine(skeletonPath, text => dataloader.skeletonTA = text);

            // Load .atlas text
            string atlasText = File.ReadAllText(scr_System_Serializer.current.GetFullPath(atlasPath));
            byte[] skelBytes = File.ReadAllBytes(scr_System_Serializer.current.GetFullPath(skeletonPath));

            if (dataloader.imageTextures != null)
            {
                Debug.LogError("ERROR dataloader.imageTextures NOT NULL");
            }
            dataloader.imageTextures = new Texture2D[texturePath.Count];
            for (int i = 0; i < texturePath.Count; i++)
            {
                Texture2D imageTexture = new(2, 2, TextureFormat.RGBA32, false);
                byte[] texs = null;
                yield return AssetsLoader.LoadSkelCoroutine(texturePath[i], texture => texs = texture);
                imageTexture.LoadImage(texs);
                imageTexture.name = Path.GetFileNameWithoutExtension(texturePath[i]);
                dataloader.imageTextures[i] = imageTexture;
            }

            // Parse skeleton
            dataloader.atlasAsset = SpineAtlasAsset.CreateRuntimeInstance(
                        new TextAsset(atlasText),
                        dataloader.imageTextures,
                        straightAlpha ? this.SGmaterial_Alpha : this.SGmaterial,
                        true
                    );

            //foreach(var m in atlasAsset.materials) m.SetInt("_StraightAlphaInput", 1);
            // Parse .skel into SkeletonData
            dataloader.attachmentLoader = new(dataloader.atlasAsset.GetAtlas());
            dataloader.skeletonBinary = new SkeletonBinary(dataloader.attachmentLoader);

            dataloader.skeletonData = dataloader.skeletonBinary.ReadSkeletonData(new MemoryStream(skelBytes));
            dataloader.skeletonBinary.Scale *= dataloader.spineScale;

            dataloader.animationStateData = new(dataloader.skeletonData);
            dataloader.skeletonDataAsset = CreateSkeletonDataAsset(
                dataloader.skeletonData,
                dataloader.animationStateData
            );
            dataloader.initialized = true;
        }


        if (refresh)
        {
            //skeletonDataAsset.scale = skeletonScale;
            //Animation = SkeletonAnimation.AddToGameObject(this.gameObject, skeletonDataAsset);

            loader.Clear();
            loader.Animation = SkeletonAnimation.NewSkeletonAnimationGameObject(dataloader.skeletonDataAsset);
            loader.Animation.Initialize(false);

            // Set default skin that has any mesh data to prevent Degenerate Triangle error.
            foreach (Skin skin in dataloader.skeletonData.Skins)
            {

                if (CheckSkinMesh(skin)) loader.Animation.Skeleton.SetSkin(skin.Name);
            }
            // Optional: play animation
            loader.Animation.Skeleton.SetToSetupPose();
        }

        while (loader.Animation == null) yield return 0;

        if (loader.idleAnimName != idleAnimName)
        {
            refresh = true;
            var idleAnim = loader.Animation.skeletonDataAsset.GetSkeletonData(true).FindAnimation(idleAnimName);
            if (idleAnim == null)
            {
                var list = loader.Animation.skeletonDataAsset.GetSkeletonData(true).Animations.ToArray();
                idleAnim = list.Length > 0 ? list[0] : null;
                var names = new List<string>();
                foreach (var i in list) names.Add(i.Name);
                Debug.Log($"Spine animation name mismatch\nAtlasPath {atlasPath}\nValid Anims: {String.Join("|", names)}");
            }
            // send looping idle animation
            //self_SkeletonGraphic.AnimationState.AddAnimation(0, idleAnim, true, 0);
            if (idleAnim != null) loader.Animation.AnimationState.SetAnimation(0, idleAnim, true);
        }
        if (loader.addonAnimName != addonAnimName)
        {
            refresh = true;
            var addonAnim = loader.Animation.skeletonDataAsset.GetSkeletonData(true).FindAnimation(addonAnimName);
            if (addonAnim == null)
            {
                var list = loader.Animation.skeletonDataAsset.GetSkeletonData(true).Animations.ToArray();
                addonAnim = list.Length > 0 ? list[0] : null;
                var names = new List<string>();
                foreach (var i in list) names.Add(i.Name);
                Debug.Log($"Spine animation name mismatch\nAtlasPath {atlasPath}\nValid Anims: {String.Join("|", names)}");
            }
            // send looping idle animation
            //self_SkeletonGraphic.AnimationState.AddAnimation(0, idleAnim, true, 0);
            if (addonAnim != null) loader.Animation.AnimationState.SetAnimation(1, addonAnim, true);
        }

        if (refresh)
        {
            loader.Animation.Update(0);
            loader.Animation.LateUpdate();
        }
    }


    public static bool CheckSkinMesh(Skin skin)
    {
        foreach (Skin.SkinEntry entry in skin.Attachments)
        {
            if (entry.Attachment is MeshAttachment meshAttachment)
            {
                if (meshAttachment.Vertices.Length > 0 || meshAttachment.Triangles.Length > 0)
                    return true;
            }
        }
        return false;
    }
    public static SkeletonDataAsset CreateSkeletonDataAsset(SkeletonData skeletonData, AnimationStateData stateData)
    {

        // Create a new instance of SkeletonDataAsset
        SkeletonDataAsset skeletonDataAsset =
            ScriptableObject.CreateInstance<SkeletonDataAsset>();

        // Get the type of SkeletonDataAsset
        Type skeletonDataAssetType = skeletonDataAsset.GetType();

        // Get the skeletonData and stateData fields
        FieldInfo skeletonDataField = skeletonDataAssetType.GetField(
            "skeletonData",
            BindingFlags.NonPublic | BindingFlags.Instance
        );
        FieldInfo stateDataField = skeletonDataAssetType.GetField(
            "stateData",
            BindingFlags.NonPublic | BindingFlags.Instance
        );

        // Set the values of skeletonData and stateData
        skeletonDataField.SetValue(skeletonDataAsset, skeletonData);
        stateDataField.SetValue(skeletonDataAsset, stateData);

        // Set a dummy value to skeletonJSON variable to make sure there's no
        // error returned if we call some method in SkeletonDataAsset
        skeletonDataAsset.skeletonJSON = new TextAsset("NIKKE");
        // Return the SkeletonDataAsset
        return skeletonDataAsset;

    }
}
