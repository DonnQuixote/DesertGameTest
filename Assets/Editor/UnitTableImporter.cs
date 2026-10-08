using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using ExcelDataReader;
using UnityEditor;
using UnityEngine;

namespace DesertStorm.EditorTools
{
    /// <summary>
    /// 单位数据表导入器（仅编辑器）——双工作簿结构：
    /// 表1 Assets/DataConfig/UnitDataDictionary.xlsx：字段定义字典（基础属性/人族/虫族/神族 4 个 Sheet），
    ///     列 = 英文字段 | 中文翻译 | 字段类型 | 默认数值 | 解释说明；
    /// 表2 Assets/DataConfig/UnitTable.xlsx：单位数据（人族/神族/虫族 3 个 Sheet），
    ///     列名 = 字典对应 Sheet 的『英文字段』，行 = 具体单位。
    /// 导入流程：读字典建字段元数据 → 读单位表逐行解析（缺列用字典默认值）→ 校验 → 写 Assets/Resources/UnitTable.json。
    /// 菜单：工具 → 导入单位数据表；两表任一变更保存后自动触发。校验失败沿用上一次 JSON。
    /// </summary>
    public static class UnitTableImporter
    {
        //两张工作簿路径（相对于工程根目录）
        private const string DictionaryPath = "Assets/DataConfig/UnitDataDictionary.xlsx";
        private const string UnitTablePath = "Assets/DataConfig/UnitTable.xlsx";
        //技能工作簿路径（技能表/状态表定义）
        private const string AbilityTablePath = "Assets/DataConfig/AbilityTable.xlsx";
        //导入产物 JSON 路径（必须位于 Resources 下）
        private const string OutputPath = "Assets/Resources/UnitTable.json";
        private const string AbilityOutputPath = "Assets/Resources/AbilityTable.json";

        //字典里标注"程序推导"的关键词（该字段不出现在单位表，也不校验默认值类型）
        private const string DerivedMark = "程序推导";

        //种族 Sheet 名（单位表与字典共用同一套种族名）
        private static readonly string[] Factions = { "人族", "神族", "虫族" };

        //基础属性字典里允许填入单位表的字段（按出现顺序）；程序推导字段不在此列
        private static readonly string[] BaseTableFields =
        {
            "unitId", "displayName", "tier", "maxHealth", "maxShield",
            "attackLightDamage", "attackHeavyDamage", "armor",
            "attackRange", "attackRangeAir", "moveSpeed", "attackCooldown", "targetDomain", "domain",
            "armorType", "unitClass", "splashRadius", "splashMultiplier", "isSupport", "suicideAttack",
            "unitSupply", "unitRadius", "threat", "impact", "abilities"
        };

        //合法枚举值（与 UnitData 语义一致）
        private static readonly string[] ValidTargetDomains = { "ground", "air", "both" };
        private static readonly string[] ValidDomains = { "ground", "air" };
        private static readonly string[] ValidArmorTypes = { "light", "armored", "massive", "psionic", "none" };
        private static readonly string[] ValidUnitClasses = { "biological", "mechanical", "psionic" };
        //合法机制类型（AbilityTable 技能表 mechanism 列）
        private static readonly string[] ValidMechanisms = { "buff", "transform", "summon", "zone", "projectile" };

