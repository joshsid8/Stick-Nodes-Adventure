using UnityEngine;

[CreateAssetMenu(fileName = "StatSO", menuName = "UI/StatSO")]
public class StatSO : ScriptableObject
{
    //[SerializeField] private string statName;
    [SerializeField] private string displayName;
    [SerializeField] [TextArea] private string statDescription;
    
    //public string StatName => statName;
    public string DisplayName => displayName;
    public string StatDescription => statDescription;
}
