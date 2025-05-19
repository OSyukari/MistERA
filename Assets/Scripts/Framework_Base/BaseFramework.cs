using UnityEngine;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System;

public interface I_IndexMergeable
{
    public void MergeWith(I_IndexMergeable list);
}
public interface I_IndexHasID
{
    public void RegisterAllID();
}

public interface I_NeedLateInitialize
{
    public void LateInitialize();
}

public interface I_SerializationCallbackReceiver
{
    public void OnAfterDeserialize();
}

public interface SpineAssetHandler
{
    public void LoadSpineJSON(string materialTexturePath, string atlasJSON_path, string skeletonJSON_path, out Texture2D spineLoader_Texture, out TextAsset spineLoader_atlasJSON, out TextAsset spineLoader_skeletonJSON);
}

public class JSON_SO_Converter<T> : CustomCreationConverter<T> where T : ScriptableObject
{   // Reference: https://discussions.unity.com/t/how-to-use-json-net-to-deserialize-into-a-scriptable-object/778840/20
    // this converter is used in global json deserialize setting, so no need to call this individually.
    // all subsequent converters should be used in the same way as this one
    public override T Create(Type objectType)
    {
        if (typeof(T).IsAssignableFrom(objectType)) return (T)ScriptableObject.CreateInstance(objectType);
        return null;
    }
}