        //旧帕斯卡字段名 → 新驼峰字段名兼容映射（表格尚未完全迁移时兜底）
        private static readonly Dictionary<string, string> LegacyHeaderMap = new Dictionary<string, string>
        {
            { "UnitId", "unitId" }, { "DisplayName", "displayName" }, { "Tier", "tier" },
            { "MaxHealth", "maxHealth" }, { "MaxShield", "maxShield" },
            { "AttackDamage", "attackLightDamage" }, { "AttackLightDamage", "attackLightDamage" },
            { "AttackHeavyDamage", "attackHeavyDamage" }, { "Armor", "armor" },
            { "AttackRange", "attackRange" }, { "AttackRangeAir", "attackRangeAir" },
            { "MoveSpeed", "moveSpeed" }, { "AttackCooldown", "attackCooldown" },
            { "TargetDomain", "targetDomain" }, { "Domain", "domain" },
            { "ArmorType", "armorType" }, { "UnitClass", "unitClass" },
            { "SplashRadius", "splashRadius" }, { "SplashMultiplier", "splashMultiplier" },
            { "IsSupport", "isSupport" }, { "SuicideAttack", "suicideAttack" },
            { "CanBeRepaired", "canBeRepaired" }, { "HasStimpack", "hasStimpack" },
            { "CanEnterSiegeMode", "canEnterSiegeMode" },
            { "StimpackMoveSpeedMultiplier", "stimpackMoveSpeedMultiplier" },
            { "StimpackAttackCooldownMultiplier", "stimpackAttackCooldownMultiplier" },
            { "CanWarpIn", "canWarpIn" }, { "ShieldRegenDelay", "shieldRegenDelay" },
            { "ProtossShieldRegenerationPerSecond", "protossShieldRegenerationPerSecond" },
            { "MaxEnergy", "maxEnergy" }, { "CanBurrow", "canBurrow" },
            { "RegeneratesHealth", "regeneratesHealth" },
            { "ZergHealthRegenerationPerSecond", "zergHealthRegenerationPerSecond" },
            { "ZergHealthRegenerationDelay", "zergHealthRegenerationDelay" },
            { "CreepMoveSpeedMultiplier", "creepMoveSpeedMultiplier" }
        };

        //上次导入的文件时间戳（用于自动触发去重）
        private static long dictStamp, tableStamp, abilityStamp;
        private static bool stampsInitialized;

        [InitializeOnLoadMethod]
        private static void RegisterFileWatcher()
        {
            EditorApplication.update -= AutoImportCheck;
            EditorApplication.update += AutoImportCheck;
        }

        [MenuItem("工具/导入单位数据表")]
        private static void ImportMenu() => Import(false);

        [MenuItem("工具/导入技能数据表")]
        private static void ImportAbilityMenu() => ImportAbilities(false);

        //编辑器轮询：任一工作簿写入时间变化即自动导入
        private static void AutoImportCheck()
        {
            long d = Stamp(DictionaryPath), t = Stamp(UnitTablePath), a = Stamp(AbilityTablePath);
            if (!stampsInitialized)
            {
                dictStamp = d; tableStamp = t; abilityStamp = a; stampsInitialized = true;
                return;
            }
            bool dictChanged = d != dictStamp, tableChanged = t != tableStamp, abilityChanged = a != abilityStamp;
            dictStamp = d; tableStamp = t; abilityStamp = a;
            //技能表先导入（单位表 abilities 校验依赖技能目录）
            if (abilityChanged && File.Exists(Path.GetFullPath(AbilityTablePath))) ImportAbilities(true);
            if (dictChanged || tableChanged) Import(true);
        }

        private static long Stamp(string path)
        {
            string full = Path.GetFullPath(path);
            return File.Exists(full) ? ((DateTimeOffset)File.GetLastWriteTime(full)).ToUnixTimeSeconds() : -1;
        }

