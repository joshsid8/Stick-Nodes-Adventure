using UnityEngine;
using UnityEngine.UI;

public class BadgeSlot : StatInfo
{
    public BadgeSO badgeSo;

    protected override void Update()
    {
        shader.SetActive(isClicked);
    }
    
    public void SetBadge(BadgeSO ba) => badgeSo = ba;
    
    public override void ClearInfo()
    {
        base.ClearInfo();
        badgeSo = null;
        gameObject.SetActive(false);
    }
    
    protected override void OnLeftClick()
    {
        statsManager.ClearStatShaders();
        isClicked = true;
        statName.text = badgeSo.itemName;
        statDescription.text = badgeSo.itemDescription;
        GetComponent<Image>().sprite = badgeSo.itemSprite;
    }

}
