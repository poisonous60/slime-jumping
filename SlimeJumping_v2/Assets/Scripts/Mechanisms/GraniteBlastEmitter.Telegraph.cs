using UnityEngine;

namespace SlimeJumping.Mechanisms
{
    // PreFire telegraph LineRenderer — warning line from muzzle along the current aim while charging.
    public partial class GraniteBlastEmitter
    {
        private LineRenderer telegraphLR;

        private void ShowTelegraph()
        {
            if (!showPreFireTelegraph) return;
            EnsureTelegraph();
            if (telegraphLR == null) return;
            telegraphLR.enabled = true;
        }

        private void HideTelegraph()
        {
            if (telegraphLR != null) telegraphLR.enabled = false;
        }

        private void UpdateTelegraph()
        {
            if (telegraphLR == null || !telegraphLR.enabled) return;
            // Aim is re-read every frame; deliberately not locked at PreFire entry.
            Vector3 origin = AimOrigin();
            Vector2 dir = AimDirection2D();
            float reach = maxRange;
            if (bulletHitMask != 0)
            {
                var hit = RaycastExcludingSelf(origin, dir, maxRange, bulletHitMask);
                if (hit.collider != null) reach = hit.distance;
            }
            Vector3 a = new Vector3(origin.x, origin.y, origin.z + lineZOffset);
            Vector3 b = new Vector3(origin.x + dir.x * reach,
                                    origin.y + dir.y * reach,
                                    origin.z + lineZOffset);
            telegraphLR.SetPosition(0, a);
            telegraphLR.SetPosition(1, b);
        }

        private void EnsureTelegraph()
        {
            if (telegraphLR != null) return;
            var go = new GameObject("PreFireTelegraph_Auto");
            go.transform.SetParent(transform, false);
            telegraphLR = go.AddComponent<LineRenderer>();
            telegraphLR.useWorldSpace = true;
            telegraphLR.positionCount = 2;
            telegraphLR.widthMultiplier = telegraphWidth;
            telegraphLR.numCapVertices = 2;
            telegraphLR.alignment = LineAlignment.View;
            telegraphLR.textureMode = LineTextureMode.Stretch;
            telegraphLR.sortingOrder = 99;
            var sh = Shader.Find("Sprites/Default");
            var m = new Material(sh);
            m.mainTexture = Texture2D.whiteTexture;
            telegraphLR.material = m;
            telegraphLR.startColor = telegraphColor;
            telegraphLR.endColor = telegraphColor;
            telegraphLR.enabled = false;
        }
    }
}