        //导入主流程
        private static void Import(bool auto)
        {
            string dictFull = Path.GetFullPath(DictionaryPath);
            string tableFull = Path.GetFullPath(UnitTablePath);
            if (!File.Exists(dictFull) || !File.Exists(tableFull))
            {
                Debug.LogWarning($"[单位表导入] 缺少 {DictionaryPath} 或 {UnitTablePath}，跳过导入。");
                return;
            }

            var errors = new List<string>();
            Dictionary<string, SheetData> dictBook, unitBook;
            try
            {
                dictBook = ReadWorkbook(dictFull);
                unitBook = ReadWorkbook(tableFull);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[单位表导入] 读取 Excel 失败（文件可能被 Excel/WPS 占用或格式损坏）：{ex.Message}");
                return;
            }

            //---- 第一步：读字典，构建 字段名→元数据 ----
            var fieldDefs = new Dictionary<string, FieldDef>(); //key = 种族|字段名
            foreach (string sheet in new[] { "基础属性", "人族", "虫族", "神族" })
            {
                if (!dictBook.ContainsKey(sheet))
                {
                    errors.Add($"字典缺少 Sheet『{sheet}』。");
                    continue;
                }
                ParseDictionarySheet(dictBook[sheet], sheet, fieldDefs);
            }
            if (errors.Count > 0)
            {
                Fail(errors, auto);
                return;
            }

            //---- 第二步：读单位表三个种族 Sheet，逐行解析 ----
            var rows = new List<UnitRow>();
            var seen = new HashSet<string>();
            foreach (string faction in Factions)
            {
                if (!unitBook.ContainsKey(faction))
                {
                    errors.Add($"单位表缺少 Sheet『{faction}』。");
                    continue;
                }
                ParseUnitSheet(unitBook[faction], faction, fieldDefs, rows, seen, errors);
            }

            if (errors.Count > 0) { Fail(errors, auto); return; }
            if (rows.Count == 0) { Fail(new List<string> { "单位表没有解析出任何单位。" }, auto); return; }

            //---- 第三步：写 JSON（结构不变，运行时 UnitCatalog 无需改动）----
            string dir = Path.GetDirectoryName(Path.GetFullPath(OutputPath));
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            string json = SerializeRows(rows);
            File.WriteAllText(Path.GetFullPath(OutputPath), json, new UTF8Encoding(false));
            AssetDatabase.Refresh();
            Debug.Log($"[单位表导入] 成功导入 {rows.Count} 个单位 → {OutputPath}" + (auto ? "（检测到表格变更，自动导入）" : ""));
        }

        //读取工作簿：ExcelDataReader 核心 API 逐行读取（不依赖 System.Data，
        //在 .NET Standard 2.1 API 兼容级别下可用），返回 Sheet名→表格数据
        private static Dictionary<string, SheetData> ReadWorkbook(string path)
        {
            var book = new Dictionary<string, SheetData>();
            using (var stream = File.Open(path, FileMode.Open, FileAccess.Read))
            using (var reader = ExcelReaderFactory.CreateReader(stream))
            {
                do
                {
                    var sheet = new SheetData { Name = reader.Name };
                    //表头行
                    reader.Read();
                    for (int c = 0; c < reader.FieldCount; c++)
                        sheet.Headers.Add(ToStringValue(reader.GetValue(c)));
                    //数据行
                    while (reader.Read())
                    {
                        var row = new string[reader.FieldCount];
                        for (int c = 0; c < reader.FieldCount; c++)
                            row[c] = ToStringValue(reader.GetValue(c));
                        sheet.Rows.Add(row);
                    }
                    sheet.RowCount = sheet.Rows.Count;
                    book[sheet.Name] = sheet;
                }
                while (reader.NextResult());
            }
            return book;
        }

        //单元格值转字符串：数字去尾零（5.0→"5"），布尔 True/False，其余原样
        private static string ToStringValue(object value)
        {
            if (value == null) return string.Empty;
            if (value is double d)
            {
                if (d == Math.Floor(d) && Math.Abs(d) < 1e15)
                    return ((long)d).ToString(System.Globalization.CultureInfo.InvariantCulture);
                return d.ToString("0.#####", System.Globalization.CultureInfo.InvariantCulture);
            }
            if (value is bool b) return b ? "True" : "False";
            return value.ToString().Trim();
        }

        //轻量表格数据（替代 System.Data.DataTable）：表头 + 字符串行
        private sealed class SheetData
        {
            public string Name;
            public List<string> Headers = new List<string>();
            public List<string[]> Rows = new List<string[]>();
            public int RowCount;

            public string this[int row, int col] => Rows[row][col];
        }

        //---- 字典解析：一行 = 英文字段|中文|类型|默认值|说明（英文字段同步映射为驼峰）----
        private static void ParseDictionarySheet(SheetData table, string sheetName, Dictionary<string, FieldDef> fieldDefs)
        {
            for (int r = 0; r < table.RowCount; r++)
            {
                var map = RowMap(table, r);
                if (map == null) continue;
                string field = Cell(map, "英文字段");
                if (string.IsNullOrEmpty(field)) continue;
                field = LegacyHeaderMap.TryGetValue(field, out var mapped) ? mapped : field;
                bool derived = (Cell(map, "默认数值") + Cell(map, "解释说明")).Contains(DerivedMark);
                fieldDefs[$"{sheetName}|{field}"] = new FieldDef
                {
                    Name = field,
                    Chinese = Cell(map, "中文翻译"),
                    Type = Cell(map, "字段类型").ToLowerInvariant(),
                    DefaultText = Cell(map, "默认数值"),
                    Derived = derived
                };
            }
        }

