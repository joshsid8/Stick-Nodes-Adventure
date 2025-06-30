using UnityEngine;

[CreateAssetMenu(fileName = "StatSO", menuName = "Scriptable Objects/StatSO")]
public class StatSO : ScriptableObject
{
    [SerializeField] private string statName;
    [SerializeField] [TextArea] private string statDescription;

    [SerializeField] private string statVariable;
    
    public string StatName => statName;
    public string StatDescription => statDescription;
}
