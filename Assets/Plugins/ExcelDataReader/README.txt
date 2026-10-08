// ASMDEF 暂不使用；此目录说明文件。
// ExcelDataReader 3.7.0 核心 DLL（MIT），取自官方 nuget 包的 netstandard2.0 版本。
// 供 Assets/Editor/UnitTableImporter.cs 在编辑期把两张数据表转为 JSON，
// 使用其核心逐行读取 API（IExcelDataReader），不依赖 System.Data
// （工程 API 兼容级别为 .NET Standard 2.1，不含 System.Data，
// 因此原 ExcelDataReader.DataSet 扩展 DLL 已移除）。
// 编辑器专用依赖，运行时程序集不引用它（导入产物是 JSON，打包不携带任何 Excel 解析代码）。