        //---- 列名标准化：剥掉表尾的中文括号注释（"attackRange(对地射程)"→"attackRange"），
        //并把旧帕斯卡字段名映射为驼峰（表格迁移期兜底）；纯列名同样兼容
        private static string NormalizeHeader(string header)
        {
            string key = header?.Trim() ?? string.Empty;
            int paren = key.IndexOf('(');
            if (paren > 0) key = key.Substring(0, paren).Trim();
            return LegacyHeaderMap.TryGetValue(key, out var mapped) ? mapped : key;
        }

        //---- 单位表解析：列名 = 字典英文字段（表头允许 英文(中文) 格式）；值缺省用字典默认值 ----
        private static void ParseUnitSheet(SheetData table, string faction, Dictionary<string, FieldDef> fieldDefs,
            List<UnitRow> rows, HashSet<string> seen, List<string> errors)
        {
            //先把表头标准化为纯英文字段名（写入 RowMap 用的映射）
            var normalizedHeaders = new List<string>();
            foreach (string name in table.Headers)
                normalizedHeaders.Add(NormalizeHeader(name));

            //校验本 Sheet 的列是否都在字典范围内（防止拼错列名静默失效）
            var sheetFieldKeys = new List<string>();
            for (int i = 0; i < normalizedHeaders.Count; i++)
            {
                string key = normalizedHeaders[i];
                if (string.IsNullOrEmpty(key) || key == "使用说明") continue;
                //重复列名（标准化后相同）只取第一列
                if (sheetFieldKeys.Contains(key)) continue;
                //基础字段对所有种族可用；扩展字段只查本族字典
                if (Array.IndexOf(BaseTableFields, key) >= 0 || fieldDefs.ContainsKey($"{faction}|{key}"))
                    sheetFieldKeys.Add(key);
                else
                    errors.Add($"{faction} Sheet：列『{table.Headers[i]}』不在字段字典中（检查拼写，或在字典中补充定义）。");
            }

            //把标准化后的表头写回 sheet.Headers，RowMap 直接按纯英文名取值
            table.Headers.Clear();
            table.Headers.AddRange(normalizedHeaders);

            //UnitId 列必须存在
            if (!sheetFieldKeys.Contains("unitId"))
            {
                errors.Add($"{faction} Sheet：缺少『unitId』列。");
                return;
            }

            for (int r = 0; r < table.RowCount; r++)
            {
                int line = r + 2;
                var map = RowMap(table, r);
                if (map == null) continue;
                string id = Cell(map, "unitId");
                if (string.IsNullOrEmpty(id)) continue;

                if (id.Any(char.IsWhiteSpace)) errors.Add($"{faction} 第{line}行：unitId『{id}』包含空格。");
                if (!seen.Add(id)) errors.Add($"{faction} 第{line}行：unitId『{id}』重复（含跨种族）。");

                var row = new UnitRow { id = id };
                row.name = TextCell(map, "displayName", $"{faction}|displayName", fieldDefs, $"{faction} 第{line}行", errors);
                row.faction = faction; //种族以 Sheet 名为准，表内不再重复填

                row.tier = (int)NumCell(map, "tier", $"{faction}|tier", fieldDefs, $"{faction} 第{line}行", errors, 1, 99);
                row.maxHealth = NumCell(map, "maxHealth", $"{faction}|maxHealth", fieldDefs, $"{faction} 第{line}行", errors, 0f, 1000000f);
                row.maxShield = NumCell(map, "maxShield", $"{faction}|maxShield", fieldDefs, $"{faction} 第{line}行", errors, 0f, 1000000f);
                row.attackLightDamage = NumCell(map, "attackLightDamage", $"{faction}|attackLightDamage", fieldDefs, $"{faction} 第{line}行", errors, 0f, 100000f);
                row.attackHeavyDamage = NumCell(map, "attackHeavyDamage", $"{faction}|attackHeavyDamage", fieldDefs, $"{faction} 第{line}行", errors, 0f, 100000f);
                row.armor = NumCell(map, "armor", $"{faction}|armor", fieldDefs, $"{faction} 第{line}行", errors, 0f, 100f);
                row.attackRange = NumCell(map, "attackRange", $"{faction}|attackRange", fieldDefs, $"{faction} 第{line}行", errors, 0f, 1000f);
                row.attackRangeAir = NumCell(map, "attackRangeAir", $"{faction}|attackRangeAir", fieldDefs, $"{faction} 第{line}行", errors, 0f, 1000f);
                row.moveSpeed = NumCell(map, "moveSpeed", $"{faction}|moveSpeed", fieldDefs, $"{faction} 第{line}行", errors, 0.1f, 100f);
                row.attackCooldown = NumCell(map, "attackCooldown", $"{faction}|attackCooldown", fieldDefs, $"{faction} 第{line}行", errors, 0.01f, 300f);

                row.targetDomain = EnumCell(map, "targetDomain", $"{faction}|targetDomain", fieldDefs, ValidTargetDomains, $"{faction} 第{line}行", errors);
                row.domain = EnumCell(map, "domain", $"{faction}|domain", fieldDefs, ValidDomains, $"{faction} 第{line}行", errors);
                row.armorType = EnumCell(map, "armorType", $"{faction}|armorType", fieldDefs, ValidArmorTypes, $"{faction} 第{line}行", errors);
                row.unitClass = EnumCell(map, "unitClass", $"{faction}|unitClass", fieldDefs, ValidUnitClasses, $"{faction} 第{line}行", errors);

                row.splashRadius = NumCell(map, "splashRadius", $"{faction}|splashRadius", fieldDefs, $"{faction} 第{line}行", errors, 0f, 10000f);
                row.splashMultiplier = NumCell(map, "splashMultiplier", $"{faction}|splashMultiplier", fieldDefs, $"{faction} 第{line}行", errors, 0f, 10f);
                row.isSupport = BoolCell(map, "isSupport", $"{faction}|isSupport", fieldDefs, $"{faction} 第{line}行", errors);
                row.isSuicide = BoolCell(map, "suicideAttack", $"{faction}|suicideAttack", fieldDefs, $"{faction} 第{line}行", errors);

                //通用扩展列：补给/半径/威胁/冲击（缺省 0 = 程序派生）
                row.unitSupply = NumCell(map, "unitSupply", $"{faction}|unitSupply", fieldDefs, $"{faction} 第{line}行", errors, 0f, 100f);
                row.unitRadius = NumCell(map, "unitRadius", $"{faction}|unitRadius", fieldDefs, $"{faction} 第{line}行", errors, 0f, 20f);
                row.threat = NumCell(map, "threat", $"{faction}|threat", fieldDefs, $"{faction} 第{line}行", errors, 0f, 100f);
                row.impact = NumCell(map, "impact", $"{faction}|impact", fieldDefs, $"{faction} 第{line}行", errors, 0f, 10f);

                //主动技能列：逗号分隔的技能 ID（对应 AbilityTable 技能表），空 = 无技能
                row.abilities = ParseAbilities(Cell(map, "abilities"), $"{faction} 第{line}行", errors);

                //种族扩展字段（存在才解析；缺列/缺行时 UnitCatalog 端有默认回退）
                if (faction == "人族")
                {
                    row.terran = new TerranExtension
                    {
                        canBeRepaired = BoolCell(map, "canBeRepaired", $"{faction}|canBeRepaired", fieldDefs, $"{faction} 第{line}行", errors, row.unitClass == "mechanical"),
                        hasStimpack = BoolCell(map, "hasStimpack", $"{faction}|hasStimpack", fieldDefs, $"{faction} 第{line}行", errors),
                        canSiege = BoolCell(map, "canEnterSiegeMode", $"{faction}|canEnterSiegeMode", fieldDefs, $"{faction} 第{line}行", errors),
                        stimSpeedMul = NumCell(map, "stimpackMoveSpeedMultiplier", $"{faction}|stimpackMoveSpeedMultiplier", fieldDefs, $"{faction} 第{line}行", errors, 0.1f, 10f),
                        stimCdMul = NumCell(map, "stimpackAttackCooldownMultiplier", $"{faction}|stimpackAttackCooldownMultiplier", fieldDefs, $"{faction} 第{line}行", errors, 0.1f, 10f)
                    };
                }
                else if (faction == "虫族")
                {
                    float regen = NumCell(map, "zergHealthRegenerationPerSecond", $"{faction}|zergHealthRegenerationPerSecond", fieldDefs, $"{faction} 第{line}行", errors, 0f, 1000f);
                    row.zerg = new ZergExtension
                    {
                        canBurrow = BoolCell(map, "canBurrow", $"{faction}|canBurrow", fieldDefs, $"{faction} 第{line}行", errors),
                        regenPerSecond = regen,
                        creepMul = NumCell(map, "creepMoveSpeedMultiplier", $"{faction}|creepMoveSpeedMultiplier", fieldDefs, $"{faction} 第{line}行", errors, 0.1f, 10f)
                    };
                }
                else
                {
                    row.protoss = new ProtossExtension
                    {
                        canWarpIn = BoolCell(map, "canWarpIn", $"{faction}|canWarpIn", fieldDefs, $"{faction} 第{line}行", errors),
                        shieldRegenDelay = NumCell(map, "shieldRegenDelay", $"{faction}|shieldRegenDelay", fieldDefs, $"{faction} 第{line}行", errors, 0f, 300f),
                        shieldRegenPerSecond = NumCell(map, "protossShieldRegenerationPerSecond", $"{faction}|protossShieldRegenerationPerSecond", fieldDefs, $"{faction} 第{line}行", errors, 0f, 10000f),
                        maxEnergy = NumCell(map, "maxEnergy", $"{faction}|maxEnergy", fieldDefs, $"{faction} 第{line}行", errors, 0f, 10000f)
                    };
                }
                rows.Add(row);
            }
        }

