using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

public partial class LLM_WorldState
{
    public class QuestStorage
    {
        public string questName;
        public List<string> stages = new List<string>();
        public QuestStorage()
        {

        }
        public QuestStorage(QuestEvaluationResult quest)
        {
            questName = QuestUtility.ParseQuestEntry(quest.quest.questID, quest.AppendStrings);
            PopulateStages(quest.stages);
        }

        void PopulateStages(List<QuestStageResult> stagess, string header = "")
        {
            // full count - the old `Count - 1` bound silently dropped the last stage (and each
            // level's last substage) compared to the missions UI's foreach over every stage
            for (int i = 0; i < stagess.Count; i++)
            {
                var stg = stagess[i];
                var prefix = header == "" ? $"{i + 1}." : $"{header}{i + 1}.";
                if (stg.isValid) this.stages.Add($"{prefix} {QuestUtility.ParseQuestEntry(stg.ID, stg.AppendStrings)}");
                PopulateStages(stg.subStages, prefix);
            }
        }
    }
}
