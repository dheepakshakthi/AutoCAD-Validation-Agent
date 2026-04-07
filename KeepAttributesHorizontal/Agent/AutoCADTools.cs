using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using System;
using System.Text.Json;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace KeepAttributesHorizontal.Agent
{
    public static class AutoCADTools
    {
        public static string DrawLine(string argumentsJson)
        {
            try
            {
                using var docJson = JsonDocument.Parse(argumentsJson);
                var root = docJson.RootElement;
                double startX = root.GetProperty("startX").GetDouble();
                double startY = root.GetProperty("startY").GetDouble();
                double startZ = root.GetProperty("startZ").GetDouble();
                double endX = root.GetProperty("endX").GetDouble();
                double endY = root.GetProperty("endY").GetDouble();
                double endZ = root.GetProperty("endZ").GetDouble();

                Document activeDoc = AcadApp.DocumentManager.MdiActiveDocument;
                if (activeDoc == null) return "Error: No active document.";

                using (DocumentLock docLock = activeDoc.LockDocument())
                {
                    using (Transaction tr = activeDoc.TransactionManager.StartTransaction())
                    {
                        BlockTable bt = (BlockTable)tr.GetObject(activeDoc.Database.BlockTableId, OpenMode.ForRead);
                        BlockTableRecord btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                        using (Line line = new Line(new Point3d(startX, startY, startZ), new Point3d(endX, endY, endZ)))
                        {
                            btr.AppendEntity(line);
                            tr.AddNewlyCreatedDBObject(line, true);
                        }
                        tr.Commit();
                    }
                }
                return $"Successfully drawn line from ({startX}, {startY}, {startZ}) to ({endX}, {endY}, {endZ}).";
            }
            catch (Exception ex)
            {
                return $"Error executing DrawLine: {ex.Message}";
            }
        }

        public static string GetSelectedEntities(string argumentsJson)
        {
            try
            {
                Document activeDoc = AcadApp.DocumentManager.MdiActiveDocument;
                if (activeDoc == null) return "Error: No active document.";

                Editor ed = activeDoc.Editor;
                PromptSelectionResult psr = ed.SelectImplied();

                if (psr.Status != PromptStatus.OK)
                {
                    return "No entities are currently selected.";
                }

                int count = psr.Value.Count;
                return $"Successfully found {count} selected entities.";
            }
            catch (Exception ex)
            {
                return $"Error executing GetSelectedEntities: {ex.Message}";
            }
        }
    }
}
