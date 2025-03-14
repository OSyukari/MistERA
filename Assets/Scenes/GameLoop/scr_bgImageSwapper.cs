using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class scr_bgImageSwapper : MonoBehaviour
{
    // Start is called before the first frame update
    private void Start()
    {
        scr_System_CampaignManager.current.Observer_CurrentRoom += OnRoomChange;
        image.color = disabledColor;

        OnRoomChange(0, scr_System_CampaignManager.current.CurrentRoom);
    }

    private void OnRoomChange(int updateSequence, Room_Instance room)
    {
        if (updateSequence != 0) return;
        if (room.Base.roomImagePath != "")
        {
            image.color = activeColor;
            spriteTex = scr_System_CentralControl.current.LoadCachedTexture(room.Base.roomImagePath);
            sp = Sprite.Create(spriteTex, new Rect(0, 0, spriteTex.width, spriteTex.height), new Vector2(0, 0), 100.0f);
            image.sprite = sp;
        }
        else
        {
            image.color = disabledColor;
        }
    }

    public Color32 disabledColor = new Color32(255, 255, 255, 0);
    public Color32 activeColor = new Color32(255, 255, 255, 255);
    public Image image, cover;
    Texture2D spriteTex = null;
    Sprite sp;
}
