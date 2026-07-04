using UnityEngine;

public class GUIManager : MonoBehaviour
{
    [SerializeField] private GUIBase[] gui;
    
    private void Update()
    {
        if (gui != null && Input.GetButtonDown("Cancel"))
        {
            for (int i = 0; i < gui.Length; i++)
            {
                if (gui[i] != null && gui[i].gameObject.activeInHierarchy)
                {
                    gui[i].gameObject.SetActive(false);
                }
            }
        }
    }
}
