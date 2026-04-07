using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;
using Timer = System.Timers.Timer;
using ElapsedEventArgs = System.Timers.ElapsedEventArgs;

namespace KeepAttributesHorizontal.Validation
{
    /// <summary>
    /// Listens to AutoCAD database events and extracts geometry data for validation.
    /// Implements debouncing to handle rapid edit bursts.
    /// </summary>
    public class GeometryListener : IDisposable
    {
        private readonly Timer _debounceTimer;
        private readonly object _lock = new object();
        private bool _pendingValidation = false;
        private bool _isListening;
        private Database? _attachedDatabase;
        private string _sessionId;
        private int _geometryVersion = 0;

        /// <summary>
        /// Event fired when geometry changes are ready for validation (after debounce).
        /// </summary>
        public event EventHandler<GeometryChangedEventArgs>? GeometryChanged;

        /// <summary>
        /// Debounce delay in milliseconds. Default 300ms.
        /// </summary>
        public int DebounceDelayMs { get; set; } = 300;

        /// <summary>
        /// Stable session identifier for this listener lifecycle.
        /// </summary>
        public string SessionId => _sessionId;

        /// <summary>
        /// Current geometry version id.
        /// </summary>
        public string GeometryVersionId => $"v{_geometryVersion}";

        /// <summary>
        /// Active drawing identifier.
        /// </summary>
        public string DrawingId => AcadApp.DocumentManager.MdiActiveDocument?.Name ?? "active-drawing";

        public GeometryListener()
        {
            _sessionId = Guid.NewGuid().ToString("N").Substring(0, 8);
            _debounceTimer = new Timer();
            _debounceTimer.AutoReset = false;
            _debounceTimer.Elapsed += OnDebounceElapsed;
        }

        /// <summary>
        /// Start listening to the active document's database events.
        /// </summary>
        public void StartListening()
        {
            if (_isListening)
            {
                return;
            }

            _isListening = true;
            AcadApp.DocumentManager.DocumentActivated += OnDocumentActivated;
            AttachToDocument(AcadApp.DocumentManager.MdiActiveDocument);

            System.Diagnostics.Debug.WriteLine($"GeometryListener started for session {_sessionId}");
        }

        /// <summary>
        /// Stop listening to database events.
        /// </summary>
        public void StopListening()
        {
            if (!_isListening)
            {
                return;
            }

            _isListening = false;
            AcadApp.DocumentManager.DocumentActivated -= OnDocumentActivated;
            DetachFromCurrentDatabase();

            _debounceTimer.Stop();
            System.Diagnostics.Debug.WriteLine($"GeometryListener stopped for session {_sessionId}");
        }

        private void OnDocumentActivated(object? sender, DocumentCollectionEventArgs e)
        {
            if (!_isListening)
            {
                return;
            }

            AttachToDocument(e.Document);
        }

        private void AttachToDocument(Document? document)
        {
            DetachFromCurrentDatabase();
            if (document == null)
            {
                return;
            }

            _attachedDatabase = document.Database;
            _attachedDatabase.ObjectModified += OnObjectModified;
            _attachedDatabase.ObjectAppended += OnObjectAppended;
            _attachedDatabase.ObjectErased += OnObjectErased;
        }

        private void DetachFromCurrentDatabase()
        {
            if (_attachedDatabase == null)
            {
                return;
            }

            _attachedDatabase.ObjectModified -= OnObjectModified;
            _attachedDatabase.ObjectAppended -= OnObjectAppended;
            _attachedDatabase.ObjectErased -= OnObjectErased;
            _attachedDatabase = null;
        }

        private void OnObjectModified(object sender, ObjectEventArgs e)
        {
            if (IsRelevantEntity(e.DBObject))
            {
                TriggerDebouncedValidation();
            }
        }

        private void OnObjectAppended(object sender, ObjectEventArgs e)
        {
            if (IsRelevantEntity(e.DBObject))
            {
                TriggerDebouncedValidation();
            }
        }

        private void OnObjectErased(object sender, ObjectErasedEventArgs e)
        {
            if (IsRelevantEntity(e.DBObject))
            {
                TriggerDebouncedValidation();
            }
        }

        private bool IsRelevantEntity(DBObject obj)
        {
            // Filter to geometric entities we care about
            return obj is Circle || obj is Line || obj is Arc ||
                   obj is Polyline || obj is DBText || obj is MText ||
                   obj is Ellipse || obj is Spline;
        }

