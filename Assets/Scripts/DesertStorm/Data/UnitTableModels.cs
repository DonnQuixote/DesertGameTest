using System;

namespace DesertStorm
{
    /// <summary>
    /// 单位表数据模型：UnitTableImporter（编辑器）把 Excel 行序列化成这些结构写入 JSON，
    /// 运行时 UnitCatalog 用 JsonUtility 读回并映射到 UnitData 子类。
    /// 字段全部为可序列化的简单类型（JsonUtility 支持）。
    /// </summary>
    [Serializable]
    public class UnitRow
    {
        //与基础属性表列一一对应（列名与表格保持小驼峰一致）
        public string id;
        public string name;
        public string faction;
        public int tier;
        public float maxHealth;
        public float maxShield;
        //对轻甲（light/psionic/none 目标）单次伤害
        public float attackLightDamage;
        //对重甲（armored/massive 目标）单次伤害
        public float attackHeavyDamage;
        public float armor;
        public float attackRange;
        public float attackRangeAir;
        public float moveSpeed;
        public float attackCooldown;
        public string targetDomain;
        public string domain;
        public string armorType;
        public string unitClass;
        public float splashRadius;
        public float splashMultiplier;
        public bool isSupport;
        public bool isSuicide;
        //通用扩展列：补给占用 / 碰撞半径 / 威胁值 / 冲击性（0 或空表示用推导值）
        public float unitSupply;
        public float unitRadius;
        public float threat;
        public float impact;
        //主动技能 ID 列表（逗号分隔的表格列拆分而来），对应 AbilityTable.json
        public string[] abilities;
        //种族扩展（未出现在扩展表中的单位为 null）
        public TerranExtension terran;
        public ZergExtension zerg;
        public ProtossExtension protoss;

        //序列化为一行 JSON 对象（null 扩展直接省略字段，JsonUtility 读回时保持 null）
        public string ToJson()
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("    {\n");
            sb.Append($"      \"id\": \"{Escape(id)}\",\n");
            sb.Append($"      \"name\": \"{Escape(name)}\",\n");
            sb.Append($"      \"faction\": \"{Escape(faction)}\",\n");
            sb.Append($"      \"tier\": {tier},\n");
            sb.Append($"      \"maxHealth\": {Fmt(maxHealth)},\n");
            sb.Append($"      \"maxShield\": {Fmt(maxShield)},\n");
            sb.Append($"      \"attackLightDamage\": {Fmt(attackLightDamage)},\n");
            sb.Append($"      \"attackHeavyDamage\": {Fmt(attackHeavyDamage)},\n");
            sb.Append($"      \"armor\": {Fmt(armor)},\n");
            sb.Append($"      \"attackRange\": {Fmt(attackRange)},\n");
            sb.Append($"      \"attackRangeAir\": {Fmt(attackRangeAir)},\n");
            sb.Append($"      \"moveSpeed\": {Fmt(moveSpeed)},\n");
            sb.Append($"      \"attackCooldown\": {Fmt(attackCooldown)},\n");
            sb.Append($"      \"targetDomain\": \"{Escape(targetDomain)}\",\n");
            sb.Append($"      \"domain\": \"{Escape(domain)}\",\n");
            sb.Append($"      \"armorType\": \"{Escape(armorType)}\",\n");
            sb.Append($"      \"unitClass\": \"{Escape(unitClass)}\",\n");
            sb.Append($"      \"splashRadius\": {Fmt(splashRadius)},\n");
            sb.Append($"      \"splashMultiplier\": {Fmt(splashMultiplier)},\n");
            sb.Append($"      \"isSupport\": {(isSupport ? "true" : "false")},\n");
            sb.Append($"      \"isSuicide\": {(isSuicide ? "true" : "false")},\n");
            sb.Append($"      \"unitSupply\": {Fmt(unitSupply)},\n");
            sb.Append($"      \"unitRadius\": {Fmt(unitRadius)},\n");
            sb.Append($"      \"threat\": {Fmt(threat)},\n");
            sb.Append($"      \"impact\": {Fmt(impact)},\n");
            sb.Append($"      \"abilities\": [{SerializedAbilities()}],\n");
            if (terran != null)
            {
                sb.Append(",\n      \"terran\": {\n");
                sb.Append($"        \"canBeRepaired\": {(terran.canBeRepaired ? "true" : "false")},\n");
                sb.Append($"        \"hasStimpack\": {(terran.hasStimpack ? "true" : "false")},\n");
                sb.Append($"        \"canSiege\": {(terran.canSiege ? "true" : "false")},\n");
                sb.Append($"        \"stimSpeedMul\": {Fmt(terran.stimSpeedMul)},\n");
                sb.Append($"        \"stimCdMul\": {Fmt(terran.stimCdMul)}\n      }}");
            }
            if (zerg != null)
            {
                sb.Append(",\n      \"zerg\": {\n");
                sb.Append($"        \"canBurrow\": {(zerg.canBurrow ? "true" : "false")},\n");
                sb.Append($"        \"regenPerSecond\": {Fmt(zerg.regenPerSecond)},\n");
                sb.Append($"        \"creepMul\": {Fmt(zerg.creepMul)}\n      }}");
            }
            if (protoss != null)
            {
                sb.Append(",\n      \"protoss\": {\n");
                sb.Append($"        \"canWarpIn\": {(protoss.canWarpIn ? "true" : "false")},\n");
                sb.Append($"        \"shieldRegenDelay\": {Fmt(protoss.shieldRegenDelay)},\n");
                sb.Append($"        \"shieldRegenPerSecond\": {Fmt(protoss.shieldRegenPerSecond)},\n");
                sb.Append($"        \"maxEnergy\": {Fmt(protoss.maxEnergy)}\n      }}");
            }
            sb.Append("\n    }");
            return sb.ToString();
        }

        //abilities 数组序列化为 JSON 字符串数组（空数组输出空）
        private string SerializedAbilities()
        {
            if (abilities == null || abilities.Length == 0) return string.Empty;
            var parts = new string[abilities.Length];
            for (int i = 0; i < abilities.Length; i++)
                parts[i] = $"\"{Escape(abilities[i])}\"";
            return string.Join(", ", parts);
        }

        //JSON 字符串转义（引号与反斜杠；中文直接输出 UTF-8）
        private static string Escape(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        //浮点格式化：保留最多 4 位小数、去掉多余的 0（如 1.10 → 1.1）
        private static string Fmt(float value)
        {
            return value.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);
        }
    }

    [Serializable]
    public class TerranExtension
    {
        public bool canBeRepaired;
        public bool hasStimpack;
        public bool canSiege;
        public float stimSpeedMul;
        public float stimCdMul;
    }

    [Serializable]
    public class ZergExtension
    {
        public bool canBurrow;
        public float regenPerSecond;
        public float creepMul;
    }

    [Serializable]
    public class ProtossExtension
    {
        public bool canWarpIn;
        public float shieldRegenDelay;
        public float shieldRegenPerSecond;
        public float maxEnergy;
    }

    [Serializable]
    public class UnitTablePayload
    {
        //导入时间戳（仅追溯用）
        public string version;
        //全部单位行
        public UnitRow[] units;
    }
}
