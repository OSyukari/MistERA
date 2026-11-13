// Test-only Unity/game boundaries. These do not model an actual Unity player or its IO scheduler.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using MistEra.Updates;

namespace UnityEngine
{
    public class MonoBehaviour
    {
        public object gameObject;
        protected static void Destroy(object o) { }
        protected static void DontDestroyOnLoad(object o) { }
        protected void StartCoroutine(IEnumerator iterator)
        {
            var stack = new Stack<IEnumerator>(); stack.Push(iterator);
            while (stack.Count > 0)
            {
                if (!stack.Peek().MoveNext()) { (stack.Pop() as IDisposable)?.Dispose(); continue; }
                if (stack.Peek().Current is IEnumerator child) stack.Push(child);
                else Thread.Sleep(1);
            }
        }
    }
    public class GameObject { public GameObject(string n) { } public T AddComponent<T>() where T : new() => new T(); }
    public enum RuntimePlatform { WindowsPlayer }
    public static class Application
    {
        public static bool isEditor = false;
        public static RuntimePlatform platform = RuntimePlatform.WindowsPlayer;
        public static string dataPath = "unused", temporaryCachePath = "unused";
        public static void Quit() { }
    }
    public static class Debug { public static void LogException(Exception e) => throw e; }
    public static class Time { public static float realtimeSinceStartup => (float)Environment.TickCount64 / 1000; }
    public static class Mathf { public static float Clamp01(float f) => Math.Clamp(f, 0, 1); }
}
namespace UnityEngine.SceneManagement
{
    public struct Scene { public bool isLoaded; }
    public static class SceneManager
    {
        public static bool Gameplay;
        public static Scene GetSceneByPath(string path) => new Scene { isLoaded = path == (Gameplay ? GlobalValues.GameScene : GlobalValues.IntroScene) };
    }
}
namespace UnityEngine.Networking
{
    public class DownloadHandlerFile { public DownloadHandlerFile(string p) { } public bool removeFileOnAbort; }
    public class WebOperation { public bool isDone = true; }
    public class UnityWebRequest : IDisposable
    {
        public enum Result { Success }
        public UnityWebRequest(string u, string m) { }
        public int redirectLimit, timeout; public long responseCode; public ulong downloadedBytes;
        public string error; public Result result; public DownloadHandlerFile downloadHandler;
        public void SetRequestHeader(string a, string b) { }
        public WebOperation SendWebRequest() => throw new NotSupportedException("Runtime regression tests must not access the network.");
        public void Abort() { } public string GetResponseHeader(string n) => null; public void Dispose() { }
    }
}
public class ModAbout { public string ModID; public List<string> dependency; }
public class ModSettingEntry { public string ModID; public bool enabled; }
public class ModSettings { public List<ModSettingEntry> ModList = new List<ModSettingEntry>(); public Dictionary<string, bool> AssetPacks = new Dictionary<string, bool>(); }
public class scr_System_CentralControl
{
    public static scr_System_CentralControl current;
    public UpdateSettings UpdateSetting = new UpdateSettings(); public ModSettings ModSetting = new ModSettings(); public void SaveUserPref() { }
}
public class Loaded { public string id, folderPath; public bool enabled; }
public class scr_System_Serializer { public static scr_System_Serializer current; public List<Loaded> Mods = new List<Loaded>(), AssetPacks = new List<Loaded>(); }
public class scr_System_SceneManager { public static scr_System_SceneManager current = new scr_System_SceneManager(); public bool SceneLoading; }
public static class GlobalValues { public const string IntroScene = "Intro", GameScene = "Game"; }
public static class AssetsLoader
{
    public static bool Closed, DrainReady = true; public static int Ends, Begins;
    public static bool BeginUpdateQuiescence() { Closed = true; Begins++; return DrainReady; }
    public static void EndUpdateQuiescence() { Closed = false; Ends++; }
}
