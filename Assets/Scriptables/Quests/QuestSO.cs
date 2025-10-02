using UnityEngine;

[CreateAssetMenu(fileName = "New Quest", menuName = "UI/QuestSO")]
public class QuestSO : ScriptableObject
{
    [SerializeField] private string questName;
    [SerializeField] private string questDescription;
    [SerializeField] private Quest.QuestType questType;
}
