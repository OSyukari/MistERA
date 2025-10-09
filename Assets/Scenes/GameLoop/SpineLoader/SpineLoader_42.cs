using UnityEngine;
using System.IO;
using Spine.Unity;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using System;
using Spine;
using System.Collections;


/// <summary>
/// Most code are from NikkeViewerEX
/// </summary>

public class SpineLoader_42 : SpineLoader
{
    public override string Version { get { return "4.2"; } }

    SpineAtlasAsset atlasAsset;
    public SkeletonAnimation Animation;
    AtlasAttachmentLoader attachmentLoader;
    SkeletonBinary skeletonBinary;
    SkeletonData skeletonData;
    AnimationStateData animationStateData;
    SkeletonDataAsset skeletonDataAsset;

    protected override void Awake()
    {
        // skeletonData = new SkeletonData();
       // SGmaterial = new Material(Shader);

    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        Destroy(skeletonDataAsset);
        Destroy(Animation);
        Destroy(atlasAsset);
        for (int i = imageTextures.Length - 1; i >= 0; i--) 
        {
            Destroy(imageTextures[i]);
            imageTextures[i] = null;
        }
        Destroy(spineLoader_Material);
        //Destroy(SGmaterial);

    }

    public override IEnumerator Initialize(List<string> texturePath, string atlasPath, string skeletonPath, bool straightAlpha, string idleAnimName = "", string touchAnimName = "action")
    {
        bool refresh = false;
        if (this.atlasPath != atlasPath || this.skeletonPath != skeletonPath || this.texturePath != texturePath)
        {
            //if (straightAlpha) Debug.LogError("Initi setting straightAlpha");
            //SGmaterial.SetInteger("_StraightAlphaInput", straightAlpha ? 1 : 0);


            refresh = true;
            this.atlasPath = atlasPath;
            this.skeletonPath = skeletonPath;
            this.texturePath = texturePath;
            //for (int i = spineLoader_Texture.Length - 1; i >= 0; i--) Destroy(spineLoader_Texture[i]);
            // Load .skel binary
            byte[] skelBytes = File.ReadAllBytes(scr_System_Serializer.current.GetFullPath(skeletonPath));

            // Load .atlas text
            string atlasText = File.ReadAllText(scr_System_Serializer.current.GetFullPath(atlasPath));

            imageTextures = new Texture2D[texturePath.Count];
            for (int i = 0; i < texturePath.Count; i++)
            {
                Texture2D imageTexture = new(2, 2, TextureFormat.RGBA32, false);
                yield return AssetsLoader.LoadSkelCoroutine(texturePath[i], texture => imageTexture.LoadImage(texture));
                imageTexture.name = Path.GetFileNameWithoutExtension(texturePath[i]);
                imageTextures[i] = imageTexture;
            }

            // Parse skeleton
            atlasAsset = SpineAtlasAsset.CreateRuntimeInstance(
                        new TextAsset(atlasText),
                        imageTextures,
                        straightAlpha ? SGmaterial_Alpha: SGmaterial,
                        true
                    );

            //foreach(var m in atlasAsset.materials) m.SetInt("_StraightAlphaInput", 1);
            // Parse .skel into SkeletonData
            attachmentLoader = new(atlasAsset.GetAtlas());
            skeletonBinary = new SkeletonBinary(attachmentLoader);
            skeletonData = skeletonBinary.ReadSkeletonData(new MemoryStream(skelBytes));
            skeletonBinary.Scale *= spineScale;

            animationStateData = new(skeletonData);
            skeletonDataAsset = CreateSkeletonDataAsset(
                skeletonData,
                animationStateData
            );
            //skeletonDataAsset.scale = skeletonScale;
            //Animation = SkeletonAnimation.AddToGameObject(this.gameObject, skeletonDataAsset);

            Animation.skeletonDataAsset = skeletonDataAsset;
            Animation.Initialize(false);

            // Set default skin that has any mesh data to prevent Degenerate Triangle error.
            foreach (Skin skin in skeletonData.Skins)
            {

                if (CheckSkinMesh(skin)) Animation.Skeleton.SetSkin(skin.Name);
            }
            // Optional: play animation
            Animation.Skeleton.SetToSetupPose();

            //meshRenderer.sharedMaterial.SetInt("_StraightAlphaInput", straightAlpha ? 1 : 0);
            //meshRenderer.material.SetInt("_StraightAlphaInput", straightAlpha ? 1 : 0);
        }

        if (this.idleAnimName != idleAnimName)
        {
            refresh = true;
            var idleAnim = Animation.skeletonDataAsset.GetSkeletonData(true).FindAnimation(idleAnimName);
            if (idleAnim == null)
            {
                var list = Animation.skeletonDataAsset.GetSkeletonData(true).Animations.ToList();
                idleAnim = list.Count > 0 ? list[0] : null;
                var names = new List<string>();
                foreach (var i in list) names.Add(i.Name);
                Debug.Log($"Spine animation name mismatch\nAtlasPath {atlasPath}\nValid Anims: {String.Join("|", names)}");
            }
            // send looping idle animation
            //self_SkeletonGraphic.AnimationState.AddAnimation(0, idleAnim, true, 0);
            if (idleAnim != null) Animation.AnimationState.SetAnimation(0, idleAnim, true);
        }

        if (refresh)
        {
            Animation.Update(0);
            Animation.LateUpdate();
        }
    }

    public static SkeletonDataAsset CreateSkeletonDataAsset( SkeletonData skeletonData, AnimationStateData stateData )
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
}