using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace DynamicBlockToYamlExporter
{
    public class ExportCommands
    {
        private static int _countBlocks = 0;
        private static List<string> _typeScheme = new List<string>();
        private static bool _debugMode = false;

        private static string GetOriginalDrawingPath(Database db, Document doc)
        {
            // Пробуем получить оригинальный путь через Document
            string originalPath = doc.Name;

            if (!string.IsNullOrEmpty(originalPath) && !originalPath.StartsWith("*"))
            {
                // doc.Name возвращает полный путь к исходному файлу
                return originalPath;
            }

            // Если документ не сохранён, используем имя чертежа с запросом у пользователя
            return null;
        }

        [CommandMethod("ExportBlockToYaml")]
        public static void ExportBlockToYaml()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Editor ed = doc.Editor;
            Database db = doc.Database;
            DateTime startTime = DateTime.Now;

            try
            {
                // Инициализация для текущего документа
                if (!InitTypeSchemeForDocument(doc, ed))
                {
                    ed.WriteMessage("\nОперация отменена пользователем.");
                    return;
                }

                InitCounterBlocks();
                
                // Получаем правильный путь к файлу
                string dwgPath = GetOriginalDrawingPath(db, doc);
                if (string.IsNullOrEmpty(dwgPath))
                {
                    // Если чертёж не сохранён, предлагаем сохранить или указать путь
                    PromptSaveDrawing(ed);
                    dwgPath = GetOriginalDrawingPath(db, doc);
                    if (string.IsNullOrEmpty(dwgPath))
                    {
                        ed.WriteMessage("\nЧертёж не сохранён. Пожалуйста, сохраните чертёж перед экспортом.");
                        return;
                    }
                }

                // Экспорт
                string dwgName = Path.GetFileNameWithoutExtension(dwgPath);
                string dwgPrefix = Path.GetDirectoryName(dwgPath);
                string filePath = Path.Combine(dwgPrefix, dwgName + ".yaml");

                if (ExportBlocksToYaml(doc, filePath))
                {
                    ed.WriteMessage($"\nДанные сохранены в файл: {filePath}");
                }
                else
                {
                    ed.WriteMessage("\nВ чертеже нет блоков или ошибка экспорта.");
                }

                // Вывод статистики
                TimeSpan elapsed = DateTime.Now - startTime;
                ed.WriteMessage($"\nКоличество выгруженных блоков: {_countBlocks}");
                ed.WriteMessage($"\nВремя выполнения: {elapsed.TotalSeconds:F4} сек.");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nОшибка: {ex.Message}");
            }
        }

        private static void PromptSaveDrawing(Editor ed)
        {
            PromptKeywordOptions pko = new PromptKeywordOptions("\nЧертёж не сохранён. Сохранить перед экспортом? [Да/Нет]: ");
            pko.Keywords.Add("Да");
            pko.Keywords.Add("Нет");
            pko.Keywords.Default = "Да";
            pko.AllowNone = false;

            PromptResult pr = ed.GetKeywords(pko);
            if (pr.Status == PromptStatus.OK && pr.StringResult == "Да")
            {
                // Отправляем команду сохранения
                Application.DocumentManager.MdiActiveDocument.SendStringToExecute("_qsave ", true, false, false);
            }
        }

        private static bool InitTypeSchemeForDocument(Document doc, Editor ed)
        {
            // Получаем сохранённый выбор для текущего документа
            string userChoice = GetUserChoiceForDocument(doc);

            if (string.IsNullOrEmpty(userChoice))
            {
                var choices = new List<string> { "Схема", "ЗЗИ", "Трасса", "ВсеБлоки" };
                PromptKeywordOptions pko = new PromptKeywordOptions("\nВыберите тип данных для инициализации [Схема/ЗЗИ/Трасса/ВсеБлоки]: ");
                foreach (var choice in choices)
                {
                    pko.Keywords.Add(choice);
                }
                pko.Keywords.Default = "ВсеБлоки";
                pko.AllowNone = false;

                PromptResult pr = ed.GetKeywords(pko);
                if (pr.Status == PromptStatus.OK)
                {
                    userChoice = pr.StringResult;
                    SaveUserChoiceForDocument(doc, userChoice);
                }
                else
                {
                    return false; // Пользователь отменил
                }
            }

            switch (userChoice)
            {
                case "Схема":
                    InitListSchemeBlocks();
                    break;
                case "ЗЗИ":
                    InitListZziBlocks();
                    break;
                case "Трасса":
                    InitListTraceBlocks();
                    break;
                default:
                    InitListAllBlocks();
                    break;
            }

            ed.WriteMessage($"\nИнициализировано типов блоков: {_typeScheme.Count} (тип: {userChoice})");
            return true;
        }

        private static string GetUserChoiceForDocument(Document doc)
        {
            // Используем UserData документа для хранения выбора
            if (doc.UserData.ContainsKey("DynamicBlockExport_UserChoice"))
            {
                return doc.UserData["DynamicBlockExport_UserChoice"] as string;
            }
            return null;
        }

        private static void SaveUserChoiceForDocument(Document doc, string choice)
        {
            doc.UserData["DynamicBlockExport_UserChoice"] = choice;
        }

        private static void InitListSchemeBlocks()
        {
            _typeScheme = new List<string>
            {
                "КЛЕММА1", "КЛЕММА2", "КЛЕММА1_2КАБ", "КЛЕММА2_2КАБ",
                "КЛЕММА_ВН1", "КЛЕММА_ВН2", "КАБЕЛЬ3", "Устройство",
                "REF", "Лист", "Материал"
            };
        }

        private static void InitListZziBlocks()
        {
            _typeScheme = new List<string>
            {
                "КОРОБ", "КОНТАКТ", "БЛОК", "ВЫНОСКА", "НольУстройства"
            };
        }

        private static void InitListTraceBlocks()
        {
            _typeScheme = new List<string> { "КОРОБ", "ШКАФ" };
        }

        private static void InitListAllBlocks()
        {
            _typeScheme = new List<string>(); // Пустой список - берём все блоки
        }

        private static void InitCounterBlocks()
        {
            _countBlocks = 0;
        }

        private static bool IsMyBlock(BlockReference br, Transaction tr)
        {
            if (_typeScheme.Count == 0) return true;
            string realName = GetBlockRealName(br, tr);
            return _typeScheme.Contains(realName);
        }

        private static bool ExportBlocksToYaml(Document doc, string yamlPath)
        {
            Database db = doc.Database;
            Editor ed = doc.Editor;

            try
            {
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    // Получаем все блоки в чертеже
                    var blockRefs = GetAllBlockReferences(db, tr);
                    if (blockRefs.Count == 0) return false;

                    // Удаляем старый файл если существует
                    if (File.Exists(yamlPath))
                    {
                        File.Delete(yamlPath);
                    }

                    using (StreamWriter writer = new StreamWriter(yamlPath, false, Encoding.GetEncoding("windows-1251")))
                    {
                        writer.WriteLine("# Экспорт динамических блоков");
                        writer.WriteLine($"# Файл: {Path.GetFileName(doc.Name)}");
                        writer.WriteLine($"# Дата: {DateTime.Now}");
                        writer.WriteLine();

                        int total = blockRefs.Count;
                        int processed = 0;

                        foreach (ObjectId id in blockRefs)
                        {
                            processed++;
                            BlockReference br = tr.GetObject(id, OpenMode.ForRead) as BlockReference;
                            if (br != null && IsMyBlock(br, tr))
                            {
                                ExportBlock(br, writer, tr);
                                _countBlocks++;
                            }
                            else if (br != null && _debugMode)
                            {
                                ed.WriteMessage($"\nПропускаем блок: {GetBlockName(br)}/{GetBlockRealName(br, tr)}");
                            }
                        }
                    }

                    tr.Commit();
                    return true;
                }
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nОшибка экспорта: {ex.Message}");
                return false;
            }
        }

        private static List<ObjectId> GetAllBlockReferences(Database db, Transaction tr)
        {
            var result = new List<ObjectId>();
            BlockTable bt = tr.GetObject(db.BlockTableId, OpenMode.ForRead) as BlockTable;

            // Проверяем ModelSpace и PaperSpace
            foreach (ObjectId btrId in bt)
            {
                BlockTableRecord btr = tr.GetObject(btrId, OpenMode.ForRead) as BlockTableRecord;
                if (btr != null && (btr.IsLayout || btr.Name == "*Model_Space"))
                {
                    foreach (ObjectId entId in btr)
                    {
                        Entity ent = tr.GetObject(entId, OpenMode.ForRead) as Entity;
                        if (ent is BlockReference)
                        {
                            result.Add(entId);
                        }
                    }
                }
            }
            return result;
        }

        private static void ExportBlock(BlockReference br, StreamWriter writer, Transaction tr)
        {
            writer.WriteLine($"- Handle: '{br.Handle.ToString()}'");
            writer.WriteLine($"  Block Name: '{GetBlockName(br)}'");
            writer.WriteLine($"  Real Name: '{GetBlockRealName(br, tr)}'");

            Point3d pos = br.Position;
            writer.WriteLine($"  X: '{pos.X:F4}'");
            writer.WriteLine($"  Y: '{pos.Y:F4}'");
            writer.WriteLine($"  Z: '{pos.Z:F4}'");
            writer.WriteLine($"  Layer: '{br.Layer}'");
            writer.WriteLine($"  Rotation: '{br.Rotation * 180 / Math.PI:F4}'");

            // Атрибуты
            ExportAttributes(br, writer, tr);

            // Постоянные атрибуты
            ExportConstantAttributes(br, writer, tr);

            // Динамические свойства
            ExportProperties(br, writer);
        }

        private static void ExportAttributes(BlockReference br, StreamWriter writer, Transaction tr)
        {
            writer.WriteLine("  Attribs:");
            if (br.AttributeCollection.Count > 0)
            {
                foreach (ObjectId attId in br.AttributeCollection)
                {
                    AttributeReference att = tr.GetObject(attId, OpenMode.ForRead) as AttributeReference;
                    if (att != null)
                    {
                        writer.WriteLine($"    {att.Tag}: '{EscapeYamlString(att.TextString)}'");
                    }
                }
            }
        }

        private static void ExportConstantAttributes(BlockReference br, StreamWriter writer, Transaction tr)
        {
            writer.WriteLine("  ConstAttribs:");
            // Постоянные атрибуты хранятся в определении блока
            BlockTableRecord btr = tr.GetObject(br.BlockTableRecord, OpenMode.ForRead) as BlockTableRecord;
            if (btr != null)
            {
                foreach (ObjectId entId in btr)
                {
                    Entity ent = tr.GetObject(entId, OpenMode.ForRead) as Entity;
                    if (ent is AttributeDefinition attDef && attDef.Constant)
                    {
                        writer.WriteLine($"    {attDef.Tag}: '{EscapeYamlString(attDef.TextString)}'");
                    }
                }
            }
        }

        private static void ExportProperties(BlockReference br, StreamWriter writer)
        {
            writer.WriteLine("  Properties:");
            if (br.IsDynamicBlock)
            {
                DynamicBlockReferencePropertyCollection props = br.DynamicBlockReferencePropertyCollection;
                foreach (DynamicBlockReferenceProperty prop in props)
                {
                    // Пропускаем свойство Origin как в оригинале
                    if (prop.PropertyName == "Origin") continue;

                    object value = prop.Value;
                    string valueStr = value != null ? value.ToString() : "";
                    writer.WriteLine($"    {prop.PropertyName}: '{EscapeYamlString(valueStr)}'");
                }
            }
        }

        private static bool IsDynamicBlock(BlockReference br)
        {
            return br.IsDynamicBlock;
        }

        private static string GetBlockName(BlockReference br)
        {
            return br.Name;
        }

        private static string GetBlockRealName(BlockReference br, Transaction tr)
        {
            if (br.IsDynamicBlock && br.Name.StartsWith("*U"))
            {
                // Для анонимных динамических блоков получаем эффективное имя
                BlockTableRecord btr = tr.GetObject(br.DynamicBlockTableRecord, OpenMode.ForRead) as BlockTableRecord;
                return btr != null ? btr.Name : br.Name;
            }
            return br.Name;
        }

        private static string EscapeYamlString(string input)
        {
            if (string.IsNullOrEmpty(input)) return "";
            // Экранируем спецсимволы YAML
            return input.Replace("\\", "\\\\").Replace("'", "''");
        }
    }
}