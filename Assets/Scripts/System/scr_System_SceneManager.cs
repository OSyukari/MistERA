using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using System;
public class scr_System_SceneManager : MonoBehaviour
{
    // Singleton
    public static scr_System_SceneManager current;

    public RectTransform menu_Intro;

    private void Awake()
    {
        if (current == null)
        {
            current = this;
        }
        else
        {
            Destroy(gameObject);
        }
        DontDestroyOnLoad(gameObject);

        scene_list = new LinkedList<string>();
        canvasList = new List<RectTransform>();
        targetCanvas = null;
    }

    public void Initialize()
    {
        LoadScene(GlobalValues.IntroScene);
    }

    LinkedList<string> scene_list;
    string scene_last;

    public void LoadScene(string path)
    {
        // clean up all panels
        UnloadAllCanvas();

        StartCoroutine(LoadYourAsyncScene(path));
        scene_list.AddLast(path);
        scene_last = path;
    }

    List<RectTransform> canvasList;
    RectTransform targetCanvas;

    public RectTransform LoadCanvasIntoScene(RectTransform prefab, RectTransform parent = null)
    {
        targetCanvas = Instantiate(prefab) as RectTransform;

        targetCanvas.anchorMax = new Vector2(0.5f, 0.5f);
        targetCanvas.anchorMin = new Vector2(0.5f, 0.5f);
        targetCanvas.anchoredPosition = new Vector2(0f, 0f);

        //targetCanvas.sca

        Transform target = parent;
        if (target != null)
        {
            if (target.parent != null && target.parent != target) target = target.parent;
            targetCanvas.SetParent(target.transform, false);
            //targetCanvas.pixel
            targetCanvas.sizeDelta = new Vector2(parent.sizeDelta.x, parent.sizeDelta.y);
        }
        canvasList.Add(targetCanvas);


        //Debug.Log("SceneManager canvasList[" + canvasList.ToString() + "] length " + canvasList.Count);
        return targetCanvas;
    }

    private void UnloadAllCanvas()
    {
        while (canvasList.Count > 0)
        {
            targetCanvas = canvasList[canvasList.Count - 1];
            canvasList.RemoveAt(canvasList.Count - 1);
            //targetCanvas = canvasList.Pop() as RectTransform;
            if (targetCanvas != null)
            {
                Destroy(targetCanvas.gameObject);
                if (canvasList.Count > 0 ) canvasList[canvasList.Count - 1].GetComponent<scr_Menu>().ValidateAll();
            }
        }
    }


    /// <summary>
    /// argument 1: specify unloading canvas with specified comp. if last canvas in list does not have this comp, function will end.
    /// </summary>
    /// <param name="comp"></param>
    public void UnloadLastCanvasFromScene(System.Type comp = null)
    {
        //if (comp != null) Debug.Log("UnloadLastCanvasFromScene COMP TYPE IS " + comp);
        if (comp != null)
        {
            List<RectTransform> targets = canvasList.FindAll(x => x.GetComponent(comp) != null);
            if (targets.Count > 0)
            {
                canvasList.Remove(targets[targets.Count - 1]);
                Destroy(targets[targets.Count - 1].gameObject);
                if (canvasList.Count > 0) canvasList[canvasList.Count - 1].GetComponent<scr_Menu>().ValidateAll();
            }
        }
        else
        {
            //&& canvasList.Peek().GetComponent(comp) == null
            if (canvasList.Count > 0)
            {
                RectTransform target = canvasList[canvasList.Count - 1];
                canvasList.Remove(target);
                Destroy(target.gameObject);
                if (canvasList.Count > 0) canvasList[canvasList.Count - 1].GetComponent<scr_Menu>().ValidateAll();
            }
            else
            {
                Destroy(this.gameObject);
            }
        }

    }

    private IEnumerator LoadYourAsyncScene(string path)
    {
        // The Application loads the Scene in the background as the current Scene runs.
        // This is particularly good for creating loading screens.
        // You could also load the Scene by using sceneBuildIndex. In this case Scene2 has
        // a sceneBuildIndex of 1 as shown in Build Settings.

        AsyncOperation asyncLoad = SceneManager.LoadSceneAsync(path, LoadSceneMode.Additive);

        // Wait until the asynchronous scene fully loads
        while (!asyncLoad.isDone)
        {
            yield return null;
        }
    }

    public void UnloadScene(string path)
    {
        if (path != null) UnloadScene(SceneManager.GetSceneByPath(path));
    }

    public void UnloadScene(Scene scene)
    {
        if (SceneManager.sceneCount > 1 && scene_list.Contains(scene.path))
        {
            scene_list.Remove(scene.path);
            SceneManager.UnloadSceneAsync(scene.buildIndex);
        }
    }

}

