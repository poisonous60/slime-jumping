using UnityEngine;
using SlimeJumping.Controller;

namespace SlimeJumping.Debugging
{
    public class SlimeStateDebugUI : MonoBehaviour
    {
        [SerializeField] private SlimeController slimeController;

        [Header("Position")]
        [SerializeField] private Vector2 screenOffset = new Vector2(20f, 20f);
        [SerializeField] private float circleSize = 40f;
        [SerializeField] private float spacing = 56f;
        [SerializeField] private int fontSize = 14;

        [Header("Colors")]
        [SerializeField] private Color attachedColor = Color.green;
        [SerializeField] private Color chargingColor = Color.yellow;
        [SerializeField] private Color airborneColor = Color.cyan;
        [SerializeField] private Color stunnedColor = new Color(1f, 0.2f, 0.2f);
        [SerializeField] private Color stickColor = new Color(1f, 0.5f, 0f);
        [SerializeField] private Color offColor = new Color(0.2f, 0.2f, 0.2f, 0.5f);

        private Texture2D circleTex;
        private GUIStyle labelStyle;

        private Rigidbody2D rb;
        private Vector2 prevVelocity;
        private Vector2 acceleration;
        private readonly System.Text.StringBuilder perContactBuilder = new System.Text.StringBuilder(128);

        private void Awake()
        {
            circleTex = CreateCircleTexture(64);
            if (slimeController != null)
                rb = slimeController.GetComponent<Rigidbody2D>();
        }

        private void FixedUpdate()
        {
            if (rb == null) return;
            Vector2 vel = rb.linearVelocity;
            acceleration = (vel - prevVelocity) / Time.fixedDeltaTime;
            prevVelocity = vel;
        }

        private void OnDestroy()
        {
            if (circleTex != null)
                Destroy(circleTex);
        }

        private void OnGUI()
        {
            if (slimeController == null) return;

            if (labelStyle == null)
            {
                labelStyle = new GUIStyle(GUI.skin.label)
                {
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleCenter
                };
            }
            labelStyle.fontSize = fontSize;

            var state = slimeController.CurrentState;

            DrawIndicator(0, "ATT", SlimeController.SlimeState.Attached, state, attachedColor);
            DrawIndicator(1, "CHG", SlimeController.SlimeState.Charging, state, chargingColor);
            DrawIndicator(2, "AIR", SlimeController.SlimeState.Airborne, state, airborneColor);
            DrawIndicator(3, "STN", SlimeController.SlimeState.Stunned, state, stunnedColor);
            DrawBoolIndicator(4, "STK", slimeController.IsSticking, stickColor);

            labelStyle.normal.textColor = Color.white;
            labelStyle.alignment = TextAnchor.MiddleLeft;
            float labelY = screenOffset.y + circleSize + 22f;

            Vector2 pos = slimeController.Position;
            GUI.Label(new Rect(screenOffset.x, labelY, 200f, 20f), $"X: {pos.x:F1}  Y: {pos.y:F1}", labelStyle);
            labelY += 18f;

            if (rb != null)
            {
                Vector2 vel = rb.linearVelocity;
                GUI.Label(new Rect(screenOffset.x, labelY, 280f, 20f), $"Vel: ({vel.x:F1}, {vel.y:F1})  |{vel.magnitude:F1}|", labelStyle);
                labelY += 18f;

                GUI.Label(new Rect(screenOffset.x, labelY, 280f, 20f), $"Acc: ({acceleration.x:F0}, {acceleration.y:F0})  |{acceleration.magnitude:F0}|", labelStyle);
                labelY += 18f;
            }

            if (slimeController.IsTouchingSurface)
            {
                float angle = Mathf.Acos(Mathf.Abs(Vector2.Dot(slimeController.SurfaceNormal, slimeController.GravityUp))) * Mathf.Rad2Deg;
                GUI.Label(new Rect(screenOffset.x, labelY, 280f, 20f), $"Surface (eff): {angle:F1}°", labelStyle);
                labelY += 18f;

                var probe = slimeController.Probe;
                if (probe != null && probe.ContactCount > 0)
                {
                    Vector2 gUp = slimeController.GravityUp;
                    int primary = probe.SelectedIndex;
                    var sb = perContactBuilder;
                    sb.Length = 0;
                    sb.Append("  ");
                    for (int i = 0; i < probe.ContactCount; i++)
                    {
                        Vector2 n = probe.GetContactNormal(i);
                        float a = Mathf.Acos(Mathf.Abs(Vector2.Dot(n, gUp))) * Mathf.Rad2Deg;
                        bool active = probe.IsContactActive(i);
                        bool isPrimary = i == primary;
                        if (i > 0) sb.Append("  ");
                        sb.Append('[').Append(i).Append(']');
                        if (isPrimary) sb.Append('*');
                        if (!active) sb.Append('~');
                        sb.Append(' ').Append(a.ToString("F1")).Append("°");
                    }
                    GUI.Label(new Rect(screenOffset.x, labelY, 520f, 20f), sb.ToString(), labelStyle);
                }
            }

            labelStyle.alignment = TextAnchor.MiddleCenter;
        }

        private void DrawIndicator(int index, string label,
            SlimeController.SlimeState targetState, SlimeController.SlimeState currentState,
            Color activeColor)
        {
            float x = screenOffset.x + index * spacing;
            float y = screenOffset.y;

            bool isActive = currentState == targetState;
            Color color = isActive ? activeColor : offColor;

            Color prev = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(new Rect(x, y, circleSize, circleSize), circleTex);
            GUI.color = prev;

            labelStyle.normal.textColor = isActive ? Color.white : new Color(1f, 1f, 1f, 0.4f);
            GUI.Label(new Rect(x, y + circleSize + 2f, circleSize, 16f), label, labelStyle);
        }

        private void DrawBoolIndicator(int index, string label, bool isActive, Color activeColor)
        {
            float x = screenOffset.x + index * spacing;
            float y = screenOffset.y;

            Color color = isActive ? activeColor : offColor;

            Color prev = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(new Rect(x, y, circleSize, circleSize), circleTex);
            GUI.color = prev;

            labelStyle.normal.textColor = isActive ? Color.white : new Color(1f, 1f, 1f, 0.4f);
            GUI.Label(new Rect(x, y + circleSize + 2f, circleSize, 16f), label, labelStyle);
        }

        private static Texture2D CreateCircleTexture(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            float center = size * 0.5f;
            float radius = center - 1f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dist = Vector2.Distance(new Vector2(x, y), new Vector2(center, center));
                    if (dist <= radius)
                        tex.SetPixel(x, y, Color.white);
                    else
                        tex.SetPixel(x, y, Color.clear);
                }
            }

            tex.Apply();
            return tex;
        }
    }
}
