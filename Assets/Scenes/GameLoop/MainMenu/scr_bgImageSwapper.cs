using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Newtonsoft.Json;
using System.Linq;
using System;
using UnityEngine.EventSystems;

public class scr_bgImageSwapper : MonoBehaviour
{
    // Start is called before the first frame update
    public bool monitorCurrentRoom = false;
    private void Start()
    {
        image.color = disabledColor;
        if (monitorCurrentRoom)
        {
            scr_System_CampaignManager.current.Observer_CurrentRoom += OnRoomChange;
            OnRoomChange(0, scr_System_CampaignManager.current.CurrentRoom);
        }
    }

    public Coroutine co = null;

    private void OnRoomChange(int updateSequence, Room_Instance room)
    {
        if (updateSequence != 0) return;
        if (!this.gameObject.activeInHierarchy) return;
        if (room.Base.roomImagePath != "")
        {
            image.color = activeColor;
            if (co != null)
            {
                StopCoroutine(co);
                co = null;
            }
            co = StartCoroutine(roomchange(room.Base.roomImagePath));
        }
        else
        {
            image.color = disabledColor;
        }
    }

    public IEnumerator roomchange(string a)
    {
        if (scr_System_CentralControl.current.GetSprite(a, out var sprite))
        {
            image.sprite = sprite;
        }
        else
        {
            Texture2D loaded = null;
            yield return AssetsLoader.LoadTextureCoroutine(a, texture => loaded = texture);
            image.sprite = scr_System_CentralControl.current.MakeSprite(a, loaded);
        }
    }


    public Color32 disabledColor = new Color32(255, 255, 255, 0);
    public Color32 activeColor = new Color32(255, 255, 255, 255);
    public Image image, cover;

}
