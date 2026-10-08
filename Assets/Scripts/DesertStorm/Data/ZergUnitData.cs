using System;

namespace DesertStorm
{
    /// <summary>
    /// 虫族单位数据：在通用单位数据基础上扩展潜地、生命自然恢复与虫苔移速加成等种族特性。
    /// </summary>
    [Serializable]
    public sealed class ZergUnitData : UnitData
    {
        // 虫族特色：生命自然恢复、潜地能力和虫苔移速加成。
        //是否可以潜地
        public bool CanBurrow;
        //是否自然恢复生命（默认开启）
        public bool RegeneratesHealth = false;
        //开始恢复生命前的延迟（秒）
        public float ZergHealthRegenerationDelay = 1.5f;
        //每秒恢复的生命值
        public float ZergHealthRegenerationPerSecond = 2f;
        //虫苔上的移速倍率
        public float CreepMoveSpeedMultiplier = 1.2f;

        //实际移速：虫族固定享受虫苔移速加成
        public override float EffectiveMoveSpeed => MoveSpeed * CreepMoveSpeedMultiplier;
        //生命恢复延迟：未开启恢复时设为无穷大（永不恢复）
        public override float HealthRegenerationDelay => RegeneratesHealth ? ZergHealthRegenerationDelay : float.PositiveInfinity;
        //每秒生命恢复量：未开启恢复时为 0
        public override float HealthRegenerationPerSecond => RegeneratesHealth ? ZergHealthRegenerationPerSecond : 0f;

        //种族特性摘要：拼接恢复速率与潜地能力文本，供 UI 展示
        public override string FactionFeatureSummary
        {
            get
            {
                string result = RegeneratesHealth ? $"生命恢复 {ZergHealthRegenerationPerSecond:0.0}/秒" : string.Empty;
                if (CanBurrow) result = string.IsNullOrEmpty(result) ? "可潜地" : result + "、可潜地";
                return result;
            }
        }
    }
}
