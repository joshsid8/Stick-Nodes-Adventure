using UnityEngine;
public class GUIManager : MonoBehaviour
{
    [SerializeField] private GameObject[] gui;
    
    private void Update()
    {
        if (gui != null && Input.GetButtonDown("Cancel"))
        {
            for (int i = 0; i < gui.Length; i++)
            {
                if (gui[i].activeInHierarchy) gui[i].SetActive(false);
                else i++;
            }
        }
    }
}