        private void TriggerDebouncedValidation()
        {
            lock (_lock)
            {
                _pendingValidation = true;
                _debounceTimer.Stop();
                _debounceTimer.Interval = DebounceDelayMs;
                _debounceTimer.Start();
            }
        }

        private void OnDebounceElapsed(object? sender, ElapsedEventArgs e)
        {
            lock (_lock)
            {
                if (!_pendingValidation) return;
                _pendingValidation = false;
            }

            _geometryVersion++;
            var payload = ExtractGeometryPayload();

            GeometryChanged?.Invoke(this, new GeometryChangedEventArgs
            {
                Payload = payload
            });
        }

        /// <summary>
        /// Manually trigger a full geometry extraction and validation.
        /// </summary>
        public GeometryPayload ExtractGeometryPayload()
        {
            var payload = new GeometryPayload
            {
                SessionId = _sessionId,
                GeometryVersion = $"v{_geometryVersion}",
                Entities = new List<Entity>()
            };

            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return payload;

            using (var tr = doc.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(doc.Database.BlockTableId, OpenMode.ForRead);
                var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

                foreach (ObjectId id in ms)
                {
                    var ent = tr.GetObject(id, OpenMode.ForRead) as Autodesk.AutoCAD.DatabaseServices.Entity;
                    if (ent == null) continue;

                    var extracted = ExtractEntity(ent);
                    if (extracted != null)
                    {
                        payload.Entities.Add(extracted);
                    }
                }

                tr.Commit();
            }

            return payload;
        }

        /// <summary>
        /// Tool-compatible extraction for latest geometry delta.
        /// </summary>
        public GeometryPayload ExtractGeometryDelta()
        {
            return ExtractGeometryPayload();
        }

        /// <summary>
        /// Tool-compatible extraction for a full geometry snapshot.
        /// </summary>
        public GeometryPayload ExtractGeometrySnapshot()
        {
            return ExtractGeometryPayload();
        }

        private Entity? ExtractEntity(Autodesk.AutoCAD.DatabaseServices.Entity ent)
        {
            var entity = new Entity
            {
                Handle = ent.Handle.ToString(),
                Layer = ent.Layer,
                Properties = new EntityProperties()
            };

            switch (ent)
            {
                case Circle circle:
                    entity.Type = "Circle";
                    entity.Properties.Radius = circle.Radius;
                    entity.Properties.Center = new List<double>
                    {
                        circle.Center.X, circle.Center.Y, circle.Center.Z
                    };
                    break;

                case Line line:
                    entity.Type = "Line";
                    entity.Properties.StartPoint = new List<double>
                    {
                        line.StartPoint.X, line.StartPoint.Y, line.StartPoint.Z
                    };
                    entity.Properties.EndPoint = new List<double>
                    {
                        line.EndPoint.X, line.EndPoint.Y, line.EndPoint.Z
                    };
                    entity.Properties.Length = line.Length;
                    break;

                case Arc arc:
                    entity.Type = "Arc";
                    entity.Properties.Radius = arc.Radius;
                    entity.Properties.Center = new List<double>
                    {
                        arc.Center.X, arc.Center.Y, arc.Center.Z
                    };
                    entity.Properties.StartAngle = arc.StartAngle * (180.0 / Math.PI);
                    entity.Properties.EndAngle = arc.EndAngle * (180.0 / Math.PI);
                    break;

                case DBText text:
                    entity.Type = "Text";
                    entity.Properties.TextHeight = text.Height;
                    entity.Properties.TextContent = text.TextString;
                    break;

                case MText mtext:
                    entity.Type = "MText";
                    entity.Properties.TextHeight = mtext.TextHeight;
                    entity.Properties.TextContent = mtext.Contents;
                    break;

                case Polyline pline:
                    entity.Type = "Polyline";
                    entity.Properties.Length = pline.Length;
                    entity.Properties.Area = pline.Closed ? pline.Area : null;
                    break;

                default:
                    return null; // Skip unsupported types
            }

            return entity;
        }

        public void Dispose()
        {
            StopListening();
            _debounceTimer.Dispose();
        }
    }

    public class GeometryChangedEventArgs : EventArgs
    {
        public GeometryPayload Payload { get; set; } = new();
    }
}
