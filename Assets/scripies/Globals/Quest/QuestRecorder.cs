using UnityEngine;
public class QuestRecorder : MonoBehaviour
{
    public Quest.QuestType questType;
    public EnemyHealth enemyHealth;
    private Quest derivedQuest;
    public bool taskCompleted;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {

    }

    // Update is called once per frame
    void Update()
    {
        switch (questType)
        {
            case Quest.QuestType.DefeatEnemy:
                if (enemyHealth.isDead)
                {
                    //derivedQuest.eTd++
                }
                break;
            case Quest.QuestType.HaveInInventory:
                
                break;
            case Quest.QuestType.GiveTo:
                
                break;
            case Quest.QuestType.TakeFrom:
                
                break;
            
        }
    }
}
