using System;

namespace DesertStorm
{
    /// <summary>
    /// 人族单位数据：在通用单位数据基础上扩展机械维修、兴奋剂与攻城模式等种族特性。
    /// 兴奋剂的数值效果由状态系统（StatusSystem）以修正器形式叠加，
    /// 这里的倍率字段仅作为表格默认值参考，实际生效以运行时状态为准。
    /// </summary>
    [Serializable]
    public sealed class TerranUnitData : UnitData
    {
        // 人族特色：机械单位可维修，生物单位可使用兴奋剂，攻城单位支持攻城模式。
        //是否可被维修（机械单位为真）
        public bool CanBeRepaired;
        //是否拥有兴奋剂技能（表格 hasStimpack 列，用于兜底与展示）
        public bool HasStimpack;
        //是否支持攻城模式
        public bool CanEnterSiegeMode;
        //兴奋剂状态移速倍率（表格默认值；运行时以状态修正器为准）
        public float StimpackMoveSpeedMultiplier = 1.5f;
        //兴奋剂状态攻速倍率（表格默认值；运行时以状态修正器为准）
        public float StimpackAttackCooldownMultiplier = 1.33f;

        //种族特性摘要：拼接已启用的特性文本，供 UI 展示
        public override string FactionFeatureSummary
        {
            get
            {
                string result = CanBeRepaired ? "可维修" : string.Empty;
                if (HasStimpack) result = Append(result, "兴奋剂");
                if (CanEnterSiegeMode) result = Append(result, "可攻城模式");
                return result;
            }
        }

        //工具方法：用顿号把特性词拼接到已有文本后（首词不加分隔符）
        private static string Append(string source, string value)
        {
            return string.IsNullOrEmpty(source) ? value : source + "、" + value;
        }
    }
}
