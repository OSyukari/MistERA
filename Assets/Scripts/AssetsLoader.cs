using UnityEngine;
using System.Collections;
using UnityEngine.Networking;
using System.IO;
using WebP;
public class AssetsLoader
{

    public static IEnumerator LoadTextureCoroutine(string path, System.Action<Texture2D> onComplete)
    {
        // 1. Try loading from Resources asynchronously
        ResourceRequest resourceRequest = Resources.LoadAsync<Texture2D>(path);
        yield return resourceRequest;

        if (resourceRequest.asset is Texture2D resourceTex)
        {
            onComplete?.Invoke(resourceTex);
            yield break;
        }else MonoBehaviour.Destroy(resourceRequest.asset);

        var fullPath = $"file://{scr_System_Serializer.current.GetFullPath(path)}";
        string extension = Path.GetExtension(path).ToLower();

        using (UnityWebRequest uwr = UnityWebRequest.Get(fullPath))
        {
            yield return uwr.SendWebRequest();

            if (uwr.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError("Error loading texture: " + uwr.error);
                onComplete?.Invoke(null); 
                yield break;
            }

            byte[] bytes = uwr.downloadHandler.data;
            Texture2D tex = null;

            if (extension == ".png" || extension == ".jpg" || extension == ".jpeg")
            {
                // Native Unity support
                tex = new Texture2D(2, 2);
                tex.LoadImage(bytes);
            }
            else if (extension == ".webp")
            {
                // Use NativeWebP if available
                var newTex = Texture2DExt.CreateTexture2DFromWebP(bytes, true, false, out var Error);
                if (Error == Error.Success)
                {
                    tex = newTex;
                }
                else
                {
                    onComplete?.Invoke(null);
                    yield break;
                }
            }
            else
            {
                Debug.LogError($"AssetsLoader LoadTextureCoroutine Error, unhandled format [{extension}]");
                onComplete?.Invoke(null);
                yield break;
            }

            onComplete?.Invoke(tex);
        }
    }

    public static string FileName(string path)
    {
        FileInfo f = new FileInfo(scr_System_Serializer.current.GetFullPath(path));
        return f.Name;
    }

    public static IEnumerator LoadTextCoroutine(string path, System.Action<TextAsset> onComplete)
    {
        // 1. Try loading from Resources asynchronously
        ResourceRequest resourceRequest = Resources.LoadAsync<TextAsset>(path);
        yield return resourceRequest;

        if (resourceRequest.asset is TextAsset resourceTex)
        {
            onComplete?.Invoke(resourceTex);
            yield break;
        }
        else MonoBehaviour.Destroy(resourceRequest.asset);

        var fullPath = $"file://{scr_System_Serializer.current.GetFullPath(path)}";

        using (UnityWebRequest uwr = UnityWebRequest.Get(fullPath))
        {
            yield return uwr.SendWebRequest();

            if (uwr.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError("Error loading text: " + uwr.error);
                onComplete?.Invoke(null);
            }
            else
            {
                TextAsset text = new TextAsset( uwr.downloadHandler.text);
                onComplete?.Invoke(text);
            }
        }
    }

    public static IEnumerator LoadSkelCoroutine(string path, System.Action<byte[]> onComplete)
    {
        // 1. Try loading from Resources asynchronously
        var fullPath = $"file://{scr_System_Serializer.current.GetFullPath(path)}";

        using (UnityWebRequest uwr = UnityWebRequest.Get(fullPath))
        {
            uwr.downloadHandler = new DownloadHandlerBuffer();
            yield return uwr.SendWebRequest();

            if (uwr.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError("Error loading text: " + uwr.error);
                onComplete?.Invoke(null);
            }
            else
            {
                byte[] bytes = uwr.downloadHandler.data;
                onComplete?.Invoke(bytes);
            }
        }
    }
}
