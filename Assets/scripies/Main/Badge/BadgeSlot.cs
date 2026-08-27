using UnityEngine;
using UnityEngine.UI;

public class BadgeSlot : StatInfo
{
    public Badge badge;

    protected override void Update()
    {
        shader.SetActive(isClicked);
    }
    
    public void SetBadge(Badge ba) => badge = ba;
    
    public override void ClearInfo()
    {
        base.ClearInfo();
        badge = null;
        gameObject.SetActive(false);
    }
    
    protected override void OnLeftClick()
    {
        statsManager.ClearStatShaders();
        isClicked = true;
        statName.text = badge.itemName;
        statDescription.text = badge.itemDescription;
        GetComponent<Image>().sprite = badge.itemSprite;
    }

}
