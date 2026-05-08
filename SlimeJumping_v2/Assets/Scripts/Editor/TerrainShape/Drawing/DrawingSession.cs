using System.Collections.Generic;
using UnityEngine;

namespace SlimeJumping.TerrainShape.Editor.Drawing
{
    // Per-editor-instance drawing state. Plain class — never serialized. Survives across
    // OnSceneGUI/OnInspectorGUI ticks but resets on selection change or assembly reload.
    internal sealed class DrawingSession
    {
        public DrawSubTool subTool = DrawSubTool.Rectangle;

        // Primitive drag state (Rectangle/Triangle/Circle): MouseDown sets dragStartLocal,
        // MouseUp commits.
        public bool dragging;
        public Vector2 dragStartLocal;
        public Vector2 dragCurrentLocal;

        // Polyline accumulator (committed points; cursor is the live preview tail).
        public readonly List<Vector2> polylinePoints = new List<Vector2>();

        // Freehand raw stream — pre-RDP. Each MouseDrag adds a point if 2px+ from the last.
        public readonly List<Vector2> freehandRaw = new List<Vector2>();

        // Stamp state. Preset can be null until user selects via Tab popup.
        public ShapePreset stampPreset;
        public float stampRotationDeg;
        public float stampScale = 1f;

        // Captured at the start of a stroke (Shift held = subtract / eraser). When true the
        // commit applies PolygonOps.Subtract instead of Union. Reset by EndStroke.
        public bool subtractMode;

        public void EndStroke()
        {
            dragging = false;
            polylinePoints.Clear();
            freehandRaw.Clear();
            subtractMode = false;
        }
    }
}