        //解析 abilities 列：逗号分隔的技能 ID，统一小写去空格；未知技能 ID 报错（防拼写错误静默失效）
        private static string[] ParseAbilities(string text, string where, List<string> errors)
        {
            if (string.IsNullOrEmpty(text)) return Array.Empty<string>();
            var result = new List<string>();
            foreach (string part in text.Split(new[] { ',', '，', ';', '；' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string id = part.Trim();
                if (string.IsNullOrEmpty(id)) continue;
                if (AbilityCatalog.GetAbility(id) == null)
                {
                    errors.Add($"{where}：abilities 引用了未知技能『{id}』（检查 AbilityTable.xlsx 技能表）。");
                    continue;
                }
                result.Add(id);
            }
            return result.ToArray();
        }

        //---- 单元格读取辅助（全部经字典默认值兜底）----
        private static string Cell(Dictionary<string, string> map, string key)
        {
            return map.TryGetValue(key, out var value) ? value : string.Empty;
        }

        private static string TextCell(Dictionary<string, string> map, string column, string defKey,
            Dictionary<string, FieldDef> defs, string where, List<string> errors)
        {
            string value = Cell(map, column);
            if (!string.IsNullOrEmpty(value)) return value;
            return defs.TryGetValue(defKey, out var def) && !def.Derived && def.DefaultText != "（必填）" ? def.DefaultText : string.Empty;
        }

        private static float NumCell(Dictionary<string, string> map, string column, string defKey,
            Dictionary<string, FieldDef> defs, string where, List<string> errors, float min, float max)
        {
            string text = Cell(map, column);
            if (string.IsNullOrEmpty(text))
            {
                return defs.TryGetValue(defKey, out var def) && float.TryParse(def.DefaultText, out var dv) ? dv : 0f;
            }
            if (!float.TryParse(text, out var value))
            {
                errors.Add($"{where}：列『{column}』值『{text}』不是合法数字。");
                return 0f;
            }
            if (value < min || value > max)
            {
                errors.Add($"{where}：列『{column}』值 {value} 超出合理区间 [{min}, {max}]。");
                return Mathf.Clamp(value, min, max);
            }
            return value;
        }

        private static string EnumCell(Dictionary<string, string> map, string column, string defKey,
            Dictionary<string, FieldDef> defs, string[] valid, string where, List<string> errors)
        {
            string value = Cell(map, column);
            if (string.IsNullOrEmpty(value))
                value = defs.TryGetValue(defKey, out var def) ? def.DefaultText : valid[0];
            if (Array.IndexOf(valid, value) < 0)
            {
                errors.Add($"{where}：列『{column}』值『{value}』非法（{string.Join("/", valid)}）。");
                return valid[0];
            }
            return value;
        }

        private static bool BoolCell(Dictionary<string, string> map, string column, string defKey,
            Dictionary<string, FieldDef> defs, string where, List<string> errors, bool fallback = false)
        {
            string text = Cell(map, column);
            if (string.IsNullOrEmpty(text))
            {
                if (defs.TryGetValue(defKey, out var def))
                {
                    text = def.DefaultText;
                    if (text == "true") return true;
                    if (text == "false") return false;
                    //默认值是描述性文字（如"按 UnitClass 推导"）时用调用方 fallback
                    return fallback;
                }
                return fallback;
            }
            text = text.Trim().ToLowerInvariant();
            if (text == "是" || text == "true" || text == "1" || text == "y" || text == "yes") return true;
            if (text == "否" || text == "false" || text == "0" || text == "n" || text == "no") return false;
            errors.Add($"{where}：列『{column}』值『{text}』不是合法布尔（是/否）。");
            return fallback;
        }

        //---- 一行转"列名→值"字典（全空行返回 null）----
        private static Dictionary<string, string> RowMap(SheetData table, int rowIndex)
        {
            bool allEmpty = true;
            var map = new Dictionary<string, string>();
            string[] row = table.Rows[rowIndex];
            for (int c = 0; c < table.Headers.Count && c < row.Length; c++)
            {
                string key = table.Headers[c]?.Trim() ?? string.Empty;
                string value = row[c] ?? string.Empty;
                if (!string.IsNullOrEmpty(value)) allEmpty = false;
                if (!string.IsNullOrEmpty(key)) map[key] = value;
            }
            return allEmpty ? null : map;
        }

        private static void Fail(List<string> errors, bool auto)
        {
            Debug.LogError($"[单位表导入] 校验失败 {errors.Count} 项，沿用上一次导入数据：\n" + string.Join("\n", errors.Take(30)));
        }

        //---- 技能表导入：AbilityTable.xlsx（技能表/状态表 两个 Sheet）→ AbilityTable.json ----
        private static void ImportAbilities(bool auto)
        {
            string abilityFull = Path.GetFullPath(AbilityTablePath);
            if (!File.Exists(abilityFull))
            {
                if (!auto) Debug.LogWarning($"[技能表导入] 缺少 {AbilityTablePath}，跳过。");
                return;
            }

            var errors = new List<string>();
            Dictionary<string, SheetData> book;
            try
            {
                book = ReadWorkbook(abilityFull);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[技能表导入] 读取 Excel 失败：{ex.Message}");
                return;
            }

            var abilityRows = new List<AbilityRow>();
            var statusRows = new List<StatusRow>();
            var statusIds = new HashSet<string>();

            if (book.TryGetValue("技能表", out var abilitySheet))
            {
                for (int r = 0; r < abilitySheet.RowCount; r++)
                {
                    var map = RowMap(abilitySheet, r);
                    if (map == null) continue;
                    string id = Cell(map, "abilityId");
                    if (string.IsNullOrEmpty(id)) continue;
                    int line = r + 2;
                    var row = new AbilityRow
                    {
                        abilityId = id,
                        displayName = Cell(map, "displayName"),
                        mechanism = Cell(map, "mechanism"),
                        statusId = Cell(map, "statusId"),
                        description = Cell(map, "description")
                    };
                    if (Array.IndexOf(ValidMechanisms, row.mechanism) < 0)
                        errors.Add($"技能表 第{line}行：mechanism『{row.mechanism}』非法（{string.Join("/", ValidMechanisms)}）。");
                    row.duration = NumCell(map, "duration", null, null, $"技能表 第{line}行", errors, 0f, 3600f);
                    row.cooldown = NumCell(map, "cooldown", null, null, $"技能表 第{line}行", errors, 0f, 3600f);
                    row.hpCost = NumCell(map, "hpCost", null, null, $"技能表 第{line}行", errors, 0f, 100000f);
                    abilityRows.Add(row);
                }
            }
            else errors.Add("AbilityTable.xlsx 缺少『技能表』Sheet。");

            if (book.TryGetValue("状态表", out var statusSheet))
            {
                for (int r = 0; r < statusSheet.RowCount; r++)
                {
                    var map = RowMap(statusSheet, r);
                    if (map == null) continue;
                    string id = Cell(map, "statusId");
                    if (string.IsNullOrEmpty(id)) continue;
                    int line = r + 2;
                    var row = new StatusRow
                    {
                        statusId = id,
                        displayName = Cell(map, "displayName"),
                        description = Cell(map, "description")
                    };
                    row.moveSpeedMul = NumCell(map, "moveSpeedMul", null, null, $"状态表 第{line}行", errors, 0f, 100f);
                    row.attackSpeedMul = NumCell(map, "attackSpeedMul", null, null, $"状态表 第{line}行", errors, 0f, 100f);
                    row.armorAdd = NumCell(map, "armorAdd", null, null, $"状态表 第{line}行", errors, -1000f, 1000f);
                    row.damageAdd = NumCell(map, "damageAdd", null, null, $"状态表 第{line}行", errors, -100000f, 100000f);
                    row.suppressAttack = BoolCell(map, "suppressAttack", null, null, $"状态表 第{line}行", errors);
                    statusIds.Add(id);
                    statusRows.Add(row);
                }
            }
            else errors.Add("AbilityTable.xlsx 缺少『状态表』Sheet。");

            //交叉校验：技能引用的状态必须存在
            foreach (var ability in abilityRows)
                if (!string.IsNullOrEmpty(ability.statusId) && !statusIds.Contains(ability.statusId))
                    errors.Add($"技能表：技能『{ability.abilityId}』引用的 statusId『{ability.statusId}』在状态表中不存在。");

            if (errors.Count > 0)
            {
                Debug.LogError($"[技能表导入] 校验失败 {errors.Count} 项，沿用上一次数据：\n" + string.Join("\n", errors.Take(30)));
                return;
            }

            string dir = Path.GetDirectoryName(Path.GetFullPath(AbilityOutputPath));
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(Path.GetFullPath(AbilityOutputPath), SerializeAbilities(abilityRows, statusRows), new UTF8Encoding(false));
            AssetDatabase.Refresh();
            Debug.Log($"[技能表导入] 成功导入 {abilityRows.Count} 个技能 / {statusRows.Count} 个状态 → {AbilityOutputPath}" + (auto ? "（检测到表格变更，自动导入）" : ""));
        }

        //技能表 JSON 输出（与 AbilityCatalog 的载荷结构一致）
        private static string SerializeAbilities(List<AbilityRow> abilities, List<StatusRow> statuses)
        {
            var sb = new StringBuilder();
            sb.Append("{\n  \"version\": \"");
            sb.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.Append("\",\n  \"abilities\": [\n");
            for (int i = 0; i < abilities.Count; i++)
            {
                sb.Append(abilities[i].ToJson());
                sb.Append(i < abilities.Count - 1 ? ",\n" : "\n");
            }
            sb.Append("  ],\n  \"statuses\": [\n");
            for (int i = 0; i < statuses.Count; i++)
            {
                sb.Append(statuses[i].ToJson());
                sb.Append(i < statuses.Count - 1 ? ",\n" : "\n");
            }
            sb.Append("  ]\n}\n");
            return sb.ToString();
        }

        //---- JSON 输出（与 UnitTableModels.UnitRow.ToJson 同构，运行时 JsonUtility 读回）----
        private static string SerializeRows(List<UnitRow> rows)
        {
            var sb = new StringBuilder();
            sb.Append("{\n  \"version\": \"");
            sb.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.Append("\",\n  \"units\": [\n");
            for (int i = 0; i < rows.Count; i++)
            {
                sb.Append(rows[i].ToJson());
                sb.Append(i < rows.Count - 1 ? ",\n" : "\n");
            }
            sb.Append("  ]\n}\n");
            return sb.ToString();
        }

        //字段元数据（字典一行）
        private sealed class FieldDef
        {
            public string Name;
            public string Chinese;
            public string Type;
            public string DefaultText;
            public bool Derived;
        }
    }
}
